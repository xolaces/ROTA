using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Maintenance;

/// <summary>
/// Clears the beta world between waves and leaves the logins standing.
/// </summary>
/// <remarks>
/// <para><b>Why this fails closed.</b> The obvious implementation is a list of DELETE statements.
/// That list is correct on the day it is written and silently wrong the first time somebody adds a
/// table — and the failure is invisible: a "reset" player quietly keeps their chapter-6 quest
/// progress or their Orange gear, and nobody finds out until a tester reports it. So this classifies
/// EVERY table in the schema as keep, wipe, or special, checks that classification against
/// <c>information_schema</c> at run time, and REFUSES to run if a single table is unaccounted for.
/// Adding a table now forces a decision about it instead of allowing an omission.</para>
///
/// <para><b>Why the FK graph is not the source of truth.</b> Twenty-two tables carry
/// <c>player_id</c> with no foreign key at all — including every quest-progress, gear, unit and
/// magic table. A wipe derived from the FK graph would miss all of them and leave a level-1 account
/// owning the campaign.</para>
///
/// <para><b>Why re-seeding borrows the real factory.</b> Fresh stats and resources come from
/// <see cref="Player.CreateWithId"/> rather than from values retyped here, so a reset account and a
/// newly-registered one cannot drift apart. If the seeded pools change, this follows for free.</para>
/// </remarks>
public sealed class BetaResetService : IBetaResetService
{
    private readonly RotaDbContext _db;

    public BetaResetService(RotaDbContext db) => _db = db;

    /// <summary>Tables that survive a reset, each with the reason it survives.</summary>
    private static readonly Dictionary<string, string> Kept = new(StringComparer.OrdinalIgnoreCase)
    {
        ["audit_log"] = "append-only by architecture rule — no DELETE on this table, ever",
        ["punishment_log"] = "moderation history; a wipe is not an amnesty",
        ["outbound_emails"] = "the ops dashboard's source of truth; player link is already SET NULL",
        ["gauntlet_events"] = "operator-created events, not player data",
        ["__EFMigrationsHistory"] = "schema bookkeeping",
    };

    /// <summary>Handled by dedicated logic rather than a blanket delete.</summary>
    private static readonly Dictionary<string, string> Special = new(StringComparer.OrdinalIgnoreCase)
    {
        ["players"] = "reset in place (or purged on request); never blanket-deleted",
        ["beta_keys"] = "unredeemed rows deleted; redeemed rows kept as the record of who got in",
    };

    /// <summary>
    /// Emptied completely, children before parents so the RESTRICT foreign keys on
    /// <c>guilds</c> and <c>active_raids</c> are satisfied without deferring constraints.
    /// </summary>
    private static readonly string[] Wiped =
    {
        // ── leaves: rows nothing else points at ───────────────────────────────────────────────
        "achievement_awards", "achievement_progress", "achievement_progress_events",
        "gem_transactions", "gauntlet_currency_transactions", "guild_currency_transactions",
        "guild_sigil_pool_transactions", "mastery_activity_events", "mastery_respec_transactions",
        "strike_transactions", "leaderboard_entry", "pinnacle_first_claims",
        "market_transactions", "market_listings",
        "private_messages", "friendships", "player_blocks",
        "password_reset_tokens", "refresh_tokens",
        "player_commander_gear", "player_equipment", "player_event_magics",
        "player_gauntlet_battalions", "player_gauntlet_trophies", "player_gear",
        "player_inventory_items", "player_legion_slots", "player_legions",
        "player_magic_honors", "player_magics", "player_masteries", "player_mastery_activities",
        "player_quest_difficulty_progress", "player_quest_progress", "player_units",
        "player_resources", "player_stats",
        // ── then the parents whose children are now gone ──────────────────────────────────────
        "raid_magics", "raid_participants", "active_raids",
        "gauntlet_entries",
        "guild_join_requests", "guild_memberships", "guilds",
    };

    /// <summary>
    /// The subset of <see cref="Wiped"/> that is a player's own character rather than shared world
    /// state. Only these are spared for an account named by <c>--keep</c>.
    /// </summary>
    /// <remarks>
    /// Everything omitted here is deliberately world state even when it carries a
    /// <c>player_id</c>: guild membership, raid participation, gauntlet standing, market listings,
    /// the social graph, leaderboard rows and world-first claims all describe a world that is being
    /// replaced. Sparing them produces rows that point at deleted parents.
    /// </remarks>
    private static readonly HashSet<string> CharacterTables = new(StringComparer.OrdinalIgnoreCase)
    {
        "player_stats", "player_resources",
        "player_gear", "player_equipment", "player_commander_gear", "player_inventory_items",
        "player_units", "player_legions", "player_legion_slots",
        "player_magics", "player_event_magics", "player_magic_honors",
        "player_masteries", "player_mastery_activities",
        "mastery_activity_events", "mastery_respec_transactions",
        "player_quest_progress", "player_quest_difficulty_progress",
        "achievement_progress", "achievement_awards", "achievement_progress_events",
        "gem_transactions", "refresh_tokens", "password_reset_tokens",
    };

    public async Task<BetaResetReport> RunAsync(
        bool dryRun,
        bool purgeAccounts = false,
        IReadOnlyCollection<string>? keepUsernames = null,
        CancellationToken ct = default)
    {
        var report = new BetaResetReport { DryRun = dryRun };
        foreach (var kv in Kept) report.TablesKept[kv.Key] = kv.Value;
        foreach (var kv in Special) report.TablesKept[kv.Key] = kv.Value;

        var strays = CharacterTables.Except(Wiped, StringComparer.OrdinalIgnoreCase).ToList();
        if (strays.Count > 0)
            throw new InvalidOperationException(
                "BetaResetService is misconfigured: CharacterTables must be a subset of Wiped. "
                + "Not in Wiped: " + string.Join(", ", strays));

        // ── the fail-closed check, before anything is written ─────────────────────────────────
        var actual = await ListSchemaTablesAsync(ct);
        var classified = new HashSet<string>(Wiped, StringComparer.OrdinalIgnoreCase);
        classified.UnionWith(Kept.Keys);
        classified.UnionWith(Special.Keys);

        report.UnclassifiedTables = actual.Where(t => !classified.Contains(t))
                                          .OrderBy(t => t, StringComparer.Ordinal).ToList();
        if (report.UnclassifiedTables.Count > 0)
            return report;   // refuse — the caller reports and exits non-zero

        var keep = new HashSet<string>(keepUsernames ?? Array.Empty<string>(),
                                       StringComparer.OrdinalIgnoreCase);

        var players = await _db.Set<Player>()
            .Where(p => !keep.Contains(p.Username))
            .ToListAsync(ct);

        report.RedeemedKeysKept = await _db.Set<BetaKey>().CountAsync(k => k.IsRedeemed, ct);
        var unredeemed = await _db.Set<BetaKey>().CountAsync(k => !k.IsRedeemed, ct);
        report.UnredeemedKeysDeleted = unredeemed;

        if (purgeAccounts) report.PlayersPurged = players.Count;
        else report.PlayersReset = players.Count;

        // ── dry run: count what each wipe would remove, write nothing ─────────────────────────
        if (dryRun)
        {
            foreach (var table in Wiped)
            {
                DeleteAllFrom(table, actual);          // same allowlist check, no write
                var n = await CountAsync(table, ct);
                if (n > 0) report.RowsDeletedByTable[table] = n;
            }
            Sort(report);
            return report;
        }

        // ── the real run, all of it inside one transaction ────────────────────────────────────
        await using var tx = await _db.Database.BeginTransactionAsync(ct);

        // A kept account keeps its CHARACTER and loses its WORLD — the only coherent reading of a
        // world wipe. "Has a player_id column" is NOT the test: guild_memberships has one and is
        // world state, and sparing it left a membership row pointing at a guild being deleted,
        // which the FK correctly refused. So the character set is explicit.
        var keptIds = keep.Count == 0
            ? new List<Guid>()
            : await _db.Set<Player>().Where(p => keep.Contains(p.Username))
                                     .Select(p => p.Id).ToListAsync(ct);
        var playerKeyed = CharacterTables;

        foreach (var table in Wiped)
        {
            var sql = DeleteAllFrom(table, actual);
            int n;
            if (keptIds.Count > 0 && playerKeyed.Contains(table))
            {
                var ids = string.Join(",", keptIds.Select(id => $"'{id:D}'::uuid"));
                n = await _db.Database.ExecuteSqlRawAsync(sql + $" WHERE player_id NOT IN ({ids})", ct);
            }
            else
            {
                n = await _db.Database.ExecuteSqlRawAsync(sql, ct);
            }
            if (n > 0) report.RowsDeletedByTable[table] = n;
        }

        await _db.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"beta_keys\" WHERE is_redeemed = false", ct);

        if (purgeAccounts)
        {
            _db.Set<Player>().RemoveRange(players);
            await _db.SaveChangesAsync(ct);
        }
        else
        {
            foreach (var p in players)
            {
                p.ResetProgress();

                // Borrowed from the registration factory so the two can never drift apart.
                var template = Player.CreateWithId(p.Id, p.Username, p.Email, p.PasswordHash);
                _db.Set<PlayerStats>().Add(template.Stats!);
                _db.Set<PlayerResource>().AddRange(template.Resources);
            }
            await _db.SaveChangesAsync(ct);
        }

        _db.Set<AuditLog>().Add(AuditLog.Create(
            playerId: null,
            action: purgeAccounts ? "BetaResetPurge" : "BetaReset",
            inputHash: null,
            resultSummary:
                $"players={(purgeAccounts ? report.PlayersPurged : report.PlayersReset)} " +
                $"purge={purgeAccounts} unredeemedKeysDeleted={report.UnredeemedKeysDeleted} " +
                $"redeemedKeysKept={report.RedeemedKeysKept} rows={report.TotalRowsDeleted}",
            ipAddress: null));
        await _db.SaveChangesAsync(ct);

        // The post-condition that makes the whole operation safe to ship. Every surviving account
        // must have exactly the rows a registered player has — one stats row and four resource
        // pools. An account missing them cannot log in, and the first version of --keep produced
        // exactly that. This runs inside the transaction, so a violation rolls the wipe back
        // instead of leaving a live server full of unloadable accounts.
        if (!purgeAccounts)
        {
            await _db.SaveChangesAsync(ct);
            var broken = await (
                from pl in _db.Set<Player>()
                let statsCount = _db.Set<PlayerStats>().Count(x => x.PlayerId == pl.Id)
                let resCount = _db.Set<PlayerResource>().Count(x => x.PlayerId == pl.Id)
                where statsCount != 1 || resCount != 4
                select new { pl.Username, statsCount, resCount }).ToListAsync(ct);

            if (broken.Count > 0)
                throw new InvalidOperationException(
                    "beta-reset aborted and rolled back: these accounts would have been left "
                    + "unloadable (expected 1 stats row and 4 resource rows): "
                    + string.Join(", ", broken.Select(b => $"{b.Username} stats={b.statsCount} res={b.resCount}")));
        }

        await tx.CommitAsync(ct);
        Sort(report);
        return report;
    }

    private static void Sort(BetaResetReport r) =>
        r.RowsDeletedByTable = r.RowsDeletedByTable
            .OrderByDescending(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.Ordinal)
            .ToDictionary(kv => kv.Key, kv => kv.Value);

    private async Task<List<string>> ListSchemaTablesAsync(CancellationToken ct)
    {
        var names = new List<string>();
        var conn = _db.Database.GetDbConnection();
        await _db.Database.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText =
            "SELECT tablename FROM pg_tables WHERE schemaname = 'public' ORDER BY tablename";
        await using var reader = await cmd.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct)) names.Add(reader.GetString(0));
        return names;
    }

    /// <summary>
    /// Builds a <c>DELETE FROM "table"</c> for a table name that has been checked against the live
    /// schema listing. Table names cannot be parameterised in SQL, so the protection has to be an
    /// allowlist: the name must appear verbatim in <paramref name="schemaTables"/> and contain
    /// nothing but lowercase letters, digits and underscores. Both hold for every entry in
    /// <see cref="Wiped"/>; the guard is here so that stays true if someone edits the list.
    /// </summary>
    private static string DeleteAllFrom(string table, IReadOnlyCollection<string> schemaTables)
    {
        if (!schemaTables.Contains(table, StringComparer.Ordinal))
            throw new InvalidOperationException($"Refusing to delete from unknown table '{table}'.");
        if (!table.All(c => char.IsAsciiLetterLower(c) || char.IsAsciiDigit(c) || c == '_'))
            throw new InvalidOperationException($"Refusing to delete from unsafe table name '{table}'.");
        return "DELETE FROM \"" + table + "\"";
    }

    private async Task<long> CountAsync(string table, CancellationToken ct)
    {
        var conn = _db.Database.GetDbConnection();
        await _db.Database.OpenConnectionAsync(ct);
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT count(*) FROM \"" + table + "\"";
        var result = await cmd.ExecuteScalarAsync(ct);
        return result is null or DBNull ? 0L : Convert.ToInt64(result);
    }
}
