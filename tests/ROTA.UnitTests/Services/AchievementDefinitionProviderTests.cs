using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Services;

namespace ROTA.UnitTests.Services;

public class AchievementDefinitionProviderTests : IDisposable
{
    private readonly string _tmpDir;

    public AchievementDefinitionProviderTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"rota_ach_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tmpDir, "content"));
    }

    public void Dispose() => Directory.Delete(_tmpDir, recursive: true);

    // ── Happy path — the shipped content/achievements.json ─────────────────────

    [Fact]
    public void Provider_LoadsShippedRoster_AllCategoriesPresent()
    {
        var provider = new AchievementDefinitionProvider(FindApiContentRoot());

        var all = provider.GetAll();
        all.Should().NotBeEmpty();
        all.Select(a => a.Category).Distinct().Should().Contain(new[]
        {
            AchievementCategory.RaidCompletion,
            AchievementCategory.QuestClear,
            AchievementCategory.EquipmentOwned,
            AchievementCategory.DaysPlayed,
            AchievementCategory.Collector,
        });
    }

    [Fact]
    public void Provider_ShippedRaidChain_LinksTiers()
    {
        var provider = new AchievementDefinitionProvider(FindApiContentRoot());

        provider.GetById("ach_raids_10")!.NextId.Should().Be("ach_raids_100");
        provider.GetById("ach_raids_100")!.NextId.Should().BeNull();
        provider.GetByMetric(AchievementMetric.RaidCompletions).Should().HaveCountGreaterThanOrEqualTo(2);
    }

    [Fact]
    public void Provider_GetById_Unknown_ReturnsNull()
    {
        var provider = new AchievementDefinitionProvider(FindApiContentRoot());
        provider.GetById("nope").Should().BeNull();
    }

    [Fact]
    public void Provider_MissingFile_Throws()
    {
        var act = () => new AchievementDefinitionProvider(_tmpDir);
        act.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
    }

    [Fact]
    public void Provider_EmptyRoster_Throws()
    {
        var act = () => WriteAndLoad(new List<AchievementDefinition>());
        act.Should().Throw<InvalidOperationException>().WithMessage("*roster is empty*");
    }

    [Fact]
    public void Provider_DuplicateId_Throws()
    {
        var list = BuildValid();
        list.Add(Make("ach_raids_10", AchievementCategory.RaidCompletion, AchievementMetric.RaidCompletions, 10, 10));

        var act = () => WriteAndLoad(list);
        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate id*ach_raids_10*");
    }

    [Fact]
    public void Provider_NonPositivePoints_Throws()
    {
        var list = BuildValid();
        list[0].Points = 0;

        var act = () => WriteAndLoad(list);
        act.Should().Throw<InvalidOperationException>().WithMessage("*points must be > 0*");
    }

    [Fact]
    public void Provider_NonPositiveThreshold_Throws()
    {
        var list = BuildValid();
        list[0].Threshold = 0;

        var act = () => WriteAndLoad(list);
        act.Should().Throw<InvalidOperationException>().WithMessage("*threshold must be > 0*");
    }

    [Fact]
    public void Provider_UnknownCategory_Throws()
    {
        var list = BuildValid();
        list[0].Category = (AchievementCategory)999;

        var act = () => WriteAndLoad(list);
        act.Should().Throw<InvalidOperationException>().WithMessage("*unknown category*");
    }

    [Fact]
    public void Provider_UnknownMetric_Throws()
    {
        var list = BuildValid();
        list[0].Metric = (AchievementMetric)999;

        var act = () => WriteAndLoad(list);
        act.Should().Throw<InvalidOperationException>().WithMessage("*unknown metric*");
    }

    [Fact]
    public void Provider_DanglingNextId_Throws()
    {
        var list = BuildValid();
        list[0].NextId = "does_not_exist";

        var act = () => WriteAndLoad(list);
        act.Should().Throw<InvalidOperationException>().WithMessage("*does not resolve*");
    }

    [Fact]
    public void Provider_CyclicNextId_Throws()
    {
        var a = Make("a", AchievementCategory.RaidCompletion, AchievementMetric.RaidCompletions, 10, 10, nextId: "b");
        var b = Make("b", AchievementCategory.RaidCompletion, AchievementMetric.RaidCompletions, 20, 20, nextId: "a");

        var act = () => WriteAndLoad(new List<AchievementDefinition> { a, b });
        act.Should().Throw<InvalidOperationException>().WithMessage("*cyclic*");
    }

    [Fact]
    public void Provider_NextIdNonIncreasingThreshold_Throws()
    {
        var a = Make("a", AchievementCategory.RaidCompletion, AchievementMetric.RaidCompletions, 10, 100, nextId: "b");
        var b = Make("b", AchievementCategory.RaidCompletion, AchievementMetric.RaidCompletions, 20, 100); // not strictly higher

        var act = () => WriteAndLoad(new List<AchievementDefinition> { a, b });
        act.Should().Throw<InvalidOperationException>().WithMessage("*strictly higher*");
    }

    [Fact]
    public void Provider_NextIdMetricMismatch_Throws()
    {
        var a = Make("a", AchievementCategory.RaidCompletion, AchievementMetric.RaidCompletions, 10, 10, nextId: "b");
        var b = Make("b", AchievementCategory.QuestClear, AchievementMetric.QuestNodesCleared, 20, 50);

        var act = () => WriteAndLoad(new List<AchievementDefinition> { a, b });
        act.Should().Throw<InvalidOperationException>().WithMessage("*different metric*");
    }

    [Fact]
    public void Provider_CollectorMissingKey_Throws()
    {
        var list = BuildValid();
        var collector = list.First(d => d.Category == AchievementCategory.Collector);
        collector.CollectorKey = null;

        var act = () => WriteAndLoad(list);
        act.Should().Throw<InvalidOperationException>().WithMessage("*missing collectorKey*");
    }

    private static AchievementDefinition Make(
        string id, AchievementCategory cat, AchievementMetric metric, int points, long threshold,
        string? nextId = null, string? collectorKey = null) => new()
    {
        Id           = id,
        Category     = cat,
        Metric       = metric,
        Name         = id,
        Description  = "desc",
        Points       = points,
        Threshold    = threshold,
        NextId       = nextId,
        CollectorKey = collectorKey,
        IconKey      = "icon",
    };

    private static List<AchievementDefinition> BuildValid() => new()
    {
        Make("ach_raids_10", AchievementCategory.RaidCompletion, AchievementMetric.RaidCompletions, 10, 10, nextId: "ach_raids_100"),
        Make("ach_raids_100", AchievementCategory.RaidCompletion, AchievementMetric.RaidCompletions, 50, 100),
        Make("ach_nodes_50", AchievementCategory.QuestClear, AchievementMetric.QuestNodesCleared, 15, 50),
        Make("ach_gear_25", AchievementCategory.EquipmentOwned, AchievementMetric.EquipmentPiecesOwned, 20, 25),
        Make("ach_days_30", AchievementCategory.DaysPlayed, AchievementMetric.DaysPlayed, 25, 30),
        Make("ach_sigils_8", AchievementCategory.Collector, AchievementMetric.CollectorItemCount, 20, 8, collectorKey: "Sigil"),
    };

    private AchievementDefinitionProvider WriteAndLoad(List<AchievementDefinition> list)
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        File.WriteAllText(
            Path.Combine(_tmpDir, "content", "achievements.json"),
            JsonSerializer.Serialize(list, options));
        return new AchievementDefinitionProvider(_tmpDir);
    }

    // ── System 25 — per-zone rerun ladder synthesis ────────────────────────────

    private sealed class FakeQuests : IQuestDefinitionProvider
    {
        private readonly List<QuestDefinition> _q;
        public FakeQuests(params QuestDefinition[] q) => _q = q.ToList();
        public IReadOnlyList<QuestDefinition> GetAll() => _q;
        public QuestDefinition? GetById(string id) => _q.FirstOrDefault(x => x.Id == id);
    }

    private static QuestDefinition Node(int ch, int zone, int idx, string zoneName) => new()
    {
        Id = $"c{ch}z{zone}n{idx}", Chapter = ch, ZoneIndex = zone, ZoneName = zoneName, NodeIndex = idx,
    };

    private void WriteValidRoster() => File.WriteAllText(
        Path.Combine(_tmpDir, "content", "achievements.json"),
        JsonSerializer.Serialize(BuildValid(), new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } }));

    [Fact]
    public void Provider_SynthesizesSixTierLadderPerZone_FromQuestTopologyAndConfig()
    {
        WriteValidRoster();
        var quests = new FakeQuests(
            Node(1, 0, 0, "Old Guard Ruins"), Node(1, 0, 1, "Old Guard Ruins"),
            Node(1, 1, 0, "Ashen Causeway"));
        var provider = new AchievementDefinitionProvider(_tmpDir, quests, new AchievementConfig());

        var tiers = provider.GetZoneRerunTiers(1, 0);
        tiers.Should().HaveCount(6);
        tiers.Select(t => t.Threshold).Should().Equal(10, 25, 50, 100, 250, 500);
        tiers[0].Id.Should().Be("ach_zonererun_c1z0_grey");
        tiers[0].NextId.Should().Be("ach_zonererun_c1z0_white");
        tiers[^1].Id.Should().Be("ach_zonererun_c1z0_orange");
        tiers[^1].NextId.Should().BeNull();
        tiers.Should().OnlyContain(t =>
            t.Metric == AchievementMetric.ZoneReruns && t.Category == AchievementCategory.ZoneMastery);

        // Two distinct zones → two ladders (12 synthesized defs total).
        provider.GetZoneRerunTiers(1, 1).Should().HaveCount(6);
        provider.GetAll().Count(a => a.Metric == AchievementMetric.ZoneReruns).Should().Be(12);
    }

    [Fact]
    public void Provider_ZoneRerunLadder_WithNonIncreasingThresholds_ThrowsAtBoot()
    {
        WriteValidRoster();
        var quests = new FakeQuests(Node(1, 0, 0, "Z"));
        var badConfig = new AchievementConfig
        {
            ZoneRerunLadder = new()
            {
                new() { Rarity = ItemRarity.Grey,  Threshold = 50, Points = 5 },
                new() { Rarity = ItemRarity.White, Threshold = 50, Points = 10 }, // not strictly increasing
            },
        };

        var act = () => new AchievementDefinitionProvider(_tmpDir, quests, badConfig);
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Provider_NoQuestProvider_SynthesizesNoZoneLadders()
    {
        var provider = new AchievementDefinitionProvider(FindApiContentRoot());
        provider.GetAll().Should().NotContain(a => a.Metric == AchievementMetric.ZoneReruns);
        provider.GetZoneRerunTiers(1, 0).Should().BeEmpty();
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
