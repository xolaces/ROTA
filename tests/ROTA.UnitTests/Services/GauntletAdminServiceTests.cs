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

// BETA (System 16 Slice 2 + 5) — unit tests for GauntletAdminService: open (≤1 active enforced),
// close (guard Active), settle (guard Closed; idempotent payout; re-settle no-op if already Settled).
public class GauntletAdminServiceTests
{
    private sealed class Bundle
    {
        public Mock<IGauntletEventRepository> Events = new();
        public Mock<IGauntletScoringService> Scoring = new();
        public Mock<IGauntletEntryRepository> Entries = new();
        public Mock<IGauntletContentProvider> Content = new();
        public Mock<IGauntletCurrencyRepository> Currency = new();
        public Mock<IPlayerGauntletTrophyRepository> Trophies = new();
        public Mock<IPlayerEventMagicRepository> EventMagics = new();
        public Mock<IPlayerMagicHonorRepository> MagicHonors = new();
        public Mock<IAuditLogRepository> Audit = new();
        public Mock<IMasteryService> Mastery = new();
        public GauntletConfig Config = new();

        public Bundle()
        {
            // Sensible defaults — most tests only override what they exercise.
            Scoring.Setup(s => s.RecomputeRanksAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Entries.Setup(e => e.GetForEventAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<GauntletEntry>());
            // No prior grant references by default → grants are fresh.
            Currency.Setup(c => c.ReferenceExistsAsync(
                    It.IsAny<Guid>(), It.IsAny<GauntletCurrency>(), It.IsAny<GauntletCurrencyTransactionType>(),
                    It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            Currency.Setup(c => c.CreateAsync(
                    It.IsAny<GauntletCurrencyTransaction>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            Trophies.Setup(t => t.UpsertAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            // No event-magics to revoke by default (the consumable hand-off is a deferred follow-up).
            EventMagics.Setup(m => m.RevokeAllForEventAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<PlayerEventMagic>());
            MagicHonors.Setup(h => h.HasHonorAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(false);
            MagicHonors.Setup(h => h.GrantAsync(
                    It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);
            // Real prize bands (matches content/gauntlet_prizes.json) so band lookups are faithful.
            Content.Setup(c => c.GetBandForRank(It.IsAny<int>()))
                .Returns((int rank) => BandForRank(rank));
        }

        public GauntletAdminService Build() => new(
            Events.Object, Scoring.Object, Entries.Object, Content.Object, Currency.Object,
            Trophies.Object, EventMagics.Object, MagicHonors.Object, Audit.Object, Mastery.Object,
            Options.Create(Config));
    }

    // The locked prize bands from content/gauntlet_prizes.json (rank → band).
    private static GauntletPrizeBand? BandForRank(int rank) => rank switch
    {
        1                       => new() { RankFrom = 1,   RankTo = 1,   Tokens = 50, Pitchfork = 10, TrophyId = "trophy_aureate", MagicId = "magic_wrath_of_the_ancients" },
        >= 2   and <= 10        => new() { RankFrom = 2,   RankTo = 10,  Tokens = 25, Pitchfork = 5,  TrophyId = "trophy_argent",  MagicId = "magic_blessing_of_the_ancients" },
        >= 11  and <= 50        => new() { RankFrom = 11,  RankTo = 50,  Tokens = 20, Pitchfork = 2 },
        >= 51  and <= 100       => new() { RankFrom = 51,  RankTo = 100, Tokens = 15, Pitchfork = 1 },
        >= 101 and <= 200       => new() { RankFrom = 101, RankTo = 200, Tokens = 10 },
        >= 201 and <= 499       => new() { RankFrom = 201, RankTo = 499, Tokens = 5 },
        500                     => new() { RankFrom = 500, RankTo = 500, Tokens = 5, TrophyId = "trophy_bronzed" },
        _                       => null,
    };

    // A ranked entry helper — sets LastRank via the public snapshot setter on a fresh entry.
    private static GauntletEntry RankedEntry(Guid eventId, Guid playerId, GauntletLeague league, int rank)
    {
        var e = GauntletEntry.Create(eventId, playerId, league);
        e.SetRank(rank);
        return e;
    }

    private static GauntletEvent EventInState(GauntletEventState state)
    {
        var ev = GauntletEvent.Create("Cycle",
            DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(7));
        if (state >= GauntletEventState.Active)  ev.Activate();
        if (state >= GauntletEventState.Closed)  ev.Close();
        if (state >= GauntletEventState.Settled) ev.MarkSettled();
        return ev;
    }

    // ── Open: ≤1 active ────────────────────────────────────────────────────────

    [Fact]
    public async Task Open_Succeeds_WhenNoActiveEvent()
    {
        var b = new Bundle();
        b.Events.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((GauntletEvent?)null);
        b.Events.Setup(r => r.CreateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GauntletEvent e, CancellationToken _) => e);

        var result = await b.Build().OpenEventAsync(
            "Cycle 1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));

        result.Success.Should().BeTrue();
        result.Event!.State.Should().Be(GauntletEventState.Active.ToString());
        b.Events.Verify(r => r.CreateAsync(
            It.Is<GauntletEvent>(e => e.State == GauntletEventState.Active), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Open_Fails_WhenAnActiveEventAlreadyExists()
    {
        var b = new Bundle();
        b.Events.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(EventInState(GauntletEventState.Active));

        var result = await b.Build().OpenEventAsync(
            "Cycle 2", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("already exists");
        b.Events.Verify(r => r.CreateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Open_Fails_WhenEndsAtNotAfterStartsAt()
    {
        var b = new Bundle();
        b.Events.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((GauntletEvent?)null);
        var now = DateTimeOffset.UtcNow;

        var result = await b.Build().OpenEventAsync("Bad", now, now); // equal → invalid

        result.Success.Should().BeFalse();
        b.Events.Verify(r => r.CreateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Slice 7: cross-event rank-magic consumable hand-off at OPEN ──────────────

    [Fact]
    public async Task Open_HandsOffRankMagics_FromMostRecentSettledEvent_ToNewEvent()
    {
        var b = new Bundle();
        b.Events.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((GauntletEvent?)null);
        GauntletEvent? newEvent = null;
        b.Events.Setup(r => r.CreateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()))
            .Callback((GauntletEvent e, CancellationToken _) => newEvent = e)
            .ReturnsAsync((GauntletEvent e, CancellationToken _) => e);

        // A prior SETTLED event with a rank-1 (Wrath) and a rank-2 (Blessing) winner + an unranked entry.
        var prior = EventInState(GauntletEventState.Settled);
        var rank1 = Guid.NewGuid();
        var rank2 = Guid.NewGuid();
        var unranked = Guid.NewGuid();
        b.Events.Setup(r => r.GetMostRecentSettledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(prior);
        b.Entries.Setup(e => e.GetForEventAsync(prior.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GauntletEntry>
            {
                RankedEntry(prior.Id, rank1, GauntletLeague.Whelpling, 1),
                RankedEntry(prior.Id, rank2, GauntletLeague.Whelpling, 2),
                GauntletEntry.Create(prior.Id, unranked, GauntletLeague.Whelpling), // LastRank null
            });
        // No existing grants on the new event.
        b.EventMagics.Setup(m => m.FindAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerEventMagic?)null);

        var result = await b.Build().OpenEventAsync("Cycle 2", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));

        result.Success.Should().BeTrue();
        newEvent.Should().NotBeNull();
        // Rank-1 → Wrath on the NEW event; rank-2 → Blessing on the NEW event.
        b.EventMagics.Verify(m => m.GrantAsync(rank1, newEvent!.Id, "magic_wrath_of_the_ancients", It.IsAny<CancellationToken>()), Times.Once);
        b.EventMagics.Verify(m => m.GrantAsync(rank2, newEvent!.Id, "magic_blessing_of_the_ancients", It.IsAny<CancellationToken>()), Times.Once);
        // The unranked entry gets nothing.
        b.EventMagics.Verify(m => m.GrantAsync(unranked, It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Open_HandOff_Idempotent_WhenGrantAlreadyExists_NoReGrant()
    {
        var b = new Bundle();
        b.Events.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((GauntletEvent?)null);
        b.Events.Setup(r => r.CreateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GauntletEvent e, CancellationToken _) => e);

        var prior = EventInState(GauntletEventState.Settled);
        var rank1 = Guid.NewGuid();
        b.Events.Setup(r => r.GetMostRecentSettledAsync(It.IsAny<CancellationToken>())).ReturnsAsync(prior);
        b.Entries.Setup(e => e.GetForEventAsync(prior.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GauntletEntry> { RankedEntry(prior.Id, rank1, GauntletLeague.Whelpling, 1) });
        // The grant ALREADY exists on the new event → the pre-check short-circuits.
        b.EventMagics.Setup(m => m.FindAsync(rank1, It.IsAny<Guid>(), "magic_wrath_of_the_ancients", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerEventMagic.Create(rank1, Guid.NewGuid(), "magic_wrath_of_the_ancients"));

        var result = await b.Build().OpenEventAsync("Cycle 3", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));

        result.Success.Should().BeTrue();
        b.EventMagics.Verify(m => m.GrantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "an existing grant must not be re-granted (idempotent hand-off)");
    }

    [Fact]
    public async Task Open_HandOff_NoOp_WhenNoPriorSettledEvent()
    {
        var b = new Bundle();
        b.Events.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((GauntletEvent?)null);
        b.Events.Setup(r => r.CreateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GauntletEvent e, CancellationToken _) => e);
        // GetMostRecentSettledAsync defaults to null (Bundle wires nothing) → first-ever event.

        var result = await b.Build().OpenEventAsync("Cycle 1", DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));

        result.Success.Should().BeTrue();
        b.EventMagics.Verify(m => m.GrantAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "no prior settled event → nobody to hand off to");
    }

    // ── Close: guard Active ─────────────────────────────────────────────────────

    [Fact]
    public async Task Close_Succeeds_FromActive()
    {
        var b = new Bundle();
        var ev = EventInState(GauntletEventState.Active);
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);

        var result = await b.Build().CloseEventAsync(ev.Id);

        result.Success.Should().BeTrue();
        result.Event!.State.Should().Be(GauntletEventState.Closed.ToString());
        b.Events.Verify(r => r.UpdateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Theory]
    [InlineData(GauntletEventState.Scheduled)]
    [InlineData(GauntletEventState.Closed)]
    [InlineData(GauntletEventState.Settled)]
    public async Task Close_Fails_WhenNotActive(GauntletEventState state)
    {
        var b = new Bundle();
        var ev = EventInState(state);
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);

        var result = await b.Build().CloseEventAsync(ev.Id);

        result.Success.Should().BeFalse();
        b.Events.Verify(r => r.UpdateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Close_Fails_WhenEventNotFound()
    {
        var b = new Bundle();
        b.Events.Setup(r => r.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>())).ReturnsAsync((GauntletEvent?)null);

        var result = await b.Build().CloseEventAsync(Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("not found");
    }

    // ── Settle: guard Closed + idempotent ───────────────────────────────────────

    [Fact]
    public async Task Settle_Succeeds_FromClosed_TransitionsToSettled()
    {
        var b = new Bundle();
        var ev = EventInState(GauntletEventState.Closed);
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);

        var result = await b.Build().SettleEventAsync(ev.Id);

        result.Success.Should().BeTrue();
        result.Event!.State.Should().Be(GauntletEventState.Settled.ToString());
        b.Events.Verify(r => r.UpdateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Settle_IsNoOp_WhenAlreadySettled()
    {
        var b = new Bundle();
        var ev = EventInState(GauntletEventState.Settled);
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);

        var result = await b.Build().SettleEventAsync(ev.Id);

        result.Success.Should().BeTrue("re-settle on an already-settled event is an idempotent no-op");
        result.Event!.State.Should().Be(GauntletEventState.Settled.ToString());
        b.Events.Verify(r => r.UpdateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()), Times.Never,
            "no state change is persisted when already settled");
    }

    [Theory]
    [InlineData(GauntletEventState.Scheduled)]
    [InlineData(GauntletEventState.Active)]
    public async Task Settle_Fails_WhenNotClosedAndNotSettled(GauntletEventState state)
    {
        var b = new Bundle();
        var ev = EventInState(state);
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);

        var result = await b.Build().SettleEventAsync(ev.Id);

        result.Success.Should().BeFalse();
        b.Events.Verify(r => r.UpdateAsync(It.IsAny<GauntletEvent>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Slice 5: settlement payout (idempotent) ─────────────────────────────────

    [Fact]
    public async Task Settle_RecomputesRanks_BeforePayout()
    {
        var b  = new Bundle();
        var ev = EventInState(GauntletEventState.Closed);
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);

        await b.Build().SettleEventAsync(ev.Id);

        b.Scoring.Verify(s => s.RecomputeRanksAsync(ev.Id, It.IsAny<CancellationToken>()), Times.Once,
            "settlement snapshots ranks before reading entries to pay");
    }

    [Fact]
    public async Task Settle_Rank1_GrantsTokensPitchforkAndAureateTrophy()
    {
        var b  = new Bundle();
        var ev = EventInState(GauntletEventState.Closed);
        var player = Guid.NewGuid();
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);
        b.Entries.Setup(e => e.GetForEventAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GauntletEntry> { RankedEntry(ev.Id, player, GauntletLeague.Whelpling, 1) });

        var result = await b.Build().SettleEventAsync(ev.Id);

        // 50 Tokens, 10 Pitchfork, Aureate trophy — band rank 1.
        b.Currency.Verify(c => c.CreateAsync(It.Is<GauntletCurrencyTransaction>(
            t => t.Currency == GauntletCurrency.Token && t.Amount == 50
              && t.TransactionType == GauntletCurrencyTransactionType.RankReward
              && t.ReferenceId == $"gauntletsettle:{ev.Id}:{player}:tokens"), It.IsAny<CancellationToken>()), Times.Once);
        b.Currency.Verify(c => c.CreateAsync(It.Is<GauntletCurrencyTransaction>(
            t => t.Currency == GauntletCurrency.Pitchfork && t.Amount == 10
              && t.ReferenceId == $"gauntletsettle:{ev.Id}:{player}:pitchfork"), It.IsAny<CancellationToken>()), Times.Once);
        b.Trophies.Verify(t => t.UpsertAsync(player, "trophy_aureate", It.IsAny<CancellationToken>()), Times.Once);

        result.Settlement!.RanksSettled.Should().Be(1);
        result.Settlement.TokensGranted.Should().Be(50);
        result.Settlement.PitchforkGranted.Should().Be(10);
        result.Settlement.TrophiesGranted.Should().Be(1);
    }

    [Theory]
    [InlineData(2)]
    [InlineData(10)]
    public async Task Settle_Ranks2To10_GrantTokensPitchforkAndArgentTrophy(int rank)
    {
        var b  = new Bundle();
        var ev = EventInState(GauntletEventState.Closed);
        var player = Guid.NewGuid();
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);
        b.Entries.Setup(e => e.GetForEventAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GauntletEntry> { RankedEntry(ev.Id, player, GauntletLeague.Wyrm, rank) });

        await b.Build().SettleEventAsync(ev.Id);

        b.Currency.Verify(c => c.CreateAsync(It.Is<GauntletCurrencyTransaction>(
            t => t.Currency == GauntletCurrency.Token && t.Amount == 25), It.IsAny<CancellationToken>()), Times.Once);
        b.Currency.Verify(c => c.CreateAsync(It.Is<GauntletCurrencyTransaction>(
            t => t.Currency == GauntletCurrency.Pitchfork && t.Amount == 5), It.IsAny<CancellationToken>()), Times.Once);
        b.Trophies.Verify(t => t.UpsertAsync(player, "trophy_argent", It.IsAny<CancellationToken>()), Times.Once,
            "rank 10 is the upper edge of the 2–10 band → Argent trophy");
    }

    [Fact]
    public async Task Settle_Rank500_GrantsTokensAndBronzedTrophy_NoPitchfork()
    {
        var b  = new Bundle();
        var ev = EventInState(GauntletEventState.Closed);
        var player = Guid.NewGuid();
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);
        b.Entries.Setup(e => e.GetForEventAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GauntletEntry> { RankedEntry(ev.Id, player, GauntletLeague.Dragon, 500) });

        var result = await b.Build().SettleEventAsync(ev.Id);

        b.Currency.Verify(c => c.CreateAsync(It.Is<GauntletCurrencyTransaction>(
            t => t.Currency == GauntletCurrency.Token && t.Amount == 5), It.IsAny<CancellationToken>()), Times.Once);
        b.Trophies.Verify(t => t.UpsertAsync(player, "trophy_bronzed", It.IsAny<CancellationToken>()), Times.Once);
        b.Currency.Verify(c => c.CreateAsync(It.Is<GauntletCurrencyTransaction>(
            t => t.Currency == GauntletCurrency.Pitchfork), It.IsAny<CancellationToken>()), Times.Never,
            "rank 500 grants NO pitchfork");
        result.Settlement!.PitchforkGranted.Should().Be(0);
    }

    [Theory]
    [InlineData(11,  20, 2)]   // 11–50 band: tokens + pitchfork
    [InlineData(101, 10, 0)]   // 101–200 band: tokens only, no pitchfork
    [InlineData(499, 5,  0)]   // 201–499 band: tokens only
    public async Task Settle_MidBands_GrantTieredTokens_NoTrophy(int rank, int expectedTokens, int expectedPitchfork)
    {
        var b  = new Bundle();
        var ev = EventInState(GauntletEventState.Closed);
        var player = Guid.NewGuid();
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);
        b.Entries.Setup(e => e.GetForEventAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GauntletEntry> { RankedEntry(ev.Id, player, GauntletLeague.Whelpling, rank) });

        var result = await b.Build().SettleEventAsync(ev.Id);

        result.Settlement!.TokensGranted.Should().Be(expectedTokens);
        result.Settlement.PitchforkGranted.Should().Be(expectedPitchfork);
        b.Trophies.Verify(t => t.UpsertAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "mid-bands grant no trophy");
    }

    [Fact]
    public async Task Settle_SkipsUnrankedAndBeyondPrizeCeiling()
    {
        var b  = new Bundle();
        var ev = EventInState(GauntletEventState.Closed);
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);

        var ranked    = RankedEntry(ev.Id, Guid.NewGuid(), GauntletLeague.Whelpling, 1);
        var unranked  = GauntletEntry.Create(ev.Id, Guid.NewGuid(), GauntletLeague.Whelpling); // LastRank null
        var beyond    = RankedEntry(ev.Id, Guid.NewGuid(), GauntletLeague.Whelpling, 501);      // > PrizeRankCount
        b.Entries.Setup(e => e.GetForEventAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GauntletEntry> { ranked, unranked, beyond });

        var result = await b.Build().SettleEventAsync(ev.Id);

        result.Settlement!.RanksSettled.Should().Be(1, "only the rank-1 entry is inside the prize ceiling");
    }

    [Fact]
    public async Task Settle_PerLeague_EachLeaguesRank1_ReceivesRank1Band()
    {
        // Ranks are per-league (Slice 3 PARTITION BY league), so 3 leagues each have a rank-1 entry.
        // Iterating all entries must pay every league's rank-1 the rank-1 band (3 trophy + token sets).
        var b  = new Bundle();
        var ev = EventInState(GauntletEventState.Closed);
        var p1 = Guid.NewGuid(); var p2 = Guid.NewGuid(); var p3 = Guid.NewGuid();
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);
        b.Entries.Setup(e => e.GetForEventAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GauntletEntry>
            {
                RankedEntry(ev.Id, p1, GauntletLeague.Whelpling, 1),
                RankedEntry(ev.Id, p2, GauntletLeague.Wyrm,      1),
                RankedEntry(ev.Id, p3, GauntletLeague.Dragon,    1),
            });

        var result = await b.Build().SettleEventAsync(ev.Id);

        result.Settlement!.RanksSettled.Should().Be(3);
        result.Settlement.TrophiesGranted.Should().Be(3);
        result.Settlement.TokensGranted.Should().Be(150, "3 × 50 rank-1 tokens");
        result.Settlement.PitchforkGranted.Should().Be(30, "3 × 10 rank-1 pitchfork");
        foreach (var p in new[] { p1, p2, p3 })
            b.Trophies.Verify(t => t.UpsertAsync(p, "trophy_aureate", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Settle_AlreadyGrantedReference_SkipsCredit_NoDoublePay()
    {
        // Token reference already exists (a prior settle committed it) → no second token credit, but the
        // run still completes and marks Settled. The summary reflects nothing new was paid for tokens.
        var b  = new Bundle();
        var ev = EventInState(GauntletEventState.Closed);
        var player = Guid.NewGuid();
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);
        b.Entries.Setup(e => e.GetForEventAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GauntletEntry> { RankedEntry(ev.Id, player, GauntletLeague.Whelpling, 1) });
        // The token reference is reported as already present.
        b.Currency.Setup(c => c.ReferenceExistsAsync(
                player, GauntletCurrency.Token, GauntletCurrencyTransactionType.RankReward,
                $"gauntletsettle:{ev.Id}:{player}:tokens", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Build().SettleEventAsync(ev.Id);

        b.Currency.Verify(c => c.CreateAsync(It.Is<GauntletCurrencyTransaction>(
            t => t.Currency == GauntletCurrency.Token), It.IsAny<CancellationToken>()), Times.Never,
            "an existing token reference must NOT be credited again (money-bug guard)");
        result.Settlement!.TokensGranted.Should().Be(0, "no new tokens were paid");
        // Pitchfork was NOT pre-existing → still credited once.
        b.Currency.Verify(c => c.CreateAsync(It.Is<GauntletCurrencyTransaction>(
            t => t.Currency == GauntletCurrency.Pitchfork), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Settle_HonorEcho_WritesHonorForRevokedHolder_Idempotent()
    {
        var b  = new Bundle();
        var ev = EventInState(GauntletEventState.Closed);
        var holder = Guid.NewGuid();
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);
        // A revoked event-magic for this event → honor must be written for the holder.
        var revoked = PlayerEventMagic.Create(holder, ev.Id, "magic_wrath_of_the_ancients");
        revoked.Revoke();
        b.EventMagics.Setup(m => m.RevokeAllForEventAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEventMagic> { revoked });

        var result = await b.Build().SettleEventAsync(ev.Id);

        b.MagicHonors.Verify(h => h.GrantAsync(holder, "magic_wrath_of_the_ancients", It.IsAny<CancellationToken>()),
            Times.Once, "the revoked holder gets a permanent honor echo");
        result.Settlement!.HonorsWritten.Should().Be(1);
    }

    [Fact]
    public async Task Settle_HonorEcho_AlreadyHasHonor_NotWrittenAgain()
    {
        var b  = new Bundle();
        var ev = EventInState(GauntletEventState.Closed);
        var holder = Guid.NewGuid();
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);
        var revoked = PlayerEventMagic.Create(holder, ev.Id, "magic_wrath_of_the_ancients");
        revoked.Revoke();
        b.EventMagics.Setup(m => m.RevokeAllForEventAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerEventMagic> { revoked });
        b.MagicHonors.Setup(h => h.HasHonorAsync(holder, "magic_wrath_of_the_ancients", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);   // honor already exists

        var result = await b.Build().SettleEventAsync(ev.Id);

        b.MagicHonors.Verify(h => h.GrantAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "honor is permanent and written once — re-settle must not re-grant");
        result.Settlement!.HonorsWritten.Should().Be(0);
    }

    [Fact]
    public async Task Settle_DoesNotGrantNextEventConsumable()
    {
        // GUARDRAIL: settle writes only honor + tokens + pitchfork + trophies — NEVER the next-event
        // PlayerEventMagic consumable (the deferred cross-event hand-off). GrantAsync on event-magics
        // must never be called from settlement.
        var b  = new Bundle();
        var ev = EventInState(GauntletEventState.Closed);
        var player = Guid.NewGuid();
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);
        b.Entries.Setup(e => e.GetForEventAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GauntletEntry> { RankedEntry(ev.Id, player, GauntletLeague.Whelpling, 1) });

        await b.Build().SettleEventAsync(ev.Id);

        b.EventMagics.Verify(m => m.GrantAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "the next-event consumable hand-off is a deferred follow-up — not granted at settle");
    }

    [Fact]
    public async Task Settle_MarksSettled_OnlyAfterGrantsCommit()
    {
        // The event is updated to Settled exactly once, and only after the payout pass.
        var b  = new Bundle();
        var ev = EventInState(GauntletEventState.Closed);
        var player = Guid.NewGuid();
        b.Events.Setup(r => r.FindByIdAsync(ev.Id, It.IsAny<CancellationToken>())).ReturnsAsync(ev);
        b.Entries.Setup(e => e.GetForEventAsync(ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<GauntletEntry> { RankedEntry(ev.Id, player, GauntletLeague.Whelpling, 1) });

        var result = await b.Build().SettleEventAsync(ev.Id);

        result.Event!.State.Should().Be(GauntletEventState.Settled.ToString());
        b.Events.Verify(r => r.UpdateAsync(
            It.Is<GauntletEvent>(e => e.State == GauntletEventState.Settled), It.IsAny<CancellationToken>()), Times.Once);
    }
}
