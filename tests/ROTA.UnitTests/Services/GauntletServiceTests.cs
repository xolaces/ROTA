using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

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
        // Slice 6 — token shop
        public Mock<IGauntletShopProvider> Shop = new();
        public Mock<ILegionService> Legions = new();
        public Mock<IEquipmentService> Equipment = new();
        // Slice 7 — ladder summon/auto-advance
        public Mock<IActiveRaidRepository> Raids = new();
        public Mock<IRaidService> RaidService = new();

        public GauntletService Build()
            => new(Events.Object, Entries.Object, Strikes.Object, Currency.Object,
                   Content.Object, Players.Object, Gems.Object, Audit.Object,
                   Options.Create(Config), Shop.Object, Legions.Object, Equipment.Object,
                   Raids.Object, RaidService.Object);
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

    // ── Slice 7 — ladder summon / auto-advance ──────────────────────────────────

    private const int StageCount = 6;

    // Six rising-HP ladder stages mirroring content/gauntlet_raids.json (relative HP only matters).
    private static List<GauntletRaidDefinition> LadderStages()
        => Enumerable.Range(1, StageCount)
            .Select(n => new GauntletRaidDefinition
            {
                Id = $"gauntlet_stage_{n}", Name = $"Stage {n}", Tier = "Event",
                LadderStage = n, BaseHp = 5000L * n, TimerHours = 24, GauntletScored = true,
            })
            .ToList();

    private void WireLadderContent(Bundle b)
    {
        var stages = LadderStages();
        b.Content.Setup(c => c.GetGauntletRaids()).Returns(stages);
        b.Content.Setup(c => c.GetGauntletRaidByStage(It.IsAny<int>()))
            .Returns<int>(n => stages.FirstOrDefault(s => s.LadderStage == n));
    }

    // A gauntlet ladder ActiveRaid for the given stage, optionally already defeated.
    private static ActiveRaid StageRaid(Guid eventId, Guid playerId, int stage, bool defeated)
    {
        var raid = ActiveRaid.Create(
            $"gauntlet_stage_{stage}", playerId, maxHp: 5000L * stage,
            expiresAt: DateTimeOffset.UtcNow.AddHours(24),
            difficulty: RaidDifficulty.Normal, size: RaidSize.Personal);
        raid.LinkGauntletEvent(eventId);
        if (defeated) { raid.TakeDamage(raid.MaxHp); raid.MarkDefeated(); }
        return raid;
    }

    // Wires the join check + the gauntlet-stages query; returns the captured list so a test can mutate.
    private List<ActiveRaid> WireJoinedWithStages(Bundle b, GauntletEvent ev, Guid playerId, List<ActiveRaid> stages)
    {
        b.Entries.Setup(r => r.FindByEventAndPlayerAsync(ev.Id, playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GauntletEntry.Create(ev.Id, playerId, GauntletLeague.Whelpling));
        b.Raids.Setup(r => r.GetGauntletStagesForPlayerAsync(playerId, ev.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => stages);
        // The projection of the current/spawned raid back to a response (id echoed through).
        b.RaidService.Setup(s => s.GetRaidByIdAsync(It.IsAny<Guid>(), playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid id, Guid _, CancellationToken _) => new ActiveRaidResponse { ActiveRaidId = id });
        return stages;
    }

    [Fact]
    public async Task GetLadder_NoActiveEvent_ReturnsNoActiveEvent()
    {
        var b = new Bundle();
        WireLadderContent(b);
        b.Events.Setup(r => r.GetActiveAsync(It.IsAny<CancellationToken>())).ReturnsAsync((GauntletEvent?)null);

        var result = await b.Build().GetLadderAsync(Guid.NewGuid());

        result.NoActiveEvent.Should().BeTrue();
        result.JoinedRequired.Should().BeFalse();
        result.Complete.Should().BeFalse();
        result.ActiveRaid.Should().BeNull();
        result.StageCount.Should().Be(StageCount);
        b.Raids.Verify(r => r.CreateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetLadder_NotJoined_ReturnsJoinedRequired_NoSpawn()
    {
        var b = new Bundle();
        var ev = ActiveEvent();
        WireLadderContent(b);
        WireActiveEvent(b, ev);
        b.Entries.Setup(r => r.FindByEventAndPlayerAsync(ev.Id, It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GauntletEntry?)null);

        var result = await b.Build().GetLadderAsync(Guid.NewGuid());

        result.JoinedRequired.Should().BeTrue();
        result.ActiveRaid.Should().BeNull();
        b.Raids.Verify(r => r.CreateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GetLadder_JoinedFirstCall_SpawnsStage1_PersonalStampedCorrectHp()
    {
        var b = new Bundle();
        var ev = ActiveEvent();
        var playerId = Guid.NewGuid();
        WireLadderContent(b);
        WireActiveEvent(b, ev);
        WireJoinedWithStages(b, ev, playerId, new List<ActiveRaid>()); // no stages yet
        ActiveRaid? created = null;
        b.Raids.Setup(r => r.CreateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()))
            .Callback((ActiveRaid r, CancellationToken _) => created = r)
            .ReturnsAsync((ActiveRaid r, CancellationToken _) => r);

        var result = await b.Build().GetLadderAsync(playerId);

        result.CurrentStage.Should().Be(1);
        result.Complete.Should().BeFalse();
        result.JoinedRequired.Should().BeFalse();
        result.ActiveRaid.Should().NotBeNull();
        created.Should().NotBeNull();
        created!.RaidDefinitionId.Should().Be("gauntlet_stage_1");
        created.Size.Should().Be(RaidSize.Personal);
        created.GauntletEventId.Should().Be(ev.Id, "the spawned stage is stamped with the active event");
        created.MaxHp.Should().Be(5000L, "stage-1 baseHp with NO difficulty multiplier");
        created.ExpiresAt.Should().Be(ev.EndsAt, "every stage shares the event window");
        b.Raids.Verify(r => r.CreateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetLadder_ActiveStageExists_ReturnedAsIs_NotReSpawned()
    {
        var b = new Bundle();
        var ev = ActiveEvent();
        var playerId = Guid.NewGuid();
        WireLadderContent(b);
        WireActiveEvent(b, ev);
        var active = StageRaid(ev.Id, playerId, stage: 3, defeated: false);
        WireJoinedWithStages(b, ev, playerId, new List<ActiveRaid> { active });

        var result = await b.Build().GetLadderAsync(playerId);

        result.CurrentStage.Should().Be(3, "the live stage is the current target");
        result.ActiveRaid!.ActiveRaidId.Should().Be(active.Id);
        b.Raids.Verify(r => r.CreateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()), Times.Never,
            "an already-active stage must never be re-spawned");
    }

    [Fact]
    public async Task GetLadder_AfterDefeat_SpawnsNextStage()
    {
        var b = new Bundle();
        var ev = ActiveEvent();
        var playerId = Guid.NewGuid();
        WireLadderContent(b);
        WireActiveEvent(b, ev);
        // Stage 1 defeated, no active stage → next is stage 2.
        var stages = new List<ActiveRaid> { StageRaid(ev.Id, playerId, stage: 1, defeated: true) };
        WireJoinedWithStages(b, ev, playerId, stages);
        ActiveRaid? created = null;
        b.Raids.Setup(r => r.CreateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()))
            .Callback((ActiveRaid r, CancellationToken _) => created = r)
            .ReturnsAsync((ActiveRaid r, CancellationToken _) => r);

        var result = await b.Build().GetLadderAsync(playerId);

        result.CurrentStage.Should().Be(2);
        created!.RaidDefinitionId.Should().Be("gauntlet_stage_2");
        created.MaxHp.Should().Be(10000L, "stage-2 baseHp");
    }

    [Fact]
    public async Task GetLadder_ClimbToStage6_ThenComplete()
    {
        var b = new Bundle();
        var ev = ActiveEvent();
        var playerId = Guid.NewGuid();
        WireLadderContent(b);
        WireActiveEvent(b, ev);

        // All six stages defeated → past the final stage → Complete, no spawn.
        var stages = Enumerable.Range(1, StageCount)
            .Select(n => StageRaid(ev.Id, playerId, n, defeated: true))
            .ToList();
        WireJoinedWithStages(b, ev, playerId, stages);

        var result = await b.Build().GetLadderAsync(playerId);

        result.Complete.Should().BeTrue();
        result.CurrentStage.Should().Be(0);
        result.ActiveRaid.Should().BeNull();
        result.StageCount.Should().Be(StageCount);
        b.Raids.Verify(r => r.CreateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()), Times.Never,
            "nothing spawns past the last stage");
    }

    [Fact]
    public async Task GetLadder_DefeatedStage5_SpawnsStage6_NotComplete()
    {
        var b = new Bundle();
        var ev = ActiveEvent();
        var playerId = Guid.NewGuid();
        WireLadderContent(b);
        WireActiveEvent(b, ev);
        // Stages 1–5 defeated, none active → next is the final stage 6 (boundary: still spawns).
        var stages = Enumerable.Range(1, 5)
            .Select(n => StageRaid(ev.Id, playerId, n, defeated: true))
            .ToList();
        WireJoinedWithStages(b, ev, playerId, stages);
        ActiveRaid? created = null;
        b.Raids.Setup(r => r.CreateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()))
            .Callback((ActiveRaid r, CancellationToken _) => created = r)
            .ReturnsAsync((ActiveRaid r, CancellationToken _) => r);

        var result = await b.Build().GetLadderAsync(playerId);

        result.Complete.Should().BeFalse();
        result.CurrentStage.Should().Be(6);
        created!.RaidDefinitionId.Should().Be("gauntlet_stage_6");
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

    // ── Slice 6 — token shop ───────────────────────────────────────────────────

    private static GauntletShopEntry UnitEntry(string id = "shop_unit", string payload = "gen_x",
        GauntletCurrency cur = GauntletCurrency.Token, int price = 100)
        => new() { Id = id, RewardKind = GauntletShopRewardKind.Unit, PayloadId = payload, Currency = cur, Price = price, MaxOwned = 1 };

    private static GauntletShopEntry GearEntry(string id = "shop_gear", string payload = "gear_x",
        GauntletCurrency cur = GauntletCurrency.Pitchfork, int price = 10)
        => new() { Id = id, RewardKind = GauntletShopRewardKind.Gear, PayloadId = payload, Currency = cur, Price = price, MaxOwned = 1 };

    private static GauntletShopEntry GemBundleEntry(string id = "shop_gems", int amount = 250,
        GauntletCurrency cur = GauntletCurrency.Token, int price = 50)
        => new() { Id = id, RewardKind = GauntletShopRewardKind.GemBundle, PayloadId = "", Amount = amount, Currency = cur, Price = price, MaxOwned = null };

    private static GauntletShopEntry StrikeRefillEntry(string id = "shop_strikes", int amount = 50,
        GauntletCurrency cur = GauntletCurrency.Token, int price = 25)
        => new() { Id = id, RewardKind = GauntletShopRewardKind.StrikeRefill, PayloadId = "", Amount = amount, Currency = cur, Price = price, MaxOwned = null };

    private static void WireOwnedNone(Bundle b)
    {
        b.Legions.Setup(l => l.GetOwnedUnitsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OwnedUnitResponse>());
        b.Legions.Setup(l => l.GetOwnedLegionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OwnedLegionResponse>());
        b.Equipment.Setup(e => e.GetOwnedGearAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OwnedGearResponse>());
    }

    [Fact]
    public async Task BuyShop_Unit_Success_DebitsTokenCurrency_AndGrantsPayload()
    {
        var b = new Bundle();
        var playerId = Guid.NewGuid();
        var entry = UnitEntry(price: 120);
        b.Shop.Setup(s => s.GetById(entry.Id)).Returns(entry);
        WireOwnedNone(b); // not owned yet
        b.Currency.Setup(c => c.SpendAsync(playerId, GauntletCurrency.Token, 120, $"gauntletshop:{playerId}:{entry.Id}", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GauntletCurrencySpendOutcome.Charged);

        var result = await b.Build().BuyFromShopAsync(playerId, entry.Id);

        result.Success.Should().BeTrue();
        result.AlreadyCharged.Should().BeFalse();
        // Debited the TOKEN currency (not Pitchfork) for exactly the price.
        b.Currency.Verify(c => c.SpendAsync(playerId, GauntletCurrency.Token, 120, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        b.Currency.Verify(c => c.SpendAsync(It.IsAny<Guid>(), GauntletCurrency.Pitchfork, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        // Granted the unit exactly once.
        b.Legions.Verify(l => l.GrantUnitAsync(playerId, entry.PayloadId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BuyShop_Insufficient_NoCharge_NoGrant()
    {
        var b = new Bundle();
        var playerId = Guid.NewGuid();
        var entry = UnitEntry();
        b.Shop.Setup(s => s.GetById(entry.Id)).Returns(entry);
        WireOwnedNone(b);
        b.Currency.Setup(c => c.SpendAsync(playerId, entry.Currency, entry.Price, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GauntletCurrencySpendOutcome.Insufficient);

        var result = await b.Build().BuyFromShopAsync(playerId, entry.Id);

        result.Success.Should().BeFalse();
        result.InsufficientTokens.Should().BeTrue();
        b.Legions.Verify(l => l.GrantUnitAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuyShop_OwnOnce_AlreadyOwned_ReturnsAlreadyOwned_WithoutCharging()
    {
        var b = new Bundle();
        var playerId = Guid.NewGuid();
        var entry = UnitEntry(payload: "gen_owned");
        b.Shop.Setup(s => s.GetById(entry.Id)).Returns(entry);
        // Player already owns the unit.
        b.Legions.Setup(l => l.GetOwnedUnitsAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OwnedUnitResponse> { new() { UnitDefinitionId = "gen_owned" } });

        var result = await b.Build().BuyFromShopAsync(playerId, entry.Id);

        result.AlreadyOwned.Should().BeTrue();
        result.Success.Should().BeFalse();
        // NO charge and NO grant when already owned.
        b.Currency.Verify(c => c.SpendAsync(It.IsAny<Guid>(), It.IsAny<GauntletCurrency>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        b.Legions.Verify(l => l.GrantUnitAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuyShop_Unknown_Entry_ReturnsNotFoundStyleFailure_NoCharge()
    {
        var b = new Bundle();
        var playerId = Guid.NewGuid();
        b.Shop.Setup(s => s.GetById("nope")).Returns((GauntletShopEntry?)null);

        var result = await b.Build().BuyFromShopAsync(playerId, "nope");

        result.Success.Should().BeFalse();
        result.AlreadyOwned.Should().BeFalse();
        result.InsufficientTokens.Should().BeFalse();
        result.FailureReason.Should().Contain("not found");
        b.Currency.Verify(c => c.SpendAsync(It.IsAny<Guid>(), It.IsAny<GauntletCurrency>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuyShop_PitchforkPriced_WithOnlyTokenBalance_ReturnsInsufficient_CurrencyIsolation()
    {
        // A Pitchfork-priced entry must be spent from the PITCHFORK balance. With only Tokens, the
        // Pitchfork SpendAsync returns Insufficient — wrong currency = insufficient in THAT currency.
        var b = new Bundle();
        var playerId = Guid.NewGuid();
        var entry = GearEntry(cur: GauntletCurrency.Pitchfork, price: 15);
        b.Shop.Setup(s => s.GetById(entry.Id)).Returns(entry);
        WireOwnedNone(b);
        // Pitchfork balance is empty → Insufficient. (Token balance is irrelevant — never queried.)
        b.Currency.Setup(c => c.SpendAsync(playerId, GauntletCurrency.Pitchfork, 15, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GauntletCurrencySpendOutcome.Insufficient);

        var result = await b.Build().BuyFromShopAsync(playerId, entry.Id);

        result.InsufficientTokens.Should().BeTrue();
        // The spend was attempted against PITCHFORK, never Token.
        b.Currency.Verify(c => c.SpendAsync(playerId, GauntletCurrency.Pitchfork, 15, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        b.Currency.Verify(c => c.SpendAsync(It.IsAny<Guid>(), GauntletCurrency.Token, It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        b.Equipment.Verify(e => e.GrantGearAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task BuyShop_OwnOnce_BuyTwice_SecondCallAlreadyOwned_NoSecondCharge()
    {
        // After a successful buy the payload is owned, so a 2nd attempt short-circuits at the
        // ownership pre-check → AlreadyOwned, never re-charging.
        var b = new Bundle();
        var playerId = Guid.NewGuid();
        var entry = UnitEntry(payload: "gen_twice");
        b.Shop.Setup(s => s.GetById(entry.Id)).Returns(entry);

        // 1st call: not owned → charge + grant.
        var ownedUnits = new List<OwnedUnitResponse>();
        b.Legions.Setup(l => l.GetOwnedUnitsAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(() => ownedUnits);
        b.Currency.Setup(c => c.SpendAsync(playerId, entry.Currency, entry.Price, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GauntletCurrencySpendOutcome.Charged);
        // Grant flips ownership for the next read (mimics the upsert).
        b.Legions.Setup(l => l.GrantUnitAsync(playerId, entry.PayloadId, It.IsAny<CancellationToken>()))
            .Callback(() => ownedUnits.Add(new OwnedUnitResponse { UnitDefinitionId = entry.PayloadId }))
            .Returns(Task.CompletedTask);

        var svc = b.Build();
        var first = await svc.BuyFromShopAsync(playerId, entry.Id);
        var second = await svc.BuyFromShopAsync(playerId, entry.Id);

        first.Success.Should().BeTrue();
        second.AlreadyOwned.Should().BeTrue();
        // Charged exactly ONCE across both calls (buy-twice charges once).
        b.Currency.Verify(c => c.SpendAsync(playerId, entry.Currency, entry.Price, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
        b.Legions.Verify(l => l.GrantUnitAsync(playerId, entry.PayloadId, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BuyShop_GemBundle_AlreadyCharged_ReGrantsIdempotently_NoReCharge()
    {
        // Lost-purchase replay on a repeatable bundle: the spend row already exists (AlreadyCharged).
        // The grant must re-run (idempotent via the gem ledger's referenceId) and report Success +
        // AlreadyCharged — never a second debit.
        var b = new Bundle();
        var playerId = Guid.NewGuid();
        var entry = GemBundleEntry(amount: 250, price: 50);
        b.Shop.Setup(s => s.GetById(entry.Id)).Returns(entry);
        b.Currency.Setup(c => c.SpendAsync(playerId, GauntletCurrency.Token, 50, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GauntletCurrencySpendOutcome.AlreadyCharged);

        var result = await b.Build().BuyFromShopAsync(playerId, entry.Id);

        result.Success.Should().BeTrue("AlreadyCharged is an idempotent success");
        result.AlreadyCharged.Should().BeTrue();
        // Gems re-granted with the SAME referenceId (gem ledger's unique index de-dupes).
        b.Gems.Verify(g => g.GrantGemsAsync(playerId, 250, GemTransactionType.GauntletShopReward,
            $"gauntletshop:{playerId}:{entry.Id}", It.IsAny<CancellationToken>()), Times.Once);
        // The spend was the only currency call (no re-charge).
        b.Currency.Verify(c => c.SpendAsync(playerId, GauntletCurrency.Token, 50, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BuyShop_StrikeRefill_Charged_CreditsStrikes_GuardedByReferenceExists()
    {
        var b = new Bundle();
        var playerId = Guid.NewGuid();
        var entry = StrikeRefillEntry(amount: 50, price: 25);
        b.Shop.Setup(s => s.GetById(entry.Id)).Returns(entry);
        b.Currency.Setup(c => c.SpendAsync(playerId, GauntletCurrency.Token, 25, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GauntletCurrencySpendOutcome.Charged);
        b.Strikes.Setup(s => s.ReferenceExistsAsync(playerId, StrikeTransactionType.ShopRefill, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await b.Build().BuyFromShopAsync(playerId, entry.Id);

        result.Success.Should().BeTrue();
        b.Strikes.Verify(s => s.CreateAsync(
            It.Is<StrikeTransaction>(t => t.Amount == 50 && t.TransactionType == StrikeTransactionType.ShopRefill),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task BuyShop_StrikeRefill_AlreadyCharged_AndStrikeAlreadyCredited_NoSecondCredit()
    {
        // Full replay: spend row + strike credit both committed → no second strike row.
        var b = new Bundle();
        var playerId = Guid.NewGuid();
        var entry = StrikeRefillEntry();
        b.Shop.Setup(s => s.GetById(entry.Id)).Returns(entry);
        b.Currency.Setup(c => c.SpendAsync(playerId, GauntletCurrency.Token, entry.Price, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GauntletCurrencySpendOutcome.AlreadyCharged);
        b.Strikes.Setup(s => s.ReferenceExistsAsync(playerId, StrikeTransactionType.ShopRefill, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // strike credit already exists

        var result = await b.Build().BuyFromShopAsync(playerId, entry.Id);

        result.Success.Should().BeTrue();
        result.AlreadyCharged.Should().BeTrue();
        b.Strikes.Verify(s => s.CreateAsync(It.IsAny<StrikeTransaction>(), It.IsAny<CancellationToken>()), Times.Never,
            "no second strike credit may be written on replay");
    }

    [Fact]
    public async Task GetShop_ListsCatalogue_WithLiveTokenAndPitchforkBalances_AndAlreadyOwnedFlags()
    {
        var b = new Bundle();
        var playerId = Guid.NewGuid();
        var unit = UnitEntry(id: "shop_unit", payload: "gen_owned", cur: GauntletCurrency.Token, price: 100);
        var gear = GearEntry(id: "shop_gear", payload: "gear_unowned", cur: GauntletCurrency.Pitchfork, price: 10);
        var gems = GemBundleEntry(id: "shop_gems", amount: 250, cur: GauntletCurrency.Token, price: 50);
        b.Shop.Setup(s => s.GetAll()).Returns(new List<GauntletShopEntry> { unit, gear, gems });
        // Player owns the unit but not the gear.
        b.Legions.Setup(l => l.GetOwnedUnitsAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OwnedUnitResponse> { new() { UnitDefinitionId = "gen_owned" } });
        b.Legions.Setup(l => l.GetOwnedLegionsAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OwnedLegionResponse>());
        b.Equipment.Setup(e => e.GetOwnedGearAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<OwnedGearResponse>());
        b.Currency.Setup(c => c.GetBalanceAsync(playerId, GauntletCurrency.Token, It.IsAny<CancellationToken>())).ReturnsAsync(500L);
        b.Currency.Setup(c => c.GetBalanceAsync(playerId, GauntletCurrency.Pitchfork, It.IsAny<CancellationToken>())).ReturnsAsync(7L);

        var result = await b.Build().GetShopAsync(playerId);

        result.Entries.Should().HaveCount(3);
        result.TokenBalance.Should().Be(500L);
        result.PitchforkBalance.Should().Be(7L);
        result.Entries.Single(e => e.Id == "shop_unit").AlreadyOwned.Should().BeTrue("the unit is owned");
        result.Entries.Single(e => e.Id == "shop_gear").AlreadyOwned.Should().BeFalse("the gear is not owned");
        result.Entries.Single(e => e.Id == "shop_gems").AlreadyOwned.Should().BeFalse("bundles are never owned");
        result.Entries.Single(e => e.Id == "shop_gear").Currency.Should().Be("Pitchfork");
    }
}
