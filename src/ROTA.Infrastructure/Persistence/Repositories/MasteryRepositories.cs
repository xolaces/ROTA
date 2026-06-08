using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class PlayerMasteryRepository : IPlayerMasteryRepository
{
    private readonly RotaDbContext _db;
    public PlayerMasteryRepository(RotaDbContext db) => _db = db;

    public async Task<IReadOnlyList<PlayerMastery>> GetForPlayerAsync(Guid playerId, CancellationToken ct = default)
        => await _db.PlayerMasteries.AsNoTracking()
            .Where(m => m.PlayerId == playerId && !m.IsDeleted)
            .ToListAsync(ct);

    public Task<PlayerMastery?> FindAsync(Guid playerId, MasteryAncient ancient, CancellationToken ct = default)
        => _db.PlayerMasteries
            .FirstOrDefaultAsync(m => m.PlayerId == playerId && m.Ancient == ancient && !m.IsDeleted, ct);

    public async Task UpsertAsync(PlayerMastery mastery, CancellationToken ct = default)
    {
        var existing = await _db.PlayerMasteries.FirstOrDefaultAsync(m => m.Id == mastery.Id, ct);
        if (existing is null)
            _db.PlayerMasteries.Add(mastery);
        else if (!ReferenceEquals(existing, mastery))
            _db.Entry(existing).CurrentValues.SetValues(mastery);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PlayerMastery>> EnsureAllAsync(Guid playerId, CancellationToken ct = default)
    {
        var existing = await _db.PlayerMasteries
            .Where(m => m.PlayerId == playerId && !m.IsDeleted)
            .ToListAsync(ct);

        var have = existing.Select(m => m.Ancient).ToHashSet();
        var added = false;
        foreach (var ancient in Enum.GetValues<MasteryAncient>())
        {
            if (have.Contains(ancient)) continue;
            var row = PlayerMastery.Create(playerId, ancient);
            _db.PlayerMasteries.Add(row);
            existing.Add(row);
            added = true;
        }

        if (added)
        {
            try
            {
                await _db.SaveChangesAsync(ct);
            }
            catch (DbUpdateException)
            {
                // Concurrent ensure created the rows first — re-read the authoritative set.
                _db.ChangeTracker.Clear();
                existing = await _db.PlayerMasteries
                    .Where(m => m.PlayerId == playerId && !m.IsDeleted)
                    .ToListAsync(ct);
            }
        }

        return existing;
    }

    public async Task<IReadOnlyList<PlayerMasteryRatingRow>> GetAllRatingsAsync(CancellationToken ct = default)
    {
        var rows = await _db.PlayerMasteries.AsNoTracking()
            .Where(m => !m.IsDeleted)
            .Select(m => new { m.PlayerId, m.Ancient, m.Level })
            .ToListAsync(ct);

        return rows
            .GroupBy(r => r.PlayerId)
            .Select(g => new PlayerMasteryRatingRow(
                g.Key,
                (IReadOnlyDictionary<MasteryAncient, int>)g.ToDictionary(x => x.Ancient, x => x.Level)))
            .ToList();
    }
}

public sealed class PlayerMasteryActivityRepository : IPlayerMasteryActivityRepository
{
    private readonly RotaDbContext _db;
    public PlayerMasteryActivityRepository(RotaDbContext db) => _db = db;

    public async Task<IReadOnlyList<PlayerMasteryActivity>> GetForPlayerAsync(Guid playerId, CancellationToken ct = default)
        => await _db.PlayerMasteryActivities.AsNoTracking()
            .Where(a => a.PlayerId == playerId && !a.IsDeleted)
            .ToListAsync(ct);

    public async Task IncrementAsync(Guid playerId, MasteryActivityType activityType, long delta, CancellationToken ct = default)
    {
        if (delta <= 0) return;

        // Raw ON CONFLICT upsert-increment — row-level atomic, no advisory lock needed.
        // Mirrors LeaderboardEntryRepository.IncrementAsync; participates in the ambient tx when open.
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        var dbTx = _db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;

        const string sql = """
            INSERT INTO player_mastery_activities
                (id, player_id, activity_type, counter, created_at, updated_at, is_deleted)
            VALUES
                (gen_random_uuid(), @playerId, @activityType, @delta, NOW(), NOW(), false)
            ON CONFLICT (player_id, activity_type) DO UPDATE
                SET counter    = player_mastery_activities.counter + EXCLUDED.counter,
                    updated_at = NOW()
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, dbTx);
        cmd.Parameters.AddWithValue("playerId",     NpgsqlDbType.Uuid,    playerId);
        cmd.Parameters.AddWithValue("activityType", NpgsqlDbType.Integer, (int)activityType);
        cmd.Parameters.AddWithValue("delta",        NpgsqlDbType.Bigint,  delta);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public Task<bool> HasEventAsync(Guid playerId, MasteryActivityType activityType, string referenceId, CancellationToken ct = default)
        => _db.MasteryActivityEvents
            .AnyAsync(e => e.PlayerId == playerId && e.ActivityType == activityType && e.ReferenceId == referenceId, ct);

    public async Task RecordEventAsync(Guid playerId, MasteryActivityType activityType, string referenceId, CancellationToken ct = default)
    {
        _db.MasteryActivityEvents.Add(MasteryActivityEvent.Create(playerId, activityType, referenceId));
        await _db.SaveChangesAsync(ct);
    }
}

public sealed class MasteryRespecRepository : IMasteryRespecRepository
{
    private readonly RotaDbContext _db;
    public MasteryRespecRepository(RotaDbContext db) => _db = db;

    public Task<bool> ReferenceExistsAsync(Guid playerId, string referenceId, CancellationToken ct = default)
        => _db.MasteryRespecTransactions
            .AnyAsync(t => t.PlayerId == playerId && t.ReferenceId == referenceId, ct);

    public async Task<bool> CreateAsync(MasteryRespecTransaction tx, CancellationToken ct = default)
    {
        _db.MasteryRespecTransactions.Add(tx);
        try
        {
            await _db.SaveChangesAsync(ct);
            return true;
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation
        })
        {
            _db.ChangeTracker.Clear();
            return false;
        }
    }
}
