using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Services;

namespace ROTA.UnitTests.Services;

public class MasteryServiceTests
{
    // Use the REAL shipped definition provider so magnitudes match production content.
    private static readonly IMasteryDefinitionProvider Defs = new MasteryDefinitionProvider(FindApiContentRoot());

    private static (MasteryService svc,
                    Mock<IPlayerMasteryRepository> masteryRepo,
                    Mock<IPlayerMasteryActivityRepository> activityRepo,
                    Mock<IPlayerRepository> players,
                    Mock<ILeaderboardEntryRepository> leaderboard)
        Build(MasteryConfig? config = null)
    {
        var masteryRepo  = new Mock<IPlayerMasteryRepository>();
        var activityRepo = new Mock<IPlayerMasteryActivityRepository>();
        var players      = new Mock<IPlayerRepository>();
        var leaderboard  = new Mock<ILeaderboardEntryRepository>();

        activityRepo.Setup(a => a.GetForPlayerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerMasteryActivity>());

        // Slice 3 deps — defaults: no respec rows, cap not used, gems unused here.
        var respec = new Mock<IMasteryRespecRepository>();
        respec.Setup(r => r.ReferenceExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        var gems = new Mock<IGemService>();
        var auditLog = new Mock<IAuditLogRepository>();
        var capStore = new Mock<IMasteryRespecCapStore>();

        var svc = new MasteryService(
            Defs, masteryRepo.Object, activityRepo.Object, players.Object, leaderboard.Object,
            respec.Object, gems.Object, capStore.Object, auditLog.Object,
            Options.Create(config ?? new MasteryConfig()));
        return (svc, masteryRepo, activityRepo, players, leaderboard);
    }

    private static PlayerMastery Mastery(Guid playerId, MasteryAncient ancient, int level)
    {
        var m = PlayerMastery.Create(playerId, ancient);
        if (level > 1) m.SetLevel(level);
        return m;
    }

    private static List<PlayerMastery> Rows(Guid p, int wrath, int bulwark, int hoard, int disc) => new()
    {
        Mastery(p, MasteryAncient.Wrath, wrath),
        Mastery(p, MasteryAncient.Bulwark, bulwark),
        Mastery(p, MasteryAncient.Hoard, hoard),
        Mastery(p, MasteryAncient.Discernment, disc),
    };

    private static Dictionary<MasteryAncient, int> Levels(int wrath, int bulwark, int hoard, int disc) => new()
    {
        [MasteryAncient.Wrath] = wrath,
        [MasteryAncient.Bulwark] = bulwark,
        [MasteryAncient.Hoard] = hoard,
        [MasteryAncient.Discernment] = disc,
    };

    private static List<PlayerMasteryActivity> Activities(Guid p, params (MasteryActivityType type, long count)[] items)
        => items.Select(i => PlayerMasteryActivity.Create(p, i.type, i.count)).ToList();

    // ── ComputeRating — Formula B worked examples ─────────────────────────────

    [Theory]
    [InlineData(5, 1, 1, 1, 10)]  // specialist
    [InlineData(3, 3, 3, 2, 14)]  // generalist
    [InlineData(1, 1, 1, 1, 4)]   // floor
    [InlineData(2, 2, 2, 2, 11)]  // all ≥2
    [InlineData(5, 5, 5, 5, 56)]  // jackpot
    public void ComputeRating_MatchesLockedSpec(int w, int b, int h, int d, int expected)
    {
        var (svc, _, _, _, _) = Build();
        svc.ComputeRating(Levels(w, b, h, d)).Should().Be(expected);
    }

    [Fact]
    public void ComputeRating_MissingAncients_DefaultToLevel1()
    {
        var (svc, _, _, _, _) = Build();
        svc.ComputeRating(new Dictionary<MasteryAncient, int>()).Should().Be(4); // all default to L1
    }

    [Fact]
    public void ComputeRating_WeakestPillarFloor_AddsTwiceMin_WhenEnabled()
    {
        var (svc, _, _, _, _) = Build(new MasteryConfig { IncludeWeakestPillarFloor = true });
        // (3,3,3,2): base 14 + 2×min(2) = 18.
        svc.ComputeRating(Levels(3, 3, 3, 2)).Should().Be(18);
    }

    // ── GetMasteriesAsync ─────────────────────────────────────────────────────

    [Fact]
    public async Task GetMasteriesAsync_HydratesLevelsRatingTitlesAndPledge()
    {
        var (svc, masteryRepo, _, players, _) = Build();
        var pid = Guid.NewGuid();

        masteryRepo.Setup(m => m.EnsureAllAsync(pid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows(pid, wrath: 5, bulwark: 5, hoard: 5, disc: 5));
        var player = Player.CreateWithId(pid, "u", "u@x.io", "h");
        player.SetPledge(MasteryAncient.Wrath);
        players.Setup(p => p.FindByIdAsync(pid, It.IsAny<CancellationToken>())).ReturnsAsync(player);

        var overview = await svc.GetMasteriesAsync(pid);

        overview.Ancients.Should().HaveCount(4);
        overview.ActivePledge.Should().Be("Wrath");
        overview.Rating.Active.Should().Be(56);
        overview.Rating.Lifetime.Should().Be(56);
        overview.Titles.Breadth.Should().Be("Ascendant of the Ancients");
        overview.Titles.Masteries.Should().HaveCount(4);

        var wrath = overview.Ancients.Single(a => a.Ancient == "Wrath");
        wrath.IsPledged.Should().BeTrue();
        wrath.GlobalPercent.Should().BeApproximately(2.5, 0.0001);
        wrath.EffectivePercent.Should().BeApproximately(5.0, 0.0001); // pledged ×2
        wrath.NextTier.Should().BeNull(); // at max level

        var hoard = overview.Ancients.Single(a => a.Ancient == "Hoard");
        hoard.IsPledged.Should().BeFalse();
        hoard.EffectivePercent.Should().BeApproximately(4.0, 0.0001); // unpledged = global
    }

    [Fact]
    public async Task GetMasteriesAsync_LazyEnsuresRows_AndShowsNextTierProgressAtLowLevel()
    {
        var (svc, masteryRepo, _, players, _) = Build();
        var pid = Guid.NewGuid();
        masteryRepo.Setup(m => m.EnsureAllAsync(pid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows(pid, 1, 1, 1, 1));
        players.Setup(p => p.FindByIdAsync(pid, It.IsAny<CancellationToken>())).ReturnsAsync((Player?)null);

        var overview = await svc.GetMasteriesAsync(pid);

        masteryRepo.Verify(m => m.EnsureAllAsync(pid, It.IsAny<CancellationToken>()), Times.Once);
        overview.ActivePledge.Should().BeNull();
        overview.Rating.Active.Should().Be(4);
        overview.Titles.Breadth.Should().BeNull();

        var wrath = overview.Ancients.Single(a => a.Ancient == "Wrath");
        wrath.Level.Should().Be(1);
        wrath.NextTier.Should().NotBeNull();
        wrath.NextTier!.FromLevel.Should().Be(1);
        wrath.NextTier.ToLevel.Should().Be(2);
        wrath.NextTier.Complete.Should().BeFalse(); // counters all 0
        wrath.NextTier.Items.Should().Contain(i => i.ActivityType == "RaidHit" && i.Threshold == 100 && i.Current == 0);
    }

    // ── Combat / loot modifiers ───────────────────────────────────────────────

    [Fact]
    public async Task GetCombatModifiersAsync_PledgedWrathDoubles_BulwarkClamped()
    {
        var (svc, masteryRepo, _, players, _) = Build();
        var pid = Guid.NewGuid();
        masteryRepo.Setup(m => m.GetForPlayerAsync(pid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows(pid, wrath: 5, bulwark: 5, hoard: 1, disc: 1));
        var player = Player.CreateWithId(pid, "u", "u@x.io", "h");
        player.SetPledge(MasteryAncient.Bulwark);
        players.Setup(p => p.FindByIdAsync(pid, It.IsAny<CancellationToken>())).ReturnsAsync(player);

        var mods = await svc.GetCombatModifiersAsync(pid);

        mods.WrathLegionPercent.Should().BeApproximately(2.5, 0.0001);          // not pledged → global
        mods.BulwarkGuildDamageFraction.Should().BeApproximately(0.01, 0.0001);  // pledged 0.5×2=1.0% → 0.01, at cap
    }

    [Fact]
    public async Task GetLootModifiersAsync_HoardAndDiscernmentScale()
    {
        var (svc, masteryRepo, _, players, _) = Build();
        var pid = Guid.NewGuid();
        masteryRepo.Setup(m => m.GetForPlayerAsync(pid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(Rows(pid, 1, 1, hoard: 5, disc: 5));
        var player = Player.CreateWithId(pid, "u", "u@x.io", "h");
        player.SetPledge(MasteryAncient.Hoard);
        players.Setup(p => p.FindByIdAsync(pid, It.IsAny<CancellationToken>())).ReturnsAsync(player);

        var mods = await svc.GetLootModifiersAsync(pid);

        mods.HoardDropMultiplier.Should().BeApproximately(1.08, 0.0001);          // pledged 4×2=8% → ×1.08
        mods.HoardGoldMultiplier.Should().BeApproximately(1.08, 0.0001);
        mods.DiscernmentSigilFindMultiplier.Should().BeApproximately(1.04, 0.0001); // unpledged 4% → ×1.04
        mods.DiscernmentQualityChance.Should().BeApproximately(0.04, 0.0001);
    }

    [Fact]
    public async Task GetLootModifiersAsync_NoMasteries_AreNeutral()
    {
        var (svc, masteryRepo, _, players, _) = Build();
        var pid = Guid.NewGuid();
        masteryRepo.Setup(m => m.GetForPlayerAsync(pid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerMastery>()); // no rows → all L1
        players.Setup(p => p.FindByIdAsync(pid, It.IsAny<CancellationToken>())).ReturnsAsync((Player?)null);

        var mods = await svc.GetLootModifiersAsync(pid);

        // L1 globals are small but non-zero (Hoard/Disc L1 = 0.8%).
        mods.HoardDropMultiplier.Should().BeApproximately(1.008, 0.0001);
        mods.DiscernmentSigilFindMultiplier.Should().BeApproximately(1.008, 0.0001);
    }

    // ── Rating-board snapshot ─────────────────────────────────────────────────

    [Fact]
    public async Task SnapshotRatingBoardAsync_WritesActiveAndLifetimePerPlayer()
    {
        var (svc, masteryRepo, _, _, leaderboard) = Build();
        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        masteryRepo.Setup(m => m.GetAllRatingsAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerMasteryRatingRow>
            {
                new(p1, Levels(5, 5, 5, 5)),
                new(p2, Levels(2, 1, 1, 1)),
            });

        var count = await svc.SnapshotRatingBoardAsync();

        count.Should().Be(2);
        leaderboard.Verify(l => l.SetValueAsync(p1, LeaderboardBoard.MasteryRatingActive,
            LeaderboardPeriod.Live, "live", 56, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        leaderboard.Verify(l => l.SetValueAsync(p1, LeaderboardBoard.MasteryRatingLifetime,
            LeaderboardPeriod.Live, "live", 56, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
        leaderboard.Verify(l => l.SetValueAsync(p2, LeaderboardBoard.MasteryRatingActive,
            LeaderboardPeriod.Live, "live", 5, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Activity recording (Slice 4) ──────────────────────────────────────────

    [Fact]
    public async Task RecordActivity_NoReference_Increments()
    {
        var (svc, _, activityRepo, _, _) = Build();
        var pid = Guid.NewGuid();

        await svc.RecordActivityAsync(pid, MasteryActivityType.RaidHit, 5);

        activityRepo.Verify(a => a.IncrementAsync(pid, MasteryActivityType.RaidHit, 5, It.IsAny<CancellationToken>()), Times.Once);
        activityRepo.Verify(a => a.RecordEventAsync(It.IsAny<Guid>(), It.IsAny<MasteryActivityType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordActivity_WithReference_NotSeen_RecordsEventThenIncrements()
    {
        var (svc, _, activityRepo, _, _) = Build();
        var pid = Guid.NewGuid();
        activityRepo.Setup(a => a.HasEventAsync(pid, MasteryActivityType.RaidKill, "k1", It.IsAny<CancellationToken>())).ReturnsAsync(false);

        await svc.RecordActivityAsync(pid, MasteryActivityType.RaidKill, 1, "k1");

        activityRepo.Verify(a => a.RecordEventAsync(pid, MasteryActivityType.RaidKill, "k1", It.IsAny<CancellationToken>()), Times.Once);
        activityRepo.Verify(a => a.IncrementAsync(pid, MasteryActivityType.RaidKill, 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordActivity_WithReference_AlreadySeen_SkipsBoth()
    {
        var (svc, _, activityRepo, _, _) = Build();
        var pid = Guid.NewGuid();
        activityRepo.Setup(a => a.HasEventAsync(pid, MasteryActivityType.RaidKill, "k1", It.IsAny<CancellationToken>())).ReturnsAsync(true);

        await svc.RecordActivityAsync(pid, MasteryActivityType.RaidKill, 1, "k1");

        activityRepo.Verify(a => a.RecordEventAsync(It.IsAny<Guid>(), It.IsAny<MasteryActivityType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        activityRepo.Verify(a => a.IncrementAsync(It.IsAny<Guid>(), It.IsAny<MasteryActivityType>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordActivity_NonPositiveAmount_NoOp()
    {
        var (svc, _, activityRepo, _, _) = Build();
        await svc.RecordActivityAsync(Guid.NewGuid(), MasteryActivityType.RaidHit, 0);
        activityRepo.Verify(a => a.IncrementAsync(It.IsAny<Guid>(), It.IsAny<MasteryActivityType>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Tier-up leveling (Slice 4) ────────────────────────────────────────────

    private void SetupTierUp(Mock<IPlayerMasteryRepository> masteryRepo, Mock<IPlayerMasteryActivityRepository> activityRepo,
        Guid pid, List<PlayerMastery> rows, List<PlayerMasteryActivity> activities)
    {
        masteryRepo.Setup(m => m.EnsureAllAsync(pid, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        activityRepo.Setup(a => a.GetForPlayerAsync(pid, It.IsAny<CancellationToken>())).ReturnsAsync(activities);
    }

    [Fact]
    public async Task EvaluateTierUps_AdvancesOneTier_WhenChecklistMet()
    {
        var (svc, masteryRepo, activityRepo, _, _) = Build();
        var pid = Guid.NewGuid();
        var rows = Rows(pid, 1, 1, 1, 1);
        // Wrath T1->2 = RaidHit 100; T2->3 = RaidDamageDealt 5M (not met) → stops at L2.
        SetupTierUp(masteryRepo, activityRepo, pid, rows, Activities(pid, (MasteryActivityType.RaidHit, 100)));

        await svc.EvaluateTierUpsAsync(pid);

        rows.Single(r => r.Ancient == MasteryAncient.Wrath).Level.Should().Be(2);
        masteryRepo.Verify(m => m.UpsertAsync(It.Is<PlayerMastery>(p => p.Ancient == MasteryAncient.Wrath), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task EvaluateTierUps_DoesNotAdvance_WhenChecklistUnmet()
    {
        var (svc, masteryRepo, activityRepo, _, _) = Build();
        var pid = Guid.NewGuid();
        var rows = Rows(pid, 1, 1, 1, 1);
        SetupTierUp(masteryRepo, activityRepo, pid, rows, Activities(pid, (MasteryActivityType.RaidHit, 99)));

        await svc.EvaluateTierUpsAsync(pid);

        rows.Single(r => r.Ancient == MasteryAncient.Wrath).Level.Should().Be(1);
        masteryRepo.Verify(m => m.UpsertAsync(It.IsAny<PlayerMastery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task EvaluateTierUps_JumpsMultipleTiers_FromCumulativeCounters()
    {
        var (svc, masteryRepo, activityRepo, _, _) = Build();
        var pid = Guid.NewGuid();
        var rows = Rows(pid, 1, 1, 1, 1);
        // Wrath: T1 RaidHit 100 ✓, T2 RaidDamageDealt 5M ✓, T3 RaidKill 25 + RaidDamageDealt 50M ✓ → L4.
        // T4 needs RaidKill 100 (have 25) → stops at L4.
        SetupTierUp(masteryRepo, activityRepo, pid, rows, Activities(pid,
            (MasteryActivityType.RaidHit, 100),
            (MasteryActivityType.RaidDamageDealt, 50_000_000),
            (MasteryActivityType.RaidKill, 25)));

        await svc.EvaluateTierUpsAsync(pid);

        rows.Single(r => r.Ancient == MasteryAncient.Wrath).Level.Should().Be(4);
    }

    [Fact]
    public async Task EvaluateTierUps_FinalTier_BlockedUntilAllCrossSystemCountersMet()
    {
        var (svc, masteryRepo, activityRepo, _, _) = Build();
        var pid = Guid.NewGuid();
        // Wrath at L4; T4->5 = RaidKill 100 + GauntletRankEarned 3 + GuildRaidContribution 20M.
        var rows = Rows(pid, wrath: 4, bulwark: 1, hoard: 1, disc: 1);
        // Missing GauntletRankEarned → stays at L4.
        SetupTierUp(masteryRepo, activityRepo, pid, rows, Activities(pid,
            (MasteryActivityType.RaidKill, 100),
            (MasteryActivityType.GuildRaidContribution, 20_000_000)));

        await svc.EvaluateTierUpsAsync(pid);
        rows.Single(r => r.Ancient == MasteryAncient.Wrath).Level.Should().Be(4);
    }

    [Fact]
    public async Task EvaluateTierUps_FinalTier_AdvancesToFive_WhenAllMet()
    {
        var (svc, masteryRepo, activityRepo, _, _) = Build();
        var pid = Guid.NewGuid();
        var rows = Rows(pid, wrath: 4, bulwark: 1, hoard: 1, disc: 1);
        SetupTierUp(masteryRepo, activityRepo, pid, rows, Activities(pid,
            (MasteryActivityType.RaidKill, 100),
            (MasteryActivityType.GauntletRankEarned, 3),
            (MasteryActivityType.GuildRaidContribution, 20_000_000)));

        await svc.EvaluateTierUpsAsync(pid);
        rows.Single(r => r.Ancient == MasteryAncient.Wrath).Level.Should().Be(5);
    }

    [Fact]
    public async Task EvaluateTierUps_AtMaxLevel_NoChangeNoWrite()
    {
        var (svc, masteryRepo, activityRepo, _, _) = Build();
        var pid = Guid.NewGuid();
        var rows = Rows(pid, 5, 5, 5, 5);
        SetupTierUp(masteryRepo, activityRepo, pid, rows, Activities(pid,
            (MasteryActivityType.RaidHit, 999_999),
            (MasteryActivityType.GuildRaidContribution, 999_999_999)));

        await svc.EvaluateTierUpsAsync(pid);

        rows.All(r => r.Level == 5).Should().BeTrue();
        masteryRepo.Verify(m => m.UpsertAsync(It.IsAny<PlayerMastery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ForceLevels_SetsLevelsUpAndDown_LeavesOthersUntouched()
    {
        var (svc, masteryRepo, _, players, _) = Build();
        var pid = Guid.NewGuid();
        var rows = Rows(pid, 3, 3, 3, 3);
        masteryRepo.Setup(m => m.EnsureAllAsync(pid, It.IsAny<CancellationToken>())).ReturnsAsync(rows);
        players.Setup(p => p.FindByIdAsync(pid, It.IsAny<CancellationToken>())).ReturnsAsync((Player?)null);

        var overview = await svc.ForceLevelsAsync(pid, new Dictionary<MasteryAncient, int>
        {
            [MasteryAncient.Wrath] = 5,  // up
            [MasteryAncient.Hoard] = 1,  // down (bypasses monotonic guard)
        });

        rows.Single(r => r.Ancient == MasteryAncient.Wrath).Level.Should().Be(5);
        rows.Single(r => r.Ancient == MasteryAncient.Hoard).Level.Should().Be(1);
        rows.Single(r => r.Ancient == MasteryAncient.Bulwark).Level.Should().Be(3, "untouched");
        masteryRepo.Verify(m => m.UpsertAsync(It.IsAny<PlayerMastery>(), It.IsAny<CancellationToken>()), Times.Exactly(2));
        overview.Ancients.Single(a => a.Ancient == "Wrath").Level.Should().Be(5);
    }

    [Fact]
    public async Task GetMasteries_TriggersTierUp_AndReflectsNewLevel()
    {
        var (svc, masteryRepo, activityRepo, players, _) = Build();
        var pid = Guid.NewGuid();
        var rows = Rows(pid, 1, 1, 1, 1);
        SetupTierUp(masteryRepo, activityRepo, pid, rows, Activities(pid, (MasteryActivityType.RaidHit, 100)));
        players.Setup(p => p.FindByIdAsync(pid, It.IsAny<CancellationToken>())).ReturnsAsync((Player?)null);

        var overview = await svc.GetMasteriesAsync(pid);

        overview.Ancients.Single(a => a.Ancient == "Wrath").Level.Should().Be(2);
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
