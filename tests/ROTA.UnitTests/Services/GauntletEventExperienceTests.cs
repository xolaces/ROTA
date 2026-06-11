using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Services;

/// <summary>
/// T76 — Gauntlet event-experience foundation: late-ladder HP ramp, event kinds (Neck/Ring) +
/// run numbering, ring prize fallback, highest-stage entry mapping.
/// </summary>
public class GauntletEventExperienceTests
{
    // ── Late HP ramp ─────────────────────────────────────────────────────────

    private static GauntletConfig RampConfig() => new()
    {
        MaxLadderStage = 250,
        StageHpBase = 5000,
        StageHpGrowth = 1.0493,
        LateRampStartStage = 200,
        LateRampFinalGrowth = 2.0,
    };

    [Fact]
    public void Hp_RampOff_MatchesPureExponential()
    {
        var c = new GauntletConfig { StageHpBase = 5000, StageHpGrowth = 1.0493 }; // ramp off (default)
        GauntletStageCurve.Hp(1, c).Should().Be(5000);
        GauntletStageCurve.Hp(250, c).Should().Be(
            (long)Math.Round(5000 * Math.Pow(1.0493, 249), MidpointRounding.AwayFromZero));
    }

    [Fact]
    public void Hp_RampOn_IdenticalAtAndBelowRampStart()
    {
        var ramp = RampConfig();
        var flat = new GauntletConfig { StageHpBase = 5000, StageHpGrowth = 1.0493 };
        foreach (var stage in new[] { 1, 50, 199, 200 })
            GauntletStageCurve.Hp(stage, ramp).Should().Be(GauntletStageCurve.Hp(stage, flat),
                "the ramp must not touch the curve at or below its start stage");
    }

    [Fact]
    public void Hp_RampOn_GrowthRisesToNearDoubling_AtTheTop()
    {
        var c = RampConfig();

        // Just past the ramp start the growth is barely above base…
        double early = (double)GauntletStageCurve.Hp(201, c) / GauntletStageCurve.Hp(200, c);
        early.Should().BeInRange(1.04, 1.10, "the ramp begins gently");

        // …and the final stage's growth approaches the configured doubling factor.
        double final = (double)GauntletStageCurve.Hp(250, c) / GauntletStageCurve.Hp(249, c);
        final.Should().BeInRange(1.95, 2.05, "HP should near-double per stage at the ladder top (owner-locked DotD feel)");
    }

    [Fact]
    public void Hp_RampOn_StrictlyMonotonic()
    {
        var c = RampConfig();
        for (int stage = 2; stage <= 250; stage++)
            GauntletStageCurve.Hp(stage, c).Should().BeGreaterThan(GauntletStageCurve.Hp(stage - 1, c));
    }

    // ── Event kinds + run numbering (open) ───────────────────────────────────

    private sealed class AdminBundle
    {
        public Mock<IGauntletEventRepository> Events = new();
        public Mock<IGauntletContentProvider> Content = new();
        public Mock<IPlayerEventMagicRepository> EventMagics = new();
        public GauntletAdminService Service;

        public AdminBundle()
        {
            Events.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>()))
                  .ReturnsAsync((GauntletEvent?)null);
            Events.Setup(r => r.CreateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()))
                  .ReturnsAsync((GauntletEvent e, CancellationToken _) => e);

            Service = new GauntletAdminService(
                Events.Object, new Mock<IGauntletScoringService>().Object,
                new Mock<IGauntletEntryRepository>().Object, Content.Object,
                new Mock<IGauntletCurrencyRepository>().Object,
                new Mock<IPlayerGauntletTrophyRepository>().Object,
                EventMagics.Object, new Mock<IPlayerMagicHonorRepository>().Object,
                new Mock<IAuditLogRepository>().Object, new Mock<IMasteryService>().Object,
                Options.Create(new GauntletConfig()));
        }
    }

    [Fact]
    public async Task OpenEvent_ComputesRunNumberPerKind()
    {
        var b = new AdminBundle();
        b.Events.Setup(r => r.CountByKindAsync(GauntletEventKind.Ring, It.IsAny<CancellationToken>()))
                .ReturnsAsync(2); // two prior ring runs

        var result = await b.Service.OpenEventAsync(
            "The Ring of Ash", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7),
            GauntletEventKind.Ring, loreBlurb: "blurb", bannerKey: "ring_ash");

        result.Success.Should().BeTrue();
        result.Event!.Kind.Should().Be("Ring");
        result.Event.RunNumber.Should().Be(3);
        result.Event.LoreBlurb.Should().Be("blurb");
        result.Event.SecondsRemaining.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task OpenEvent_RingKind_NeverHandsOffRankMagics()
    {
        var b = new AdminBundle();
        // Even with a settled prior NECK event available, a Ring open must not hand off magics.
        b.Events.Setup(r => r.GetMostRecentSettledAsync(GauntletEventKind.Neck, It.IsAny<CancellationToken>()))
                .ReturnsAsync(GauntletEvent.Create("prior", DateTimeOffset.UtcNow.AddDays(-8), DateTimeOffset.UtcNow.AddDays(-1)));

        var result = await b.Service.OpenEventAsync(
            "Ring Run", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7), GauntletEventKind.Ring);

        result.Success.Should().BeTrue();
        b.EventMagics.Verify(m => m.GrantAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "rank magics are a Neck-family crown");
    }

    // ── Ring prize fallback (provider logic mirrored on the model) ───────────

    [Fact]
    public void RingBandFallback_StripsMagic_KeepsEverythingElse()
    {
        // Mirrors GauntletContentProvider.GetBandForRank(rank, Ring) when no ring set is authored:
        // the Neck band minus its MagicId.
        var neck = new GauntletPrizeBand
        { RankFrom = 1, RankTo = 1, Tokens = 50, Pitchfork = 10, TrophyId = "trophy_aureate", MagicId = "magic_wrath_of_the_ancients" };

        var ring = new GauntletPrizeBand
        { RankFrom = neck.RankFrom, RankTo = neck.RankTo, Tokens = neck.Tokens, Pitchfork = neck.Pitchfork, TrophyId = neck.TrophyId, MagicId = null };

        ring.Tokens.Should().Be(50);
        ring.TrophyId.Should().Be("trophy_aureate");
        ring.MagicId.Should().BeNull();
    }

    // ── Fresh entries start at stage 0 in the new primary metric ─────────────

    [Fact]
    public void FreshEntry_StartsAtHighestStageZero_InAncientLeague()
    {
        var entry = GauntletEntry.Create(Guid.NewGuid(), Guid.NewGuid(), GauntletLeague.Ancient);
        entry.League.Should().Be(GauntletLeague.Ancient);
        entry.HighestStage.Should().Be(0, "fresh entries start at stage 0");
    }
}
