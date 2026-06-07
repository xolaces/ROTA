using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using ROTA.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ROTA.IntegrationTests;

// BETA (System 16 Slice 3) — Postgres-backed tests for scoring + snapshot rank + leaderboard read:
//   • IncrementScoreAsync: additive + atomic; tie_break_at moves on a positive delta, NOT on zero.
//   • RecomputeRanksAsync: per-league ROW_NUMBER snapshot into last_rank; idempotent.
//   • GetLeaderboardPageAsync: ordered by last_rank, league-isolated, joined to players for the name.
//   • GetRankAndScoreAsync: caller rank returned even when below the page take; tie determinism.
public class GauntletScoringRepositoryTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("rota_gauntlet_score_test")
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

    private async Task<Guid> SeedPlayerAsync(string? displayName = null)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var player = Player.Create($"g_{suffix}", $"{suffix}@test.dev", "hash");
        if (displayName is not null)
            player.UpdateDisplayName(displayName);
        await using var db = NewDbContext();
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player.Id;
    }

    private async Task<Guid> SeedEventAsync()
    {
        await using var db = NewDbContext();
        var ev = GauntletEvent.Create("Cycle", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));
        ev.Activate();
        db.GauntletEvents.Add(ev);
        await db.SaveChangesAsync();
        return ev.Id;
    }

    // Inserts an entry with an explicit score + tie_break_at (bypassing AddScore so we can control
    // both directly), returning the player's id.
    private async Task<Guid> SeedEntryAsync(
        Guid eventId, GauntletLeague league, long score, DateTimeOffset tieBreakAt, string? displayName = null)
    {
        var playerId = await SeedPlayerAsync(displayName);
        await using var db = NewDbContext();
        var entry = GauntletEntry.Create(eventId, playerId, league);
        if (score > 0)
            entry.AddScore(score, tieBreakAt); // sets score + tie_break_at together
        db.GauntletEntries.Add(entry);
        await db.SaveChangesAsync();
        return playerId;
    }

    // ── IncrementScoreAsync: additive + atomic ──────────────────────────────────

    [Fact]
    public async Task IncrementScore_IsAdditive()
    {
        var eventId  = await SeedEventAsync();
        var playerId = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 0, DateTimeOffset.UtcNow);

        await using var db = NewDbContext();
        var repo = new GauntletEntryRepository(db);

        await repo.IncrementScoreAsync(eventId, playerId, 100, DateTimeOffset.UtcNow);
        await repo.IncrementScoreAsync(eventId, playerId, 250, DateTimeOffset.UtcNow);

        var entry = await db.GauntletEntries.AsNoTracking()
            .SingleAsync(e => e.GauntletEventId == eventId && e.PlayerId == playerId);
        entry.Score.Should().Be(350L, "100 + 250");
    }

    [Fact]
    public async Task IncrementScore_PositiveDelta_MovesTieBreakAt()
    {
        var eventId  = await SeedEventAsync();
        var t0       = new DateTimeOffset(2026, 6, 6, 10, 0, 0, TimeSpan.Zero);
        var playerId = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 0, t0);

        await using var db = NewDbContext();
        var repo = new GauntletEntryRepository(db);

        var hitAt = new DateTimeOffset(2026, 6, 6, 11, 30, 0, TimeSpan.Zero);
        await repo.IncrementScoreAsync(eventId, playerId, 500, hitAt);

        var entry = await db.GauntletEntries.AsNoTracking()
            .SingleAsync(e => e.GauntletEventId == eventId && e.PlayerId == playerId);
        entry.Score.Should().Be(500L);
        entry.TieBreakAt.Should().Be(hitAt, "a positive delta advances the tiebreak to the hit instant");
    }

    [Fact]
    public async Task IncrementScore_ZeroDelta_DoesNotMoveTieBreakAt()
    {
        var eventId  = await SeedEventAsync();
        var t0       = new DateTimeOffset(2026, 6, 6, 10, 0, 0, TimeSpan.Zero);
        // Seed a non-zero score so we can observe that a later zero-delta hit leaves tie_break_at at t0.
        var playerId = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 1000, t0);

        await using var db = NewDbContext();
        var repo = new GauntletEntryRepository(db);

        var later = new DateTimeOffset(2026, 6, 6, 12, 0, 0, TimeSpan.Zero);
        await repo.IncrementScoreAsync(eventId, playerId, 0, later);

        var entry = await db.GauntletEntries.AsNoTracking()
            .SingleAsync(e => e.GauntletEventId == eventId && e.PlayerId == playerId);
        entry.Score.Should().Be(1000L, "a zero delta does not change the score");
        entry.TieBreakAt.Should().Be(t0, "a zero delta must NOT advance the earliest-to-reach tiebreak");
    }

    [Fact]
    public async Task IncrementScore_Concurrent_NoLostUpdates()
    {
        // 20 parallel +100 increments. The single atomic UPDATE serialises the read-modify-write at
        // the row level, so the total is exactly 2000 with no lost updates.
        const int taskCount = 20;
        const long delta    = 100L;

        var eventId  = await SeedEventAsync();
        var playerId = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 0, DateTimeOffset.UtcNow);
        var hitAt    = DateTimeOffset.UtcNow;

        var tasks = Enumerable.Range(0, taskCount).Select(_ => Task.Run(async () =>
        {
            await using var db = NewDbContext();
            var repo = new GauntletEntryRepository(db);
            await repo.IncrementScoreAsync(eventId, playerId, delta, hitAt);
        })).ToArray();

        await Task.WhenAll(tasks);

        await using var readDb = NewDbContext();
        var entry = await readDb.GauntletEntries.AsNoTracking()
            .SingleAsync(e => e.GauntletEventId == eventId && e.PlayerId == playerId);
        entry.Score.Should().Be(taskCount * delta, "every concurrent increment must land exactly");
    }

    [Fact]
    public async Task IncrementScore_UnknownEntry_IsNoOp()
    {
        var eventId = await SeedEventAsync();

        await using var db = NewDbContext();
        var repo = new GauntletEntryRepository(db);

        // No entry exists for this player — the UPDATE matches 0 rows and must not throw.
        var act = async () => await repo.IncrementScoreAsync(eventId, Guid.NewGuid(), 100, DateTimeOffset.UtcNow);
        await act.Should().NotThrowAsync();
    }

    // ── RecomputeRanksAsync: per-league snapshot + idempotency ──────────────────

    [Fact]
    public async Task RecomputeRanks_SetsLastRank_ByScoreDescThenTieBreakAtAsc()
    {
        var eventId = await SeedEventAsync();
        var t       = new DateTimeOffset(2026, 6, 6, 0, 0, 0, TimeSpan.Zero);

        // Whelpling: A=5000, B=3000@t+1m, C=3000@t+2m (B and C tie; B reached it first), D=1000.
        var a = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 5000, t);
        var bPlayer = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 3000, t.AddMinutes(1));
        var c = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 3000, t.AddMinutes(2));
        var d = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 1000, t);

        await using var db = NewDbContext();
        var repo = new GauntletEntryRepository(db);
        await repo.RecomputeRanksAsync(eventId);

        async Task<int?> RankOf(Guid p) => (await db.GauntletEntries.AsNoTracking()
            .SingleAsync(e => e.GauntletEventId == eventId && e.PlayerId == p)).LastRank;

        (await RankOf(a)).Should().Be(1, "highest score");
        (await RankOf(bPlayer)).Should().Be(2, "tie with C but earlier tie_break_at wins");
        (await RankOf(c)).Should().Be(3, "tie loser — later tie_break_at");
        (await RankOf(d)).Should().Be(4, "lowest score");
    }

    [Fact]
    public async Task RecomputeRanks_PartitionsByLeague_IndependentRankSequences()
    {
        var eventId = await SeedEventAsync();
        var t       = new DateTimeOffset(2026, 6, 6, 0, 0, 0, TimeSpan.Zero);

        var w1 = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 900, t);
        var w2 = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 100, t);
        var y1 = await SeedEntryAsync(eventId, GauntletLeague.Wyrm, 50, t); // far lower abs score…

        await using var db = NewDbContext();
        var repo = new GauntletEntryRepository(db);
        await repo.RecomputeRanksAsync(eventId);

        async Task<int?> RankOf(Guid p) => (await db.GauntletEntries.AsNoTracking()
            .SingleAsync(e => e.GauntletEventId == eventId && e.PlayerId == p)).LastRank;

        (await RankOf(w1)).Should().Be(1);
        (await RankOf(w2)).Should().Be(2);
        (await RankOf(y1)).Should().Be(1, "ranks are per-league: the lone Wyrm entry is rank 1 in Wyrm");
    }

    [Fact]
    public async Task RecomputeRanks_IsIdempotent()
    {
        var eventId = await SeedEventAsync();
        var t       = new DateTimeOffset(2026, 6, 6, 0, 0, 0, TimeSpan.Zero);

        var a = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 5000, t);
        var bPlayer = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 3000, t.AddMinutes(1));

        await using var db = NewDbContext();
        var repo = new GauntletEntryRepository(db);

        await repo.RecomputeRanksAsync(eventId);
        await repo.RecomputeRanksAsync(eventId); // second pass over unchanged data

        async Task<int?> RankOf(Guid p) => (await db.GauntletEntries.AsNoTracking()
            .SingleAsync(e => e.GauntletEventId == eventId && e.PlayerId == p)).LastRank;

        (await RankOf(a)).Should().Be(1, "re-running yields identical ranks");
        (await RankOf(bPlayer)).Should().Be(2);
    }

    [Fact]
    public async Task RecomputeRanks_ExcludesSoftDeletedEntries()
    {
        var eventId = await SeedEventAsync();
        var t       = new DateTimeOffset(2026, 6, 6, 0, 0, 0, TimeSpan.Zero);

        var top     = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 9000, t);
        var deleted = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 5000, t);

        // Soft-delete the middle entry directly.
        await using (var del = NewDbContext())
        {
            await del.Database.ExecuteSqlRawAsync(
                "UPDATE gauntlet_entries SET is_deleted = true WHERE gauntlet_event_id = {0} AND player_id = {1}",
                eventId, deleted);
        }

        var bottom = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 1000, t);

        await using var db = NewDbContext();
        var repo = new GauntletEntryRepository(db);
        await repo.RecomputeRanksAsync(eventId);

        async Task<int?> RankOf(Guid p) => (await db.GauntletEntries.AsNoTracking()
            .SingleAsync(e => e.GauntletEventId == eventId && e.PlayerId == p)).LastRank;

        (await RankOf(top)).Should().Be(1);
        (await RankOf(bottom)).Should().Be(2, "the soft-deleted entry is skipped, so bottom is rank 2");
    }

    // ── GetLeaderboardPageAsync + GetRankAndScoreAsync ──────────────────────────

    [Fact]
    public async Task GetLeaderboardPage_OrdersByLastRank_AndHydratesDisplayName()
    {
        var eventId = await SeedEventAsync();
        var t       = new DateTimeOffset(2026, 6, 6, 0, 0, 0, TimeSpan.Zero);

        var first  = await SeedEntryAsync(eventId, GauntletLeague.Wyrm, 8000, t, displayName: "Alpha");
        var second = await SeedEntryAsync(eventId, GauntletLeague.Wyrm, 4000, t, displayName: "Bravo");

        await using var db = NewDbContext();
        var repo = new GauntletEntryRepository(db);
        await repo.RecomputeRanksAsync(eventId);

        var page = await repo.GetLeaderboardPageAsync(eventId, GauntletLeague.Wyrm, take: 200);

        page.Should().HaveCount(2);
        page[0].Rank.Should().Be(1);
        page[0].PlayerId.Should().Be(first);
        page[0].DisplayName.Should().Be("Alpha");
        page[0].Score.Should().Be(8000L);
        page[1].Rank.Should().Be(2);
        page[1].PlayerId.Should().Be(second);
        page[1].DisplayName.Should().Be("Bravo");
    }

    [Fact]
    public async Task GetLeaderboardPage_EmptyLeague_ReturnsEmptyList()
    {
        var eventId = await SeedEventAsync();
        // Seed only a Whelpling entry; the Dragon board must be empty.
        await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 1000, DateTimeOffset.UtcNow);

        await using var db = NewDbContext();
        var repo = new GauntletEntryRepository(db);
        await repo.RecomputeRanksAsync(eventId);

        var page = await repo.GetLeaderboardPageAsync(eventId, GauntletLeague.Dragon, take: 200);

        page.Should().BeEmpty();
    }

    [Fact]
    public async Task GetLeaderboardPage_LeagueIsolation_WyrmEntryNeverAppearsInWhelpling()
    {
        var eventId = await SeedEventAsync();
        var t       = new DateTimeOffset(2026, 6, 6, 0, 0, 0, TimeSpan.Zero);

        var whelp = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 1000, t);
        var wyrm  = await SeedEntryAsync(eventId, GauntletLeague.Wyrm, 9999, t); // higher score, other league

        await using var db = NewDbContext();
        var repo = new GauntletEntryRepository(db);
        await repo.RecomputeRanksAsync(eventId);

        var whelpPage = await repo.GetLeaderboardPageAsync(eventId, GauntletLeague.Whelpling, take: 200);

        whelpPage.Should().ContainSingle();
        whelpPage[0].PlayerId.Should().Be(whelp);
        whelpPage.Should().NotContain(r => r.PlayerId == wyrm,
            "a Wyrm entry must never appear on the Whelpling board even with a higher score");
    }

    [Fact]
    public async Task GetLeaderboardPage_ExcludesUnsnapshottedEntries()
    {
        var eventId = await SeedEventAsync();
        var t       = new DateTimeOffset(2026, 6, 6, 0, 0, 0, TimeSpan.Zero);

        var ranked = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 5000, t);
        await repoRank(eventId); // snapshot now → only `ranked` has a last_rank

        // A late joiner with no snapshot yet (last_rank stays null) must not appear on the board.
        var lateJoiner = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 9999, t);

        await using var db = NewDbContext();
        var repo = new GauntletEntryRepository(db);
        var page = await repo.GetLeaderboardPageAsync(eventId, GauntletLeague.Whelpling, take: 200);

        page.Should().ContainSingle();
        page[0].PlayerId.Should().Be(ranked);
        page.Should().NotContain(r => r.PlayerId == lateJoiner,
            "entries without a snapshot rank are excluded until the next snapshot");

        async Task repoRank(Guid id)
        {
            await using var r = NewDbContext();
            await new GauntletEntryRepository(r).RecomputeRanksAsync(id);
        }
    }

    [Fact]
    public async Task GetRankAndScore_ReturnsCallerRank_EvenWhenBelowPageTake()
    {
        // Seed 5 entries, snapshot, then read a page with take=2. The 5th-ranked caller is OFF the
        // page but GetRankAndScoreAsync still returns their true rank — mirrors the "caller outside
        // top-200" requirement at a smaller scale.
        var eventId = await SeedEventAsync();
        var t       = new DateTimeOffset(2026, 6, 6, 0, 0, 0, TimeSpan.Zero);

        var scores = new (Guid id, long score)[5];
        scores[0] = (await SeedEntryAsync(eventId, GauntletLeague.Wyrm, 5000, t), 5000);
        scores[1] = (await SeedEntryAsync(eventId, GauntletLeague.Wyrm, 4000, t), 4000);
        scores[2] = (await SeedEntryAsync(eventId, GauntletLeague.Wyrm, 3000, t), 3000);
        scores[3] = (await SeedEntryAsync(eventId, GauntletLeague.Wyrm, 2000, t), 2000);
        var caller = await SeedEntryAsync(eventId, GauntletLeague.Wyrm, 1000, t); // rank 5

        await using var db = NewDbContext();
        var repo = new GauntletEntryRepository(db);
        await repo.RecomputeRanksAsync(eventId);

        var page = await repo.GetLeaderboardPageAsync(eventId, GauntletLeague.Wyrm, take: 2);
        page.Should().HaveCount(2, "the page is capped at the take");
        page.Should().NotContain(r => r.PlayerId == caller, "the caller is rank 5, below the take of 2");

        var mine = await repo.GetRankAndScoreAsync(eventId, caller, GauntletLeague.Wyrm);
        mine.Should().NotBeNull();
        mine!.Rank.Should().Be(5, "the caller's true rank is returned regardless of page position");
        mine.Score.Should().Be(1000L);
    }

    [Fact]
    public async Task GetRankAndScore_WrongLeague_ReturnsNull()
    {
        // The caller's locked entry is in Whelpling; asking for their standing on the Dragon board
        // must return null (they don't belong to that league's board).
        var eventId = await SeedEventAsync();
        var caller  = await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 1000, DateTimeOffset.UtcNow);

        await using var db = NewDbContext();
        var repo = new GauntletEntryRepository(db);
        await repo.RecomputeRanksAsync(eventId);

        var sameLeague  = await repo.GetRankAndScoreAsync(eventId, caller, GauntletLeague.Whelpling);
        var otherLeague = await repo.GetRankAndScoreAsync(eventId, caller, GauntletLeague.Dragon);

        sameLeague.Should().NotBeNull();
        sameLeague!.Rank.Should().Be(1);
        otherLeague.Should().BeNull("the caller has no entry in the Dragon league");
    }

    [Fact]
    public async Task GetRankAndScore_NoEntry_ReturnsNull()
    {
        var eventId = await SeedEventAsync();
        await using var db = NewDbContext();
        var repo = new GauntletEntryRepository(db);

        var mine = await repo.GetRankAndScoreAsync(eventId, Guid.NewGuid());

        mine.Should().BeNull();
    }

    [Fact]
    public async Task CountRanked_CountsOnlySnapshottedEntriesInLeague()
    {
        var eventId = await SeedEventAsync();
        var t       = new DateTimeOffset(2026, 6, 6, 0, 0, 0, TimeSpan.Zero);

        await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 5000, t);
        await SeedEntryAsync(eventId, GauntletLeague.Whelpling, 3000, t);
        await SeedEntryAsync(eventId, GauntletLeague.Wyrm, 1000, t); // different league

        await using var db = NewDbContext();
        var repo = new GauntletEntryRepository(db);
        await repo.RecomputeRanksAsync(eventId);

        (await repo.CountRankedAsync(eventId, GauntletLeague.Whelpling)).Should().Be(2);
        (await repo.CountRankedAsync(eventId, GauntletLeague.Wyrm)).Should().Be(1);
        (await repo.CountRankedAsync(eventId, GauntletLeague.Dragon)).Should().Be(0);
    }
}
