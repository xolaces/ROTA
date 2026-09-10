using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;

namespace ROTA.UnitTests.Balance;

/// <summary>
/// R11 — the tutorial tells the player, in its second beat, that four passes of q001 will level them:
///
///     "You have twenty-five. The ruins ask five.
///      Four passes will teach you more than any of it will kill you."
///
/// That is exact arithmetic, not a rounded estimate, and it is the kind a player checks on their first
/// four clicks. It is also the product of FOUR independent settings that no single file relates:
///
///     quests.json          q001.baseEnergyCost      5
///     QuestConfig          XpPerEnergyRollMin/Max   1.5 / 1.5
///     QuestConfig          ChapterScaling[1] + EnergyZoneRampPerZone
///     LevelingConfig       XpBaseMultiplier / XpExponent / XpLinearPerLevel
///
/// Retune any one of them and the line becomes a lie with nothing failing. So the number is derived
/// here from the SAME code the server runs — ResourceReward.RollSummed for the roll, a real StatService
/// for the level curve — rather than restated, and the config is read from the shipped appsettings.json
/// rather than from the C# defaults, because appsettings is what actually runs.
///
/// A CORRECTION THIS TEST BAKES IN. The design doc derived the four as "4 x 7.5 XP = 30 = TNL(1)".
/// The 7.5 is right as a product but wrong as a grant: RollSummed rounds away from zero, so an
/// attempt pays 8 XP, and four of them pay 32 against a TNL(1) of 30. The ANSWER is four either way;
/// the derivation was not exact, and a doc that claims exactness should be exact.
/// </summary>
public class TutorialFourPassesTests
{
    private const string TutorialQuestId = "q001";

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ROTA.slnx")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("Could not locate the repo root (ROTA.slnx).");
        return dir.FullName;
    }

    private static JsonElement AppSettingsSection(string name)
    {
        var json = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "ROTA.Api", "appsettings.json"));
        using var doc = JsonDocument.Parse(json);
        return doc.RootElement.GetProperty(name).Clone();
    }

    private static double Setting(JsonElement section, string key, double fallback)
        => section.TryGetProperty(key, out var v) ? v.GetDouble() : fallback;

    /// <summary>The shipped q001 row, so a content edit moves this test.</summary>
    private static (int BaseEnergyCost, int Chapter, int ZoneIndex) TutorialQuest()
    {
        var json = File.ReadAllText(Path.Combine(FindRepoRoot(), "src", "ROTA.Api", "content", "quests.json"));
        using var doc = JsonDocument.Parse(json);
        var q = doc.RootElement.EnumerateArray()
            .First(x => x.GetProperty("id").GetString() == TutorialQuestId);
        return (q.GetProperty("baseEnergyCost").GetInt32(),
                q.GetProperty("chapter").GetInt32(),
                q.GetProperty("zoneIndex").GetInt32());
    }

    /// <summary>
    /// Mirrors QuestService.ComputeEnergyCost, which is private. The inputs it reads are asserted
    /// separately below, so a change to any of them fails loudly rather than drifting past this copy.
    /// </summary>
    private static int EffectiveEnergyCost(QuestConfig cfg, int baseCost, int chapter, int zoneIndex)
    {
        var scaling = cfg.GetChapterScaling(chapter);
        double zoneRamp = 1.0 + zoneIndex * cfg.EnergyZoneRampPerZone;
        return (int)Math.Ceiling(baseCost * 1.0 /* Normal */ * scaling.EnergyCostMultiplier * zoneRamp);
    }

    /// <summary>A real StatService — XpToNextLevel is pure over LevelingConfig, so the rest can be mocks.</summary>
    private static StatService StatServiceWith(LevelingConfig leveling)
        => new(
            new Mock<IPlayerRepository>().Object,
            new Mock<IEnergyService>().Object,
            new Mock<IGemService>().Object,
            new Mock<IAuditLogRepository>().Object,
            Options.Create(leveling),
            Options.Create(new CombatConfig()),
            new Mock<IClassService>().Object,
            new Mock<IEquipmentService>().Object,
            new Mock<IPinnacleService>().Object,
            new Mock<IMagicService>().Object,
            new Mock<IMagicDefinitionProvider>().Object,
            new ROTA.UnitTests.TestSupport.PassThroughPlayerMutationLock());

    private static (QuestConfig Quest, LevelingConfig Leveling) ShippedConfig()
    {
        var qs = AppSettingsSection("QuestConfig");
        var ls = AppSettingsSection("LevelingConfig");
        var quest = new QuestConfig
        {
            XpPerEnergyRollMin    = Setting(qs, "XpPerEnergyRollMin", new QuestConfig().XpPerEnergyRollMin),
            XpPerEnergyRollMax    = Setting(qs, "XpPerEnergyRollMax", new QuestConfig().XpPerEnergyRollMax),
            EnergyZoneRampPerZone = Setting(qs, "EnergyZoneRampPerZone", new QuestConfig().EnergyZoneRampPerZone),
        };
        var leveling = new LevelingConfig
        {
            XpBaseMultiplier = Setting(ls, "XpBaseMultiplier", new LevelingConfig().XpBaseMultiplier),
            XpExponent       = Setting(ls, "XpExponent",       new LevelingConfig().XpExponent),
            XpLinearPerLevel = Setting(ls, "XpLinearPerLevel", new LevelingConfig().XpLinearPerLevel),
        };
        return (quest, leveling);
    }

    private static int AttemptsToReachLevelTwo()
    {
        var (quest, leveling) = ShippedConfig();
        var (baseCost, chapter, zone) = TutorialQuest();

        int cost = EffectiveEnergyCost(quest, baseCost, chapter, zone);
        // The REAL roll, so its rounding is included rather than re-derived. Min == Max collapses the
        // draw to a constant, so the Random is never actually consulted for a value that matters.
        long xpPerAttempt = ResourceReward.RollSummed(
            new Random(0), cost, quest.XpPerEnergyRollMin, quest.XpPerEnergyRollMax);
        int tnl = StatServiceWith(leveling).XpToNextLevel(1);

        return (int)Math.Ceiling((double)tnl / xpPerAttempt);
    }

    [Fact]
    public void FourPassesOfTheTutorialQuest_ReachLevelTwo()
    {
        AttemptsToReachLevelTwo().Should().Be(4,
            "docs/design/TUTORIAL_OPENING.md beat 2 promises the player exactly four passes of "
            + TutorialQuestId + ", and it is the first arithmetic they can check");
    }

    [Fact]
    public void TheStartingPool_AffordsThoseFourPasses_WithOneToSpare()
    {
        // The other half of the same beat: "You have twenty-five. The ruins ask five." The player must
        // be able to finish the four without waiting on regen — five attempts fit in the pool, and the
        // level lands on the fourth, which is the beat's whole shape.
        var (quest, _) = ShippedConfig();
        var (baseCost, chapter, zone) = TutorialQuest();
        int cost = EffectiveEnergyCost(quest, baseCost, chapter, zone);

        int affordable = PlayerStats.BaseMaxEnergy / cost;
        affordable.Should().BeGreaterThanOrEqualTo(AttemptsToReachLevelTwo(),
            $"a fresh pool of {PlayerStats.BaseMaxEnergy} must cover the passes the tutorial promises");
        affordable.Should().Be(5, "the beat says the pool affords five and the level lands on the fourth");
    }

    [Fact]
    public void TheInputsTheDerivationDependsOn_AreWhatTheTutorialAssumes()
    {
        // EffectiveEnergyCost above is a copy of a private method. These pin its inputs, so a retune
        // fails HERE with a readable reason rather than silently drifting past the copy.
        var (quest, _) = ShippedConfig();
        var (baseCost, chapter, zone) = TutorialQuest();

        baseCost.Should().Be(5, "the tutorial says 'the ruins ask five'");
        chapter.Should().Be(1);
        zone.Should().Be(0, "zone 0 is what makes the zone ramp a no-op for the tutorial node");
        quest.GetChapterScaling(1).EnergyCostMultiplier.Should().Be(1.0,
            "chapter 1 is the unscaled baseline; anything else changes the tutorial's cost");
        quest.XpPerEnergyRollMin.Should().Be(quest.XpPerEnergyRollMax,
            "the tutorial's promise is exact, which is only honest while the XP roll has no variance");
    }

    [Fact]
    public void TheDerivation_IsFourTimesEight_NotFourTimesSevenPointFive()
    {
        // The correction. RollSummed rounds away from zero, so 5 energy x 1.5 pays 8, not 7.5, and the
        // four passes total 32 against a TNL(1) of 30 — comfortably over, not exactly on. Three passes
        // pay 24 and fall short, which is what actually makes the answer four.
        var (quest, leveling) = ShippedConfig();
        var (baseCost, chapter, zone) = TutorialQuest();
        int cost = EffectiveEnergyCost(quest, baseCost, chapter, zone);

        long perAttempt = ResourceReward.RollSummed(
            new Random(0), cost, quest.XpPerEnergyRollMin, quest.XpPerEnergyRollMax);
        int tnl = StatServiceWith(leveling).XpToNextLevel(1);

        perAttempt.Should().Be(8, "5 energy x 1.5 is 7.5, and the roll rounds away from zero");
        tnl.Should().Be(30);
        (perAttempt * 3).Should().BeLessThan(tnl, "three passes must NOT be enough, or the answer is three");
        (perAttempt * 4).Should().BeGreaterThanOrEqualTo(tnl);
    }
}
