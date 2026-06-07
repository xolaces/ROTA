using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using ROTA.Infrastructure.Persistence.Repositories;
using ROTA.Infrastructure.Services;
using Testcontainers.PostgreSql;

namespace ROTA.IntegrationTests;

// BETA (System 16 Slice 5) — Postgres-backed settlement tests. These exercise the REAL
// GauntletAdminService against REAL ledger repositories + the REAL content provider (shipped
// gauntlet_prizes.json), so the money-bug guard is verified against ACTUAL ledger balances:
//   • settle pays the correct per-band tokens/pitchfork/trophy,
//   • SETTLE-TWICE-PAYS-ONCE — re-settle leaves token/pitchfork balances + trophy counts UNCHANGED,
//   • honor write-back creates exactly one PlayerMagicHonor for a revoked holder (idempotent),
//   • per-league rank-1s each receive the rank-1 band.
public class GauntletSettlementTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("rota_gauntlet_settle_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        await _postgres.StartAsync();

        await using var db = NewDbContext();
        await db.Database.MigrateAsync(); // includes AddGauntletSystem
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private RotaDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<RotaDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new RotaDbContext(options);
    }

    // Real content provider over the shipped Api JSON (the genuine prize bands).
    private static GauntletContentProvider Content()
    {
        var apiRoot = FindApiContentRoot();
        var magic   = new MagicDefinitionProvider(apiRoot);
        return new GauntletContentProvider(apiRoot, magic, ConfigOpts());
    }

    private static IOptions<GauntletConfig> ConfigOpts() => Options.Create(DefaultConfig());
    private static GauntletConfig DefaultConfig() => new();   // defaults: PrizeRankCount 500, etc.

    // Build the REAL admin service over one shared DbContext (mirrors scoped DI for one request).
    private GauntletAdminService BuildAdmin(RotaDbContext db, GauntletContentProvider content)
    {
        var events   = new GauntletEventRepository(db);
        var entries  = new GauntletEntryRepository(db);
        var scoring  = new GauntletScoringService(entries, ConfigOpts());
        var currency = new GauntletCurrencyRepository(db);
        var trophies = new PlayerGauntletTrophyRepository(db);
        var evMagics = new PlayerEventMagicRepository(db);
        var honors   = new PlayerMagicHonorRepository(db);
        var audit    = new AuditLogRepository(db);
        return new GauntletAdminService(
            events, scoring, entries, content, currency, trophies, evMagics, honors, audit, ConfigOpts());
    }

    private async Task<Guid> SeedPlayerAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var player = Player.Create($"g_{suffix}", $"{suffix}@test.dev", "hash");
        await using var db = NewDbContext();
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player.Id;
    }

    // Seed a CLOSED event (Scheduled → Active → Closed) ready to settle.
    private async Task<Guid> SeedClosedEventAsync()
    {
        await using var db = NewDbContext();
        var ev = GauntletEvent.Create("Cycle", DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(7));
        ev.Activate();
        ev.Close();
        db.GauntletEvents.Add(ev);
        await db.SaveChangesAsync();
        return ev.Id;
    }

    // Seed an entry with an explicit score so RecomputeRanksAsync yields a deterministic per-league rank.
    private async Task<Guid> SeedEntryAsync(Guid eventId, GauntletLeague league, long score, DateTimeOffset tieBreakAt)
    {
        var playerId = await SeedPlayerAsync();
        await using var db = NewDbContext();
        var entry = GauntletEntry.Create(eventId, playerId, league);
        if (score > 0) entry.AddScore(score, tieBreakAt);
        db.GauntletEntries.Add(entry);
        await db.SaveChangesAsync();
        return playerId;
    }

    private async Task<long> TokenBalanceAsync(Guid playerId)
    {
        await using var db = NewDbContext();
        return await new GauntletCurrencyRepository(db).GetBalanceAsync(playerId, GauntletCurrency.Token);
    }

    private async Task<long> PitchforkBalanceAsync(Guid playerId)
    {
        await using var db = NewDbContext();
        return await new GauntletCurrencyRepository(db).GetBalanceAsync(playerId, GauntletCurrency.Pitchfork);
    }

    private async Task<int> TrophyCountAsync(Guid playerId)
    {
        await using var db = NewDbContext();
        return await db.PlayerGauntletTrophies.CountAsync(t => t.PlayerId == playerId && !t.IsDeleted);
    }

    // ── Per-band correctness ────────────────────────────────────────────────────

    [Fact]
    public async Task Settle_GrantsCorrectPrizesPerBand()
    {
        var eventId = await SeedClosedEventAsync();
        var now = DateTimeOffset.UtcNow;
        // One Whelpling league: scores descending → ranks 1, 2, 500-equivalent.
        var rank1 = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 1000, now);
        var rank2 = await SeedEntryAsync(eventId, GauntletLeague.Whelpling,  900, now);
        var rank3 = await SeedEntryAsync(eventId, GauntletLeague.Whelpling,  800, now);

        await using (var db = NewDbContext())
        {
            var content = Content();
            var result  = await BuildAdmin(db, content).SettleEventAsync(eventId);
            result.Success.Should().BeTrue();
        }

        // Rank 1 → 50 tokens + 10 pitchfork + Aureate trophy.
        (await TokenBalanceAsync(rank1)).Should().Be(50);
        (await PitchforkBalanceAsync(rank1)).Should().Be(10);
        (await TrophyCountAsync(rank1)).Should().Be(1);

        // Ranks 2 & 3 fall in the 2–10 band → 25 tokens + 5 pitchfork + Argent trophy.
        (await TokenBalanceAsync(rank2)).Should().Be(25);
        (await PitchforkBalanceAsync(rank2)).Should().Be(5);
        (await TrophyCountAsync(rank2)).Should().Be(1);
        (await TokenBalanceAsync(rank3)).Should().Be(25);
    }

    // ── THE MONEY-BUG GUARD: settle twice pays once ─────────────────────────────

    [Fact]
    public async Task SettleTwice_PaysOnce_BalancesUnchanged()
    {
        var eventId = await SeedClosedEventAsync();
        var now = DateTimeOffset.UtcNow;
        var rank1 = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 1000, now);
        var rank2 = await SeedEntryAsync(eventId, GauntletLeague.Whelpling,  900, now);

        // First settle.
        await using (var db = NewDbContext())
            (await BuildAdmin(db, Content()).SettleEventAsync(eventId)).Success.Should().BeTrue();

        var token1AfterFirst     = await TokenBalanceAsync(rank1);
        var pitchfork1AfterFirst = await PitchforkBalanceAsync(rank1);
        var trophy1AfterFirst    = await TrophyCountAsync(rank1);
        token1AfterFirst.Should().Be(50, "rank-1 tokens credited once");
        pitchfork1AfterFirst.Should().Be(10);
        trophy1AfterFirst.Should().Be(1);

        // SECOND settle on the now-Settled event → idempotent no-op. Must NOT throw, must NOT double-pay.
        await using (var db = NewDbContext())
        {
            var result = await BuildAdmin(db, Content()).SettleEventAsync(eventId);
            result.Success.Should().BeTrue("re-settle of a Settled event is an idempotent no-op");
            result.Settlement!.TokensGranted.Should().Be(0, "nothing new is paid on re-settle");
            result.Settlement.PitchforkGranted.Should().Be(0);
            result.Settlement.TrophiesGranted.Should().Be(0);
        }

        // Balances + trophy counts are UNCHANGED — the core money-bug assertion against real ledgers.
        (await TokenBalanceAsync(rank1)).Should().Be(50, "tokens must not be paid twice");
        (await PitchforkBalanceAsync(rank1)).Should().Be(10, "pitchfork must not be paid twice");
        (await TrophyCountAsync(rank1)).Should().Be(1, "trophy stays a single row");
        (await TokenBalanceAsync(rank2)).Should().Be(25);
        (await PitchforkBalanceAsync(rank2)).Should().Be(5);
    }

    // Force the payout pass to run a SECOND time (bypassing the already-Settled fast-path) by
    // resetting the event back to Closed between runs — proves the per-grant referenceId guard
    // ALONE prevents double-pay even if the state guard were somehow bypassed (defense in depth).
    [Fact]
    public async Task SettlePayoutRunTwice_OnReopenedEvent_StillPaysOnce()
    {
        var eventId = await SeedClosedEventAsync();
        var now = DateTimeOffset.UtcNow;
        var rank1 = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 1000, now);

        await using (var db = NewDbContext())
            (await BuildAdmin(db, Content()).SettleEventAsync(eventId)).Success.Should().BeTrue();

        (await TokenBalanceAsync(rank1)).Should().Be(50);

        // Re-close the event so the second settle re-enters the payout pass (not the no-op fast-path).
        // Settled → Closed isn't a legal domain transition, so flip the row's state via raw SQL to
        // simulate a re-triggered settle that reaches the payout loop again.
        await using (var db = NewDbContext())
        {
            await db.Database.ExecuteSqlRawAsync(
                "UPDATE gauntlet_events SET state = {0}, settled_at = NULL WHERE id = {1}",
                (int)GauntletEventState.Closed, eventId);
        }

        await using (var db = NewDbContext())
        {
            var result = await BuildAdmin(db, Content()).SettleEventAsync(eventId);
            result.Success.Should().BeTrue();
            result.Settlement!.TokensGranted.Should().Be(0,
                "the per-grant referenceId guard blocks the re-credit even when the payout pass re-runs");
        }

        // Ledger balance is STILL 50 — the unique referenceId / index is the true backstop.
        (await TokenBalanceAsync(rank1)).Should().Be(50, "the referenceId guard alone prevents double-pay");
    }

    // ── Honor-echo write-back ────────────────────────────────────────────────────

    [Fact]
    public async Task Settle_HonorEcho_RevokesEventMagic_AndWritesHonor_IdempotentOnReSettle()
    {
        var eventId = await SeedClosedEventAsync();
        var holder  = await SeedPlayerAsync();

        // Seed an ACTIVE PlayerEventMagic for the event (the consumable that expires at settle).
        await using (var db = NewDbContext())
        {
            db.PlayerEventMagics.Add(PlayerEventMagic.Create(holder, eventId, "magic_wrath_of_the_ancients"));
            await db.SaveChangesAsync();
        }

        // First settle → revoke the consumable + write the honor.
        await using (var db = NewDbContext())
            (await BuildAdmin(db, Content()).SettleEventAsync(eventId)).Success.Should().BeTrue();

        await using (var db = NewDbContext())
        {
            // The consumable is soft-deleted (revoked).
            var em = await db.PlayerEventMagics.SingleAsync(
                m => m.PlayerId == holder && m.GauntletEventId == eventId);
            em.IsDeleted.Should().BeTrue("the per-event consumable is revoked at settle");

            // Exactly one honor record exists for the holder.
            (await db.PlayerMagicHonors.CountAsync(
                h => h.PlayerId == holder && h.MagicDefinitionId == "magic_wrath_of_the_ancients" && !h.IsDeleted))
                .Should().Be(1, "a permanent honor echo is written for the revoked holder");
        }

        // Re-settle (no-op) → still exactly one honor (idempotent; no second row).
        await using (var db = NewDbContext())
            (await BuildAdmin(db, Content()).SettleEventAsync(eventId)).Success.Should().BeTrue();

        await using (var db = NewDbContext())
            (await db.PlayerMagicHonors.CountAsync(
                h => h.PlayerId == holder && h.MagicDefinitionId == "magic_wrath_of_the_ancients" && !h.IsDeleted))
                .Should().Be(1, "honor is written once and never duplicated on re-settle");
    }

    // ── Per-league: each league's rank-1 gets the rank-1 band ───────────────────

    [Fact]
    public async Task Settle_PerLeague_EachLeaguesRank1_GetsRank1Band()
    {
        var eventId = await SeedClosedEventAsync();
        var now = DateTimeOffset.UtcNow;
        // One rank-1 in each of the 3 leagues (each is its own partition).
        var whelp = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 500, now);
        var wyrm  = await SeedEntryAsync(eventId, GauntletLeague.Wyrm,      500, now);
        var dragon= await SeedEntryAsync(eventId, GauntletLeague.Dragon,    500, now);
        // A second, lower-scoring Wyrm entry so Wyrm's rank-1 is genuinely a per-league #1.
        var wyrm2 = await SeedEntryAsync(eventId, GauntletLeague.Wyrm,      100, now);

        await using (var db = NewDbContext())
        {
            var result = await BuildAdmin(db, Content()).SettleEventAsync(eventId);
            result.Success.Should().BeTrue();
            result.Settlement!.TrophiesGranted.Should().Be(4, "3 rank-1 Aureate + 1 rank-2 Argent (the second Wyrm)");
        }

        // Every league's rank-1 received the rank-1 band: 50 tokens + 10 pitchfork + a trophy.
        foreach (var p in new[] { whelp, wyrm, dragon })
        {
            (await TokenBalanceAsync(p)).Should().Be(50, "each league's rank-1 gets the rank-1 token band");
            (await PitchforkBalanceAsync(p)).Should().Be(10);
            (await TrophyCountAsync(p)).Should().Be(1);
        }
        // The second Wyrm entry is rank 2 → 25 tokens (proves the first Wyrm was a true per-league #1).
        (await TokenBalanceAsync(wyrm2)).Should().Be(25);
    }

    private static string FindApiContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ROTA.Api");
            if (Directory.Exists(Path.Combine(candidate, "content")))
                return candidate;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }
}
