using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Npgsql;
using NpgsqlTypes;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

// BETA (System 16 Slice 2 + 3) — per-event standings persistence.
//
// Slice-3 score/rank operations use raw parameterised Npgsql (mirrors LeaderboardEntryRepository):
//   • IncrementScoreAsync — a single atomic UPDATE (score = score + @delta) so concurrent qualifying
//     hits never lose an update. tie_break_at advances only on a positive delta. We DO NOT
//     load-modify-save through the change tracker.
//   • RecomputeRanksAsync — one set-based UPDATE using ROW_NUMBER() so the per-league snapshot is
//     computed entirely inside Postgres and is idempotent.
// Both participate in an ambient transaction when present (e.g. the RaidService advisory-lock tx).
public sealed class GauntletEntryRepository : IGauntletEntryRepository
{
    private readonly RotaDbContext _db;

    public GauntletEntryRepository(RotaDbContext db) => _db = db;

    public Task<GauntletEntry?> FindByEventAndPlayerAsync(
        Guid gauntletEventId, Guid playerId, CancellationToken ct = default)
        => _db.GauntletEntries
            .FirstOrDefaultAsync(
                e => e.GauntletEventId == gauntletEventId
                  && e.PlayerId == playerId
                  && !e.IsDeleted, ct);

    public async Task<IReadOnlyList<GauntletEntry>> GetForEventAsync(
        Guid gauntletEventId, CancellationToken ct = default)
        => await _db.GauntletEntries
            .Where(e => e.GauntletEventId == gauntletEventId && !e.IsDeleted)
            .ToListAsync(ct);

    public async Task<GauntletEntry> UpsertAsync(GauntletEntry entry, CancellationToken ct = default)
    {
        // Fast path: an entry already exists → return it unchanged (league never re-evaluated).
        var existing = await FindByEventAndPlayerAsync(entry.GauntletEventId, entry.PlayerId, ct);
        if (existing is not null)
            return existing;

        // Insert; the unique index on (gauntlet_event_id, player_id) makes a concurrent double-join
        // race-safe — the loser catches the unique violation and re-reads the winner's row.
        try
        {
            _db.GauntletEntries.Add(entry);
            await _db.SaveChangesAsync(ct);
            return entry;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _db.ChangeTracker.Clear();
            // Re-read the row the winning insert committed.
            return (await FindByEventAndPlayerAsync(entry.GauntletEventId, entry.PlayerId, ct))!;
        }
    }

    // ── Slice 3: atomic score increment ────────────────────────────────────────

    public async Task IncrementScoreAsync(
        Guid eventId, Guid playerId, long delta, DateTimeOffset hitAt, CancellationToken ct = default)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await EnsureOpenAsync(conn, ct);

        // Pick up an ambient transaction (e.g. RaidService advisory-lock tx) so the score update
        // commits atomically with the hit that produced it (Slice 4).
        var dbTx = _db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;

        // Single race-safe UPDATE. tie_break_at only moves forward on a positive delta — a zero or
        // negative delta leaves the earliest-to-reach tiebreak untouched.
        const string sql = """
            UPDATE gauntlet_entries
               SET score        = score + @delta,
                   tie_break_at = CASE WHEN @delta > 0 THEN @hitAt ELSE tie_break_at END,
                   updated_at   = NOW()
             WHERE gauntlet_event_id = @eventId
               AND player_id         = @playerId
               AND is_deleted        = false
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, dbTx);
        cmd.Parameters.AddWithValue("delta",    NpgsqlDbType.Bigint,      delta);
        cmd.Parameters.AddWithValue("hitAt",    NpgsqlDbType.TimestampTz, hitAt);
        cmd.Parameters.AddWithValue("eventId",  NpgsqlDbType.Uuid,        eventId);
        cmd.Parameters.AddWithValue("playerId", NpgsqlDbType.Uuid,        playerId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── T76: atomic highest-stage update ───────────────────────────────────────

    public async Task RecordStageDefeatAsync(
        Guid eventId, Guid playerId, int stage, CancellationToken ct = default)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await EnsureOpenAsync(conn, ct);

        var dbTx = _db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;

        // GREATEST makes this monotonic + race-safe: concurrent / out-of-order kills can never
        // lower the recorded peak. Rides the RaidService advisory-lock tx when present.
        const string sql = """
            UPDATE gauntlet_entries
               SET highest_stage = GREATEST(highest_stage, @stage),
                   updated_at    = NOW()
             WHERE gauntlet_event_id = @eventId
               AND player_id         = @playerId
               AND is_deleted        = false
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, dbTx);
        cmd.Parameters.AddWithValue("stage",    NpgsqlDbType.Integer, stage);
        cmd.Parameters.AddWithValue("eventId",  NpgsqlDbType.Uuid,    eventId);
        cmd.Parameters.AddWithValue("playerId", NpgsqlDbType.Uuid,    playerId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Slice 3: per-league rank snapshot ──────────────────────────────────────

    public async Task RecomputeRanksAsync(Guid eventId, CancellationToken ct = default)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await EnsureOpenAsync(conn, ct);

        var dbTx = _db.Database.CurrentTransaction?.GetDbTransaction() as NpgsqlTransaction;

        // One set-based UPDATE: ROW_NUMBER() partitions by league. T76 — the PRIMARY metric is
        // highest_stage (highest ladder stage completed, DotD-parity); cumulative damage then
        // tie_break_at ASC (earliest-to-reach) break ties. The CTE materialises the rank per entry
        // id; the UPDATE writes it into last_rank. Idempotent over an unchanged board.
        const string sql = """
            WITH ranked AS (
                SELECT id,
                       ROW_NUMBER() OVER (
                           PARTITION BY league
                           ORDER BY highest_stage DESC, score DESC, tie_break_at ASC
                       ) AS rn
                FROM gauntlet_entries
                WHERE gauntlet_event_id = @eventId
                  AND is_deleted        = false
            )
            UPDATE gauntlet_entries g
               SET last_rank  = ranked.rn,
                   updated_at = NOW()
              FROM ranked
             WHERE g.id = ranked.id
            """;

        await using var cmd = new NpgsqlCommand(sql, conn, dbTx);
        cmd.Parameters.AddWithValue("eventId", NpgsqlDbType.Uuid, eventId);
        await cmd.ExecuteNonQueryAsync(ct);
    }

    // ── Slice 3: leaderboard page (snapshot-ranked, joined to players) ──────────

    public async Task<IReadOnlyList<GauntletLeaderboardRow>> GetLeaderboardPageAsync(
        Guid eventId, GauntletLeague league, int take, CancellationToken ct = default)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await EnsureOpenAsync(conn, ct);

        // Read directly from the snapshot: ORDER BY last_rank (NOT recomputed here). Entries with a
        // null last_rank are not yet snapshotted and are excluded from the public board.
        const string sql = """
            SELECT ge.last_rank,
                   ge.player_id,
                   p.display_name,
                   p.username,
                   ge.score,
                   ge.highest_stage
            FROM gauntlet_entries ge
            JOIN players p ON p.id = ge.player_id
            WHERE ge.gauntlet_event_id = @eventId
              AND ge.league            = @league
              AND ge.is_deleted        = false
              AND ge.last_rank IS NOT NULL
            ORDER BY ge.last_rank ASC
            LIMIT @take
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("eventId", NpgsqlDbType.Uuid,    eventId);
        cmd.Parameters.AddWithValue("league",  NpgsqlDbType.Integer, (int)league);
        cmd.Parameters.AddWithValue("take",    NpgsqlDbType.Integer, take);

        var rows = new List<GauntletLeaderboardRow>();
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
        {
            var displayName = reader.GetString(2);
            var username    = reader.GetString(3);
            rows.Add(new GauntletLeaderboardRow
            {
                Rank         = reader.GetInt32(0),
                PlayerId     = reader.GetGuid(1),
                DisplayName  = string.IsNullOrWhiteSpace(displayName) ? username : displayName,
                Score        = reader.GetInt64(4),
                HighestStage = reader.GetInt32(5),
            });
        }

        return rows;
    }

    // ── Slice 3: caller's own rank + score ──────────────────────────────────────

    public async Task<GauntletRankScore?> GetRankAndScoreAsync(
        Guid eventId, Guid playerId, GauntletLeague? league = null, CancellationToken ct = default)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await EnsureOpenAsync(conn, ct);

        // The optional league predicate is a no-op when @league is NULL; otherwise the caller's
        // (single, locked) entry must be in that league to count toward that league's board.
        const string sql = """
            SELECT last_rank, score, highest_stage
            FROM gauntlet_entries
            WHERE gauntlet_event_id = @eventId
              AND player_id         = @playerId
              AND is_deleted        = false
              AND (@league IS NULL OR league = @league)
            LIMIT 1
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("eventId",  NpgsqlDbType.Uuid, eventId);
        cmd.Parameters.AddWithValue("playerId", NpgsqlDbType.Uuid, playerId);
        // Explicit Integer type so Postgres can infer the parameter even when the value is NULL
        // (AddWithValue(DBNull) leaves the type unknown → 42P08 on the `league = @league` branch).
        cmd.Parameters.Add(new NpgsqlParameter("league", NpgsqlDbType.Integer)
        {
            Value = league.HasValue ? (int)league.Value : DBNull.Value,
        });

        await using var reader = await cmd.ExecuteReaderAsync(ct);
        if (!await reader.ReadAsync(ct))
            return null; // caller has no entry in this event

        int? rank = reader.IsDBNull(0) ? null : reader.GetInt32(0);
        return new GauntletRankScore
        {
            Rank = rank,
            Score = reader.GetInt64(1),
            HighestStage = reader.GetInt32(2),
        };
    }

    // ── Slice 3: count of ranked entries in a league ────────────────────────────

    public async Task<int> CountRankedAsync(
        Guid eventId, GauntletLeague league, CancellationToken ct = default)
    {
        var conn = (NpgsqlConnection)_db.Database.GetDbConnection();
        await EnsureOpenAsync(conn, ct);

        const string sql = """
            SELECT COUNT(*)::int
            FROM gauntlet_entries
            WHERE gauntlet_event_id = @eventId
              AND league            = @league
              AND is_deleted        = false
              AND last_rank IS NOT NULL
            """;

        await using var cmd = new NpgsqlCommand(sql, conn);
        cmd.Parameters.AddWithValue("eventId", NpgsqlDbType.Uuid,    eventId);
        cmd.Parameters.AddWithValue("league",  NpgsqlDbType.Integer, (int)league);

        var result = await cmd.ExecuteScalarAsync(ct);
        return result is int i ? i : Convert.ToInt32(result);
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static async Task EnsureOpenAsync(NpgsqlConnection conn, CancellationToken ct)
    {
        if (conn.State != System.Data.ConnectionState.Open)
            await conn.OpenAsync(ct);
    }
}
