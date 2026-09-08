using System.Text.Json;
using FluentAssertions;

namespace ROTA.UnitTests.Balance;

/// <summary>
/// Pins the damage-proc band, which is the one place ROTA can repeat Dawn of the Dragons' fatal
/// mistake without anybody noticing for a year.
/// </summary>
/// <remarks>
/// <para>The research paper documents the arc precisely: Dawn's 2016 anniversary proc was 10% for
/// +160,000 flat; its 2019 one was 9% for +2,000%, plus +500% against most raid types, plus +100%
/// per set piece owned. Nothing about that happened in a single release. Each year added one more
/// multiplier onto a stack that already existed, every set obsoleted the previous year's investment,
/// and new-player acquisition had collapsed by 2017.</para>
///
/// <para>Before this rebalance ROTA's own catalogue was already partway along that arc: 24 damage
/// procs from +2% to <b>+4,200%</b>, expected values 840x apart, and the worst offender delivering a
/// perfectly ordinary EV as a 1-in-250 slot machine.</para>
///
/// <para><b>The rule these tests enforce.</b> Magnitude is fixed per rarity inside the owner's
/// 70-110% band; rarity moves only the frequency. That makes expected value monotonic with rarity
/// <i>by construction</i> — which is what kills the whole class of bug where an Orange item is
/// quietly worse than the Grey starter (a live example of which existed on mounts).</para>
///
/// <para>Deliberately out of scope: GoldProc and XpProc magics keep large multipliers, because a
/// 600% XP proc is a pacing knob rather than a damage-scaling risk, and CritChanceFlat magics carry
/// no proc magnitude at all. The five inert pinnacle placeholders are reserved for the milestone-level
/// winners to design and are exempt until they carry real values.</para>
/// </remarks>
public class MagicProcBandTests
{
    private const double MaxDamageMagnitude = 1.10;   // the owner's ceiling
    private const double MinDamageMagnitude = 0.70;   // the owner's floor

    private static readonly string[] RarityOrder = { "Grey", "White", "Green", "Blue", "Purple", "Orange" };

    /// <summary>Always-on magics: 100% chance at a small magnitude, exempt from the band by design.</summary>
    private static readonly HashSet<string> HitChasers = new() { "magic_whetstone", "magic_metronome" };

    /// <summary>
    /// Gauntlet RANK magics — seasonal trophies handed to the top Gauntlet player, carrying
    /// <c>OffCap = true</c> and their own locked-number tests. They are prize content, not catalogue
    /// content, so the band does not govern them.
    /// </summary>
    /// <remarks>
    /// Worth knowing: the off-cap aura path these were built for was REMOVED (see RaidService — "there
    /// is no longer any off-cap aura path at all; offCapBonus stays 0"), so they now flow through the
    /// ordinary magic loop at EV 0.675 and 0.637 — roughly 3.4x the strongest normal Orange. That is
    /// an owner decision, not a test failure, and it is recorded in docs/AGENT_BACKLOG.md.
    /// </remarks>
    private static readonly HashSet<string> RankMagics = new()
    {
        "magic_wrath_of_the_ancients", "magic_blessing_of_the_ancients",
    };

    /// <summary>Inert placeholders awaiting a milestone winner's design.</summary>
    private static readonly HashSet<string> Reserved = new()
    {
        "magic_pinnacle_5000", "magic_pinnacle_7500", "magic_pinnacle_10000",
        "magic_pinnacle_15000", "magic_pinnacle_25000",
    };

    private record Magic(string Id, string Rarity, string EffectType, double Chance, double Amount)
    {
        public double Ev => Chance * Amount;
    }

    private static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ROTA.slnx")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("Could not locate the repo root (ROTA.slnx).");
        return dir.FullName;
    }

    private static List<Magic> DamageProcs()
    {
        var json = File.ReadAllText(Path.Combine(
            FindRepoRoot(), "src", "ROTA.Api", "content", "magics.json"));
        using var doc = JsonDocument.Parse(json);
        var list = new List<Magic>();
        foreach (var m in doc.RootElement.EnumerateArray())
        {
            var effect = m.GetProperty("effectType").GetString() ?? "";
            if (effect != "DamageProc") continue;
            var id = m.GetProperty("id").GetString() ?? "?";
            if (Reserved.Contains(id) || RankMagics.Contains(id)) continue;
            list.Add(new Magic(
                id,
                m.GetProperty("rarity").GetString() ?? "?",
                effect,
                m.GetProperty("procChance").GetDouble(),
                m.GetProperty("procAmount").GetDouble()));
        }
        return list;
    }

    /// <summary>
    /// The ceiling. A single entry above this is how the Dawniversary spiral starts — it is never one
    /// release that breaks a game's damage curve, it is the first entry that establishes a new normal.
    /// </summary>
    [Fact]
    public void NoDamageProcExceedsTheCeiling()
    {
        var over = DamageProcs()
            .Where(m => !HitChasers.Contains(m.Id) && m.Amount > MaxDamageMagnitude)
            .Select(m => $"{m.Id} at {m.Amount:P0}")
            .ToList();

        over.Should().BeEmpty("the owner's damage-proc ceiling is 110%; Dawn's 2019 mount was 2,000%");
    }

    /// <summary>The floor — a band proc below it is a rounding error a player will never feel.</summary>
    [Fact]
    public void NoBandProcIsBelowTheFloor()
    {
        var under = DamageProcs()
            .Where(m => !HitChasers.Contains(m.Id) && m.Amount < MinDamageMagnitude)
            .Select(m => $"{m.Id} at {m.Amount:P0}")
            .ToList();

        under.Should().BeEmpty("a proc under 70% is invisible; make it a hit chaser instead");
    }

    /// <summary>
    /// Rarity buys chance, not magnitude — so every entry of a rarity shares one magnitude. This is
    /// the mechanism that makes the monotonicity below structural rather than coincidental.
    /// </summary>
    [Fact]
    public void MagnitudeIsFixedPerRarity()
    {
        foreach (var group in DamageProcs().Where(m => !HitChasers.Contains(m.Id)).GroupBy(m => m.Rarity))
        {
            var distinct = group.Select(m => m.Amount).Distinct().ToList();
            distinct.Should().HaveCount(1,
                $"{group.Key} damage procs must all share one magnitude, found "
                + string.Join(", ", distinct.Select(d => d.ToString("P0"))));
        }
    }

    /// <summary>
    /// The payoff. Every entry of a higher rarity must beat every entry of a lower one on expected
    /// value — bands must not even touch. The live counter-example this prevents: the Orange Pano
    /// Steed's proc (EV 0.084) sat BELOW the Grey starter Draft Horse's (0.100), so the chase mount's
    /// proc line read as a downgrade from the one the player was given for free.
    /// </summary>
    [Fact]
    public void ExpectedValueIsMonotonicWithRarity()
    {
        var byRarity = DamageProcs()
            .Where(m => !HitChasers.Contains(m.Id))
            .GroupBy(m => m.Rarity)
            .OrderBy(g => Array.IndexOf(RarityOrder, g.Key))
            .ToList();

        for (int i = 1; i < byRarity.Count; i++)
        {
            var lower = byRarity[i - 1];
            var upper = byRarity[i];
            var lowerMax = lower.Max(m => m.Ev);
            var upperMin = upper.Min(m => m.Ev);

            upperMin.Should().BeGreaterThan(lowerMax,
                $"the weakest {upper.Key} ({upper.OrderBy(m => m.Ev).First().Id}, EV {upperMin:F3}) "
                + $"must beat the strongest {lower.Key} ({lower.OrderByDescending(m => m.Ev).First().Id}, "
                + $"EV {lowerMax:F3})");
        }
    }

    /// <summary>
    /// Owner decision: Smite and Blessing of Might are the pinnacle pair, so nothing may out-value
    /// them. Pinning it here means a later content pass cannot quietly demote them by adding a
    /// stronger Orange.
    /// </summary>
    [Fact]
    public void SmiteAndBlessingOfMightAreThePinnacle()
    {
        var all = DamageProcs().Where(m => !HitChasers.Contains(m.Id)).ToList();
        var top2 = all.OrderByDescending(m => m.Ev).Take(2).Select(m => m.Id).ToHashSet();

        top2.Should().BeEquivalentTo(new[] { "magic_smite", "magic_blessing_of_might" });
        all.Single(m => m.Id == "magic_smite").Rarity.Should().Be("Orange");
        all.Single(m => m.Id == "magic_blessing_of_might").Rarity.Should().Be("Orange");
    }

    /// <summary>
    /// At most two always-on magics (owner). Four had accumulated; every additional one flattens the
    /// difference between a considered loadout and a full one.
    /// </summary>
    [Fact]
    public void AtMostTwoHitChasersExist()
    {
        var alwaysOn = DamageProcs().Where(m => m.Chance >= 1.0).Select(m => m.Id).ToList();

        alwaysOn.Should().HaveCountLessThanOrEqualTo(2);
        alwaysOn.Should().BeSubsetOf(HitChasers);
    }

    /// <summary>
    /// A hit chaser trades magnitude for certainty. If one ever reaches band magnitude it is strictly
    /// better than every rolled proc of its rarity and the whole ladder collapses onto it.
    /// </summary>
    [Fact]
    public void HitChasersStaySmall()
    {
        foreach (var m in DamageProcs().Where(m => HitChasers.Contains(m.Id)))
        {
            m.Chance.Should().Be(1.0, $"{m.Id} is a hit chaser");
            m.Amount.Should().BeLessThan(0.15,
                $"{m.Id} fires on every hit; at band magnitude it would dominate the catalogue");
        }
    }
}
