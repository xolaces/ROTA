using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
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
