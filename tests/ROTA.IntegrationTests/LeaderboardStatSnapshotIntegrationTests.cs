using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using ROTA.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ROTA.IntegrationTests;

// BETA — integration tests for the Slice-5 Stat board snapshot.
// These use real PostgreSQL (Testcontainers) because:
//   - SetValueAsync idempotency (ON CONFLICT overwrite, exact one row) lives in SQL.
//   - last_progress_at preservation (only bumped when value changes) is a SQL CASE expression.
//   - The GetEligibleStatSnapshotAsync eligibility join is DB behaviour.
//   - The end-to-end read via GetEligiblePageAsync verifies the snapshot feeds the read path.
//
// Properties under test:
//   StatAttack  → PlayerStats.BaseAttack
//   StatDefense → PlayerStats.BaseDefense
//   StatDisc    → PlayerStats.DiscernmentInvestment
//
// Eligibility predicate (same as Slice 3):
//   p.is_deleted=false AND p.is_banned=false AND p.level>=20 AND (roles & 4)=0 when excludeAdmins
public class LeaderboardStatSnapshotIntegrationTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("rota_lb_stat_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();

        await _postgres.StartAsync();

        await using var db = NewDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    // ── Helpers ───────────────────────────────────────────────────────────────

    private RotaDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<RotaDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new RotaDbContext(options);
    }

    private async Task<LeaderboardEntryRepository> OpenRepoAsync()
    {
        var db = NewDbContext();
        await db.Database.OpenConnectionAsync();
        return new LeaderboardEntryRepository(db);
    }

    private LeaderboardService BuildService(LeaderboardEntryRepository repo)
    {
        var players  = new Mock<IPlayerRepository>();
        var resolver = new Mock<IPeriodKeyResolver>();
        resolver.Setup(r => r.Resolve(It.IsAny<DateTimeOffset>(), LeaderboardPeriod.Live))
            .Returns("live");

        var cfg = Options.Create(new LeaderboardConfig
        {
            PageSize      = 200,
            MinLevel      = 20,
            ExcludeAdmins = true,
        });

        return new LeaderboardService(repo, players.Object, resolver.Object, cfg);
    }

    // Seeds a player + player_stats row with the given raw stat values and eligibility attributes.
    // Returns the player Id.
    private async Task<Guid> SeedPlayerWithStatsAsync(
        int baseAttack   = 10,
        int baseDefense  = 10,
        int discernment  = 0,
        int level        = 25,
        bool isBanned    = false,
        bool isDeleted   = false,
        PlayerRoles roles = PlayerRoles.Player)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var player = Player.Create($"stat_{suffix}", $"{suffix}@stat.test", "hash");

        await using var db = NewDbContext();
        db.Players.Add(player);
        await db.SaveChangesAsync();

        // Set level, ban, delete, roles via raw SQL to avoid needing domain method coverage.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE players SET level = {0}, is_banned = {1}, is_deleted = {2}, roles = {3} WHERE id = {4}",
            level, isBanned, isDeleted, (int)roles, player.Id);

        // Set raw stat values directly.
        await db.Database.ExecuteSqlRawAsync(
            "UPDATE player_stats SET base_attack = {0}, base_defense = {1}, discernment_investment = {2} WHERE player_id = {3}",
            baseAttack, baseDefense, discernment, player.Id);

        return player.Id;
    }

    // ── Test 1: snapshot ranks players by raw ATK on StatAttack/Live read ─────

    [Fact]
    public async Task Snapshot_StatAttack_OrdersPlayersByBaseAttack()
    {
        var highAtk  = await SeedPlayerWithStatsAsync(baseAttack: 100, level: 25);
        var midAtk   = await SeedPlayerWithStatsAsync(baseAttack: 60,  level: 25);
        var lowAtk   = await SeedPlayerWithStatsAsync(baseAttack: 20,  level: 25);

        var repo    = await OpenRepoAsync();
        var service = BuildService(repo);

        var count = await service.SnapshotStatBoardAsync();
        count.Should().BeGreaterThanOrEqualTo(3, "at least the 3 seeded players were eligible");

        // Read via the eligibility-aware page — same path the API uses.
        var page = await repo.GetEligiblePageAsync(
            LeaderboardBoard.StatAttack, "live", 1, 200, minLevel: 20, excludeAdmins: true);

        var relevant = page.Where(e => new[] { highAtk, midAtk, lowAtk }.Contains(e.PlayerId))
                           .ToList();

        relevant.Should().HaveCount(3);
        relevant[0].PlayerId.Should().Be(highAtk, "highest BaseAttack ranks first");
        relevant[1].PlayerId.Should().Be(midAtk);
        relevant[2].PlayerId.Should().Be(lowAtk,  "lowest BaseAttack ranks last");

        relevant[0].Value.Should().Be(100L);
        relevant[1].Value.Should().Be(60L);
        relevant[2].Value.Should().Be(20L);
    }

    // ── Test 2: snapshot ranks by DEF and Discernment correctly ──────────────

    [Fact]
    public async Task Snapshot_StatDefenseAndDiscernment_OrderCorrectly()
    {
        var highDef  = await SeedPlayerWithStatsAsync(baseDefense: 90, discernment: 5,  level: 25);
        var lowDef   = await SeedPlayerWithStatsAsync(baseDefense: 30, discernment: 50, level: 25);

        var repo    = await OpenRepoAsync();
        var service = BuildService(repo);

        await service.SnapshotStatBoardAsync();

        var defPage  = await repo.GetEligiblePageAsync(
            LeaderboardBoard.StatDefense, "live", 1, 200, 20, true);
        var discPage = await repo.GetEligiblePageAsync(
            LeaderboardBoard.StatDiscernment, "live", 1, 200, 20, true);

        var defRelevant = defPage
            .Where(e => new[] { highDef, lowDef }.Contains(e.PlayerId)).ToList();
        defRelevant[0].PlayerId.Should().Be(highDef, "higher BaseDefense ranks first");
        defRelevant[1].PlayerId.Should().Be(lowDef);

        var discRelevant = discPage
            .Where(e => new[] { highDef, lowDef }.Contains(e.PlayerId)).ToList();
        discRelevant[0].PlayerId.Should().Be(lowDef,  "higher DiscernmentInvestment ranks first for Disc board");
        discRelevant[1].PlayerId.Should().Be(highDef);
    }

    // ── Test 3: banned / soft-deleted / level<20 / Admin excluded ─────────────

    [Fact]
    public async Task Snapshot_IneligiblePlayers_ExcludedFromStatPage()
    {
        var eligible  = await SeedPlayerWithStatsAsync(baseAttack: 50, level: 25);
        var banned    = await SeedPlayerWithStatsAsync(baseAttack: 99, level: 25, isBanned: true);
        var deleted   = await SeedPlayerWithStatsAsync(baseAttack: 99, level: 25, isDeleted: true);
        var lowLevel  = await SeedPlayerWithStatsAsync(baseAttack: 99, level: 5);
        var adminRole = await SeedPlayerWithStatsAsync(baseAttack: 99, level: 25,
                            roles: PlayerRoles.Player | PlayerRoles.Admin);

        var repo    = await OpenRepoAsync();
        var service = BuildService(repo);

        await service.SnapshotStatBoardAsync();

        var page = await repo.GetEligiblePageAsync(
            LeaderboardBoard.StatAttack, "live", 1, 200, 20, true);

        var ids = page.Select(e => e.PlayerId).ToList();
        ids.Should().Contain(eligible);
        ids.Should().NotContain(banned,    "banned players excluded");
        ids.Should().NotContain(deleted,   "soft-deleted players excluded");
        ids.Should().NotContain(lowLevel,  "below-MinLevel players excluded");
        ids.Should().NotContain(adminRole, "Admin players excluded");
    }

    // ── Test 4: Moderator IS included ────────────────────────────────────────

    [Fact]
    public async Task Snapshot_Moderator_IsIncludedInStatPage()
    {
        var modId = await SeedPlayerWithStatsAsync(
            baseAttack: 55, level: 25,
            roles: PlayerRoles.Player | PlayerRoles.Moderator);

        var repo    = await OpenRepoAsync();
        var service = BuildService(repo);

        await service.SnapshotStatBoardAsync();

        var page = await repo.GetEligiblePageAsync(
            LeaderboardBoard.StatAttack, "live", 1, 200, 20, true);

        page.Select(e => e.PlayerId).Should().Contain(modId,
            "Moderators appear as regular players on the leaderboard");
    }

    // ── Test 5: idempotent — two snapshots → exactly one row per player per board

    [Fact]
    public async Task Snapshot_RunTwice_ExactlyOneRowPerPlayerPerBoard()
    {
        var playerId = await SeedPlayerWithStatsAsync(baseAttack: 40, level: 25);

        var repo    = await OpenRepoAsync();
        var service = BuildService(repo);

        // Run snapshot twice.
        await service.SnapshotStatBoardAsync();
        await service.SnapshotStatBoardAsync();

        // Verify exactly one row per board for this player in the live period.
        await using var db = NewDbContext();

        var atkRows = await db.LeaderboardEntries
            .AsNoTracking()
            .Where(e => e.PlayerId == playerId
                     && e.Board == LeaderboardBoard.StatAttack
                     && e.PeriodKey == "live")
            .ToListAsync();
        atkRows.Should().HaveCount(1, "idempotent — no duplicate rows on repeated snapshot");

        var defRows = await db.LeaderboardEntries
            .AsNoTracking()
            .Where(e => e.PlayerId == playerId
                     && e.Board == LeaderboardBoard.StatDefense
                     && e.PeriodKey == "live")
            .ToListAsync();
        defRows.Should().HaveCount(1);

        var discRows = await db.LeaderboardEntries
            .AsNoTracking()
            .Where(e => e.PlayerId == playerId
                     && e.Board == LeaderboardBoard.StatDiscernment
                     && e.PeriodKey == "live")
            .ToListAsync();
        discRows.Should().HaveCount(1);
    }

    // ── Test 6: value update — re-snapshot after stat change → Live row updated ─

    [Fact]
    public async Task Snapshot_AfterStatChange_UpdatesLiveRowValue()
    {
        var playerId = await SeedPlayerWithStatsAsync(baseAttack: 30, level: 25);

        var repo    = await OpenRepoAsync();
        var service = BuildService(repo);

        // First snapshot.
        await service.SnapshotStatBoardAsync();

        await using (var db = NewDbContext())
        {
            var row = await db.LeaderboardEntries
                .AsNoTracking()
                .SingleAsync(e => e.PlayerId == playerId
                              && e.Board == LeaderboardBoard.StatAttack
                              && e.PeriodKey == "live");
            row.Value.Should().Be(30L, "first snapshot writes BaseAttack = 30");
        }

        // Simulate the player investing more SkillPoints into Attack.
        await using (var db2 = NewDbContext())
        {
            await db2.Database.ExecuteSqlRawAsync(
                "UPDATE player_stats SET base_attack = 55 WHERE player_id = {0}", playerId);
        }

        // Second snapshot.
        await service.SnapshotStatBoardAsync();

        await using (var db3 = NewDbContext())
        {
            var row = await db3.LeaderboardEntries
                .AsNoTracking()
                .SingleAsync(e => e.PlayerId == playerId
                              && e.Board == LeaderboardBoard.StatAttack
                              && e.PeriodKey == "live");
            row.Value.Should().Be(55L, "second snapshot overwrites value with new BaseAttack = 55");
        }
    }

    // ── Test 7: last_progress_at preserved when value unchanged across two snapshots ─

    [Fact]
    public async Task Snapshot_ValueUnchanged_LastProgressAtNotBumped()
    {
        var playerId = await SeedPlayerWithStatsAsync(baseAttack: 42, level: 25);

        var repo    = await OpenRepoAsync();
        var service = BuildService(repo);

        // First snapshot.
        await service.SnapshotStatBoardAsync();

        DateTimeOffset firstLastProgressAt;
        await using (var db = NewDbContext())
        {
            var row = await db.LeaderboardEntries
                .AsNoTracking()
                .SingleAsync(e => e.PlayerId == playerId
                              && e.Board == LeaderboardBoard.StatAttack
                              && e.PeriodKey == "live");
            firstLastProgressAt = row.LastProgressAt;
        }

        // Wait a tiny bit so the clock can advance, then run a second snapshot
        // WITHOUT changing the stat — value stays at 42.
        await Task.Delay(10);
        await service.SnapshotStatBoardAsync();

        await using (var db2 = NewDbContext())
        {
            var row = await db2.LeaderboardEntries
                .AsNoTracking()
                .SingleAsync(e => e.PlayerId == playerId
                              && e.Board == LeaderboardBoard.StatAttack
                              && e.PeriodKey == "live");

            row.Value.Should().Be(42L, "value unchanged — still 42");
            row.LastProgressAt.Should().Be(firstLastProgressAt,
                "last_progress_at must NOT advance when the value did not change — earliest-to-reach tiebreak must survive repeated snapshots");
        }
    }

    // ── Test 8: last_progress_at bumped when value DOES change ────────────────

    [Fact]
    public async Task Snapshot_ValueChanged_LastProgressAtBumped()
    {
        var playerId = await SeedPlayerWithStatsAsync(baseAttack: 10, level: 25);

        var repo    = await OpenRepoAsync();
        var service = BuildService(repo);

        await service.SnapshotStatBoardAsync();

        DateTimeOffset firstLastProgressAt;
        await using (var db = NewDbContext())
        {
            var row = await db.LeaderboardEntries
                .AsNoTracking()
                .SingleAsync(e => e.PlayerId == playerId
                              && e.Board == LeaderboardBoard.StatAttack
                              && e.PeriodKey == "live");
            firstLastProgressAt = row.LastProgressAt;
        }

        // Change the stat value.
        await using (var db2 = NewDbContext())
        {
            await db2.Database.ExecuteSqlRawAsync(
                "UPDATE player_stats SET base_attack = 99 WHERE player_id = {0}", playerId);
        }

        // Small delay to ensure the clock advances before the second snapshot.
        await Task.Delay(20);
        await service.SnapshotStatBoardAsync();

        await using (var db3 = NewDbContext())
        {
            var row = await db3.LeaderboardEntries
                .AsNoTracking()
                .SingleAsync(e => e.PlayerId == playerId
                              && e.Board == LeaderboardBoard.StatAttack
                              && e.PeriodKey == "live");

            row.Value.Should().Be(99L, "value updated to 99");
            row.LastProgressAt.Should().BeAfter(firstLastProgressAt,
                "last_progress_at must advance when the value changes");
        }
    }
}
