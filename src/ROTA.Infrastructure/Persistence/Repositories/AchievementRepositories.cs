using System.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Repositories;

// TICKET 46 — Achievement progress + award ledger repos. Mirror MasteryRepositories.cs:
// progress uses raw ON CONFLICT upserts (row-level atomic, ambient-tx aware) for increment + the
// absolute recount; the award ledger uses unique-violation-idempotent create (the gem-ledger pattern).

public sealed class AchievementProgressRepository : IAchievementProgressRepository
{
    private readonly RotaDbContext _db;
    public AchievementProgressRepository(RotaDbContext db) => _db = db;

    public async Task<IReadOnlyList<AchievementProgress>> GetForPlayerAsync(Guid playerId, CancellationToken ct = default)
        => await _db.AchievementProgress.AsNoTracking()
            .Where(p => p.PlayerId == playerId && !p.IsDeleted)
            .ToListAsync(ct);

    public Task<AchievementProgress?> FindAsync(Guid playerId, string achievementId, CancellationToken ct = default)
        => _db.AchievementProgress
            .FirstOrDefaultAsync(p => p.PlayerId == playerId && p.AchievementId == achievementId && !p.IsDeleted, ct);

    public async Task IncrementAsync(Guid playerId, string achievementId, long delta, CancellationToken ct = default)
    {
        if (delta <= 0) return;

        // Raw ON CONFLICT upsert-increment — row-level atomic, no advisory lock needed.
        // Mirrors PlayerMasteryActivityRepository.IncrementAsync; participates in the ambient tx when open.
        var (conn, dbTx) = await PrepareAsync(ct);

        const string sql = """
            INSERT INTO achievement_progress
                (id, player_id, achievement_id, progress_value, is_completed, completed_at, created_at, updated_at, is_deleted)
            VALUES
                (gen_random_uuid(), @playerId, @achievementId, @delta, false, NULL, NOW(), NOW(), false)
            ON CONFLICT (player_id, achievement_id) DO UPDATE
                SET progress_value = achievement_progress.progress_value + EXCLUDED.progress_value,
                    updated_at     = NOW()
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, dbTx);
        cmd.Parameters.AddWithValue("playerId",      NpgsqlDbType.Uuid,    playerId);
        cmd.Parameters.AddWithValue("achievementId", NpgsqlDbType.Text,    achievementId);
        cmd.Parameters.AddWithValue("delta",         NpgsqlDbType.Bigint,  delta);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task SetCounterAsync(Guid playerId, string achievementId, long absoluteValue, CancellationToken ct = default)
    {
        if (absoluteValue < 0) absoluteValue = 0;

        // Raw ON CONFLICT upsert-SET — recount metrics write an ABSOLUTE value (not a delta), so a
        // re-grant of an already-owned piece never inflates the total. Ambient-tx aware.
        var (conn, dbTx) = await PrepareAsync(ct);

        const string sql = """
            INSERT INTO achievement_progress
                (id, player_id, achievement_id, progress_value, is_completed, completed_at, created_at, updated_at, is_deleted)
            VALUES
                (gen_random_uuid(), @playerId, @achievementId, @value, false, NULL, NOW(), NOW(), false)
            ON CONFLICT (player_id, achievement_id) DO UPDATE
                SET progress_value = EXCLUDED.progress_value,
                    updated_at     = NOW()
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, dbTx);
        cmd.Parameters.AddWithValue("playerId",      NpgsqlDbType.Uuid,   playerId);
        cmd.Parameters.AddWithValue("achievementId", NpgsqlDbType.Text,   achievementId);
        cmd.Parameters.AddWithValue("value",         NpgsqlDbType.Bigint, absoluteValue);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    public async Task UpsertAsync(AchievementProgress progress, CancellationToken ct = default)
    {
        var existing = await _db.AchievementProgress.FirstOrDefaultAsync(p => p.Id == progress.Id, ct);
        if (existing is null)
            _db.AchievementProgress.Add(progress);
        else if (!ReferenceEquals(existing, progress))
            _db.Entry(existing).CurrentValues.SetValues(progress);
        await _db.SaveChangesAsync(ct);
    }

    private async Task<(NpgsqlConnection conn, NpgsqlTransaction? tx)> PrepareAsync(CancellationToken ct)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        if (conn.State != ConnectionState.Open) await conn.OpenAsync(ct);
        var dbTx = _db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;
        return (conn, dbTx);
    }
}

public sealed class AchievementAwardRepository : IAchievementAwardRepository
{
    private readonly RotaDbContext _db;
    public AchievementAwardRepository(RotaDbContext db) => _db = db;

    public async Task<int> GetTotalPointsAsync(Guid playerId, CancellationToken ct = default)
        => await _db.AchievementAwards
            .Where(a => a.PlayerId == playerId)
            .SumAsync(a => a.Points, ct);

    public Task<bool> ReferenceExistsAsync(Guid playerId, string referenceId, CancellationToken ct = default)
        => _db.AchievementAwards
            .AnyAsync(a => a.PlayerId == playerId && a.ReferenceId == referenceId, ct);

    public async Task<IReadOnlyList<AchievementAward>> GetForPlayerAsync(Guid playerId, CancellationToken ct = default)
        => await _db.AchievementAwards.AsNoTracking()
            .Where(a => a.PlayerId == playerId)
            .ToListAsync(ct);

    public async Task<bool> CreateAsync(AchievementAward award, CancellationToken ct = default)
    {
        _db.AchievementAwards.Add(award);
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

// Append-only progress-event idempotency ledger. Clones the mastery respec / award create pattern:
// ExistsAsync is the pre-check; CreateAsync is unique-violation-idempotent (false on a concurrent dup).
public sealed class AchievementProgressEventRepository : IAchievementProgressEventRepository
{
    private readonly RotaDbContext _db;
    public AchievementProgressEventRepository(RotaDbContext db) => _db = db;

    public Task<bool> ExistsAsync(Guid playerId, string achievementId, string referenceId, CancellationToken ct = default)
        => _db.AchievementProgressEvents
            .AnyAsync(e => e.PlayerId == playerId && e.AchievementId == achievementId && e.ReferenceId == referenceId, ct);

    public async Task<bool> CreateAsync(AchievementProgressEvent progressEvent, CancellationToken ct = default)
    {
        _db.AchievementProgressEvents.Add(progressEvent);
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
