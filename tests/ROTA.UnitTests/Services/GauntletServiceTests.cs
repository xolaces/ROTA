using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Services;

// BETA (System 16 Slice 2) — unit tests for GauntletService: join (league lock at band edges,
// reject below MinEntryLevel / banned / soft-deleted, idempotent double-join) and gem→strikes buy.
public class GauntletServiceTests
{
    // ── Helpers ────────────────────────────────────────────────────────────────

    private sealed class Bundle
    {
        public Mock<IGauntletEventRepository> Events = new();
        public Mock<IGauntletEntryRepository> Entries = new();
        public Mock<IStrikeRepository> Strikes = new();
        public Mock<IGauntletCurrencyRepository> Currency = new();
        public Mock<IGauntletContentProvider> Content = new();
        public Mock<IPlayerRepository> Players = new();
        public Mock<IGemService> Gems = new();
        public Mock<IAuditLogRepository> Audit = new();
        public GauntletConfig Config = new();

        public GauntletService Build()
            => new(Events.Object, Entries.Object, Strikes.Object, Currency.Object,
                   Content.Object, Players.Object, Gems.Object, Audit.Object,
                   Options.Create(Config));
    }

    // Builds a player at an exact level: each level costs exactly 1 XP, so AddExperience(level-1)
    // takes Level 1 → level.
    private static Player PlayerAtLevel(int level)
    {
        var p = Player.Create("gauntleteer", "g@rota.test", "hash");
        if (level > 1) p.AddExperience(level - 1, _ => 1);
        return p;
    }

    private static GauntletEvent ActiveEvent()
    {
        var ev = GauntletEvent.Create("Cycle 1",
            DateTimeOffset.UtcNow.AddHours(-1), DateTimeOffset.UtcNow.AddDays(7));
        ev.Activate();
        return ev;
    }

    private void WireActiveEvent(Bundle b, GauntletEvent ev)
        => b.Events.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync(ev);

    private void WireLeague(Bundle b)
        // Real league resolution from default bounds (Whelpling 1-1999, Wyrm 2000-9999, Dragon 10000+).
        => b.Content.Setup(c => c.ResolveLeague(It.IsAny<int>()))
            .Returns<int>(level => level <= 1999 ? GauntletLeague.Whelpling
                                  : level <= 9999 ? GauntletLeague.Wyrm
                                  : GauntletLeague.Dragon);

    // ── Join: league locked at each band edge ────────────────────────────────

    [Theory]
    [InlineData(1999, GauntletLeague.Whelpling)]
    [InlineData(2000, GauntletLeague.Wyrm)]
    [InlineData(9999, GauntletLeague.Wyrm)]
    [InlineData(10000, GauntletLeague.Dragon)]
    public async Task Join_AssignsCorrectLeague_AtBandEdges(int level, GauntletLeague expected)
    {
        var b = new Bundle();
        var ev = ActiveEvent();
        var player = PlayerAtLevel(level);
        WireActiveEvent(b, ev);
        WireLeague(b);
        b.Players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        b.Entries.Setup(r => r.FindByEventAndPlayerAsync(ev.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GauntletEntry?)null);
        b.Entries.Setup(r => r.UpsertAsync(It.IsAny<GauntletEntry>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GauntletEntry e, CancellationToken _) => e);

        var result = await b.Build().JoinEventAsync(player.Id);

        result.Success.Should().BeTrue();
        result.Entry!.League.Should().Be(expected.ToString());
    }

    [Fact]
    public async Task Join_Rejected_WhenBelowMinEntryLevel()
    {
        var b = new Bundle();
        var ev = ActiveEvent();
        var player = PlayerAtLevel(19); // MinEntryLevel default 20
        WireActiveEvent(b, ev);
        WireLeague(b);
        b.Players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        b.Entries.Setup(r => r.FindByEventAndPlayerAsync(ev.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GauntletEntry?)null);

        var result = await b.Build().JoinEventAsync(player.Id);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("level 20");
        b.Entries.Verify(r => r.UpsertAsync(It.IsAny<GauntletEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Join_Rejected_WhenBanned()
    {
        var b = new Bundle();
        var ev = ActiveEvent();
        var player = PlayerAtLevel(50);
        player.Ban("cheating");
        WireActiveEvent(b, ev);
        b.Players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        b.Entries.Setup(r => r.FindByEventAndPlayerAsync(ev.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GauntletEntry?)null);

        var result = await b.Build().JoinEventAsync(player.Id);

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("Banned");
        b.Entries.Verify(r => r.UpsertAsync(It.IsAny<GauntletEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Join_Rejected_WhenSoftDeleted()
    {
        var b = new Bundle();
        var ev = ActiveEvent();
        var player = PlayerAtLevel(50);
        player.SoftDelete();
        WireActiveEvent(b, ev);
        b.Players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        b.Entries.Setup(r => r.FindByEventAndPlayerAsync(ev.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((GauntletEntry?)null);

        var result = await b.Build().JoinEventAsync(player.Id);

        result.Success.Should().BeFalse();
        b.Entries.Verify(r => r.UpsertAsync(It.IsAny<GauntletEntry>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Join_Rejected_WhenNoActiveEvent()
    {
        var b = new Bundle();
        b.Events.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((GauntletEvent?)null);

        var result = await b.Build().JoinEventAsync(Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("no active");
    }

    [Fact]
    public async Task Join_Idempotent_ReturnsExistingEntry_LeagueNotReEvaluated_EvenIfLevelChanged()
    {
        var b = new Bundle();
        var ev = ActiveEvent();
        var playerId = Guid.NewGuid();
        // Existing entry locked in Whelpling (the player joined while ≤1999).
        var existing = GauntletEntry.Create(ev.Id, playerId, GauntletLeague.Whelpling);
        WireActiveEvent(b, ev);
        b.Entries.Setup(r => r.FindByEventAndPlayerAsync(ev.Id, playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existing);

        var result = await b.Build().JoinEventAsync(playerId);

        result.Success.Should().BeTrue();
        result.Entry!.League.Should().Be(GauntletLeague.Whelpling.ToString(),
            "league is locked at first join and must not be re-evaluated on re-join");
        // No new entry created, league never recomputed, no player lookup needed.
        b.Entries.Verify(r => r.UpsertAsync(It.IsAny<GauntletEntry>(), It.IsAny<CancellationToken>()), Times.Never);
        b.Content.Verify(c => c.ResolveLeague(It.IsAny<int>()), Times.Never);
    }

    // ── Buy strikes (gem → strikes) ───────────────────────────────────────────

    [Fact]
    public async Task BuyStrikes_CreditsStrikes_OnChargedGems()
    {
        var b = new Bundle();
        b.Config.StrikeGemPrice = 2;
        var playerId = Guid.NewGuid();
        b.Gems.Setup(g => g.SpendGemsAsync(playerId, 20, GemTransactionType.GauntletStrikePurchase,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GemSpendOutcome.Charged);
        b.Strikes.Setup(s => s.ReferenceExistsAsync(playerId, StrikeTransactionType.GemPurchase,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        b.Strikes.Setup(s => s.GetBalanceAsync(playerId, It.IsAny<CancellationToken>())).ReturnsAsync(10L);

        var result = await b.Build().BuyStrikesAsync(playerId, 10, "key-1");

        result.Success.Should().BeTrue();
        result.GemCost.Should().Be(20);
        result.NewStrikeBalance.Should().Be(10L);
        b.Strikes.Verify(s => s.CreateAsync(
            It.Is<StrikeTransaction>(t => t.Amount == 10 && t.TransactionType == StrikeTransactionType.GemPurchase),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BuyStrikes_InsufficientGems_NoCredit()
    {
        var b = new Bundle();
        var playerId = Guid.NewGuid();
        b.Gems.Setup(g => g.SpendGemsAsync(playerId, It.IsAny<int>(), GemTransactionType.GauntletStrikePurchase,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GemSpendOutcome.InsufficientBalance);

        var result = await b.Build().BuyStrikesAsync(playerId, 5, "key-2");

        result.Success.Should().BeFalse();
        result.FailureReason.Should().Contain("Insufficient gems");
        b.Strikes.Verify(s => s.CreateAsync(It.IsAny<StrikeTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuyStrikes_Idempotent_OnRetry_GemAlreadyProcessed_StrikeAlreadyCredited_NoDoubleCredit()
    {
        // Crash-recovery replay: the gem charge AND the strike credit both committed on the first
        // attempt. The retry must NOT write a second strike row.
        var b = new Bundle();
        var playerId = Guid.NewGuid();
        b.Gems.Setup(g => g.SpendGemsAsync(playerId, It.IsAny<int>(), GemTransactionType.GauntletStrikePurchase,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GemSpendOutcome.AlreadyProcessed);
        b.Strikes.Setup(s => s.ReferenceExistsAsync(playerId, StrikeTransactionType.GemPurchase,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // strike credit already exists
        b.Strikes.Setup(s => s.GetBalanceAsync(playerId, It.IsAny<CancellationToken>())).ReturnsAsync(10L);

        var result = await b.Build().BuyStrikesAsync(playerId, 10, "key-3");

        result.Success.Should().BeTrue("AlreadyProcessed is an idempotent success");
        b.Strikes.Verify(s => s.CreateAsync(It.IsAny<StrikeTransaction>(), It.IsAny<CancellationToken>()), Times.Never,
            "no second strike credit may be written on replay");
    }

    [Fact]
    public async Task BuyStrikes_Recovers_WhenGemAlreadyProcessed_ButStrikeCreditWasLost()
    {
        // The gem charge committed but the strike credit was lost on a previous crash. The retry
        // gets AlreadyProcessed from gems AND no existing strike reference → it MUST credit strikes.
        var b = new Bundle();
        var playerId = Guid.NewGuid();
        b.Gems.Setup(g => g.SpendGemsAsync(playerId, It.IsAny<int>(), GemTransactionType.GauntletStrikePurchase,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GemSpendOutcome.AlreadyProcessed);
        b.Strikes.Setup(s => s.ReferenceExistsAsync(playerId, StrikeTransactionType.GemPurchase,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // strike credit NOT yet written
        b.Strikes.Setup(s => s.GetBalanceAsync(playerId, It.IsAny<CancellationToken>())).ReturnsAsync(10L);

        var result = await b.Build().BuyStrikesAsync(playerId, 10, "key-4");

        result.Success.Should().BeTrue();
        b.Strikes.Verify(s => s.CreateAsync(It.IsAny<StrikeTransaction>(), It.IsAny<CancellationToken>()), Times.Once,
            "the lost strike credit must be re-applied (idempotent recovery)");
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public async Task BuyStrikes_Rejected_WhenStrikesNotPositive(int strikes)
    {
        var b = new Bundle();
        var result = await b.Build().BuyStrikesAsync(Guid.NewGuid(), strikes, "key");
        result.Success.Should().BeFalse();
        b.Gems.Verify(g => g.SpendGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuyStrikes_Rejected_WhenIdempotencyKeyMissing()
    {
        var b = new Bundle();
        var result = await b.Build().BuyStrikesAsync(Guid.NewGuid(), 5, "  ");
        result.Success.Should().BeFalse();
        b.Gems.Verify(g => g.SpendGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
