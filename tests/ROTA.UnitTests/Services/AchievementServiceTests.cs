using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Services;

namespace ROTA.UnitTests.Services;

public class AchievementServiceTests
{
    // Use the REAL shipped roster so thresholds/points match production content.
    private static readonly IAchievementDefinitionProvider Defs = new AchievementDefinitionProvider(FindApiContentRoot());

    private static (AchievementService svc,
                    Mock<IAchievementProgressRepository> progress,
                    Mock<IAchievementProgressEventRepository> progressEvents,
                    Mock<IAchievementAwardRepository> awards,
                    Mock<IAuditLogRepository> audit,
                    Mock<IPlayerInventoryRepository> inventory,
                    Mock<IItemDefinitionProvider> itemDefs)
        Build()
    {
        var progress       = new Mock<IAchievementProgressRepository>();
        var progressEvents = new Mock<IAchievementProgressEventRepository>();
        var awards         = new Mock<IAchievementAwardRepository>();
        var audit          = new Mock<IAuditLogRepository>();
        var inventory      = new Mock<IPlayerInventoryRepository>();
        var itemDefs       = new Mock<IItemDefinitionProvider>();

        progress.Setup(p => p.GetForPlayerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AchievementProgress>());
        // Default: the progress-event ledger has no prior row, so a referenced increment proceeds.
        progressEvents.Setup(e => e.ExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        progressEvents.Setup(e => e.CreateAsync(It.IsAny<AchievementProgressEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        awards.Setup(a => a.GetTotalPointsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0);
        awards.Setup(a => a.ReferenceExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        awards.Setup(a => a.CreateAsync(It.IsAny<AchievementAward>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var svc = new AchievementService(Defs, progress.Object, progressEvents.Object, awards.Object, audit.Object, inventory.Object, itemDefs.Object);
        return (svc, progress, progressEvents, awards, audit, inventory, itemDefs);
    }

    private static AchievementProgress ProgressRow(Guid pid, string id, long value, bool completed = false)
    {
        var row = AchievementProgress.Create(pid, id, value);
        if (completed) row.MarkComplete(DateTimeOffset.UtcNow);
        return row;
    }

    // ── RecordProgressAsync ────────────────────────────────────────────────────

    [Fact]
    public async Task RecordProgress_NoReference_IncrementsEachAchievementOnMetric()
    {
        var (svc, progress, _, _, _, _, _) = Build();
        var pid = Guid.NewGuid();

        await svc.RecordProgressAsync(pid, AchievementMetric.RaidCompletions, 1);

        // Two raid achievements share the RaidCompletions metric → both increment.
        progress.Verify(p => p.IncrementAsync(pid, "ach_raids_10", 1, It.IsAny<CancellationToken>()), Times.Once);
        progress.Verify(p => p.IncrementAsync(pid, "ach_raids_100", 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordProgress_WithReference_NotSeen_RecordsEventThenIncrements()
    {
        var (svc, progress, progressEvents, _, _, _, _) = Build();
        var pid = Guid.NewGuid();
        // Event ledger has no prior row → CreateAsync succeeds → the increment proceeds.
        progressEvents.Setup(e => e.CreateAsync(
                It.Is<AchievementProgressEvent>(x => x.AchievementId == "ach_days_30" && x.ReferenceId == "ach:day:x"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await svc.RecordProgressAsync(pid, AchievementMetric.DaysPlayed, 1, "ach:day:x");

        // The progress event is written FIRST (per-achievement, per source ref) ...
        progressEvents.Verify(e => e.CreateAsync(
            It.Is<AchievementProgressEvent>(x => x.PlayerId == pid && x.AchievementId == "ach_days_30" && x.ReferenceId == "ach:day:x"),
            It.IsAny<CancellationToken>()), Times.Once);
        // ... then the counter increments.
        progress.Verify(p => p.IncrementAsync(pid, "ach_days_30", 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordProgress_WithReference_AlreadySeen_SkipsIncrement()
    {
        var (svc, progress, progressEvents, _, _, _, _) = Build();
        var pid = Guid.NewGuid();
        // The progress-event ledger already has this (player, achievement, ref) → CreateAsync returns
        // false on the unique-violation, so the increment must be SKIPPED for that achievement.
        progressEvents.Setup(e => e.CreateAsync(
                It.Is<AchievementProgressEvent>(x => x.AchievementId == "ach_days_30" && x.ReferenceId == "ach:day:x"),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        await svc.RecordProgressAsync(pid, AchievementMetric.DaysPlayed, 1, "ach:day:x");

        progress.Verify(p => p.IncrementAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task RecordProgress_NonPositiveAmount_NoOp()
    {
        var (svc, progress, _, _, _, _, _) = Build();
        await svc.RecordProgressAsync(Guid.NewGuid(), AchievementMetric.RaidCompletions, 0);
        progress.Verify(p => p.IncrementAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── RecordZoneRerunAsync (System 25) ───────────────────────────────────────

    private static AchievementDefinition ZoneTier(string id, int ch, int zone, long threshold) => new()
    {
        Id = id, Category = AchievementCategory.ZoneMastery, Metric = AchievementMetric.ZoneReruns,
        Chapter = ch, ZoneIndex = zone, Threshold = threshold, Points = 5,
    };

    private static AchievementService SvcWith(IAchievementDefinitionProvider defs,
        Mock<IAchievementProgressRepository> progress, Mock<IAchievementProgressEventRepository> events)
        => new(defs, progress.Object, events.Object,
            new Mock<IAchievementAwardRepository>().Object, new Mock<IAuditLogRepository>().Object,
            new Mock<IPlayerInventoryRepository>().Object, new Mock<IItemDefinitionProvider>().Object);

    [Fact]
    public async Task RecordZoneRerun_RoutesOnlyToMatchingZoneLadder_IncrementingEachTierOnce()
    {
        var defs = new Mock<IAchievementDefinitionProvider>();
        defs.Setup(d => d.GetZoneRerunTiers(1, 0)).Returns(new List<AchievementDefinition>
        {
            ZoneTier("ach_zonererun_c1z0_grey", 1, 0, 10),
            ZoneTier("ach_zonererun_c1z0_white", 1, 0, 25),
        });
        var progress = new Mock<IAchievementProgressRepository>();
        var events = new Mock<IAchievementProgressEventRepository>();
        events.Setup(e => e.CreateAsync(It.IsAny<AchievementProgressEvent>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        var svc = SvcWith(defs.Object, progress, events);
        var pid = Guid.NewGuid();

        await svc.RecordZoneRerunAsync(pid, 1, 0, "zonererun:1:0:5");

        progress.Verify(p => p.IncrementAsync(pid, "ach_zonererun_c1z0_grey", 1, It.IsAny<CancellationToken>()), Times.Once);
        progress.Verify(p => p.IncrementAsync(pid, "ach_zonererun_c1z0_white", 1, It.IsAny<CancellationToken>()), Times.Once);
        defs.Verify(d => d.GetZoneRerunTiers(1, 0), Times.Once);
    }

    [Fact]
    public async Task RecordZoneRerun_SameReferenceTwice_IncrementsOnlyOnce()
    {
        var defs = new Mock<IAchievementDefinitionProvider>();
        defs.Setup(d => d.GetZoneRerunTiers(1, 0)).Returns(new List<AchievementDefinition>
        {
            ZoneTier("ach_zonererun_c1z0_grey", 1, 0, 10),
        });
        var progress = new Mock<IAchievementProgressRepository>();
        var events = new Mock<IAchievementProgressEventRepository>();
        // First call writes the event (true); the replay loses the unique race (false) → skip increment.
        events.SetupSequence(e => e.CreateAsync(It.IsAny<AchievementProgressEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true).ReturnsAsync(false);
        var svc = SvcWith(defs.Object, progress, events);
        var pid = Guid.NewGuid();

        await svc.RecordZoneRerunAsync(pid, 1, 0, "zonererun:1:0:5");
        await svc.RecordZoneRerunAsync(pid, 1, 0, "zonererun:1:0:5");

        progress.Verify(p => p.IncrementAsync(pid, "ach_zonererun_c1z0_grey", 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecordZoneRerun_NoLadderForZone_NoOp()
    {
        var defs = new Mock<IAchievementDefinitionProvider>();
        defs.Setup(d => d.GetZoneRerunTiers(It.IsAny<int>(), It.IsAny<int>())).Returns(Array.Empty<AchievementDefinition>());
        var progress = new Mock<IAchievementProgressRepository>();
        var svc = SvcWith(defs.Object, progress, new Mock<IAchievementProgressEventRepository>());

        await svc.RecordZoneRerunAsync(Guid.NewGuid(), 99, 9, "zonererun:99:9:1");

        progress.Verify(p => p.IncrementAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── EvaluateCompletionsAsync ───────────────────────────────────────────────

    [Fact]
    public async Task Evaluate_AtThreshold_AwardsExactlyOneLedgerRow_MarksComplete_Audits()
    {
        var (svc, progress, _, awards, audit, _, _) = Build();
        var pid = Guid.NewGuid();
        var row = ProgressRow(pid, "ach_raids_10", 10);
        progress.Setup(p => p.GetForPlayerAsync(pid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AchievementProgress> { row });

        await svc.EvaluateCompletionsAsync(pid);

        awards.Verify(a => a.CreateAsync(
            It.Is<AchievementAward>(x => x.AchievementId == "ach_raids_10" && x.Points == 10), It.IsAny<CancellationToken>()), Times.Once);
        progress.Verify(p => p.UpsertAsync(It.Is<AchievementProgress>(x => x.AchievementId == "ach_raids_10" && x.IsCompleted), It.IsAny<CancellationToken>()), Times.Once);
        audit.Verify(a => a.AppendAsync(It.Is<AuditLog>(l => l.Action == "AchievementUnlocked"), It.IsAny<CancellationToken>()), Times.Once);
        row.IsCompleted.Should().BeTrue();
        row.CompletedAt.Should().NotBeNull();
    }

    [Fact]
    public async Task Evaluate_BelowThreshold_DoesNotAward()
    {
        var (svc, progress, _, awards, _, _, _) = Build();
        var pid = Guid.NewGuid();
        progress.Setup(p => p.GetForPlayerAsync(pid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AchievementProgress> { ProgressRow(pid, "ach_raids_10", 9) });

        await svc.EvaluateCompletionsAsync(pid);

        awards.Verify(a => a.CreateAsync(It.IsAny<AchievementAward>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Evaluate_AlreadyCompleted_DoesNotReAward()
    {
        var (svc, progress, _, awards, _, _, _) = Build();
        var pid = Guid.NewGuid();
        progress.Setup(p => p.GetForPlayerAsync(pid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AchievementProgress> { ProgressRow(pid, "ach_raids_10", 50, completed: true) });

        await svc.EvaluateCompletionsAsync(pid);

        awards.Verify(a => a.CreateAsync(It.IsAny<AchievementAward>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Evaluate_ReferenceAlreadyAwarded_LatchesWithoutDoubleAward()
    {
        var (svc, progress, _, awards, _, _, _) = Build();
        var pid = Guid.NewGuid();
        var row = ProgressRow(pid, "ach_raids_10", 10);
        progress.Setup(p => p.GetForPlayerAsync(pid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AchievementProgress> { row });
        // The award ledger already has this row (crash after ledger write before latch).
        awards.Setup(a => a.ReferenceExistsAsync(pid, $"achievement:{pid}:ach_raids_10", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await svc.EvaluateCompletionsAsync(pid);

        awards.Verify(a => a.CreateAsync(It.IsAny<AchievementAward>(), It.IsAny<CancellationToken>()), Times.Never);
        progress.Verify(p => p.UpsertAsync(It.Is<AchievementProgress>(x => x.IsCompleted), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Evaluate_TierChain_AwardsBothTiersIndependently()
    {
        var (svc, progress, _, awards, _, _, _) = Build();
        var pid = Guid.NewGuid();
        // Crossed the 100 threshold → both ach_raids_10 (10) and ach_raids_100 (100) are met.
        progress.Setup(p => p.GetForPlayerAsync(pid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AchievementProgress>
            {
                ProgressRow(pid, "ach_raids_10", 120),
                ProgressRow(pid, "ach_raids_100", 120),
            });

        await svc.EvaluateCompletionsAsync(pid);

        awards.Verify(a => a.CreateAsync(It.Is<AchievementAward>(x => x.AchievementId == "ach_raids_10"), It.IsAny<CancellationToken>()), Times.Once);
        awards.Verify(a => a.CreateAsync(It.Is<AchievementAward>(x => x.AchievementId == "ach_raids_100"), It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Recount metrics ────────────────────────────────────────────────────────

    [Fact]
    public async Task SetCounter_WritesAbsoluteToEachAchievementOnMetric()
    {
        var (svc, progress, _, _, _, _, _) = Build();
        var pid = Guid.NewGuid();

        await svc.SetCounterAsync(pid, AchievementMetric.EquipmentPiecesOwned, 30);

        progress.Verify(p => p.SetCounterAsync(pid, "ach_gear_25", 30, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecountCollector_CountsDistinctOwnedItemsByKey_Absolute()
    {
        var (svc, progress, _, _, _, inventory, itemDefs) = Build();
        var pid = Guid.NewGuid();

        // 3 distinct sigils owned + 1 material (excluded). Recount should be 3 (absolute).
        inventory.Setup(i => i.GetAllForPlayerAsync(pid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerInventoryItem>
            {
                PlayerInventoryItem.Create(pid, "sig_a", 1),
                PlayerInventoryItem.Create(pid, "sig_b", 5),
                PlayerInventoryItem.Create(pid, "sig_c", 2),
                PlayerInventoryItem.Create(pid, "mat_x", 9),
            });
        itemDefs.Setup(d => d.GetById(It.IsRegex("^sig_"))).Returns((string id) => new ItemDefinition { Id = id, Type = ItemType.Sigil });
        itemDefs.Setup(d => d.GetById("mat_x")).Returns(new ItemDefinition { Id = "mat_x", Type = ItemType.Material });

        await svc.RecountCollectorCountersAsync(pid);

        progress.Verify(p => p.SetCounterAsync(pid, "ach_collect_sigils_8", 3, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task RecountCollector_ReGrantOfOwnedItem_DoesNotInflate()
    {
        var (svc, progress, _, _, _, inventory, itemDefs) = Build();
        var pid = Guid.NewGuid();
        // Same single distinct sigil, quantity bumped to 4 — distinct count stays 1.
        inventory.Setup(i => i.GetAllForPlayerAsync(pid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerInventoryItem> { PlayerInventoryItem.Create(pid, "sig_a", 4) });
        itemDefs.Setup(d => d.GetById("sig_a")).Returns(new ItemDefinition { Id = "sig_a", Type = ItemType.Sigil });

        await svc.RecountCollectorCountersAsync(pid);

        progress.Verify(p => p.SetCounterAsync(pid, "ach_collect_sigils_8", 1, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── GetForPlayerAsync ──────────────────────────────────────────────────────

    [Fact]
    public async Task GetForPlayer_ReturnsTotalAndPerAchievementState()
    {
        var (svc, progress, _, awards, _, _, _) = Build();
        var pid = Guid.NewGuid();
        progress.Setup(p => p.GetForPlayerAsync(pid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AchievementProgress> { ProgressRow(pid, "ach_raids_10", 7) });
        awards.Setup(a => a.GetTotalPointsAsync(pid, It.IsAny<CancellationToken>())).ReturnsAsync(45);

        var overview = await svc.GetForPlayerAsync(pid);

        overview.TotalPoints.Should().Be(45);
        var slayer = overview.Achievements.Single(a => a.Id == "ach_raids_10");
        slayer.Progress.Should().Be(7);
        slayer.Threshold.Should().Be(10);
        slayer.IsCompleted.Should().BeFalse();
        slayer.Category.Should().Be("RaidCompletion");
    }

    [Fact]
    public async Task GetForPlayer_ClampsProgressToThreshold()
    {
        var (svc, progress, _, _, _, _, _) = Build();
        var pid = Guid.NewGuid();
        progress.Setup(p => p.GetForPlayerAsync(pid, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<AchievementProgress> { ProgressRow(pid, "ach_raids_10", 999, completed: true) });

        var overview = await svc.GetForPlayerAsync(pid);

        overview.Achievements.Single(a => a.Id == "ach_raids_10").Progress.Should().Be(10);
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
