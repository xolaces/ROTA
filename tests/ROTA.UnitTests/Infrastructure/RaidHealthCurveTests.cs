using System.Text.Json;
using FluentAssertions;

namespace ROTA.UnitTests.Infrastructure;

/// <summary>
/// The campaign raid health curve, and the thing it is coupled to.
///
/// Owner anchors (2026-09-04): "the first few raids should have a few handful of thousands, and we
/// scale into couple million for normal raids by our final chapter, as difficulty tier scales raid
/// health." The shipped curve runs 4,000 -> 2,000,000 across the 23 campaign raids at +32.6% a step,
/// with the difficulty multipliers (1.0 / 1.4 / 2.0 / 3.6) stacking on top.
///
/// THE COUPLING IS THE POINT OF THIS FILE. Raid loot ladders are keyed to FRACTIONS of the raid's own
/// Normal pool — 0.05% of it for the first rung, 40% for the last. Retuning health without
/// regenerating the ladders leaves every rung stranded at the old scale, which is silent: the raid
/// still works, players simply stop reaching rungs they used to. That nearly shipped with the retune
/// that produced these numbers, and the last test here is what would have caught it.
/// </summary>
public class RaidHealthCurveTests
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

    private record Raid(string Id, string Tier, long BaseHp, long PersonalHp);

    private static List<Raid> Raids()
    {
        using var doc = Content("raids.json");
        return doc.RootElement.EnumerateArray().Select(r => new Raid(
            r.GetProperty("id").GetString()!,
            r.GetProperty("tier").GetString()!,
            r.GetProperty("baseHp").GetInt64(),
            r.TryGetProperty("personalBaseHp", out var p) ? p.GetInt64() : 0)).ToList();
    }

    /// <summary>Campaign raids only: World raids are timer-only with baseHp 0 and sit outside the curve.</summary>
    private static List<Raid> Campaign()
        => Raids().Where(r => r.Tier != "World" && !r.Id.StartsWith("raid_lastwatch")).ToList();

    [Fact]
    public void TheFirstCampaignRaid_IsAFewThousand_NotAHundredThousand()
    {
        // It shipped at 120,000, which is what prompted the retune.
        var first = Campaign().First();
        first.BaseHp.Should().BeInRange(2_000, 10_000,
            $"{first.Id} is the first raid a player ever meets; a few handful of thousands, per the owner");
    }

    [Fact]
    public void TheLastCampaignRaid_IsACoupleOfMillion()
    {
        // It shipped at 560,000,000 — 280x the intended ceiling.
        var last = Campaign().Last();
        last.BaseHp.Should().BeInRange(1_500_000, 3_000_000,
            $"{last.Id} closes the campaign at Normal difficulty; Nightmare then puts it at 3.6x");
    }

    [Fact]
    public void TheCurve_RisesMonotonically_AndNeverJumpsATier()
    {
        // The old curve had cliffs — c2z2b 265,000 to c2z3b 863,000 was a 3.3x step inside one chapter,
        // and c5z2b to c5z3b was 3.0x. A smooth ratio is what makes the next raid feel like the next
        // raid rather than a wall.
        var hp = Campaign().Select(r => r.BaseHp).ToList();
        hp.Should().BeInAscendingOrder();
        for (int i = 1; i < hp.Count; i++)
        {
            double step = (double)hp[i] / hp[i - 1];
            step.Should().BeInRange(1.1, 1.6,
                $"step {i} ({Campaign()[i].Id}) jumps {step:F2}x, which is a wall rather than a curve");
        }
    }

    [Fact]
    public void EveryCampaignRaid_HasASoloPoolSmallerThanItsSharedOne()
        => Campaign().Should().AllSatisfy(r =>
            r.PersonalHp.Should().BeLessThan(r.BaseHp,
                $"{r.Id}: a Personal raid is one player, so its pool must be the smaller of the two"));

    [Fact]
    public void RaidLootLadders_StayKeyedToTheirRaidsOwnPool()
    {
        // THE COUPLING. Each generated ladder's top rung is 40% of its raid's Normal pool and its first
        // is 0.05%. If health is retuned and the ladders are not, every rung is stranded at the old
        // scale — the raid still works and players simply stop reaching rungs, which no other test sees.
        var raids = Raids().ToDictionary(r => r.Id, r => r.BaseHp);
        using var tables = Content("loot_tables.json");

        var checked_ = 0;
        foreach (var t in tables.RootElement.EnumerateArray())
        {
            if (t.GetProperty("type").GetString() != "Raid") continue;
            var id = t.GetProperty("id").GetString()!;
            var raidId = id["lt_".Length..];
            // The two hand-authored World tables are keyed on absolute damage, not on a pool fraction.
            if (!raids.TryGetValue(raidId, out var baseHp) || baseHp == 0) continue;

            var rungs = t.GetProperty("difficulties").GetProperty("Normal")
                         .GetProperty("thresholdRewards").EnumerateArray().ToList();
            var top = rungs.Last().GetProperty("damageThreshold").GetInt64();

            top.Should().BeCloseTo((long)(baseHp * 0.40), (ulong)Math.Max(100, baseHp / 100),
                $"{id}'s top rung should be 40% of {raidId}'s {baseHp:N0} pool");
            checked_++;
        }

        checked_.Should().BeGreaterThan(20, "the walk must actually be finding the generated ladders");
    }
}
