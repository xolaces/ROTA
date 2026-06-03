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

    // ── GetEligiblePageAsync ─────────────────────────────────────────────────

    public async Task<IReadOnlyList<EligibleLeaderboardEntry>> GetEligiblePageAsync(
        LeaderboardBoard board,
        string periodKey,
        int page,
        int pageSize,
        int minLevel,
        bool excludeAdmins,
        CancellationToken ct = default)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await EnsureOpenAsync(conn, ct);

        // Admin bit = 4 (PlayerRoles.Admin).
        // The predicate uses bitwise AND: (p.roles & 4) = 0 means no Admin flag set.
        // Moderator bit (2) is deliberately NOT filtered — Moderators appear as regular players.
        const string sql = """
            SELECT le.player_id,
                   le.value,
                   le.last_progress_at,
                   p.display_name,
                   p.username
            FROM leaderboard_entry le
            JOIN players p ON p.id = le.player_id
            WHERE le.board      = @board
              AND le.period_key = @periodKey
              AND le.is_deleted = false
              AND p.is_deleted  = false
              AND p.is_banned   = false
              AND p.level       >= @minLevel
              AND (@excludeAdmins = false OR (p.roles & 4) = 0)
            ORDER BY le.value DESC,
                     le.last_progress_at ASC
            LIMIT @pageSize OFFSET @offset
            """;

        var offset = (page - 1) * pageSize;
        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("board",          NpgsqlDbType.Integer,  (int)board);
        cmd.Parameters.AddWithValue("periodKey",      NpgsqlDbType.Varchar,  periodKey);
        cmd.Parameters.AddWithValue("minLevel",       NpgsqlDbType.Integer,  minLevel);
        cmd.Parameters.AddWithValue("excludeAdmins",  NpgsqlDbType.Boolean,  excludeAdmins);
        cmd.Parameters.AddWithValue("pageSize",       NpgsqlDbType.Integer,  pageSize);
        cmd.Parameters.AddWithValue("offset",         NpgsqlDbType.Integer,  offset);

        var results = new List<EligibleLeaderboardEntry>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var displayName = reader.GetString(3);
            var username    = reader.GetString(4);
            results.Add(new EligibleLeaderboardEntry
            {
                PlayerId       = reader.GetGuid(0),
                Value          = reader.GetInt64(1),
                LastProgressAt = reader.GetFieldValue<DateTimeOffset>(2),
                DisplayName    = string.IsNullOrWhiteSpace(displayName) ? username : displayName,
                Username       = username,
            });
        }

        return results;
    }

    // ── CountEligibleAsync ───────────────────────────────────────────────────

    public async Task<int> CountEligibleAsync(
        LeaderboardBoard board,
        string periodKey,
        int minLevel,
        bool excludeAdmins,
        CancellationToken ct = default)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await EnsureOpenAsync(conn, ct);

        const string sql = """
            SELECT COUNT(*)::int
            FROM leaderboard_entry le
            JOIN players p ON p.id = le.player_id
            WHERE le.board      = @board
              AND le.period_key = @periodKey
              AND le.is_deleted = false
              AND p.is_deleted  = false
              AND p.is_banned   = false
              AND p.level       >= @minLevel
              AND (@excludeAdmins = false OR (p.roles & 4) = 0)
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("board",         NpgsqlDbType.Integer, (int)board);
        cmd.Parameters.AddWithValue("periodKey",     NpgsqlDbType.Varchar, periodKey);
        cmd.Parameters.AddWithValue("minLevel",      NpgsqlDbType.Integer, minLevel);
        cmd.Parameters.AddWithValue("excludeAdmins", NpgsqlDbType.Boolean, excludeAdmins);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is int i ? i : Convert.ToInt32(result);
    }

    // ── GetCallerRankAsync ───────────────────────────────────────────────────

    public async Task<CallerRankEntry?> GetCallerRankAsync(
        Guid callerId,
        LeaderboardBoard board,
        string periodKey,
        int minLevel,
        bool excludeAdmins,
        CancellationToken ct = default)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await EnsureOpenAsync(conn, ct);

        // First verify the caller is eligible and has an entry.
        // Then compute rank as (count of eligible entries strictly ranked above them) + 1.
        // "Strictly above" = value > callerValue OR (value = callerValue AND last_progress_at < callerLastProgressAt).
        const string sql = """
            WITH caller AS (
                SELECT le.value, le.last_progress_at
                FROM leaderboard_entry le
                JOIN players p ON p.id = le.player_id
                WHERE le.player_id  = @callerId
                  AND le.board      = @board
                  AND le.period_key = @periodKey
                  AND le.is_deleted = false
                  AND p.is_deleted  = false
                  AND p.is_banned   = false
                  AND p.level       >= @minLevel
                  AND (@excludeAdmins = false OR (p.roles & 4) = 0)
            )
            SELECT
                c.value,
                (SELECT COUNT(*)::int
                 FROM leaderboard_entry le2
                 JOIN players p2 ON p2.id = le2.player_id
                 WHERE le2.board      = @board
                   AND le2.period_key = @periodKey
                   AND le2.is_deleted = false
                   AND p2.is_deleted  = false
                   AND p2.is_banned   = false
                   AND p2.level       >= @minLevel
                   AND (@excludeAdmins = false OR (p2.roles & 4) = 0)
                   AND (le2.value > c.value
                        OR (le2.value = c.value AND le2.last_progress_at < c.last_progress_at))
                ) + 1 AS rank
            FROM caller c
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("callerId",      NpgsqlDbType.Uuid,    callerId);
        cmd.Parameters.AddWithValue("board",         NpgsqlDbType.Integer, (int)board);
        cmd.Parameters.AddWithValue("periodKey",     NpgsqlDbType.Varchar, periodKey);
        cmd.Parameters.AddWithValue("minLevel",      NpgsqlDbType.Integer, minLevel);
        cmd.Parameters.AddWithValue("excludeAdmins", NpgsqlDbType.Boolean, excludeAdmins);

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null; // no entry or ineligible

        return new CallerRankEntry
        {
            PlayerId = callerId,
            Value    = reader.GetInt64(0),
            Rank     = reader.GetInt32(1),
        };
    }

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
