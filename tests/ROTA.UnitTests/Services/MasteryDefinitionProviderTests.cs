using System.Text.Json;
using System.Text.Json.Serialization;
using FluentAssertions;
using ROTA.Application.Models;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Services;

namespace ROTA.UnitTests.Services;

public class MasteryDefinitionProviderTests : IDisposable
{
    private readonly string _tmpDir;

    public MasteryDefinitionProviderTests()
    {
        _tmpDir = Path.Combine(Path.GetTempPath(), $"rota_mastery_test_{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(_tmpDir, "content"));
    }

    public void Dispose() => Directory.Delete(_tmpDir, recursive: true);

    // Happy path — the shipped content/masteries.json

    [Fact]
    public void Provider_LoadsShippedRoster_FourAncients()
    {
        var provider = new MasteryDefinitionProvider(FindApiContentRoot());

        var all = provider.GetAll();
        all.Should().HaveCount(4);
        all.Select(a => a.Ancient).Should().BeEquivalentTo(
            new[] { MasteryAncient.Wrath, MasteryAncient.Bulwark, MasteryAncient.Hoard, MasteryAncient.Discernment });
    }

    [Fact]
    public void Provider_ShippedMagnitudes_MatchLockedSpec()
    {
        var provider = new MasteryDefinitionProvider(FindApiContentRoot());

        provider.GlobalPercent(MasteryAncient.Wrath, 5).Should().BeApproximately(2.5, 0.0001);
        provider.GlobalPercent(MasteryAncient.Wrath, 1).Should().BeApproximately(0.5, 0.0001);
        provider.GlobalPercent(MasteryAncient.Bulwark, 5).Should().BeApproximately(0.5, 0.0001);
        provider.GlobalPercent(MasteryAncient.Hoard, 5).Should().BeApproximately(4.0, 0.0001);
        provider.GlobalPercent(MasteryAncient.Discernment, 5).Should().BeApproximately(4.0, 0.0001);
    }

    [Fact]
    public void Provider_ShippedTierChallenge_Wrath_T1_IsRaidHit100()
    {
        var provider = new MasteryDefinitionProvider(FindApiContentRoot());

        var tier = provider.GetTierChallenge(MasteryAncient.Wrath, 1);
        tier.Should().NotBeNull();
        tier!.Checklist.Should().ContainSingle()
            .Which.Should().BeEquivalentTo(new { ActivityType = MasteryActivityType.RaidHit, Threshold = 100L });
    }

    [Fact]
    public void Provider_ShippedFinalTiers_SpanMultipleSystems()
    {
        var provider = new MasteryDefinitionProvider(FindApiContentRoot());

        foreach (var ancient in Enum.GetValues<MasteryAncient>())
        {
            var finalTier = provider.GetTierChallenge(ancient, 4);
            finalTier.Should().NotBeNull();
            finalTier!.Checklist.Select(c => c.ActivityType).Distinct().Count()
                .Should().BeGreaterThanOrEqualTo(2, $"{ancient} T4→5 must be cross-system");
        }
    }

    [Fact]
    public void GlobalPercent_OutOfRangeLevel_Throws()
    {
        var provider = new MasteryDefinitionProvider(FindApiContentRoot());

        ((Action)(() => provider.GlobalPercent(MasteryAncient.Wrath, 0))).Should().Throw<ArgumentOutOfRangeException>();
        ((Action)(() => provider.GlobalPercent(MasteryAncient.Wrath, 6))).Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Get_UnknownLookupAfterLoad_DefaultsHandledGracefully()
    {
        var provider = new MasteryDefinitionProvider(FindApiContentRoot());
        provider.Get(MasteryAncient.Wrath).Should().NotBeNull();
        provider.GetTierChallenge(MasteryAncient.Wrath, 9).Should().BeNull(); // no tier advances from L9
    }

    // Startup validation

    [Fact]
    public void Provider_MissingFile_Throws()
    {
        var act = () => new MasteryDefinitionProvider(_tmpDir); // nothing written
        act.Should().Throw<InvalidOperationException>().WithMessage("*not found*");
    }

    [Fact]
    public void Provider_MissingAncient_Throws()
    {
        var list = BuildValid();
        list.RemoveAll(a => a.Ancient == MasteryAncient.Discernment);

        var act = () => WriteAndLoad(list);
        act.Should().Throw<InvalidOperationException>().WithMessage("*missing ancient*Discernment*");
    }

    [Fact]
    public void Provider_DuplicateAncient_Throws()
    {
        var list = BuildValid();
        list.Add(MakeAncient(MasteryAncient.Wrath, [0.5, 1.0, 1.5, 2.0, 2.5]));

        var act = () => WriteAndLoad(list);
        act.Should().Throw<InvalidOperationException>().WithMessage("*duplicate ancient*Wrath*");
    }

    [Fact]
    public void Provider_BadMagnitudeTableLength_Throws()
    {
        var list = BuildValid();
        list[0].GlobalPercentByLevel = [0.5, 1.0, 1.5]; // only 3 entries

        var act = () => WriteAndLoad(list);
        act.Should().Throw<InvalidOperationException>().WithMessage("*globalPercentByLevel must have 5 entries*");
    }

    [Fact]
    public void Provider_NonDecreasingMagnitudeViolated_Throws()
    {
        var list = BuildValid();
        list[0].GlobalPercentByLevel = [0.5, 1.0, 0.9, 2.0, 2.5]; // dips at index 2

        var act = () => WriteAndLoad(list);
        act.Should().Throw<InvalidOperationException>().WithMessage("*not non-decreasing*");
    }

    [Fact]
    public void Provider_EmptyChecklist_Throws()
    {
        var list = BuildValid();
        list[0].TierChallenges[0].Checklist.Clear();

        var act = () => WriteAndLoad(list);
        act.Should().Throw<InvalidOperationException>().WithMessage("*empty checklist*");
    }

    [Fact]
    public void Provider_ThresholdNotPositive_Throws()
    {
        var list = BuildValid();
        list[0].TierChallenges[0].Checklist[0].Threshold = 0;

        var act = () => WriteAndLoad(list);
        act.Should().Throw<InvalidOperationException>().WithMessage("*threshold must be > 0*");
    }

    [Fact]
    public void Provider_UnknownActivityType_Throws()
    {
        var list = BuildValid();
        list[0].TierChallenges[0].Checklist[0].ActivityType = (MasteryActivityType)999;

        var act = () => WriteAndLoad(list);
        act.Should().Throw<InvalidOperationException>().WithMessage("*unknown activityType*");
    }

    [Fact]
    public void Provider_FinalTierSingleType_Throws_BreadthCurve()
    {
        var list = BuildValid();
        // Collapse the final tier to a single activity type → violates the breadth curve.
        list[0].TierChallenges[3].Checklist = new()
        {
            new() { ActivityType = MasteryActivityType.RaidHit, Threshold = 40 },
        };

        var act = () => WriteAndLoad(list);
        act.Should().Throw<InvalidOperationException>().WithMessage("*≥2 distinct activity types*");
    }

    // Helpers

    private static AncientDefinition MakeAncient(MasteryAncient ancient, double[] globals) => new()
    {
        Ancient = ancient,
        Name = ancient.ToString(),
        Theme = "theme",
        Description = "desc",
        IconKey = "icon",
        GlobalPercentByLevel = globals,
        TierChallenges = new()
        {
            new() { FromLevel = 1, Checklist = new() { new() { ActivityType = MasteryActivityType.RaidHit, Threshold = 10 } } },
            new() { FromLevel = 2, Checklist = new() { new() { ActivityType = MasteryActivityType.RaidHit, Threshold = 20 } } },
            new() { FromLevel = 3, Checklist = new() { new() { ActivityType = MasteryActivityType.RaidHit, Threshold = 30 } } },
            new() { FromLevel = 4, Checklist = new()
                {
                    new() { ActivityType = MasteryActivityType.RaidHit,  Threshold = 40 },
                    new() { ActivityType = MasteryActivityType.RaidKill, Threshold = 5  },
                }
            },
        },
    };

    private static List<AncientDefinition> BuildValid() => new()
    {
        MakeAncient(MasteryAncient.Wrath,       [0.5, 1.0, 1.5, 2.0, 2.5]),
        MakeAncient(MasteryAncient.Bulwark,     [0.1, 0.2, 0.3, 0.4, 0.5]),
        MakeAncient(MasteryAncient.Hoard,       [0.8, 1.6, 2.4, 3.2, 4.0]),
        MakeAncient(MasteryAncient.Discernment, [0.8, 1.6, 2.4, 3.2, 4.0]),
    };

    private MasteryDefinitionProvider WriteAndLoad(List<AncientDefinition> list)
    {
        var options = new JsonSerializerOptions { Converters = { new JsonStringEnumConverter() } };
        File.WriteAllText(
            Path.Combine(_tmpDir, "content", "masteries.json"),
            JsonSerializer.Serialize(list, options));
        return new MasteryDefinitionProvider(_tmpDir);
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
