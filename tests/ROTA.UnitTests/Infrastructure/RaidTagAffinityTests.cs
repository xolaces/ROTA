using System.Text.Json;
using FluentAssertions;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Infrastructure;

/// <summary>
/// Raid tags and the legion affinities that answer them.
///
/// THE RULE THIS PROTECTS IS "HIGHEST-ONLY, NEVER SUMMED". A raid can carry several tags and a legion
/// can answer several of them, so summing is the obvious implementation and the wrong one: a legion
/// with a small bonus against three tags would beat a true specialist at the specialist's own
/// specialty. Combat resolves the best single match, the same way the Gauntlet trophy stage does.
///
/// The content side is checked here rather than only in the boot validator, because the failure a
/// validator cannot catch is a tag that PARSES and is still wrong for the raid it sits on — so these
/// assert the shape of the tagging, not just its legality.
/// </summary>
public class RaidTagAffinityTests
{
    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ROTA.slnx")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("Could not locate the repo root (ROTA.slnx).");
        return dir.FullName;
    }

    private static JsonDocument Content(string file)
        => JsonDocument.Parse(File.ReadAllText(
            Path.Combine(FindRepoRoot(), "src", "ROTA.Api", "content", file)));

    private static List<(string Id, List<string> Tags)> RaidTags()
    {
        using var doc = Content("raids.json");
        return doc.RootElement.EnumerateArray().Select(r => (
            r.GetProperty("id").GetString()!,
            r.TryGetProperty("tags", out var t) && t.ValueKind == JsonValueKind.Array
                ? t.EnumerateArray().Select(x => x.GetString()!).ToList()
                : new List<string>())).ToList();
    }

    private static List<(string Id, Dictionary<string, double> Aff)> LegionAffinities()
    {
        using var doc = Content("legions.json");
        return doc.RootElement.EnumerateArray().Select(l =>
        {
            var d = new Dictionary<string, double>(StringComparer.Ordinal);
            if (l.TryGetProperty("tagAffinities", out var a) && a.ValueKind == JsonValueKind.Object)
                foreach (var p in a.EnumerateObject()) d[p.Name] = p.Value.GetDouble();
            return (l.GetProperty("id").GetString()!, d);
        }).ToList();
    }

    [Fact]
    public void EveryRaidTag_IsARealRaidTag()
    {
        foreach (var (id, tags) in RaidTags())
            foreach (var tag in tags)
                Enum.TryParse<RaidTag>(tag, ignoreCase: false, out _).Should().BeTrue(
                    $"raid '{id}' carries tag '{tag}', which no legion could ever match");
    }

    [Fact]
    public void NoRaidNamesTheNoneTag()
    {
        // "Untagged" is an EMPTY list. Naming None would read as a tag a legion could answer, and
        // an affinity for None is a flat power bonus wearing a counter-play costume.
        foreach (var (id, tags) in RaidTags())
            tags.Should().NotContain("None", $"raid '{id}' should express 'no tag' as an empty list");
    }

    [Fact]
    public void SomeRaidsAreUntagged_OnPurpose()
    {
        // If every raid were tagged, affinity would stop being a bonus for the prepared and become a
        // tax on everyone who did not bring the right legion. The set-pieces answer to nobody.
        var untagged = RaidTags().Where(r => r.Tags.Count == 0).ToList();
        untagged.Should().NotBeEmpty(
            "an untagged raid is the base case the whole mechanic is defined against");
    }

    [Fact]
    public void EveryLegionAffinity_NamesARealTagAtAPositivePercent()
    {
        foreach (var (id, aff) in LegionAffinities())
            foreach (var (tag, pct) in aff)
            {
                Enum.TryParse<RaidTag>(tag, ignoreCase: false, out var parsed).Should().BeTrue(
                    $"legion '{id}' claims an affinity for '{tag}', which is not a RaidTag");
                parsed.Should().NotBe(RaidTag.None,
                    $"legion '{id}' has an affinity for None — that is powerBonus, not counter-play");
                pct.Should().BeGreaterThan(0,
                    $"legion '{id}' has affinity {pct} for '{tag}'; an affinity is a bonus");
            }
    }

    [Fact]
    public void EveryLegionAffinity_HasSomethingInTheWorldToAnswer()
    {
        // An affinity for a tag no raid carries is a promise the content does not keep. This is the
        // check a per-file validator cannot make, because it needs both files at once.
        var live = RaidTags().SelectMany(r => r.Tags).ToHashSet(StringComparer.Ordinal);
        foreach (var (id, aff) in LegionAffinities())
            foreach (var tag in aff.Keys)
                live.Should().Contain(tag,
                    $"legion '{id}' is built to counter '{tag}', but no raid in the game carries it");
    }

    [Fact]
    public void TheStartingLegion_CountersNothing()
    {
        // The Free Warband is what a player is given. If the free kit were also a specialist kit,
        // going and earning a specialist legion would be a downgrade.
        var warband = LegionAffinities().SingleOrDefault(l => l.Id == "legion_warband");
        warband.Aff.Should().BeEmpty(
            "a counter-build should be something you go and get, not something you start holding");
    }

    [Fact]
    public void AtLeastOneLegion_AnswersEachOfTheCommonTags()
    {
        // Not every tag needs an answer — the deep ones (Dragon, Horror) are deliberately unanswered
        // for now, which is content headroom rather than an omission. But the tags a player meets
        // early should have a legion that wants them, or the mechanic never surfaces.
        var answered = LegionAffinities().SelectMany(l => l.Aff.Keys).ToHashSet(StringComparer.Ordinal);
        foreach (var early in new[] { "Construct", "Goblin", "Undead", "Shadow", "Legion" })
            answered.Should().Contain(early,
                $"no legion answers '{early}', which players meet inside the first three chapters");
    }
}
