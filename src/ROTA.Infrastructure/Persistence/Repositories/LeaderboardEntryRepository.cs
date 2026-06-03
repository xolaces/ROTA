using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

// BETA — all writes are raw parameterised SQL executed on the Npgsql connection that backs
// the RotaDbContext.  This avoids EF change-tracker interference on the upsert path and
// matches the pattern used by BetaKeyRepository.TryRedeemAsync and
// ActiveRaidRepository.AtomicApplyHitAsync.
//
// Concurrency guarantee (IncrementAsync):
//   PostgreSQL's ON CONFLICT DO UPDATE is atomic at the row level — the read-modify-write
//   of `value = leaderboard_entry.value + EXCLUDED.value` executes inside the database engine
//   under a row-level lock.  Concurrent INSERTs that conflict on the unique index queue behind
//   that lock; each sees the already-committed value and adds its own delta correctly.
//   No advisory lock is needed because we never read-then-write from application code.
//
// The `EXCLUDED` pseudo-table contains the row that *would* have been inserted.  We put the
// delta into the insert's value column so that `EXCLUDED.value` carries the delta on the
// conflict branch — a standard PostgreSQL upsert-increment pattern.
public sealed class LeaderboardEntryRepository : ILeaderboardEntryRepository
{
    private readonly RotaDbContext _db;

    public LeaderboardEntryRepository(RotaDbContext db)
    {
        _db = db;
    }

    // ── IncrementAsync ───────────────────────────────────────────────────────

    public async Task IncrementAsync(
        Guid playerId,
        LeaderboardBoard board,
        LeaderboardPeriod period,
        string periodKey,
        long delta,
        DateTimeOffset at,
        CancellationToken ct = default)
    {
        // We need a raw Npgsql command so we can use the ON CONFLICT upsert syntax.
        // Using ExecuteSqlRawAsync (EF) would require positional {0} placeholders; using
        // Npgsql directly lets us name parameters which is safer with EXCLUDED references.
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await EnsureOpenAsync(conn, ct);

        // Get the current transaction if one is ambient (e.g. advisory-lock tx from RaidService).
        var dbTx = _db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;

        const string sql = """
            INSERT INTO leaderboard_entry
                (id, player_id, board, period, period_key, value, last_progress_at, rank,
                 created_at, updated_at, is_deleted)
            VALUES
                (gen_random_uuid(), @playerId, @board, @period, @periodKey, @delta, @at, NULL,
                 @at, @at, false)
            ON CONFLICT (player_id, board, period_key) DO UPDATE
                SET value            = leaderboard_entry.value + EXCLUDED.value,
                    last_progress_at = @at,
                    updated_at       = NOW()
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, dbTx);
        AddCommonParams(cmd, playerId, board, period, periodKey, at);
        cmd.Parameters.AddWithValue("delta", NpgsqlDbType.Bigint, delta);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── MaxUpdateAsync ───────────────────────────────────────────────────────

    public async Task MaxUpdateAsync(
        Guid playerId,
        LeaderboardBoard board,
        LeaderboardPeriod period,
        string periodKey,
        long candidate,
        DateTimeOffset at,
        CancellationToken ct = default)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await EnsureOpenAsync(conn, ct);

        var dbTx = _db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;

        // ON CONFLICT branch:
        //   value            ← GREATEST(stored, candidate)     — never lowers
        //   last_progress_at ← @at only when candidate exceeded the old value; otherwise keep
        //                      the existing timestamp (earliest-to-reach tiebreak must not
        //                      regress when a smaller hit comes in later).
        const string sql = """
            INSERT INTO leaderboard_entry
                (id, player_id, board, period, period_key, value, last_progress_at, rank,
                 created_at, updated_at, is_deleted)
            VALUES
                (gen_random_uuid(), @playerId, @board, @period, @periodKey, @candidate, @at, NULL,
                 @at, @at, false)
            ON CONFLICT (player_id, board, period_key) DO UPDATE
                SET value            = GREATEST(leaderboard_entry.value, EXCLUDED.value),
                    last_progress_at = CASE
                                           WHEN EXCLUDED.value > leaderboard_entry.value
                                           THEN @at
                                           ELSE leaderboard_entry.last_progress_at
                                       END,
                    updated_at       = NOW()
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, dbTx);
        AddCommonParams(cmd, playerId, board, period, periodKey, at);
        cmd.Parameters.AddWithValue("candidate", NpgsqlDbType.Bigint, candidate);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── GetPageAsync ─────────────────────────────────────────────────────────

    public async Task<IReadOnlyList<LeaderboardEntry>> GetPageAsync(
        LeaderboardBoard board,
        string periodKey,
        int page,
        int pageSize,
        CancellationToken ct = default)
    {
        var offset = (page - 1) * pageSize;

        return await _db.LeaderboardEntries
            .AsNoTracking()
            .Where(e => e.Board == board
                     && e.PeriodKey == periodKey
                     && !e.IsDeleted)
            .OrderByDescending(e => e.Value)
            .ThenBy(e => e.LastProgressAt)
            .Skip(offset)
            .Take(pageSize)
            .ToListAsync(ct);
    }

    // ── GetPlayerEntryAsync ──────────────────────────────────────────────────

    public async Task<LeaderboardEntry?> GetPlayerEntryAsync(
        Guid playerId,
        LeaderboardBoard board,
        string periodKey,
        CancellationToken ct = default)
        => await _db.LeaderboardEntries
            .AsNoTracking()
            .Where(e => e.PlayerId == playerId
                     && e.Board == board
                     && e.PeriodKey == periodKey
                     && !e.IsDeleted)
            .FirstOrDefaultAsync(ct);

    // ── Private helpers ──────────────────────────────────────────────────────

    private static void AddCommonParams(
        NpgsqlCommand cmd,
        Guid playerId,
        LeaderboardBoard board,
        LeaderboardPeriod period,
        string periodKey,
        DateTimeOffset at)
    {
        cmd.Parameters.AddWithValue("playerId",  NpgsqlDbType.Uuid,        playerId);
        cmd.Parameters.AddWithValue("board",     NpgsqlDbType.Integer,     (int)board);
        cmd.Parameters.AddWithValue("period",    NpgsqlDbType.Integer,     (int)period);
        cmd.Parameters.AddWithValue("periodKey", NpgsqlDbType.Varchar,     periodKey);
        // DateTimeOffset → timestamptz (Npgsql maps DateTimeOffset to TimestampTz by default,
        // but we're explicit here to avoid any UTC-offset ambiguity).
        cmd.Parameters.AddWithValue("at",        NpgsqlDbType.TimestampTz, at);
    }

    private static async Task EnsureOpenAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
    }
}
