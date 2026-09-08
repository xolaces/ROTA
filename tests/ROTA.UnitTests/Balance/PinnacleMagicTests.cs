using System.Text.Json;
using FluentAssertions;

namespace ROTA.UnitTests.Balance;

/// <summary>
/// Pinnacle magics are the one place in the game where a player designs the mechanic, so the rules
/// around them have to be enforced rather than remembered.
/// </summary>
/// <remarks>
/// <para><b>The bargain.</b> The first player to reach a milestone level designs that level's magic
/// outright — a proc, an item drop, or something stranger. Everyone who reaches the same level
/// afterwards inherits that design and keeps it permanently. A player may own one of each; the
/// per-raid magic slot cap is untouched, so owning them all is not casting them all.</para>
///
/// <para><b>The exclusivity</b> (owner, 2026-09-07). Exotic effect types exist ONLY on
/// <c>magic_pinnacle_*</c> entries. No ordinary magic and no item may carry one until the owner says
/// otherwise. Without a test this rule erodes the first time somebody copies a pinnacle entry as a
/// template for something else — which is exactly how a "special case" becomes the norm.</para>
/// </remarks>
public class PinnacleMagicTests
{
    /// <summary>The four effect types any magic may use.</summary>
    private static readonly HashSet<string> OrdinaryEffects = new()
    {
        "DamageProc", "CritChanceFlat", "GoldProc", "XpProc",
    };

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ROTA.slnx")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("Could not locate the repo root (ROTA.slnx).");
        return dir.FullName;
    }

    private static JsonDocument Magics()
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "ROTA.Api", "content", "magics.json")));

    private static JsonDocument AppSettings()
        => JsonDocument.Parse(File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "ROTA.Api", "appsettings.json")));

    private static IEnumerable<(string Id, string Effect, double Amount, string Rarity)> All()
    {
        using var doc = Magics();
        foreach (var m in doc.RootElement.EnumerateArray())
            yield return (
                m.GetProperty("id").GetString() ?? "?",
                m.GetProperty("effectType").GetString() ?? "?",
                m.GetProperty("procAmount").GetDouble(),
                m.GetProperty("rarity").GetString() ?? "?");
    }

    /// <summary>
    /// The exclusivity rule. An exotic effect on a non-pinnacle magic is the whole special case
    /// leaking into the ordinary catalogue.
    /// </summary>
    [Fact]
    public void ExoticEffectTypesAppearOnlyOnPinnacleMagics()
    {
        var leaked = All()
            .Where(m => !OrdinaryEffects.Contains(m.Effect) && !m.Id.StartsWith("magic_pinnacle"))
            .Select(m => $"{m.Id} uses {m.Effect}")
            .ToList();

        leaked.Should().BeEmpty(
            "exotic effect types are reserved for milestone-level magics until the owner says otherwise");
    }

    /// <summary>
    /// Every milestone level the config recognises must have a magic behind it, and every pinnacle
    /// magic must sit on a level the config recognises. These drifted apart once already: 1,000 and
    /// 2,500 were milestones with no magic, while 15,000 and 25,000 had magics but were not
    /// milestones at all — so neither the gem reward nor the first-claim log fired for them.
    /// </summary>
    [Fact]
    public void EveryPinnacleLevelHasAMagicAndViceVersa()
    {
        using var app = AppSettings();
        var levels = app.RootElement
            .GetProperty("LevelingConfig").GetProperty("PinnacleGemRewards")
            .EnumerateObject().Select(p => int.Parse(p.Name)).OrderBy(n => n).ToList();

        var magicLevels = All()
            .Where(m => m.Id.StartsWith("magic_pinnacle"))
            .Select(m => int.Parse(m.Id.Split('_').Last()))
            .OrderBy(n => n).ToList();

        magicLevels.Should().BeEquivalentTo(levels,
            "IsPinnacleLevel reads PinnacleGemRewards, so a magic on a level absent from that map is "
            + "unreachable, and a level in the map with no magic has nothing to award");
    }

    /// <summary>
    /// Pinnacle magics are the top prize in the game; a non-Orange one would read as a demotion.
    /// </summary>
    [Fact]
    public void AllPinnacleMagicsAreOrange()
        => All().Where(m => m.Id.StartsWith("magic_pinnacle"))
                .Should().OnlyContain(m => m.Rarity == "Orange");

    /// <summary>
    /// The level-1,000 showcase. Its value is that it is FLAT and RAID-WIDE, which makes it the only
    /// power in the game that helps the weakest participant most — a 500-Attack newcomer gains about
    /// a quarter of their damage from it, the level-1,000 veteran who applied it about two percent.
    /// If it ever became proportional it would widen the veteran gap instead of narrowing it, which
    /// is the failure the research paper blames for ending Dawn's new-player acquisition.
    /// </summary>
    [Fact]
    public void TheLevel1000ShowcaseIsAFlatAlwaysOnAura()
    {
        var banner = All().Single(m => m.Id == "magic_pinnacle_1000");

        banner.Effect.Should().Be("FlatAttackAura");
        banner.Amount.Should().BeGreaterThan(1.0,
            "procAmount on an aura is a flat Attack value, not a multiplier — a value under 1 would "
            + "mean somebody has treated it as a percentage");
        banner.Amount.Should().BeLessThanOrEqualTo(500,
            "a flat aura large enough to dwarf a mid-game player's own Attack stops being a helping "
            + "hand and becomes the only thing that matters");
    }

    /// <summary>
    /// An inert placeholder must be genuinely inert. A placeholder carrying a live number is the
    /// "silent stub that looks complete" the code-labelling rules forbid — it would ship a real
    /// effect nobody designed.
    /// </summary>
    [Fact]
    public void UndesignedPlaceholdersAreTrulyInert()
    {
        using var doc = Magics();
        foreach (var m in doc.RootElement.EnumerateArray())
        {
            var id = m.GetProperty("id").GetString() ?? "?";
            if (!id.StartsWith("magic_pinnacle")) continue;
            var desc = m.GetProperty("description").GetString() ?? "";
            if (!desc.Contains("PLACEHOLDER")) continue;

            m.GetProperty("procAmount").GetDouble().Should().Be(0.0, $"{id} is still a placeholder");
            m.GetProperty("procChance").GetDouble().Should().Be(0.0, $"{id} is still a placeholder");
        }
    }
}
