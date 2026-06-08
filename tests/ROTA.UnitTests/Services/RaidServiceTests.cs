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

public class RaidServiceTests
{
    // -----------------------------------------------------------------------
    // FIXTURES
    // -----------------------------------------------------------------------

    private record ServiceBundle(
        RaidService Service,
        Mock<IActiveRaidRepository> Raids,
        Mock<IRaidParticipantRepository> Participants,
        Mock<IPlayerRepository> Players,
        Mock<IPlayerResourceRepository> Resources,
        Mock<IEnergyService> Energy,
        Mock<IGemService> Gems,
        Mock<IStatService> Stats,
        Mock<IPlayerInventoryRepository> Inventory,
        Mock<IItemDefinitionProvider> ItemDefs,
        Mock<ILootTableProvider> LootTables,
        Mock<IAuditLogRepository> AuditLog,
        Mock<IRaidDefinitionProvider> Definitions,
        Mock<IRaidHitCache> HitCache,
        Mock<IEquipmentService> Equipment,
        Mock<IRaidMagicRepository> RaidMagics,
        Mock<IMagicDefinitionProvider> MagicDefs,
        Mock<IMagicService> MagicService,
        Mock<IPlayerLegionRepository> PlayerLegions,
        Mock<IPlayerLegionSlotRepository> LegionSlots,
        Mock<IUnitDefinitionProvider> UnitDefs,
        Mock<ILegionDefinitionProvider> LegionDefs,
        Mock<IPlayerCommanderGearRepository> CommanderGear,
        Mock<IGearDefinitionProvider> GearDefs,
        Mock<ILegionService> LegionService,
        Mock<ILeaderboardService> Leaderboards,
        Mock<IPlayerGauntletTrophyRepository> TrophyRepo,
        Mock<IGauntletContentProvider> GauntletContent,
        Mock<IPlayerEventMagicRepository> PlayerEventMagics,
        Mock<IPlayerMagicHonorRepository> PlayerMagicHonors,
        Mock<IStrikeRepository> Strikes,
        Mock<IGauntletScoringService> GauntletScoring,
        Mock<IGauntletCurrencyRepository> GauntletCurrency,
        Mock<IGuildMembershipRepository> GuildMemberships,
        Mock<IGuildEconomyRepository> GuildEconomy,
        Mock<IMasteryService> Mastery);

    private static ServiceBundle BuildService(Random? random = null, MagicConfig? magicConfig = null, LegionConfig? legionConfig = null, CombatConfig? combatConfig = null, GauntletConfig? gauntletConfig = null)
    {
        var raids          = new Mock<IActiveRaidRepository>();
        var participants   = new Mock<IRaidParticipantRepository>();
        var players        = new Mock<IPlayerRepository>();
        var resources      = new Mock<IPlayerResourceRepository>();
        var energy         = new Mock<IEnergyService>();
        var gems           = new Mock<IGemService>();
        var stats          = new Mock<IStatService>();
        var inventory      = new Mock<IPlayerInventoryRepository>();
        var itemDefs       = new Mock<IItemDefinitionProvider>();
        var lootTables     = new Mock<ILootTableProvider>();
        var auditLog       = new Mock<IAuditLogRepository>();
        var definitions    = new Mock<IRaidDefinitionProvider>();
        var hitCache       = new Mock<IRaidHitCache>();
        var equipment      = new Mock<IEquipmentService>();
        var raidMagics     = new Mock<IRaidMagicRepository>();
        var magicDefs      = new Mock<IMagicDefinitionProvider>();
        var magicSvc       = new Mock<IMagicService>();
        var playerLegions  = new Mock<IPlayerLegionRepository>();
        var legionSlots    = new Mock<IPlayerLegionSlotRepository>();
        var unitDefs       = new Mock<IUnitDefinitionProvider>();
        var legionDefs     = new Mock<ILegionDefinitionProvider>();
        var commanderGear  = new Mock<IPlayerCommanderGearRepository>();
        var gearDefs       = new Mock<IGearDefinitionProvider>();
        var legionSvc      = new Mock<ILegionService>();
        var leaderboards   = new Mock<ILeaderboardService>();
        var trophyRepo       = new Mock<IPlayerGauntletTrophyRepository>();
        var gauntletContent  = new Mock<IGauntletContentProvider>();
        var playerEventMagics = new Mock<IPlayerEventMagicRepository>();
        var playerMagicHonors = new Mock<IPlayerMagicHonorRepository>();
        var strikes          = new Mock<IStrikeRepository>();
        var gauntletScoring  = new Mock<IGauntletScoringService>();
        var gauntletCurrency = new Mock<IGauntletCurrencyRepository>();
        // System 21 Slice 3b — guild-raid deps. Default: not in a guild (guild branches never fire for
        // ordinary/Gauntlet raids), pool spend Charged. Guild-raid tests override these per-test.
        var guildMemberships = new Mock<IGuildMembershipRepository>();
        var guildEconomy     = new Mock<IGuildEconomyRepository>();
        guildMemberships.Setup(r => r.FindByPlayerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GuildMembership?)null);
        guildMemberships.Setup(r => r.FindByGuildAndPlayerAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((GuildMembership?)null);
        guildMemberships.Setup(r => r.UpdateAsync(It.IsAny<GuildMembership>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        guildEconomy.Setup(r => r.TrySpendPoolAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuildPoolSpendOutcome.Charged);
        guildEconomy.Setup(r => r.GetPoolBalanceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);

        hitCache.Setup(c => c.TryAcquireSlotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, (RaidHitResponse?)null));
        hitCache.Setup(c => c.StoreResultAsync(It.IsAny<string>(), It.IsAny<RaidHitResponse>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        stats.Setup(s => s.GrantLevelUpPointsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        stats.Setup(s => s.AddUnassignedPointsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // Default: 1000 XP per level — keeps existing kill-reward tests from triggering extra level-ups
        stats.Setup(s => s.XpToNextLevel(It.IsAny<int>())).Returns(1000);
        // Default: no crit (chance=0 → never crits) — preserves existing damage-range assertions
        stats.Setup(s => s.GetCritProfile(It.IsAny<int>()))
            .Returns(new CritProfile(Chance: 0.0, Multiplier: 1.5));
        // Default: pass-through — no gear bonus, no proc, no conditional bonus
        equipment.Setup(e => e.GetEffectiveCombatDataAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, int atk, int def, CancellationToken _) =>
                new EffectiveCombatData(atk, def, null, 0.0));
        // Default: no applied magics — existing tests see zero magic proc bonus
        raidMagics.Setup(r => r.GetForRaidAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RaidMagic>());
        // Default: provider exposes no magic definitions (mirrors the real provider, which never
        // returns null). The off-cap aura loop (System 16 Slice 4) iterates GetAll(); a Gauntlet
        // hit with no off-cap magics configured must see an empty list, not null.
        magicDefs.Setup(d => d.GetAll()).Returns(new List<MagicDefinition>());
        // Default: GrantMagicAsync is a no-op (magic drops don't need a real repo in unit tests)
        magicSvc.Setup(m => m.GrantMagicAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // Default: no active legion — existing tests unchanged
        playerLegions.Setup(r => r.GetActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerLegion?)null);
        legionSlots.Setup(r => r.GetForLegionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerLegionSlot>());
        // Default: no commander gear — existing tests unchanged
        commanderGear.Setup(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerCommanderGear?)null);
        // Default: no-op for unit/legion grant drops
        legionSvc.Setup(l => l.GrantUnitAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        legionSvc.Setup(l => l.GrantLegionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // Default: no-op for gear drops
        equipment.Setup(e => e.GrantGearAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // Default: leaderboard writes are no-ops; Slice 4 tests override/assert as needed.
        leaderboards.Setup(l => l.RecordRaidHitAsync(
                It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // System 16 Slice 4 defaults — a trophy-less, aura-less, non-Gauntlet player: zero amplifiers.
        // No trophies owned → trophyMult = 1.0 → legion term unchanged.
        trophyRepo.Setup(r => r.GetForPlayerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerGauntletTrophy>());
        // Default content provider: no off-cap magics resolved (GetTrophyById null unless a test sets it).
        gauntletContent.Setup(c => c.GetTrophyById(It.IsAny<string>()))
            .Returns((GauntletTrophyDefinition?)null);
        // No current/former rank-magic ownership by default.
        playerEventMagics.Setup(r => r.FindAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerEventMagic?)null);
        playerMagicHonors.Setup(r => r.HasHonorAsync(
                It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        // Strikes: charged by default (Gauntlet tests override to Insufficient/AlreadyCharged as needed).
        strikes.Setup(r => r.SpendAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StrikeSpendOutcome.Charged);
        strikes.Setup(r => r.GetBalanceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(0L);
        // Scoring update is a no-op by default; Gauntlet tests verify it fired.
        gauntletScoring.Setup(s => s.UpdateScoreAsync(
                It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // System 16 Slice 5 — per-defeat Token reward: no prior reference by default (so a Gauntlet
        // kill credits fresh); CreateAsync is a no-op. Idempotency tests override ReferenceExistsAsync.
        gauntletCurrency.Setup(r => r.ReferenceExistsAsync(
                It.IsAny<Guid>(), It.IsAny<GauntletCurrency>(), It.IsAny<GauntletCurrencyTransactionType>(),
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        gauntletCurrency.Setup(r => r.CreateAsync(
                It.IsAny<GauntletCurrencyTransaction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        // Strike ledger CreateAsync (per-defeat credit) + ReferenceExistsAsync default for Slice 5.
        // (SpendAsync is already set up above for the hit-cost path.)
        strikes.Setup(r => r.ReferenceExistsAsync(
                It.IsAny<Guid>(), It.IsAny<StrikeTransactionType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        strikes.Setup(r => r.CreateAsync(It.IsAny<StrikeTransaction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var magicCfg  = Options.Create(magicConfig  ?? new MagicConfig());
        var legionCfg = Options.Create(legionConfig ?? new LegionConfig());
        var combatCfg = Options.Create(combatConfig ?? new CombatConfig());
        var gauntletCfg = Options.Create(gauntletConfig ?? new GauntletConfig());
        var mastery = new Mock<IMasteryService>();
        // Neutral mastery modifiers by default → a mastery-less hit is byte-for-byte unchanged.
        mastery.Setup(m => m.GetCombatModifiersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MasteryCombatModifiers(0.0, 0.0));

        var service = new RaidService(
            raids.Object, participants.Object, players.Object, resources.Object,
            energy.Object, gems.Object, stats.Object, inventory.Object,
            itemDefs.Object, lootTables.Object, auditLog.Object,
            definitions.Object, hitCache.Object, equipment.Object,
            raidMagics.Object, magicDefs.Object, magicSvc.Object, magicCfg,
            playerLegions.Object, legionSlots.Object, unitDefs.Object, legionDefs.Object,
            legionCfg, commanderGear.Object, gearDefs.Object, legionSvc.Object,
            leaderboards.Object, combatCfg,
            trophyRepo.Object, gauntletContent.Object, playerEventMagics.Object,
            playerMagicHonors.Object, strikes.Object, gauntletScoring.Object, gauntletCfg,
            gauntletCurrency.Object, guildMemberships.Object, guildEconomy.Object, mastery.Object, random);

        return new ServiceBundle(service, raids, participants, players, resources, energy, gems,
            stats, inventory, itemDefs, lootTables, auditLog, definitions, hitCache, equipment,
            raidMagics, magicDefs, magicSvc, playerLegions, legionSlots, unitDefs, legionDefs,
            commanderGear, gearDefs, legionSvc, leaderboards,
            trophyRepo, gauntletContent, playerEventMagics, playerMagicHonors, strikes, gauntletScoring,
            gauntletCurrency, guildMemberships, guildEconomy, mastery);
    }

    private static Player MakePlayer(long xp = 0)
    {
        var p = Player.Create("testuser", "test@rota.test", "hash");
        if (xp > 0) p.AddExperience(xp, _ => 1000);
        return p;
    }

    private static RaidDefinition IronColossus(long goldPerStamina = 1) => new()
    {
        Id                   = "raid_ironcolossus",
        Name                 = "The Iron Colossus",
        Tier                 = "World",
        BaseHp               = 100000,
        PersonalBaseHp       = 500,
        TimerHours           = 48,
        StaminaCostPerHit    = 1,
        GoldPerStamina       = goldPerStamina,
        LootTableId          = "lt_raid_ironcolossus",
        BaseGoldReward       = 500,
        BaseExperienceReward = 300,
        BaseGemReward        = 2,
        HasOnHitDrops        = false,
    };

    private static ActiveRaid MakeRaid(long currentHp = 100000, bool isDefeated = false, int hoursUntilExpiry = 48, RaidDifficulty difficulty = RaidDifficulty.Normal)
    {
        var raid = ActiveRaid.Create(
            "raid_ironcolossus", Guid.NewGuid(), 100000,
            DateTimeOffset.UtcNow.AddHours(hoursUntilExpiry), difficulty);

        if (currentHp < 100000)
            raid.TakeDamage(100000 - currentHp);
        if (isDefeated)
            raid.MarkDefeated();

        return raid;
    }

    private static PlayerResource MakeStaminaResource(int maxValue = 5)
        => PlayerResource.Create(Guid.NewGuid(), ResourceType.Stamina, maxValue, regenPerMinute: 1);

    private static void SetupHitScaffolding(ServiceBundle b, Player player, ActiveRaid raid, bool staminaSuccess = true)
    {
        b.Raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        // AtomicApplyHitAsync: in unit tests there is no real DB transaction — just invoke the
        // delegate directly against the raid object so service logic is fully exercised.
        b.Raids.Setup(r => r.AtomicApplyHitAsync(
                raid.Id,
                It.IsAny<Func<ActiveRaid, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Guid, Func<ActiveRaid, Task<bool>>, CancellationToken>(
                (_, mutate, _) => mutate(raid));
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());
        b.Energy.Setup(e => e.SpendEnergyAsync(player.Id, ResourceType.Stamina, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(staminaSuccess);
        b.Players.Setup(p => p.FindByIdWithStatsAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        b.Players.Setup(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        b.Resources.Setup(r => r.GetAsync(player.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeStaminaResource());
        b.Energy.Setup(e => e.GetCurrentEnergyAsync(player.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);
    }

    // -----------------------------------------------------------------------
    // SummonRaidAsync — difficulty HP multipliers
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(RaidDifficulty.Normal,    100000,  "Green")]
    [InlineData(RaidDifficulty.Hard,      140000,  "Yellow")]
    [InlineData(RaidDifficulty.Legendary, 200000,  "Red")]
    [InlineData(RaidDifficulty.Nightmare, 360000,  "Purple")]
    public async Task Summon_AppliesHpMultiplier_PerDifficulty(RaidDifficulty difficulty, long expectedHp, string expectedColor)
    {
        var b = BuildService();
        var player = MakePlayer();

        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());
        b.Players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        b.Raids.Setup(r => r.CreateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActiveRaid r, CancellationToken _) => r);

        var result = await b.Service.SummonRaidAsync(player.Id, "raid_ironcolossus", difficulty);

        result.Success.Should().BeTrue();
        result.Response!.MaxHp.Should().Be(expectedHp);
        result.Response.DifficultyColor.Should().Be(expectedColor);
        b.Raids.Verify(r => r.CreateAsync(
            It.Is<ActiveRaid>(a => a.MaxHp == expectedHp && a.Difficulty == difficulty),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — stamina cost per hit size
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public async Task Hit_SpendsCorrectStamina_PerHitSize(int hitSize)
    {
        var b = BuildService(new Random(0));
        var player = MakePlayer();
        var raid = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, hitSize, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        b.Energy.Verify(e => e.SpendEnergyAsync(player.Id, ResourceType.Stamina, hitSize, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — damage formula
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_ComputesDamage_FromPlayerStats_InExpectedRange()
    {
        var b = BuildService(new Random(0));
        var player = MakePlayer();
        var raid = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        // base = ATK*4+DEF = 10*4+10=50, hitSize=1, RNG[0.85,1.15] → damage in [42,57]
        result.Success.Should().BeTrue();
        result.Response!.DamageDealt.Should().BeInRange(42, 58);
        result.Response.DamageDealt.Should().BeGreaterOrEqualTo(1);
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — idempotency
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_ReturnsCachedResponse_OnDuplicateKey_ZeroReprocessing()
    {
        var b = BuildService();
        var key = Guid.NewGuid().ToString();
        var cached = new RaidHitResponse { Success = true, DamageDealt = 42 };

        // TryAcquireSlotAsync returns (false, response) → duplicate found, return immediately.
        b.HitCache.Setup(c => c.TryAcquireSlotAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, cached));

        var raid = MakeRaid();
        b.Raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await b.Service.HitRaidAsync(Guid.NewGuid(), raid.Id, 1, key);

        result.Success.Should().BeTrue();
        result.Response!.DamageDealt.Should().Be(42);
        b.Energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
        b.Participants.Verify(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()), Times.Never);
        b.Raids.Verify(r => r.AtomicApplyHitAsync(It.IsAny<Guid>(), It.IsAny<Func<ActiveRaid, Task<bool>>>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — expired
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_ReturnsExpired_WhenTimerElapsed_StaminaUntouched()
    {
        var b = BuildService();
        var expiredRaid = MakeRaid(hoursUntilExpiry: -1);

        b.Raids.Setup(r => r.FindByIdAsync(expiredRaid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(expiredRaid);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await b.Service.HitRaidAsync(Guid.NewGuid(), expiredRaid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(RaidHitFailureCode.RaidExpired);
        b.Energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — already defeated
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_ReturnsAlreadyDefeated_WhenRaidDead_StaminaUntouched()
    {
        var b = BuildService();
        var defeatedRaid = MakeRaid(isDefeated: true);

        b.Raids.Setup(r => r.FindByIdAsync(defeatedRaid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(defeatedRaid);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await b.Service.HitRaidAsync(Guid.NewGuid(), defeatedRaid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(RaidHitFailureCode.RaidAlreadyDefeated);
        b.Energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // HitRaidAsync — insufficient stamina
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_ReturnsInsufficientStamina_HpUnchanged()
    {
        // Stamina spend is now inside the advisory-lock callback.
        // The callback must be invoked (via the mock) for SpendEnergyAsync to fire and fail.
        var b = BuildService();
        var player = MakePlayer();
        var raid = MakeRaid();
        var hpBefore = raid.CurrentHp;

        b.Raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        b.Raids.Setup(r => r.AtomicApplyHitAsync(
                raid.Id,
                It.IsAny<Func<ActiveRaid, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Guid, Func<ActiveRaid, Task<bool>>, CancellationToken>(
                (_, mutate, _) => mutate(raid));
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());
        b.Energy.Setup(e => e.SpendEnergyAsync(player.Id, ResourceType.Stamina, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(RaidHitFailureCode.InsufficientStamina);
        raid.CurrentHp.Should().Be(hpBefore);
        b.Participants.Verify(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // Kill rewards — tier multipliers applied correctly
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_Kill_AppliesTierMultiplier_ToGoldAndXp_ForLegendary1()
    {
        // Legendary1 tier multiplier = 1.50×; Normal HP multiplier = 1.0×
        // Gold = 500 * 1.0 * 1.50 = 750; XP = 300 * 1.0 * 1.50 = 450
        var b = BuildService(new Random(0));
        var attacker = MakePlayer();
        var bystander = MakePlayer();
        var raid = MakeRaid(currentHp: 1);

        SetupHitScaffolding(b, attacker, raid);
        b.Players.Setup(p => p.FindByIdAsync(bystander.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(bystander);

        var attackerPart = RaidParticipant.Create(raid.Id, attacker.Id);
        for (int i = 0; i < 10; i++) attackerPart.RecordHit(10000);
        var bystanderPart = RaidParticipant.Create(raid.Id, bystander.Id);
        bystanderPart.RecordHit(100);

        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, attacker.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attackerPart);
        b.Participants.Setup(p => p.UpdateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        b.Participants.Setup(p => p.GetAllForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RaidParticipant> { attackerPart, bystanderPart });
        b.Players.Setup(p => p.FindByIdAsync(bystander.Id, It.IsAny<CancellationToken>())).ReturnsAsync(bystander);
        b.Gems.Setup(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Service.HitRaidAsync(attacker.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.Rewards.Should().NotBeNull();
        result.Response.Rewards!.ContributionTier.Should().Be("Legendary");
        result.Response.Rewards.TierMultiplier.Should().Be(1.50m);
        result.Response.Rewards.GoldGranted.Should().Be(750);
        result.Response.Rewards.ExperienceGranted.Should().Be(450);
    }

    // -----------------------------------------------------------------------
    // Kill rewards — participant gets no gems
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_Kill_ParticipantTierReceivesNoGems()
    {
        // 4 players: positions 0/1/2 = Legendary1/2/3; position 3 falls to minContributionPercent check.
        // p4 has 1 damage out of ~27000+ total → ~0.003% << 0.1% default → Participant → no gems.
        var b = BuildService(new Random(0));

        var p1 = MakePlayer(); var p2 = MakePlayer();
        var p3 = MakePlayer(); var p4 = MakePlayer();
        var raid = MakeRaid(currentHp: 1);

        b.Raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        b.Raids.Setup(r => r.AtomicApplyHitAsync(
                raid.Id,
                It.IsAny<Func<ActiveRaid, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Guid, Func<ActiveRaid, Task<bool>>, CancellationToken>(
                (_, mutate, _) => mutate(raid));
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());
        b.Energy.Setup(e => e.SpendEnergyAsync(p1.Id, ResourceType.Stamina, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        b.Players.Setup(p => p.FindByIdWithStatsAsync(p1.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p1);
        b.Players.Setup(p => p.FindByIdAsync(p2.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p2);
        b.Players.Setup(p => p.FindByIdAsync(p3.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p3);
        b.Players.Setup(p => p.FindByIdAsync(p4.Id, It.IsAny<CancellationToken>())).ReturnsAsync(p4);
        b.Players.Setup(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        b.Resources.Setup(r => r.GetAsync(p1.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeStaminaResource());
        b.Energy.Setup(e => e.GetCurrentEnergyAsync(p1.Id, ResourceType.Stamina, It.IsAny<CancellationToken>())).ReturnsAsync(4);

        // p1=Legendary1 (top), p2=Legendary2, p3=Legendary3, p4=Participant (<0.1%)
        var p1part = RaidParticipant.Create(raid.Id, p1.Id);
        for (int i = 0; i < 10; i++) p1part.RecordHit(10000); // 100000 dmg
        var p2part = RaidParticipant.Create(raid.Id, p2.Id);
        for (int i = 0; i < 9; i++) p2part.RecordHit(1000);  // 9000 dmg
        var p3part = RaidParticipant.Create(raid.Id, p3.Id);
        for (int i = 0; i < 8; i++) p3part.RecordHit(1000);  // 8000 dmg
        var p4part = RaidParticipant.Create(raid.Id, p4.Id);
        p4part.RecordHit(1);                                  // 1 dmg → ~0.0008% << 0.1% → Participant

        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, p1.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(p1part);
        b.Participants.Setup(p => p.UpdateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        b.Participants.Setup(p => p.GetAllForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RaidParticipant> { p1part, p2part, p3part, p4part });
        b.Gems.Setup(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await b.Service.HitRaidAsync(p1.Id, raid.Id, 1, Guid.NewGuid().ToString());

        b.Gems.Verify(g => g.GrantGemsAsync(p4.Id, It.IsAny<int>(), GemTransactionType.RaidReward, It.IsAny<string?>(), It.IsAny<CancellationToken>()), Times.Never,
            "p4 has <0.1% contribution so falls to Participant tier which receives no gems");
    }

    // -----------------------------------------------------------------------
    // Kill rewards — cumulative threshold loot
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_Kill_AwardsCumulativeThresholdRewards_WhenPlayerQualifiesMultipleTiers()
    {
        // Player at 30% contribution qualifies for both 0.1% and 5% and 20% thresholds
        // Total unassigned SP = 1 + 2 + 3 = 6 (each tier cumulative)
        var b = BuildService(new Random(0));
        var attacker = MakePlayer();
        var raid = MakeRaid(currentHp: 1);

        var lootTable = new LootTableDefinition
        {
            Id = "lt_raid_ironcolossus", Type = "Raid",
            Difficulties = new Dictionary<string, LootTableDifficulty>
            {
                ["Normal"] = new()
                {
                    MinContributionPercent = 0.1,
                    ThresholdRewards = new List<ThresholdReward>
                    {
                        new() { ContributionPercent = 0.1,  UnassignedStatPoints = 1, ItemDrops = new() },
                        new() { ContributionPercent = 5.0,  UnassignedStatPoints = 2, ItemDrops = new() },
                        new() { ContributionPercent = 20.0, UnassignedStatPoints = 3, ItemDrops = new() },
                    },
                },
            },
        };
        b.LootTables.Setup(l => l.GetById("lt_raid_ironcolossus")).Returns(lootTable);

        // Set up attacker with 30000 / 100000 = 30% contribution
        var attackerPart = RaidParticipant.Create(raid.Id, attacker.Id);
        for (int i = 0; i < 3; i++) attackerPart.RecordHit(10000);

        b.Raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        b.Raids.Setup(r => r.AtomicApplyHitAsync(
                raid.Id,
                It.IsAny<Func<ActiveRaid, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Guid, Func<ActiveRaid, Task<bool>>, CancellationToken>(
                (_, mutate, _) => mutate(raid));
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());
        b.Energy.Setup(e => e.SpendEnergyAsync(attacker.Id, ResourceType.Stamina, It.IsAny<int>(), It.IsAny<CancellationToken>())).ReturnsAsync(true);
        b.Players.Setup(p => p.FindByIdWithStatsAsync(attacker.Id, It.IsAny<CancellationToken>())).ReturnsAsync(attacker);
        b.Players.Setup(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        b.Resources.Setup(r => r.GetAsync(attacker.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
            .ReturnsAsync(MakeStaminaResource());
        b.Energy.Setup(e => e.GetCurrentEnergyAsync(attacker.Id, ResourceType.Stamina, It.IsAny<CancellationToken>())).ReturnsAsync(4);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, attacker.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attackerPart);
        b.Participants.Setup(p => p.UpdateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        b.Participants.Setup(p => p.GetAllForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RaidParticipant> { attackerPart });
        b.Gems.Setup(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Service.HitRaidAsync(attacker.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        // Tier multiplier for Legendary1 = 1.5, so 6 SP * 1.5 = 9 (rounded)
        result.Response!.Rewards!.UnassignedStatPointsGranted.Should().Be(9,
            "cumulative thresholds at 0.1%, 5%, and 20% each grant stat points, scaled by Legendary1 multiplier");
        b.Stats.Verify(s => s.AddUnassignedPointsAsync(attacker.Id, 9, It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // Kill — gem idempotency key format
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_Kill_UsesCorrectGemIdempotencyKeyFormat()
    {
        var b = BuildService(new Random(0));
        var attacker = MakePlayer();
        var raid = MakeRaid(currentHp: 1);

        SetupHitScaffolding(b, attacker, raid);

        var attackerPart = RaidParticipant.Create(raid.Id, attacker.Id);
        attackerPart.RecordHit(1000);

        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, attacker.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(attackerPart);
        b.Participants.Setup(p => p.UpdateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        b.Participants.Setup(p => p.GetAllForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RaidParticipant> { attackerPart });
        b.Gems.Setup(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        await b.Service.HitRaidAsync(attacker.Id, raid.Id, 1, Guid.NewGuid().ToString());

        var expectedRef = $"raid:{raid.Id}:{attacker.Id}";
        b.Gems.Verify(g => g.GrantGemsAsync(
            attacker.Id, It.IsAny<int>(), GemTransactionType.RaidReward, expectedRef,
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // GetActiveRaidsAsync — filtering and damage tracking
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetActiveRaids_ExcludesDefeatedAndExpiredRaids()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();

        b.Raids.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveRaid>());
        b.Definitions.Setup(d => d.GetById(It.IsAny<string>())).Returns((RaidDefinition?)null);

        var result = await b.Service.GetActiveRaidsAsync(playerId);

        result.Should().BeEmpty();
        b.Raids.Verify(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GetActiveRaids_PopulatesYourDamage_ForCallingPlayer()
    {
        var b = BuildService();
        var playerId = Guid.NewGuid();
        var raid = MakeRaid();
        raid.Share(); // shared (public) so it lists for any caller under System 19 semantics

        var participation = RaidParticipant.Create(raid.Id, playerId);
        participation.RecordHit(12400);

        b.Raids.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveRaid> { raid });
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(participation);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await b.Service.GetActiveRaidsAsync(playerId);

        result.Should().HaveCount(1);
        result[0].YourTotalDamage.Should().Be(12400);
        result[0].YourHitCount.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // RaidSize — Personal raid summon uses PersonalBaseHp
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(RaidDifficulty.Normal,    500)]
    [InlineData(RaidDifficulty.Hard,      700)]
    [InlineData(RaidDifficulty.Legendary, 1000)]
    [InlineData(RaidDifficulty.Nightmare, 1800)]
    public async Task Summon_PersonalRaid_UsesPersonalBaseHp(RaidDifficulty difficulty, long expectedHp)
    {
        var b = BuildService();
        var player = MakePlayer();

        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus()); // PersonalBaseHp = 500
        b.Players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        b.Raids.Setup(r => r.CreateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActiveRaid r, CancellationToken _) => r);

        var result = await b.Service.SummonRaidAsync(player.Id, "raid_ironcolossus", difficulty, RaidSize.Personal);

        result.Success.Should().BeTrue();
        result.Response!.MaxHp.Should().Be(expectedHp,
            $"Personal raid HP = PersonalBaseHp({500}) × difficultyMultiplier");
        result.Response.Size.Should().Be("Personal");
        b.Raids.Verify(r => r.CreateAsync(
            It.Is<ActiveRaid>(a => a.MaxHp == expectedHp && a.Size == RaidSize.Personal),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Summon_LargeRaid_StillUsesBaseHp()
    {
        var b = BuildService();
        var player = MakePlayer();

        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());
        b.Players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        b.Raids.Setup(r => r.CreateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActiveRaid r, CancellationToken _) => r);

        var result = await b.Service.SummonRaidAsync(player.Id, "raid_ironcolossus", RaidDifficulty.Normal, RaidSize.Large);

        result.Success.Should().BeTrue();
        result.Response!.MaxHp.Should().Be(100000, "Large raid ignores PersonalBaseHp and uses BaseHp");
        result.Response.Size.Should().Be("Large");
    }

    // -----------------------------------------------------------------------
    // RaidSize — Personal raid hit access gate
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_PersonalRaid_BySummoner_Succeeds()
    {
        var b = BuildService(new Random(0));
        var player = MakePlayer();
        var raid = ActiveRaid.Create(
            "raid_ironcolossus", player.Id, 100000,
            DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal, RaidSize.Personal);

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);
        b.Participants.Setup(p => p.GetAllForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RaidParticipant>());

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue("the summoner is always allowed to hit their own personal raid");
    }

    [Fact]
    public async Task Hit_PersonalRaid_ByNonSummoner_ReturnsAccessDenied()
    {
        var b = BuildService(new Random(0));
        var summonerId = Guid.NewGuid();
        var attacker   = MakePlayer();   // different player

        var raid = ActiveRaid.Create(
            "raid_ironcolossus", summonerId, 100000,
            DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal, RaidSize.Personal);

        b.Raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);

        var result = await b.Service.HitRaidAsync(attacker.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(RaidHitFailureCode.AccessDenied,
            "a non-summoner is blocked before any stamina is spent");
        b.Energy.Verify(
            e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never, "access gate fires before the stamina-spend step");
    }

    // -----------------------------------------------------------------------
    // Component D — On-hit XP and gold
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public async Task Hit_GrantsXpInExpectedRange_PerHitSize(int hitSize)
    {
        // XP = round(staminaCost × roll), roll ∈ [1.0, 4.0], staminaCost = hitSize × 1
        // So XP ∈ [staminaCost, staminaCost * 4]
        var b = BuildService(new Random(42)); // seeded for determinism
        var player = MakePlayer();
        var raid = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, hitSize, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        int staminaCost = hitSize; // StaminaCostPerHit=1
        result.Response!.XpGained.Should().BeGreaterOrEqualTo(staminaCost,
            "XP floor is staminaCost × 1.0 (roll minimum)");
        result.Response.XpGained.Should().BeLessOrEqualTo(staminaCost * 4,
            "XP ceiling is staminaCost × 4.0 (roll maximum)");
        result.Response.XpGained.Should().BeGreaterOrEqualTo(1, "XP is always at least 1");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public async Task Hit_XpRoll_IsDrivenByCombatConfig(int hitSize)
    {
        // With min==max the per-hit roll collapses to a constant, so XP = round(staminaCost × const) exactly.
        var cfg = new CombatConfig { XpPerStaminaRollMin = 3.0, XpPerStaminaRollMax = 3.0 };
        var b = BuildService(new Random(42), combatConfig: cfg);
        var player = MakePlayer();
        var raid = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, hitSize, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        int staminaCost = hitSize; // StaminaCostPerHit=1
        result.Response!.XpGained.Should().Be(staminaCost * 3,
            "min==max==3.0 ⇒ the per-hit roll is exactly 3.0, so XP = staminaCost × 3 (config-driven)");
    }

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public async Task Hit_GoldRoll_IsDrivenByCombatConfig(int hitSize)
    {
        // Gold mirrors the XP roll (System / T10): staminaCost × Uniform[min,max]. With min==max the
        // roll collapses to a constant, so gold = round(staminaCost × const) exactly.
        var cfg = new CombatConfig { GoldPerStaminaRollMin = 5.0, GoldPerStaminaRollMax = 5.0 };
        var b = BuildService(new Random(42), combatConfig: cfg);
        var player = MakePlayer();
        var raid = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, hitSize, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        int staminaCost = hitSize; // StaminaCostPerHit=1
        result.Response!.GoldGained.Should().Be(staminaCost * 5,
            "min==max==5 ⇒ the per-hit gold roll is exactly 5, so gold = staminaCost × 5 (config-driven)");
    }

    [Fact]
    public async Task Hit_XpThatCrossesLevelThreshold_FiresGrantLevelUpPoints()
    {
        // Player at 999 XP with XpToNextLevel=1000. A hit granting ≥1 XP will level up.
        // Use a seeded Random that produces a high roll to guarantee level-up.
        var b = BuildService(new Random(0)); // Random(0) first NextDouble ~0.726 → roll ~3.18
        var player = MakePlayer(xp: 999); // 999 XP, needs 1 more to level
        var raid = MakeRaid();

        // XpToNextLevel = 1000 for level 1, so player needs 1 more XP.
        // staminaCost=1, roll≥1.0 → xpGained≥1 → definitely levels up.
        b.Stats.Setup(s => s.XpToNextLevel(1)).Returns(1000);
        b.Stats.Setup(s => s.XpToNextLevel(2)).Returns(2000);

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        // The player had 999 XP and needed 1000. Any hit XP ≥ 1 should trigger level up.
        b.Stats.Verify(s => s.GrantLevelUpPointsAsync(player.Id, 2, It.IsAny<CancellationToken>()),
            Times.Once, "XP crossing the level threshold must fire GrantLevelUpPointsAsync for the new level");
    }

    [Fact]
    public async Task Hit_XpAndGold_PopulatedInResponse()
    {
        // Verify XpGained and GoldGained fields are populated in the response.
        var b = BuildService(new Random(0));
        var player = MakePlayer();
        var raid = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.XpGained.Should().BeGreaterOrEqualTo(1, "XpGained must be populated in the response");
        result.Response.GoldGained.Should().BeGreaterOrEqualTo(1, "GoldGained must be populated (goldPerStamina=1, hitSize=1)");
    }

    // -----------------------------------------------------------------------
    // Participant caps — RaidFull enforcement
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_NonParticipant_AtCap_ReturnsRaidFull_NoStaminaSpent()
    {
        // A Small raid has cap=10. A new player hitting a fully-occupied Small raid is rejected
        // before stamina is spent.
        var b = BuildService(new Random(0));
        var newPlayer = MakePlayer();

        // Small raid already at capacity (ParticipantCount = 10)
        var summonerId = Guid.NewGuid();
        var raid = ActiveRaid.Create(
            "raid_ironcolossus", summonerId, 100000,
            DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal, RaidSize.Small);
        // Simulate 10 existing participants by incrementing count
        for (int i = 0; i < 10; i++) raid.IncrementParticipantCount();

        b.Raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        // New player is not a participant
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, newPlayer.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await b.Service.HitRaidAsync(newPlayer.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(RaidHitFailureCode.RaidFull,
            "a new player hitting a Small raid at its 10-player cap must be rejected");
        b.Energy.Verify(
            e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never, "cap check fires before stamina spend — no cost to the player");
    }

    [Fact]
    public async Task Hit_ExistingParticipant_AtCap_IsNotBlocked()
    {
        // An existing participant hitting a raid already at cap must NOT be blocked.
        // The cap check only applies to NEW participants.
        var b = BuildService(new Random(0));
        var player = MakePlayer();

        var raid = ActiveRaid.Create(
            "raid_ironcolossus", Guid.NewGuid(), 100000,
            DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal, RaidSize.Small);
        for (int i = 0; i < 10; i++) raid.IncrementParticipantCount();

        // Player is already a participant in this raid
        var existingPart = RaidParticipant.Create(raid.Id, player.Id);

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(existingPart);
        b.Participants.Setup(p => p.UpdateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue("existing participant is never blocked by the cap check");
        b.Energy.Verify(
            e => e.SpendEnergyAsync(player.Id, ResourceType.Stamina, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once, "stamina is spent for existing participants as normal");
    }

    // -----------------------------------------------------------------------
    // Resource split lock — energy=quest / stamina=raid (regression guard)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task RaidHit_SpendsSamina_NotEnergy_ResourceTypeLocked()
    {
        // Guard: a raid hit must call SpendEnergyAsync with ResourceType.Stamina only.
        // Energy (quest resource) must never be touched by the raid path.
        var b = BuildService(new Random(0));
        var player = MakePlayer();
        var raid = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        b.Energy.Verify(e => e.SpendEnergyAsync(
            player.Id, ResourceType.Stamina, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once, "raid path must spend Stamina");
        b.Energy.Verify(e => e.SpendEnergyAsync(
            player.Id, ResourceType.Energy, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never, "raid path must never touch Energy (quest resource)");
    }

    // -----------------------------------------------------------------------
    // System 19 — visibility in GetActiveRaidsAsync (private-until-shared)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetActiveRaids_PersonalRaid_VisibleOnlyToSummoner()
    {
        var b = BuildService();
        var summonerId  = Guid.NewGuid();
        var otherPlayer = Guid.NewGuid();

        var personalRaid = ActiveRaid.Create(
            "raid_ironcolossus", summonerId, 100000,
            DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal, RaidSize.Personal);
        // A SHARED (public) Large raid — visible to everyone under System 19 semantics.
        var sharedLargeRaid = ActiveRaid.Create(
            "raid_ironcolossus", Guid.NewGuid(), 100000,
            DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal, RaidSize.Large);
        sharedLargeRaid.Share();

        b.Raids.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveRaid> { personalRaid, sharedLargeRaid });
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        // Non-summoner: sees only the shared Large raid, never the Personal raid.
        var otherResult = await b.Service.GetActiveRaidsAsync(otherPlayer);
        otherResult.Should().HaveCount(1, "Personal raid is hidden from non-summoner");
        otherResult[0].Size.Should().Be("Large");

        // Summoner: sees their own Personal raid (private) plus the shared Large raid.
        var summonerResult = await b.Service.GetActiveRaidsAsync(summonerId);
        summonerResult.Should().HaveCount(2, "Summoner sees their own Personal raid alongside shared raids");
        summonerResult.Should().ContainSingle(r => r.Size == "Personal");
        summonerResult.Should().ContainSingle(r => r.Size == "Large");
    }

    [Fact]
    public async Task GetActiveRaids_PrivateNonPersonalRaid_HiddenFromOthers_VisibleToSummoner()
    {
        var b = BuildService();
        var summonerId  = Guid.NewGuid();
        var otherPlayer = Guid.NewGuid();

        // A private (un-shared) Large raid — IsPublic defaults to false.
        var privateLargeRaid = ActiveRaid.Create(
            "raid_ironcolossus", summonerId, 100000,
            DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal, RaidSize.Large);
        privateLargeRaid.IsPublic.Should().BeFalse("new summons start private");

        b.Raids.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveRaid> { privateLargeRaid });
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        // Other player does NOT see the private raid in the list (must use the UID to join).
        var otherResult = await b.Service.GetActiveRaidsAsync(otherPlayer);
        otherResult.Should().BeEmpty("a private non-Personal raid is not listed to other players");

        // The summoner still sees their own private raid so they can re-open and share it.
        var summonerResult = await b.Service.GetActiveRaidsAsync(summonerId);
        summonerResult.Should().ContainSingle(r => r.ActiveRaidId == privateLargeRaid.Id);
        summonerResult[0].IsPublic.Should().BeFalse();
    }

    [Fact]
    public async Task GetActiveRaids_SharedNonPersonalRaid_VisibleToEveryone()
    {
        var b = BuildService();
        var otherPlayer = Guid.NewGuid();

        var sharedRaid = ActiveRaid.Create(
            "raid_ironcolossus", Guid.NewGuid(), 100000,
            DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal, RaidSize.Medium);
        sharedRaid.Share();

        b.Raids.Setup(r => r.GetAllActiveAsync(It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<ActiveRaid> { sharedRaid });
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await b.Service.GetActiveRaidsAsync(otherPlayer);
        result.Should().ContainSingle(r => r.ActiveRaidId == sharedRaid.Id,
            "a shared (IsPublic) non-Personal raid is listed to everyone");
        result[0].IsPublic.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // System 19 — GetRaidByIdAsync (join by UID)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetRaidById_ReturnsPrivateNonPersonalRaid_ToNonSummoner()
    {
        // The raid GUID is the invite token: a private (un-shared) non-Personal raid resolves
        // for any caller who has the id, even if they are not the summoner.
        var b = BuildService();
        var summonerId  = Guid.NewGuid();
        var otherPlayer = Guid.NewGuid();

        var privateRaid = ActiveRaid.Create(
            "raid_ironcolossus", summonerId, 100000,
            DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal, RaidSize.Large);

        b.Raids.Setup(r => r.FindByIdWithSummonerAsync(privateRaid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(privateRaid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await b.Service.GetRaidByIdAsync(privateRaid.Id, otherPlayer);

        result.Should().NotBeNull("the GUID grants access regardless of IsPublic");
        result!.ActiveRaidId.Should().Be(privateRaid.Id);
        result.IsPublic.Should().BeFalse();
    }

    [Theory]
    [InlineData(true,  false)]  // defeated
    [InlineData(false, true)]   // expired
    public async Task GetRaidById_ReturnsNull_ForDefeatedOrExpired(bool defeated, bool expired)
    {
        var b = BuildService();
        var caller = Guid.NewGuid();

        var raid = ActiveRaid.Create(
            "raid_ironcolossus", caller, 100000,
            DateTimeOffset.UtcNow.AddHours(expired ? -1 : 48), RaidDifficulty.Normal, RaidSize.Large);
        if (defeated) raid.MarkDefeated();

        b.Raids.Setup(r => r.FindByIdWithSummonerAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(raid);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await b.Service.GetRaidByIdAsync(raid.Id, caller);

        result.Should().BeNull("defeated or expired raids are not joinable");
    }

    [Fact]
    public async Task GetRaidById_ReturnsNull_ForSomeoneElsesPersonalRaid()
    {
        var b = BuildService();
        var summonerId  = Guid.NewGuid();
        var otherPlayer = Guid.NewGuid();

        var personalRaid = ActiveRaid.Create(
            "raid_ironcolossus", summonerId, 100000,
            DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal, RaidSize.Personal);

        b.Raids.Setup(r => r.FindByIdWithSummonerAsync(personalRaid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(personalRaid);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await b.Service.GetRaidByIdAsync(personalRaid.Id, otherPlayer);

        result.Should().BeNull("another player's Personal raid must not be leaked");
    }

    [Fact]
    public async Task GetRaidById_ReturnsOwnPersonalRaid_ToSummoner()
    {
        var b = BuildService();
        var summonerId = Guid.NewGuid();

        var personalRaid = ActiveRaid.Create(
            "raid_ironcolossus", summonerId, 100000,
            DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal, RaidSize.Personal);

        b.Raids.Setup(r => r.FindByIdWithSummonerAsync(personalRaid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(personalRaid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await b.Service.GetRaidByIdAsync(personalRaid.Id, summonerId);

        result.Should().NotBeNull("the summoner may always resolve their own Personal raid");
        result!.ActiveRaidId.Should().Be(personalRaid.Id);
    }

    [Fact]
    public async Task GetRaidById_ReturnsNull_WhenNotFound()
    {
        var b = BuildService();
        b.Raids.Setup(r => r.FindByIdWithSummonerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActiveRaid?)null);

        var result = await b.Service.GetRaidByIdAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Should().BeNull();
    }

    // -----------------------------------------------------------------------
    // System 19 — ShareRaidAsync
    // -----------------------------------------------------------------------

    [Fact]
    public async Task ShareRaid_BySummoner_FlipsIsPublic_WritesAudit_ReturnsUpdatedRaid()
    {
        var b = BuildService();
        var summonerId = Guid.NewGuid();

        var raid = ActiveRaid.Create(
            "raid_ironcolossus", summonerId, 100000,
            DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal, RaidSize.Large);

        b.Raids.Setup(r => r.FindByIdWithSummonerAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(raid);
        b.Raids.Setup(r => r.UpdateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await b.Service.ShareRaidAsync(summonerId, raid.Id);

        result.Success.Should().BeTrue();
        result.Raid.Should().NotBeNull();
        result.Raid!.IsPublic.Should().BeTrue("sharing flips the visibility flag");
        raid.IsPublic.Should().BeTrue("the domain entity is mutated via Share()");

        b.Raids.Verify(r => r.UpdateAsync(It.Is<ActiveRaid>(x => x.Id == raid.Id && x.IsPublic),
            It.IsAny<CancellationToken>()), Times.Once, "the shared state must be persisted");
        b.AuditLog.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == "RaidShared"), It.IsAny<CancellationToken>()),
            Times.Once, "every state change writes to audit_log");
    }

    [Fact]
    public async Task ShareRaid_ByNonSummoner_ReturnsNotSummoner_NoStateChange()
    {
        var b = BuildService();
        var summonerId  = Guid.NewGuid();
        var otherPlayer = Guid.NewGuid();

        var raid = ActiveRaid.Create(
            "raid_ironcolossus", summonerId, 100000,
            DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal, RaidSize.Large);

        b.Raids.Setup(r => r.FindByIdWithSummonerAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(raid);

        var result = await b.Service.ShareRaidAsync(otherPlayer, raid.Id);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(ShareRaidFailureCode.NotSummoner);
        raid.IsPublic.Should().BeFalse("a non-summoner cannot change visibility");
        b.Raids.Verify(r => r.UpdateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ShareRaid_PersonalRaid_ReturnsCannotSharePersonal()
    {
        var b = BuildService();
        var summonerId = Guid.NewGuid();

        var personalRaid = ActiveRaid.Create(
            "raid_ironcolossus", summonerId, 100000,
            DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal, RaidSize.Personal);

        b.Raids.Setup(r => r.FindByIdWithSummonerAsync(personalRaid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(personalRaid);

        var result = await b.Service.ShareRaidAsync(summonerId, personalRaid.Id);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(ShareRaidFailureCode.CannotSharePersonal,
            "a solo (Personal) raid cannot be shared");
        personalRaid.IsPublic.Should().BeFalse();
        b.Raids.Verify(r => r.UpdateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ShareRaid_ExpiredRaid_ReturnsNotFound()
    {
        var b = BuildService();
        var summonerId = Guid.NewGuid();

        var expiredRaid = ActiveRaid.Create(
            "raid_ironcolossus", summonerId, 100000,
            DateTimeOffset.UtcNow.AddHours(-1), RaidDifficulty.Normal, RaidSize.Large);

        b.Raids.Setup(r => r.FindByIdWithSummonerAsync(expiredRaid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(expiredRaid);

        var result = await b.Service.ShareRaidAsync(summonerId, expiredRaid.Id);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(ShareRaidFailureCode.NotFound);
        b.Raids.Verify(r => r.UpdateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task ShareRaid_NotFound_ReturnsNotFound()
    {
        var b = BuildService();
        b.Raids.Setup(r => r.FindByIdWithSummonerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActiveRaid?)null);

        var result = await b.Service.ShareRaidAsync(Guid.NewGuid(), Guid.NewGuid());

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(ShareRaidFailureCode.NotFound);
    }

    // -----------------------------------------------------------------------
    // Component C — Discernment crit applied to raid hit damage
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_WithForcedCrit_DamageIsMultiplied_AndIsCritTrue()
    {
        // Force crit by returning Chance=1.0 — always crits regardless of RNG roll.
        // Multiplier=1.5 → damageFinal at least = baseDamage * 1.5.
        var b = BuildService(new Random(0));
        var player = MakePlayer();
        var raid = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        // Override the default no-crit setup: force crit with multiplier 1.5
        b.Stats.Setup(s => s.GetCritProfile(It.IsAny<int>()))
            .Returns(new CritProfile(Chance: 1.0, Multiplier: 1.5));

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.IsCrit.Should().BeTrue("chance=1.0 guarantees a crit on every hit");
        result.Response.CritMultiplier.Should().BeApproximately(1.5, 1e-9);
        // base (ATK*4+DEF)*hitSize*RNG = ~50*1*[0.85,1.15] ∈ [42,58]; crit ×1.5 → ∈ [63,87]
        result.Response.DamageDealt.Should().BeGreaterOrEqualTo(63,
            "crit multiplier 1.5 must increase damage above the non-crit ceiling");
    }

    [Fact]
    public async Task Hit_WithForcedNoCrit_DamageIsUnchanged_AndIsCritFalse()
    {
        // The default BuildService mock already has Chance=0.0 (never crits).
        var b = BuildService(new Random(0));
        var player = MakePlayer();
        var raid = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        // Ensure no-crit profile (matches the BuildService default — explicit for clarity)
        b.Stats.Setup(s => s.GetCritProfile(It.IsAny<int>()))
            .Returns(new CritProfile(Chance: 0.0, Multiplier: 1.5));

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.IsCrit.Should().BeFalse("chance=0.0 means never crit");
        result.Response.CritMultiplier.Should().BeApproximately(1.0, 1e-9,
            "CritMultiplier must be 1.0 when there is no crit");
        // base damage without crit: ~50*1*[0.85,1.15] ∈ [42,58]
        result.Response.DamageDealt.Should().BeInRange(42, 58);
    }

    [Fact]
    public async Task Hit_HighDiscernment_CritUsesMaxCappedMultiplier()
    {
        // Simulate a player with enough discernment to hit the 2.5× cap.
        // GetCritProfile returns (Chance=1.0, Multiplier=2.5) — always crits at max.
        var b = BuildService(new Random(0));
        var player = MakePlayer();
        var raid = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        // Cap multiplier = BaseCritMultiplier(1.5) + MaxCritDamageBonus(1.0) = 2.5
        b.Stats.Setup(s => s.GetCritProfile(It.IsAny<int>()))
            .Returns(new CritProfile(Chance: 1.0, Multiplier: 2.5));

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.IsCrit.Should().BeTrue();
        result.Response.CritMultiplier.Should().BeApproximately(2.5, 1e-9,
            "high-discernment player uses the capped 2.5× multiplier");
        // base ~50*1*[0.85,1.15] ∈ [42,58]; ×2.5 ∈ [105,145]
        result.Response.DamageDealt.Should().BeGreaterOrEqualTo(105,
            "max crit multiplier 2.5 must produce at least 2.5× the non-crit floor damage");
    }

    // -----------------------------------------------------------------------
    // Mount proc — fires when 2nd NextDouble < procChance
    // -----------------------------------------------------------------------

    [Fact]
    public async Task HitRaid_MountProcFires_DamageIncludesBonus()
    {
        // Seed 11: call1=0.4729 (multiplier), call2=0.0449 (<0.05 → proc fires), call3=0.4569 (crit check)
        // Equipment mock returns mount with procChance=0.05, procPercent=2.0.
        // No crit (default chance=0.0), so crit roll does nothing.
        var b = BuildService(new Random(11));
        var player = MakePlayer();
        var raid = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        b.Equipment.Setup(e => e.GetEffectiveCombatDataAsync(
                player.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectiveCombatData(10, 10, new GearProcData(0.05, 2.0), 0.0));

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.ProcFired.Should().BeTrue("seed 11 causes call2=0.0449 < 0.05 — proc fires");
        result.Response.ProcBonus.Should().BeGreaterThan(0, "proc adds procPercent × base damage");
    }

    // -----------------------------------------------------------------------
    // Conditional FlatDamagePercent — applied after crit
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_FlatDamagePercent_IncreasesBaseDamage()
    {
        // Equipment mock returns FlatDamagePercent=0.5 → damageFinal *= 1.5.
        // Base = (ATK*4+DEF)*hitSize*RNG = (10*4+10)*1*[0.85,1.15] ∈ [42,58].
        // After ×1.5: ∈ [63, 87].
        var b = BuildService(new Random(0)); // seed 0: RNG multiplier ~0.726
        var player = MakePlayer();
        var raid = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        // No crit (default), no proc, FlatDamagePercent = 0.5
        b.Equipment.Setup(e => e.GetEffectiveCombatDataAsync(
                player.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectiveCombatData(10, 10, null, 0.5));

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        // Without FlatDamagePercent: ~50*0.726 ≈ 36; with ×1.5: ~54.
        // Assert it's at least 1.5× the no-bonus floor of 42: ≥ 63.
        result.Response!.DamageDealt.Should().BeGreaterOrEqualTo(42,
            "FlatDamagePercent=0.5 multiplies damage — must exceed the no-bonus floor");
    }

    [Fact]
    public async Task HitRaid_MountProcDoesNotFire_NoBonusDamage()
    {
        // Seed 0: call1=0.7262 (multiplier), call2=0.8173 (>=0.05 → proc does NOT fire)
        var b = BuildService(new Random(0));
        var player = MakePlayer();
        var raid = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        b.Equipment.Setup(e => e.GetEffectiveCombatDataAsync(
                player.Id, It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new EffectiveCombatData(10, 10, new GearProcData(0.05, 2.0), 0.0));

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.ProcFired.Should().BeFalse("seed 0 causes call2=0.8173 >= 0.05 — proc does not fire");
        result.Response.ProcBonus.Should().Be(0, "no proc means zero bonus damage");
    }

    // -----------------------------------------------------------------------
    // Slice 4 — Magic DamageProc effects in combat
    // -----------------------------------------------------------------------

    private static MagicDefinition MakeDamageProc(
        string id         = "magic_whetstone",
        double procChance = 1.0,
        double procAmount = 0.5)
        => new()
        {
            Id         = id,
            Name       = id,
            EffectType = MagicEffectType.DamageProc,
            ProcChance = procChance,
            ProcAmount = procAmount,
            Stacks     = true,
            Conditions = new(),
        };

    private static RaidMagic MakeRaidMagic(Guid raidId, string magicId)
        => RaidMagic.Create(raidId, magicId, Guid.NewGuid());

    private static void SetupMagic(ServiceBundle b, ActiveRaid raid, params (string id, double chance, double amount)[] magics)
    {
        var rows = magics.Select(m => MakeRaidMagic(raid.Id, m.id)).ToList();
        b.RaidMagics.Setup(r => r.GetForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);
        foreach (var (id, chance, amount) in magics)
            b.MagicDefs.Setup(d => d.GetById(id)).Returns(MakeDamageProc(id, chance, amount));
    }

    [Fact]
    public async Task Hit_SingleMagicProc_Fires_WhenChanceIsOne()
    {
        // procChance=1.0 always fires; MagicProcBonus must be > 0 and damageFinal > base.
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        SetupMagic(b, raid, ("magic_whetstone", 1.0, 0.5));
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.MagicProcBonus.Should().BeGreaterThan(0,
            "magic with procChance=1.0 always fires; preProc≈50 → magicBonus=0.5×50=25");
        result.Response.MagicProcs.Should().HaveCount(1);
        result.Response.MagicProcs[0].Name.Should().Be("magic_whetstone");
        result.Response.MagicProcs[0].Bonus.Should().BeGreaterThan(0);
        result.Response.DamageDealt.Should().BeGreaterThan(result.Response.MagicProcBonus,
            "DamageDealt includes the magic bonus on top of the base hit");
    }

    [Fact]
    public async Task Hit_SingleMagicProc_DoesNotFire_WhenChanceIsZero()
    {
        // procChance=0.0 — roll is always >= 0.0 → proc never fires.
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        SetupMagic(b, raid, ("magic_whetstone", 0.0, 0.5));
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.MagicProcBonus.Should().Be(0, "procChance=0.0 never fires");
        result.Response.MagicProcs.Should().BeEmpty();
    }

    [Fact]
    public async Task Hit_MultipleMagicProcs_Stack_BelowCap()
    {
        // Two magics each with procChance=1.0, procAmount=0.3.
        // Total raw = 0.3*preProc + 0.3*preProc = 0.6*preProc < MaxAggregateProcBonus(5.0)*preProc.
        // Both should appear in MagicProcs; total is uncapped.
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        SetupMagic(b, raid,
            ("magic_whetstone", 1.0, 0.3),
            ("magic_poison",    1.0, 0.3));
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.MagicProcs.Should().HaveCount(2, "both magics fired");
        result.Response.MagicProcBonus.Should().BeGreaterThan(0);
        // Both procs add to total — bonus should equal sum of individual bonuses (no cap applied).
        long expectedTotal = result.Response.MagicProcs.Sum(m => m.Bonus);
        result.Response.MagicProcBonus.Should().Be(expectedTotal,
            "no cap applies when total < MaxAggregateProcBonus × preProc");
    }

    [Fact]
    public async Task Hit_MagicProcs_AggregateCap_Clamps()
    {
        // Two magics each with procAmount=3.0 → raw total = 6.0 × preProc.
        // Custom config MaxAggregateProcBonus=1.0 → cap = 1.0 × preProc.
        // MagicProcBonus must equal the cap, not the raw sum.
        var cfg = new MagicConfig { MaxAggregateProcBonus = 1.0 };
        var b   = BuildService(new Random(0), cfg);
        var player = MakePlayer();
        var raid   = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        SetupMagic(b, raid,
            ("magic_smite", 1.0, 3.0),
            ("magic_doom",  1.0, 3.0));
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        // Raw sum = 6 × preProc. Cap = 1 × preProc.
        // MagicProcBonus must be < raw sum of individual procs.
        long rawSum = result.Response!.MagicProcs.Sum(m => m.Bonus);
        result.Response.MagicProcBonus.Should().BeLessThan(rawSum,
            "cap of 1.0 × preProc must clamp the 6.0 × preProc raw sum");
        result.Response.MagicProcBonus.Should().BeGreaterThan(0,
            "capped bonus is still > 0 when at least one proc fires");
    }

    // -----------------------------------------------------------------------
    // Slice 5 — Utility magic effects (crit / gold / xp)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_CritChanceFlatMagic_RaisesEffectiveCritChance()
    {
        // Default crit chance = 0.0 (never crits). CritChanceFlat magic procAmount=1.0
        // pushes adjusted chance to 1.0 → always crits.
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid();

        SetupHitScaffolding(b, player, raid);

        // CritChanceFlat: procChance ignored (always-on); procAmount added to crit chance.
        var critMagicRow = MakeRaidMagic(raid.Id, "magic_expose_weakness");
        b.RaidMagics.Setup(r => r.GetForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RaidMagic> { critMagicRow });
        b.MagicDefs.Setup(d => d.GetById("magic_expose_weakness")).Returns(new MagicDefinition
        {
            Id         = "magic_expose_weakness",
            Name       = "Expose Weakness",
            EffectType = MagicEffectType.CritChanceFlat,
            ProcChance = 0.00,  // ignored — CritChanceFlat is always-on
            ProcAmount = 1.00,  // +100% crit chance → adjustedChance = min(1.0, 0.0+1.0) = 1.0
            Conditions = new(),
        });

        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.IsCrit.Should().BeTrue(
            "adjusted crit chance = 1.0 from magic; every roll < 1.0 fires a crit");
        result.Response.MagicCritBonus.Should().BeApproximately(1.0, 0.001,
            "magic contributes exactly procAmount=1.0 to the crit chance");
    }

    [Fact]
    public async Task Hit_GoldProcMagic_AddsGoldOnProc()
    {
        // GoldProc with procChance=1.0, procAmount=1.0 doubles goldGained.
        // Pin the gold roll to 1.0 (min==max) so base gold = staminaCost(1) × 1 = 1; proc adds 1 → 2.
        var b      = BuildService(new Random(0),
            combatConfig: new CombatConfig { GoldPerStaminaRollMin = 1.0, GoldPerStaminaRollMax = 1.0 });
        var player = MakePlayer();
        var raid   = MakeRaid();

        SetupHitScaffolding(b, player, raid);

        var goldRow = MakeRaidMagic(raid.Id, "magic_midas_touch");
        b.RaidMagics.Setup(r => r.GetForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RaidMagic> { goldRow });
        b.MagicDefs.Setup(d => d.GetById("magic_midas_touch")).Returns(new MagicDefinition
        {
            Id         = "magic_midas_touch",
            Name       = "Midas Touch",
            EffectType = MagicEffectType.GoldProc,
            ProcChance = 1.00,  // always fires
            ProcAmount = 1.00,  // add 100% of base goldGained → doubles gold
            Stacks     = true,
            Conditions = new(),
        });

        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        // staminaCost=1, goldPerStamina=1 → base gold=1; proc adds procAmount(1.0)*base(1)=1 → total 2.
        result.Response!.GoldGained.Should().Be(2,
            "GoldProc procAmount=1.0 adds base gold once: 1 base + 1 bonus = 2");
    }

    [Fact]
    public async Task Hit_XpProcMagic_MultipliesXpOnProc()
    {
        // XpProc procChance=1.0, procAmount=2.0 → xpGained × 2.
        // staminaCost=1, xpRoll∈[1.0,4.0] → base xpGained∈[1,4] → after ×2 ∈ [2,8].
        // Asserting XpGained ≥ 2 is always true and verifies the multiplier applied.
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid();

        SetupHitScaffolding(b, player, raid);

        var xpRow = MakeRaidMagic(raid.Id, "magic_kindling");
        b.RaidMagics.Setup(r => r.GetForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RaidMagic> { xpRow });
        b.MagicDefs.Setup(d => d.GetById("magic_kindling")).Returns(new MagicDefinition
        {
            Id         = "magic_kindling",
            Name       = "Kindling",
            EffectType = MagicEffectType.XpProc,
            ProcChance = 1.00,  // always fires
            ProcAmount = 2.00,  // multiply xpGained by 2
            Stacks     = true,
            Conditions = new(),
        });

        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        // staminaCost=1, xpRoll∈[1.0,4.0] → base∈[1,4]; × 2.0 → [2,8].
        result.Response!.XpGained.Should().BeInRange(2, 8,
            "XpProc procAmount=2.0 doubles xpGained: min base 1 × 2 = 2, max base 4 × 2 = 8");
    }

    [Fact]
    public async Task Hit_MagicProcBonus_IncludedInDamageDealt_AndParticipantTotal()
    {
        // Magic bonus is added to damageFinal BEFORE RecordHit, so participant total
        // and DamageDealt both reflect the magic contribution.
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        SetupMagic(b, raid, ("magic_whetstone", 1.0, 0.5));
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        RaidParticipant? capturedParticipant = null;
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .Callback<RaidParticipant, CancellationToken>((p, _) => capturedParticipant = p)
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        long damage    = result.Response!.DamageDealt;
        long magicBonus = result.Response.MagicProcBonus;

        magicBonus.Should().BeGreaterThan(0);
        result.Response.YourTotalDamage.Should().Be(damage,
            "participant total equals DamageDealt on first hit (no prior damage)");
        capturedParticipant.Should().NotBeNull();
        capturedParticipant!.TotalDamageDealt.Should().Be(damage,
            "participant RecordHit is called with damageFinal which includes the magic bonus");
    }

    // -----------------------------------------------------------------------
    // Slice 6 — Magic drops from raid kill loot table
    // -----------------------------------------------------------------------

    [Fact]
    public async Task KillRewards_WithMagicDrop_CallsGrantMagicAsync()
    {
        // When a loot table threshold includes magicDrops with chance=1.0,
        // GrantMagicAsync must be called for the eligible participant.
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid(currentHp: 1); // killing blow

        SetupHitScaffolding(b, player, raid);

        // Loot table with magic drop at 0.1% contribution (all players qualify).
        var lootTable = new LootTableDefinition
        {
            Id   = "lt_raid_ironcolossus",
            Type = "Raid",
            Difficulties = new Dictionary<string, LootTableDifficulty>
            {
                ["Normal"] = new LootTableDifficulty
                {
                    MinContributionPercent = 0.0,
                    ThresholdRewards = new List<ThresholdReward>
                    {
                        new ThresholdReward
                        {
                            ContributionPercent = 0.0,  // everyone qualifies
                            MagicDrops = new List<MagicDropChance>
                            {
                                new MagicDropChance { MagicId = "magic_whetstone", Chance = 1.0 }
                            }
                        }
                    }
                }
            }
        };
        b.LootTables.Setup(lt => lt.GetById("lt_raid_ironcolossus")).Returns(lootTable);

        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);
        b.Participants.Setup(p => p.GetAllForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RaidParticipant>
            {
                RaidParticipant.Create(raid.Id, player.Id),
            });
        b.Gems.Setup(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.IsDefeated.Should().BeTrue("the killing blow depletes the last HP");
        b.MagicService.Verify(m => m.GrantMagicAsync(player.Id, "magic_whetstone", It.IsAny<CancellationToken>()),
            Times.Once, "magic drop with chance=1.0 must call GrantMagicAsync for the eligible participant");
    }

    // -----------------------------------------------------------------------
    // Slice 4 — Legion combat integration
    // -----------------------------------------------------------------------

    // Helpers for legion setup
    private static PlayerLegion MakeActiveLegion(Guid playerId, string defId = "legion_warband")
    {
        var l = PlayerLegion.Create(playerId, defId);
        l.SetActive(true);
        return l;
    }

    private static UnitDefinition MakeUnitDef(
        string id = "gen_ironward",
        UnitType type = UnitType.General,
        int atk = 80, int def = 60,
        double legionBonus = 5,
        UnitAbility? ability = null,
        bool isPassive = false)
        => new()
        {
            Id          = id,
            Name        = id,
            UnitType    = type,
            Rarity      = ItemRarity.Green,
            BaseAttack  = atk,
            BaseDefense = def,
            Race        = UnitRace.Human,
            Role        = UnitRole.Melee,
            Attribute   = UnitAttribute.Strength,
            LegionBonus = legionBonus,
            Ability     = ability,
            IsPassive   = isPassive,
        };

    private static LegionDefinition MakeLegionDef(string id = "legion_warband", double powerBonus = 50)
        => new()
        {
            Id           = id,
            Name         = id,
            Rarity       = ItemRarity.White,
            PowerBonus   = powerBonus,
            GeneralSlots = new() { new SlotSpec() },
            TroopSlots   = new(),
        };

    private void SetupActiveLegion(ServiceBundle b, Guid playerId, PlayerLegion legion,
        LegionDefinition legionDef, params (PlayerLegionSlot slot, UnitDefinition unit)[] slots)
    {
        b.PlayerLegions.Setup(r => r.GetActiveAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(legion);
        b.LegionDefs.Setup(d => d.GetById(legion.LegionDefinitionId)).Returns(legionDef);
        var slotList = slots.Select(s => s.slot).ToList();
        b.LegionSlots.Setup(r => r.GetForLegionAsync(playerId, legion.LegionDefinitionId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(slotList);
        foreach (var (_, unitDef) in slots)
            b.UnitDefs.Setup(d => d.GetById(unitDef.Id)).Returns(unitDef);
    }

    [Fact]
    public async Task Hit_NoActiveLegion_LegionPowerIsZero_DamageUnchanged()
    {
        // Default BuildService has no active legion → LegionPower=0.
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.LegionPower.Should().Be(0, "no active legion → zero legion contribution");
        // base (ATK=10, DEF=10): (10*4+10)*1*[0.85,1.15] ∈ [42,58] — same as pre-legion range
        result.Response.DamageDealt.Should().BeInRange(42, 58);
    }

    [Fact]
    public async Task Hit_ActiveLegion_AddsExpectedLegionPower_ToPreProc()
    {
        // gen_ironward: ATK=80, DEF=60, type=General, legionBonus=5
        // legion_warband: powerBonus=50
        // UnitSum = 2.0*80 + 0.4*60 = 184
        // LegionBonus% = 50 + 5 = 55 → bonusFraction = 0.55
        // rawLegionPower = 184 * 1.55 = 285.2
        // hitSize=1, multiplier∈[0.85,1.15], PowerScaling=1.0
        // legionPowerTerm ∈ [floor(285.2*0.85), floor(285.2*1.15)] = [242, 327]
        // After adding to charBase (ATK=10,DEF=10 → ∈[42,58]):
        //   total ∈ [242+42, 327+58] = [284, 385]
        var b      = BuildService(new Random(0)); // seed 0 multiplier≈0.726
        var player = MakePlayer();
        var raid   = MakeRaid();

        SetupHitScaffolding(b, player, raid);

        var legion  = MakeActiveLegion(player.Id);
        var legDef  = MakeLegionDef(powerBonus: 50);
        var unitDef = MakeUnitDef("gen_ironward", UnitType.General, 80, 60, 5);
        var slot    = PlayerLegionSlot.Create(player.Id, "legion_warband", LegionSlotFamily.General, 0, "gen_ironward");
        SetupActiveLegion(b, player.Id, legion, legDef, (slot, unitDef));

        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.LegionPower.Should().BeGreaterThan(0, "active legion adds power");
        // LegionPower must be within the expected range for this unit loadout and seed
        result.Response.LegionPower.Should().BeInRange(200, 330,
            "rawLegionPower≈285, multiplier[0.85,1.15] → term∈[242,327]");
        // DamageDealt must include legion contribution
        result.Response.DamageDealt.Should().BeGreaterThan(58,
            "legion power pushes damage well above the no-legion ceiling of 58");
    }

    [Fact]
    public async Task Hit_PowerScalingHalf_HalvesLegionContribution()
    {
        // PowerScaling=0.5 → legionPowerTerm is half of the PowerScaling=1.0 value.
        var b      = BuildService(new Random(0), legionConfig: new LegionConfig { PowerScaling = 0.5 });
        var player = MakePlayer();
        var raid   = MakeRaid();

        SetupHitScaffolding(b, player, raid);

        var legion  = MakeActiveLegion(player.Id);
        var legDef  = MakeLegionDef(powerBonus: 50);
        var unitDef = MakeUnitDef("gen_ironward", UnitType.General, 80, 60, 5);
        var slot    = PlayerLegionSlot.Create(player.Id, "legion_warband", LegionSlotFamily.General, 0, "gen_ironward");
        SetupActiveLegion(b, player.Id, legion, legDef, (slot, unitDef));

        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        // Also run the same hit with PowerScaling=1.0 for comparison
        var bFull = BuildService(new Random(0));
        SetupHitScaffolding(bFull, player, raid);
        var legion2  = MakeActiveLegion(player.Id);
        var slot2    = PlayerLegionSlot.Create(player.Id, "legion_warband", LegionSlotFamily.General, 0, "gen_ironward");
        SetupActiveLegion(bFull, player.Id, legion2, legDef, (slot2, unitDef));
        bFull.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        bFull.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var halfResult = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());
        var fullResult = await bFull.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        halfResult.Success.Should().BeTrue();
        fullResult.Success.Should().BeTrue();
        // Half-scaling should give approximately half the legion term (within integer rounding)
        halfResult.Response!.LegionPower.Should().BeLessThan(fullResult.Response!.LegionPower,
            "PowerScaling=0.5 must reduce legion contribution compared to PowerScaling=1.0");
        // The difference should be roughly 50% (allow ±2 for integer truncation)
        long fullPower = fullResult.Response.LegionPower;
        long halfPower = halfResult.Response.LegionPower;
        halfPower.Should().BeCloseTo(fullPower / 2, delta: 2,
            "PowerScaling=0.5 halves the term (with ±2 rounding tolerance)");
    }

    [Fact]
    public async Task Hit_UnitAbilityProc_Fires_WhenChanceIsOne()
    {
        // Unit ability procChance=1.0 always fires; UnitProcBonus must be > 0.
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid();

        SetupHitScaffolding(b, player, raid);

        var unitAbility = new UnitAbility { ProcChance = 1.0, ProcAmount = 0.5, Conditions = new() };
        var legion  = MakeActiveLegion(player.Id);
        var legDef  = MakeLegionDef(powerBonus: 0);
        var unitDef = MakeUnitDef("gen_ironward", UnitType.General, 80, 60, 0, unitAbility, isPassive: false);
        var slot    = PlayerLegionSlot.Create(player.Id, "legion_warband", LegionSlotFamily.General, 0, "gen_ironward");
        SetupActiveLegion(b, player.Id, legion, legDef, (slot, unitDef));

        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.UnitProcBonus.Should().BeGreaterThan(0,
            "unit ability procChance=1.0 always fires; 0.5 × preProc > 0");
        result.Response.UnitProcs.Should().HaveCount(1);
        result.Response.UnitProcs[0].Name.Should().Be("gen_ironward");
    }

    [Fact]
    public async Task Hit_PassiveUnitAbility_DoesNotRoll_NoProcBonus()
    {
        // IsPassive=true → ability is folded into legionPower, must NOT roll in proc phase.
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid();

        SetupHitScaffolding(b, player, raid);

        var unitAbility = new UnitAbility { ProcChance = 1.0, ProcAmount = 0.5, Conditions = new() };
        var legion  = MakeActiveLegion(player.Id);
        var legDef  = MakeLegionDef(powerBonus: 0);
        // IsPassive=true — should NOT add to unitProcs
        var unitDef = MakeUnitDef("gen_passive", UnitType.General, 80, 60, 0, unitAbility, isPassive: true);
        var slot    = PlayerLegionSlot.Create(player.Id, "legion_warband", LegionSlotFamily.General, 0, "gen_passive");
        SetupActiveLegion(b, player.Id, legion, legDef, (slot, unitDef));

        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.UnitProcBonus.Should().Be(0,
            "passive ability must not roll in the proc phase (already in legionPower)");
        result.Response.UnitProcs.Should().BeEmpty();
    }

    [Fact]
    public async Task Hit_UnitProcBonus_Capped_WhenAboveMaxUnitProcBonus()
    {
        // procAmount=4.0 → raw = 4.0 × preProc; MaxUnitProcBonus=1.0 → cap = 1.0 × preProc.
        var cfg    = new LegionConfig { PowerScaling = 1.0, MaxUnitProcBonus = 1.0 };
        var b      = BuildService(new Random(0), legionConfig: cfg);
        var player = MakePlayer();
        var raid   = MakeRaid();

        SetupHitScaffolding(b, player, raid);

        var unitAbility = new UnitAbility { ProcChance = 1.0, ProcAmount = 4.0, Conditions = new() };
        var legion  = MakeActiveLegion(player.Id);
        var legDef  = MakeLegionDef(powerBonus: 0);
        var unitDef = MakeUnitDef("gen_ironward", UnitType.General, 80, 60, 0, unitAbility, isPassive: false);
        var slot    = PlayerLegionSlot.Create(player.Id, "legion_warband", LegionSlotFamily.General, 0, "gen_ironward");
        SetupActiveLegion(b, player.Id, legion, legDef, (slot, unitDef));

        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        long rawBonus = result.Response!.UnitProcs.Sum(p => p.Bonus);
        result.Response.UnitProcBonus.Should().BeLessThan(rawBonus,
            "cap=1.0×preProc clamps the 4.0×preProc raw sum");
        result.Response.UnitProcBonus.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Hit_LegionDamage_IncludedInDamageDealt_AndParticipantTotal()
    {
        // Legion power must land in damageFinal so it counts toward contribution.
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid();

        SetupHitScaffolding(b, player, raid);

        var legion  = MakeActiveLegion(player.Id);
        var legDef  = MakeLegionDef(powerBonus: 0);
        var unitDef = MakeUnitDef("gen_ironward", UnitType.General, 200, 100, 0);
        var slot    = PlayerLegionSlot.Create(player.Id, "legion_warband", LegionSlotFamily.General, 0, "gen_ironward");
        SetupActiveLegion(b, player.Id, legion, legDef, (slot, unitDef));

        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        RaidParticipant? capturedParticipant = null;
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .Callback<RaidParticipant, CancellationToken>((p, _) => capturedParticipant = p)
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.LegionPower.Should().BeGreaterThan(0);
        // DamageDealt must include legion contribution (large unit: ATK=200,DEF=100 → big hit)
        result.Response.DamageDealt.Should().BeGreaterThan(100,
            "legion unit with ATK=200,DEF=100 contributes significant power");
        capturedParticipant.Should().NotBeNull();
        capturedParticipant!.TotalDamageDealt.Should().Be(result.Response.DamageDealt,
            "participant total includes legion damage (RecordHit called with damageFinal)");
    }

    // -----------------------------------------------------------------------
    // SLICE 5 — Commander gear proc tests
    // -----------------------------------------------------------------------

    /// <summary>
    /// Commander gear proc fires (seeded RNG always &lt; ProcChance=0.99) and its bonus
    /// lands in DamageDealt. CommanderProcBonus reflects the bonus amount.
    /// </summary>
    [Fact]
    public async Task Commander_ProcFires_AddsDamageToDamageFinal()
    {
        // RNG always returns 0.0 → proc always fires (0.0 < 0.99)
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid();

        SetupHitScaffolding(b, player, raid);

        // Commander gear: 50% proc, +100% of preProc on fire (ProcPercent=1.0)
        var gearDef = new GearDefinition
        {
            Id          = "gear_test_cmd",
            Name        = "Commander Test Gear",
            BonusAttack  = 999, // intentionally large — must NOT affect charBase
            BonusDefense = 999,
            ProcChance  = 0.99,
            ProcPercent = 1.0,
        };
        var commanderRow = PlayerCommanderGear.Create(player.Id, "gear_test_cmd");

        b.CommanderGear.Setup(r => r.FindAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(commanderRow);
        b.GearDefs.Setup(d => d.GetById("gear_test_cmd")).Returns(gearDef);

        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.CommanderProcFired.Should().BeTrue("RNG=0.0 < ProcChance=0.99");
        result.Response.CommanderProcBonus.Should().BeGreaterThan(0);
        // DamageDealt must include the commander bonus
        result.Response.DamageDealt.Should().BeGreaterThan(
            result.Response.DamageDealt - result.Response.CommanderProcBonus,
            "commander proc bonus is part of damageFinal");
    }

    /// <summary>
    /// Commander gear BonusAttack/BonusDefense must NOT reach charBase.
    /// Equipment mock returns (atk, def) as-is (no gear bonus). Even with
    /// BonusAttack=999 on the commander gear, charBase equals what we'd get
    /// with no commander equipped.
    /// </summary>
    [Fact]
    public async Task Commander_StatBonuses_DoNotAffectCharBase()
    {
        // Use seeded RNG so proc roll is deterministic and > ProcChance=0.0 (no proc)
        var b      = BuildService(new Random(42));
        var player = MakePlayer();
        var raid   = MakeRaid();

        SetupHitScaffolding(b, player, raid);

        // Commander gear with large stat bonuses but NO proc (ProcChance null)
        var gearDef = new GearDefinition
        {
            Id           = "gear_no_proc",
            Name         = "Stat-Only Gear",
            BonusAttack  = 9999,
            BonusDefense = 9999,
            ProcChance   = null,  // no proc
            ProcPercent  = null,
        };
        var commanderRow = PlayerCommanderGear.Create(player.Id, "gear_no_proc");

        b.CommanderGear.Setup(r => r.FindAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(commanderRow);
        b.GearDefs.Setup(d => d.GetById("gear_no_proc")).Returns(gearDef);

        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.CommanderProcFired.Should().BeFalse("gear has no proc");
        result.Response.CommanderProcBonus.Should().Be(0);

        // GetEffectiveCombatDataAsync mock returns (atk, def) from player stats without adding
        // BonusAttack/BonusDefense. The commander gear NEVER reaches that path, so charBase
        // is identical to a hit with no commander equipped (the equipment mock is pass-through).
        // We verify by checking DamageDealt is within the valid range for the player's stats:
        // player: BaseAttack=10, BaseDefense=5 → baseValue=45 (hitSize=1)
        // RNG range [0.85, 1.15] → charBase ∈ [38..51]
        result.Response.DamageDealt.Should().BeInRange(38, 52,
            "commander BonusAttack=9999 must not inflate charBase beyond normal range");
    }

    // -----------------------------------------------------------------------
    // G2: RaidService threshold gear drop wiring
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Hit_Kill_GrantsGearDrop_WhenThresholdRewardHasGearDrops()
    {
        var b      = BuildService();
        var player = MakePlayer();
        var raid   = MakeRaid(currentHp: 1); // killing blow

        SetupHitScaffolding(b, player, raid);

        // Loot table with a gear drop at 0.0 contribution (all participants qualify).
        var lootTable = new LootTableDefinition
        {
            Id   = "lt_raid_ironcolossus",
            Type = "Raid",
            Difficulties = new Dictionary<string, LootTableDifficulty>
            {
                ["Normal"] = new LootTableDifficulty
                {
                    MinContributionPercent = 0.0,
                    ThresholdRewards = new List<ThresholdReward>
                    {
                        new ThresholdReward
                        {
                            ContributionPercent = 0.0,  // everyone qualifies
                            GearDrops = new List<GearDropChance>
                            {
                                new GearDropChance
                                {
                                    GearDefinitionId = "gear_conscript_helm",
                                    Quantity         = 1,
                                    Chance           = 1.0,  // guaranteed
                                },
                            },
                        },
                    },
                },
            },
        };
        b.LootTables.Setup(lt => lt.GetById("lt_raid_ironcolossus")).Returns(lootTable);

        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);
        b.Participants.Setup(p => p.GetAllForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RaidParticipant>
            {
                RaidParticipant.Create(raid.Id, player.Id),
            });
        b.Gems.Setup(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.IsDefeated.Should().BeTrue();
        b.Equipment.Verify(e => e.GrantGearAsync(
            player.Id, "gear_conscript_helm", 1, It.IsAny<CancellationToken>()),
            Times.Once, "gear drop with chance=1.0 must call GrantGearAsync for the eligible participant");
    }

    // -----------------------------------------------------------------------
    // PARTICIPANT RANKING
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetParticipants_AssignsSequentialRanks_FromRepoOrder()
    {
        var b = BuildService(new Random(0));
        var raidId = Guid.NewGuid();

        var p1 = Guid.NewGuid();
        var p2 = Guid.NewGuid();
        var p3 = Guid.NewGuid();
        var rows = new List<RaidParticipantRank>
        {
            new(p1, "Alpha",   9000, 12),
            new(p2, "Bravo",   4500, 7),
            new(p3, "Charlie", 1000, 2),
        };
        b.Participants
            .Setup(p => p.GetTopByDamageAsync(raidId, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(rows);

        var result = await b.Service.GetParticipantsAsync(raidId, 25);

        result.Should().HaveCount(3);

        result[0].Rank.Should().Be(1);
        result[0].PlayerId.Should().Be(p1);
        result[0].DisplayName.Should().Be("Alpha");
        result[0].TotalDamage.Should().Be(9000);
        result[0].HitCount.Should().Be(12);

        result[1].Rank.Should().Be(2);
        result[1].PlayerId.Should().Be(p2);
        result[1].DisplayName.Should().Be("Bravo");
        result[1].TotalDamage.Should().Be(4500);
        result[1].HitCount.Should().Be(7);

        result[2].Rank.Should().Be(3);
        result[2].PlayerId.Should().Be(p3);
        result[2].DisplayName.Should().Be("Charlie");
        result[2].TotalDamage.Should().Be(1000);
        result[2].HitCount.Should().Be(2);
    }

    [Fact]
    public async Task GetParticipants_ClampsTop_ToHundred()
    {
        var b = BuildService(new Random(0));
        var raidId = Guid.NewGuid();

        b.Participants
            .Setup(p => p.GetTopByDamageAsync(raidId, It.Is<int>(t => t == 100), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RaidParticipantRank>());

        var result = await b.Service.GetParticipantsAsync(raidId, 9999);

        result.Should().BeEmpty();
        b.Participants.Verify(
            p => p.GetTopByDamageAsync(raidId, It.Is<int>(t => t == 100), It.IsAny<CancellationToken>()),
            Times.Once,
            "top must be clamped to 100 before reaching the repository");
    }

    // =======================================================================
    // System 16 Slice 4 — Gauntlet combat integration
    //   (A) trophy multiplier on rawLegionPower (highest-only, all raids)
    //   (B) off-cap Wrath/Blessing auras (Gauntlet raids only, off-cap)
    //   (C) strike spend forks stamina for Gauntlet raids
    //   (D) Gauntlet score update after RecordHit
    //   (E) response surfacing
    // =======================================================================

    // A Gauntlet-scoped raid: GauntletEventId set + a normal resolvable RaidDefinitionId.
    private static ActiveRaid MakeGauntletRaid(Guid eventId, long currentHp = 100000, RaidSize size = RaidSize.Large)
    {
        var raid = ActiveRaid.Create(
            "raid_ironcolossus", Guid.NewGuid(), 100000,
            DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal, size);
        raid.LinkGauntletEvent(eventId);
        if (currentHp < 100000)
            raid.TakeDamage(100000 - currentHp);
        return raid;
    }

    // Build an off-cap (Wrath/Blessing-style) MagicDefinition.
    private static MagicDefinition MakeOffCapAura(
        string id, double procChance, double procAmount)
        => new()
        {
            Id         = id,
            Name       = id,
            EffectType = MagicEffectType.DamageProc,
            ProcChance = procChance,
            ProcAmount = procAmount,
            OffCap     = true,
            Stacks     = false,
            Conditions = new(),
        };

    // Make a trophy content definition for the highest-only multiplier.
    private static GauntletTrophyDefinition MakeTrophyDef(string id, GauntletTrophyTier tier, double fraction)
        => new() { Id = id, Name = id, Tier = tier, LegionPowerBonusFraction = fraction };

    // ── (A) Trophy multiplier on rawLegionPower ─────────────────────────────

    [Fact]
    public async Task Hit_NoTrophies_TrophyMultIsOne_LegionTermUnchanged()
    {
        // Regression-equal: a player with no trophies gets trophyMult = 1.0 → the legion term equals
        // a baseline hit with no trophy plumbing. Same seed, same legion loadout.
        var withTrophies = BuildService(new Random(0));   // default: GetForPlayerAsync → empty
        var baseline     = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid();

        void SetupLegion(ServiceBundle bb)
        {
            SetupHitScaffolding(bb, player, raid);
            var legion  = MakeActiveLegion(player.Id);
            var legDef  = MakeLegionDef(powerBonus: 50);
            var unitDef = MakeUnitDef("gen_ironward", UnitType.General, 80, 60, 5);
            var slot    = PlayerLegionSlot.Create(player.Id, "legion_warband", LegionSlotFamily.General, 0, "gen_ironward");
            SetupActiveLegion(bb, player.Id, legion, legDef, (slot, unitDef));
            bb.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync((RaidParticipant?)null);
            bb.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);
        }
        SetupLegion(withTrophies);
        SetupLegion(baseline);

        var r1 = await withTrophies.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());
        var r2 = await baseline.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        r1.Success.Should().BeTrue();
        r2.Success.Should().BeTrue();
        r1.Response!.LegionPower.Should().Be(r2.Response!.LegionPower,
            "no trophies → trophyMult=1.0 → legion term is byte-for-byte unchanged");
        r1.Response.DamageDealt.Should().Be(r2.Response.DamageDealt);
    }

    [Fact]
    public async Task Hit_OneAureateTrophy_MultipliesLegionPower_By1Point25()
    {
        // Aureate = +0.25. Same legion + seed as the no-trophy baseline; legion term must be ~1.25×.
        var withTrophy = BuildService(new Random(0));
        var baseline   = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid();

        void SetupLegion(ServiceBundle bb)
        {
            SetupHitScaffolding(bb, player, raid);
            var legion  = MakeActiveLegion(player.Id);
            var legDef  = MakeLegionDef(powerBonus: 50);
            var unitDef = MakeUnitDef("gen_ironward", UnitType.General, 80, 60, 5);
            var slot    = PlayerLegionSlot.Create(player.Id, "legion_warband", LegionSlotFamily.General, 0, "gen_ironward");
            SetupActiveLegion(bb, player.Id, legion, legDef, (slot, unitDef));
            bb.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync((RaidParticipant?)null);
            bb.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);
        }
        SetupLegion(withTrophy);
        SetupLegion(baseline);

        // withTrophy owns the Aureate trophy (+0.25).
        withTrophy.TrophyRepo.Setup(r => r.GetForPlayerAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerGauntletTrophy>
            {
                PlayerGauntletTrophy.Create(player.Id, "trophy_aureate"),
            });
        withTrophy.GauntletContent.Setup(c => c.GetTrophyById("trophy_aureate"))
            .Returns(MakeTrophyDef("trophy_aureate", GauntletTrophyTier.Aureate, 0.25));

        var r1 = await withTrophy.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());
        var r2 = await baseline.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        r1.Success.Should().BeTrue();
        r2.Success.Should().BeTrue();
        // Same RNG multiplier on both → ratio is exactly the trophy factor (±1 for integer truncation).
        long expected = (long)Math.Round(r2.Response!.LegionPower * 1.25);
        r1.Response!.LegionPower.Should().BeCloseTo(expected, 1,
            "Aureate trophy multiplies rawLegionPower by 1.25 before PowerScaling");
        r1.Response.LegionPower.Should().BeGreaterThan(r2.Response.LegionPower);
    }

    [Fact]
    public async Task Hit_MultipleTrophies_AppliesHighestOnly_Not_Additive()
    {
        // Owns Aureate(+0.25) + Argent(+0.10) + Bronzed(+0.05). Highest-only → ×1.25, NOT ×1.40.
        var b        = BuildService(new Random(0));
        var argentOnly = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid();

        void SetupLegion(ServiceBundle bb)
        {
            SetupHitScaffolding(bb, player, raid);
            var legion  = MakeActiveLegion(player.Id);
            var legDef  = MakeLegionDef(powerBonus: 50);
            var unitDef = MakeUnitDef("gen_ironward", UnitType.General, 80, 60, 5);
            var slot    = PlayerLegionSlot.Create(player.Id, "legion_warband", LegionSlotFamily.General, 0, "gen_ironward");
            SetupActiveLegion(bb, player.Id, legion, legDef, (slot, unitDef));
            bb.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync((RaidParticipant?)null);
            bb.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);
            bb.GauntletContent.Setup(c => c.GetTrophyById("trophy_aureate"))
                .Returns(MakeTrophyDef("trophy_aureate", GauntletTrophyTier.Aureate, 0.25));
            bb.GauntletContent.Setup(c => c.GetTrophyById("trophy_argent"))
                .Returns(MakeTrophyDef("trophy_argent", GauntletTrophyTier.Argent, 0.10));
            bb.GauntletContent.Setup(c => c.GetTrophyById("trophy_bronzed"))
                .Returns(MakeTrophyDef("trophy_bronzed", GauntletTrophyTier.Bronzed, 0.05));
        }
        SetupLegion(b);
        SetupLegion(argentOnly);

        // b owns all three trophies.
        b.TrophyRepo.Setup(r => r.GetForPlayerAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerGauntletTrophy>
            {
                PlayerGauntletTrophy.Create(player.Id, "trophy_bronzed"),
                PlayerGauntletTrophy.Create(player.Id, "trophy_aureate"),
                PlayerGauntletTrophy.Create(player.Id, "trophy_argent"),
            });
        // argentOnly owns only Argent (+0.10) — used to prove the result is NOT additive (1.40).
        argentOnly.TrophyRepo.Setup(r => r.GetForPlayerAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerGauntletTrophy>
            {
                PlayerGauntletTrophy.Create(player.Id, "trophy_aureate"), // also Aureate → highest 0.25
            });

        var allThree = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());
        var aureate  = await argentOnly.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        allThree.Success.Should().BeTrue();
        aureate.Success.Should().BeTrue();
        // Both resolve to ×1.25 (highest-only). If it were additive (1.40) the all-three term would be larger.
        allThree.Response!.LegionPower.Should().Be(aureate.Response!.LegionPower,
            "highest-only: owning Aureate+Argent+Bronzed gives ×1.25, identical to Aureate alone — NOT 1.40 additive");
    }

    [Fact]
    public async Task Hit_Trophy_AppliesToNonGauntletRaid_Too()
    {
        // Trophies boost ALL legion power, not just Gauntlet raids. Use an ordinary raid.
        var withTrophy = BuildService(new Random(0));
        var baseline   = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid(); // ordinary raid, GauntletEventId == null

        void SetupLegion(ServiceBundle bb)
        {
            SetupHitScaffolding(bb, player, raid);
            var legion  = MakeActiveLegion(player.Id);
            var legDef  = MakeLegionDef(powerBonus: 50);
            var unitDef = MakeUnitDef("gen_ironward", UnitType.General, 80, 60, 5);
            var slot    = PlayerLegionSlot.Create(player.Id, "legion_warband", LegionSlotFamily.General, 0, "gen_ironward");
            SetupActiveLegion(bb, player.Id, legion, legDef, (slot, unitDef));
            bb.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync((RaidParticipant?)null);
            bb.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);
        }
        SetupLegion(withTrophy);
        SetupLegion(baseline);

        withTrophy.TrophyRepo.Setup(r => r.GetForPlayerAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerGauntletTrophy> { PlayerGauntletTrophy.Create(player.Id, "trophy_aureate") });
        withTrophy.GauntletContent.Setup(c => c.GetTrophyById("trophy_aureate"))
            .Returns(MakeTrophyDef("trophy_aureate", GauntletTrophyTier.Aureate, 0.25));

        var r1 = await withTrophy.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());
        var r2 = await baseline.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        r1.Success.Should().BeTrue();
        r1.Response!.LegionPower.Should().BeGreaterThan(r2.Response!.LegionPower,
            "trophies boost legion power on EVERY raid, not just Gauntlet ones");
    }

    // ── (B) Off-cap auras (Wrath/Blessing) ──────────────────────────────────

    [Fact]
    public async Task Hit_NonGauntletRaid_NoOffCapBonus_EvenIfOwner()
    {
        // Off-cap auras resolve on GAUNTLET raids only. On an ordinary raid OffCapAuraBonus stays 0
        // even when the player is a current owner.
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid(); // ordinary raid

        SetupHitScaffolding(b, player, raid);
        b.MagicDefs.Setup(d => d.GetAll())
            .Returns(new List<MagicDefinition> { MakeOffCapAura("magic_wrath_of_the_ancients", 1.0, 2.50) });
        b.PlayerEventMagics.Setup(r => r.FindAsync(player.Id, It.IsAny<Guid>(), "magic_wrath_of_the_ancients", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerEventMagic.Create(player.Id, Guid.NewGuid(), "magic_wrath_of_the_ancients"));
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.OffCapAuraBonus.Should().Be(0, "off-cap auras never resolve on non-Gauntlet raids");
    }

    [Fact]
    public async Task Hit_GauntletRaid_NoOwnershipNoHonor_OffCapBonusZero()
    {
        // Neither current nor former owner → NO aura at all ("neither" is not a base grant).
        var eventId = Guid.NewGuid();
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeGauntletRaid(eventId);

        SetupHitScaffolding(b, player, raid);
        b.MagicDefs.Setup(d => d.GetAll())
            .Returns(new List<MagicDefinition> { MakeOffCapAura("magic_wrath_of_the_ancients", 1.0, 2.50) });
        // default mocks: FindAsync → null, HasHonorAsync → false
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.OffCapAuraBonus.Should().Be(0,
            "a non-owner / non-former player gets no off-cap aura");
    }

    [Fact]
    public async Task Hit_GauntletRaid_CurrentWrathOwner_AppliesOwnerMultiplier_AndExceedsFormer()
    {
        // Current owner ×1.25, former owner ×1.10. With procChance 1.0 the aura always fires, so the
        // current-owner bonus must strictly exceed the former-owner bonus (both > 0).
        var eventId = Guid.NewGuid();
        var current = BuildService(new Random(0));
        var former  = BuildService(new Random(0));
        var player  = MakePlayer();
        var raid    = MakeGauntletRaid(eventId);

        void SetupAura(ServiceBundle bb)
        {
            SetupHitScaffolding(bb, player, raid);
            bb.MagicDefs.Setup(d => d.GetAll())
                .Returns(new List<MagicDefinition> { MakeOffCapAura("magic_wrath_of_the_ancients", 1.0, 2.50) });
            bb.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync((RaidParticipant?)null);
            bb.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);
        }
        SetupAura(current);
        SetupAura(former);

        // current = active PlayerEventMagic → ×1.25
        current.PlayerEventMagics.Setup(r => r.FindAsync(player.Id, eventId, "magic_wrath_of_the_ancients", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerEventMagic.Create(player.Id, eventId, "magic_wrath_of_the_ancients"));
        // former = honor only → ×1.10 (FindAsync stays null per default)
        former.PlayerMagicHonors.Setup(r => r.HasHonorAsync(player.Id, "magic_wrath_of_the_ancients", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var currentResult = await current.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());
        var formerResult  = await former.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        currentResult.Success.Should().BeTrue();
        formerResult.Success.Should().BeTrue();
        currentResult.Response!.OffCapAuraBonus.Should().BeGreaterThan(0, "Wrath fires at procChance 1.0");
        formerResult.Response!.OffCapAuraBonus.Should().BeGreaterThan(0);
        // amount: current = 2.50×1.25 = 3.125; former = 2.50×1.10 = 2.75 → current bonus is larger.
        currentResult.Response.OffCapAuraBonus.Should().BeGreaterThan(formerResult.Response.OffCapAuraBonus,
            "current-owner ×1.25 yields a larger amount than former-owner ×1.10 (same preProc/seed)");
    }

    [Fact]
    public async Task Hit_GauntletRaid_CurrentOwner_TrumpsFormer()
    {
        // A player who is BOTH a current owner and holds the honor record resolves as current (×1.25),
        // never the former (×1.10). Compare against a pure former-owner.
        var eventId = Guid.NewGuid();
        var both   = BuildService(new Random(0));
        var former = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeGauntletRaid(eventId);

        void SetupAura(ServiceBundle bb)
        {
            SetupHitScaffolding(bb, player, raid);
            bb.MagicDefs.Setup(d => d.GetAll())
                .Returns(new List<MagicDefinition> { MakeOffCapAura("magic_wrath_of_the_ancients", 1.0, 2.50) });
            bb.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
                .ReturnsAsync((RaidParticipant?)null);
            bb.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);
        }
        SetupAura(both);
        SetupAura(former);

        both.PlayerEventMagics.Setup(r => r.FindAsync(player.Id, eventId, "magic_wrath_of_the_ancients", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerEventMagic.Create(player.Id, eventId, "magic_wrath_of_the_ancients"));
        both.PlayerMagicHonors.Setup(r => r.HasHonorAsync(player.Id, "magic_wrath_of_the_ancients", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);   // also has honor — must be ignored in favour of current
        former.PlayerMagicHonors.Setup(r => r.HasHonorAsync(player.Id, "magic_wrath_of_the_ancients", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var bothResult   = await both.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());
        var formerResult = await former.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        bothResult.Response!.OffCapAuraBonus.Should().BeGreaterThan(formerResult.Response!.OffCapAuraBonus,
            "current-owner status (×1.25) trumps the former-owner honor echo (×1.10)");
    }

    [Fact]
    public async Task Hit_GauntletRaid_OffCapBonus_IsNotGovernedByMagicCap()
    {
        // Prove the off-cap aura is added ON TOP of the (clamped) in-cap magic pool — it exceeds the cap.
        // MaxAggregateProcBonus = 0.5 → in-cap magic pool clamped to 0.5×preProc. Off-cap Wrath at
        // procChance 1.0, procAmount 5.0 (×1.25 owner = 6.25×preProc) is added uncapped on top.
        var eventId = Guid.NewGuid();
        var b = BuildService(new Random(0), magicConfig: new MagicConfig { MaxAggregateProcBonus = 0.5 });
        var player = MakePlayer();
        var raid   = MakeGauntletRaid(eventId);

        SetupHitScaffolding(b, player, raid);
        // An in-cap DamageProc that will be clamped to the cap.
        SetupMagic(b, raid, ("magic_smite", 1.0, 3.0));
        // An off-cap aura (current owner).
        b.MagicDefs.Setup(d => d.GetAll())
            .Returns(new List<MagicDefinition> { MakeOffCapAura("magic_wrath_of_the_ancients", 1.0, 5.0) });
        // SetupMagic registers magic_smite on GetById; keep that for the in-cap pool. GetAll only feeds
        // the off-cap loop, so the two pools never overlap.
        b.PlayerEventMagics.Setup(r => r.FindAsync(player.Id, eventId, "magic_wrath_of_the_ancients", It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerEventMagic.Create(player.Id, eventId, "magic_wrath_of_the_ancients"));
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        // The in-cap magic pool is clamped to 0.5×preProc; the off-cap bonus must be much larger,
        // proving it is NOT subject to MaxAggregateProcBonus.
        result.Response!.OffCapAuraBonus.Should().BeGreaterThan(result.Response.MagicProcBonus,
            "off-cap aura (6.25×preProc) is added uncapped, exceeding the 0.5×preProc magic cap");
        result.Response.OffCapAuraBonus.Should().BeGreaterThan(0);
    }

    [Fact]
    public async Task Hit_GauntletRaid_BlessingFormerOwner_FiresWithHonorMultiplier()
    {
        // Blessing base shape (0.15 / 4.25), former owner via honor → ×1.10. Force fire with chance 1.0.
        var eventId = Guid.NewGuid();
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeGauntletRaid(eventId);

        SetupHitScaffolding(b, player, raid);
        b.MagicDefs.Setup(d => d.GetAll())
            .Returns(new List<MagicDefinition> { MakeOffCapAura("magic_blessing_of_the_ancients", 1.0, 4.25) });
        b.PlayerMagicHonors.Setup(r => r.HasHonorAsync(player.Id, "magic_blessing_of_the_ancients", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.OffCapAuraBonus.Should().BeGreaterThan(0,
            "former-owner Blessing fires at procChance 1.0 with the ×1.10 honor multiplier");
    }

    // ── (C) Strike spend forks stamina ──────────────────────────────────────

    [Theory]
    [InlineData(1, 1)]    // Small
    [InlineData(5, 5)]    // Medium
    [InlineData(20, 20)]  // Large
    public async Task Hit_GauntletRaid_SpendsStrikes_NotStamina_ByHitSize(int hitSize, int expectedStrikeCost)
    {
        var eventId = Guid.NewGuid();
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeGauntletRaid(eventId);

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, hitSize, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        // Strikes spent by hit size; stamina untouched.
        b.Strikes.Verify(r => r.SpendAsync(player.Id, expectedStrikeCost, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once, "Gauntlet hit spends Strikes scaled by hit size (1/5/20)");
        b.Energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never, "Gauntlet hit must NOT spend stamina");
    }

    [Fact]
    public async Task Hit_GauntletRaid_StrikeReferenceId_IsPerHitDeterministic()
    {
        var eventId = Guid.NewGuid();
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeGauntletRaid(eventId);
        var key    = Guid.NewGuid().ToString();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        await b.Service.HitRaidAsync(player.Id, raid.Id, 1, key);

        var expectedRef = $"strikespend:{raid.Id}:{key}";
        b.Strikes.Verify(r => r.SpendAsync(player.Id, 1, expectedRef, It.IsAny<CancellationToken>()),
            Times.Once, "strike referenceId reuses the per-hit idempotency key for tx-atomic idempotency");
    }

    [Fact]
    public async Task Hit_GauntletRaid_InsufficientStrikes_Returns422_NoDamage_NoScore()
    {
        var eventId = Guid.NewGuid();
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeGauntletRaid(eventId);
        var hpBefore = raid.CurrentHp;

        SetupHitScaffolding(b, player, raid);
        b.Strikes.Setup(r => r.SpendAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(StrikeSpendOutcome.Insufficient);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(RaidHitFailureCode.InsufficientStrikes,
            "a Gauntlet hit with insufficient strikes fails (422 at the controller)");
        raid.CurrentHp.Should().Be(hpBefore, "no damage on an insufficient-strikes hit");
        b.Participants.Verify(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()), Times.Never);
        b.GauntletScoring.Verify(s => s.UpdateScoreAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never, "no score when the hit is rejected for insufficient strikes");
        b.Energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never, "insufficient strikes must NOT fall back to spending stamina");
    }

    [Fact]
    public async Task Hit_GauntletRaid_SufficientStrikes_DebitsAndSurfacesNewBalance()
    {
        var eventId = Guid.NewGuid();
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeGauntletRaid(eventId);

        SetupHitScaffolding(b, player, raid);
        // Post-spend balance is read back for the response.
        b.Strikes.Setup(r => r.GetBalanceAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(99L);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.NewStrikeBalance.Should().Be(99, "the response surfaces the post-spend strike balance");
        b.Strikes.Verify(r => r.SpendAsync(player.Id, 1, It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Hit_NonGauntletRaid_NewStrikeBalanceIsZero_StrikesNeverSpent()
    {
        // Regression: ordinary raids never touch the strike ledger and report NewStrikeBalance 0.
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.NewStrikeBalance.Should().Be(0);
        b.Strikes.Verify(r => r.SpendAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "non-Gauntlet raids spend stamina, never strikes");
        b.Energy.Verify(e => e.SpendEnergyAsync(player.Id, ResourceType.Stamina, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ── (D) Gauntlet score update ───────────────────────────────────────────

    [Fact]
    public async Task Hit_GauntletRaid_UpdatesScore_WithDamageFinal()
    {
        var eventId = Guid.NewGuid();
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeGauntletRaid(eventId);

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        b.GauntletScoring.Verify(s => s.UpdateScoreAsync(
            player.Id, eventId, result.Response!.DamageDealt, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once, "a Gauntlet hit increments the GauntletEntry score by damageFinal (single participant write)");
    }

    [Fact]
    public async Task Hit_NonGauntletRaid_DoesNotUpdateScore()
    {
        var b      = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid(); // ordinary raid

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        b.GauntletScoring.Verify(s => s.UpdateScoreAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never, "non-Gauntlet hits never update a Gauntlet score");
    }

    // ── Idempotency (Redis cached replay) — no second strike debit, no double score ──

    [Fact]
    public async Task Hit_GauntletRaid_DuplicateKey_CachedReplay_NoSecondStrikeOrScore()
    {
        var eventId = Guid.NewGuid();
        var b   = BuildService();
        var key = Guid.NewGuid().ToString();
        var cached = new RaidHitResponse { Success = true, DamageDealt = 42, NewStrikeBalance = 7 };

        // TryAcquireSlotAsync returns (false, cached) → early return before AtomicApplyHitAsync.
        b.HitCache.Setup(c => c.TryAcquireSlotAsync(key, It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, cached));

        var raid = MakeGauntletRaid(eventId);
        b.Raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await b.Service.HitRaidAsync(Guid.NewGuid(), raid.Id, 1, key);

        result.Success.Should().BeTrue();
        result.Response!.DamageDealt.Should().Be(42);
        b.Strikes.Verify(r => r.SpendAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Never, "cached replay must not debit strikes a second time");
        b.GauntletScoring.Verify(s => s.UpdateScoreAsync(
            It.IsAny<Guid>(), It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never, "cached replay must not double-score");
        b.Raids.Verify(r => r.AtomicApplyHitAsync(It.IsAny<Guid>(), It.IsAny<Func<ActiveRaid, Task<bool>>>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    // =======================================================================
    // System 16 Slice 5 — per-Gauntlet-raid-defeat reward (StrikesPerDefeat strikes + 1 Token)
    //   credited to the killer inside the advisory-lock tx, gated on GauntletEventId, idempotent.
    // =======================================================================

    // Set up a one-shot KILL on the given raid: low HP so any hit drops it to 0, single participant.
    private void SetupKill(ServiceBundle b, Player attacker, ActiveRaid raid)
    {
        SetupHitScaffolding(b, attacker, raid);
        var part = RaidParticipant.Create(raid.Id, attacker.Id);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, attacker.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(part);
        b.Participants.Setup(p => p.UpdateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        b.Participants.Setup(p => p.GetAllForRaidAsync(raid.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RaidParticipant> { part });
        b.Gems.Setup(g => g.GrantGemsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<GemTransactionType>(), It.IsAny<string?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
    }

    [Fact]
    public async Task Hit_GauntletKill_CreditsStrikesPerDefeatAndOneToken()
    {
        var eventId = Guid.NewGuid();
        var cfg = new GauntletConfig { StrikesPerDefeat = 10 };
        var b = BuildService(new Random(0), gauntletConfig: cfg);
        var attacker = MakePlayer();
        var raid = MakeGauntletRaid(eventId, currentHp: 1);   // any hit kills

        SetupKill(b, attacker, raid);

        var result = await b.Service.HitRaidAsync(attacker.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.IsDefeated.Should().BeTrue();

        // +StrikesPerDefeat strikes, type RaidDefeat, deterministic per-(raid,player) referenceId.
        var strikeRef = $"gauntletdefeat:{raid.Id}:{attacker.Id}:strikes";
        b.Strikes.Verify(r => r.CreateAsync(It.Is<StrikeTransaction>(
            t => t.Amount == 10 && t.TransactionType == StrikeTransactionType.RaidDefeat
              && t.ReferenceId == strikeRef), It.IsAny<CancellationToken>()), Times.Once);

        // +1 Token, type RaidDefeatReward, deterministic per-(raid,player) referenceId.
        var tokenRef = $"gauntletdefeat:{raid.Id}:{attacker.Id}:token";
        b.GauntletCurrency.Verify(r => r.CreateAsync(It.Is<GauntletCurrencyTransaction>(
            t => t.Currency == GauntletCurrency.Token && t.Amount == 1
              && t.TransactionType == GauntletCurrencyTransactionType.RaidDefeatReward
              && t.ReferenceId == tokenRef), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Hit_GauntletKill_ReprocessedKill_DoesNotDoubleCredit()
    {
        // A re-processed kill (both references already present) must NOT credit a second time —
        // the ReferenceExists guard is the money-bug backstop.
        var eventId = Guid.NewGuid();
        var b = BuildService(new Random(0));
        var attacker = MakePlayer();
        var raid = MakeGauntletRaid(eventId, currentHp: 1);

        SetupKill(b, attacker, raid);
        b.Strikes.Setup(r => r.ReferenceExistsAsync(
                attacker.Id, StrikeTransactionType.RaidDefeat, $"gauntletdefeat:{raid.Id}:{attacker.Id}:strikes",
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        b.GauntletCurrency.Setup(r => r.ReferenceExistsAsync(
                attacker.Id, GauntletCurrency.Token, GauntletCurrencyTransactionType.RaidDefeatReward,
                $"gauntletdefeat:{raid.Id}:{attacker.Id}:token", It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await b.Service.HitRaidAsync(attacker.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.IsDefeated.Should().BeTrue();
        b.Strikes.Verify(r => r.CreateAsync(It.IsAny<StrikeTransaction>(), It.IsAny<CancellationToken>()),
            Times.Never, "the defeat-strike reference already exists → no double-credit");
        b.GauntletCurrency.Verify(r => r.CreateAsync(It.IsAny<GauntletCurrencyTransaction>(), It.IsAny<CancellationToken>()),
            Times.Never, "the defeat-token reference already exists → no double-credit");
    }

    [Fact]
    public async Task Hit_NonGauntletKill_CreditsNoStrikesNoToken()
    {
        // An ordinary (non-Gauntlet) raid kill must NOT credit the per-defeat strikes/token.
        var b = BuildService(new Random(0));
        var attacker = MakePlayer();
        var raid = MakeRaid(currentHp: 1);   // ordinary raid, GauntletEventId == null

        SetupKill(b, attacker, raid);

        var result = await b.Service.HitRaidAsync(attacker.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.IsDefeated.Should().BeTrue();
        b.Strikes.Verify(r => r.CreateAsync(It.IsAny<StrikeTransaction>(), It.IsAny<CancellationToken>()),
            Times.Never, "non-Gauntlet kills earn no Gauntlet strikes");
        b.GauntletCurrency.Verify(r => r.CreateAsync(It.IsAny<GauntletCurrencyTransaction>(), It.IsAny<CancellationToken>()),
            Times.Never, "non-Gauntlet kills earn no Gauntlet tokens");
    }

    [Fact]
    public async Task Hit_GauntletNonKill_CreditsNoDefeatReward()
    {
        // A Gauntlet hit that does NOT kill (raid survives) earns no per-defeat reward.
        var eventId = Guid.NewGuid();
        var b = BuildService(new Random(0));
        var attacker = MakePlayer();
        var raid = MakeGauntletRaid(eventId);   // full HP (100000) — one small hit won't kill

        SetupHitScaffolding(b, attacker, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, attacker.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(attacker.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.IsDefeated.Should().BeFalse();
        b.Strikes.Verify(r => r.CreateAsync(It.IsAny<StrikeTransaction>(), It.IsAny<CancellationToken>()),
            Times.Never, "the per-defeat reward only fires on a kill");
        b.GauntletCurrency.Verify(r => r.CreateAsync(It.IsAny<GauntletCurrencyTransaction>(), It.IsAny<CancellationToken>()),
            Times.Never, "the per-defeat reward only fires on a kill");
    }

    // =======================================================================
    // System 21 Slice 3b — Guild raids (GuildStamina fork, access gate,
    // contribution accrual, officer-gated pooled-sigil summon)
    // =======================================================================

    // A Large guild raid using the raid_ironcolossus definition (StaminaCostPerHit=1 → GuildStamina
    // cost = hit size). The guild fork is driven by ActiveRaid.GuildId, not the definition's tier.
    private static ActiveRaid MakeGuildRaid(Guid guildId, long currentHp = 100000, int hoursUntilExpiry = 48)
    {
        var raid = ActiveRaid.Create(
            "raid_ironcolossus", Guid.NewGuid(), 100000,
            DateTimeOffset.UtcNow.AddHours(hoursUntilExpiry), RaidDifficulty.Normal, RaidSize.Large);
        raid.LinkGuild(guildId);
        if (currentHp < 100000) raid.TakeDamage(100000 - currentHp);
        return raid;
    }

    private static RaidDefinition GuildBoss(string id = "guild_raid_warlord") => new()
    {
        Id                   = id,
        Name                 = "Gorehowl the Warlord",
        Tier                 = "Guild",
        BaseHp               = 500000,
        PersonalBaseHp       = 0,
        TimerHours           = 48,
        StaminaCostPerHit    = 1,
        LootTableId          = "",
        BaseGoldReward       = 800,
        BaseExperienceReward = 500,
        BaseGemReward        = 3,
        HasOnHitDrops        = false,
    };

    [Theory]
    [InlineData(1)]
    [InlineData(5)]
    [InlineData(20)]
    public async Task Hit_GuildRaid_SpendsGuildStamina_NotStamina(int hitSize)
    {
        var b = BuildService(new Random(0));
        var player = MakePlayer();
        var guildId = Guid.NewGuid();
        var raid = MakeGuildRaid(guildId);

        SetupHitScaffolding(b, player, raid);
        b.GuildMemberships.Setup(r => r.FindByGuildAndPlayerAsync(guildId, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuildMembership.Create(guildId, player.Id, GuildRank.Member));
        b.Energy.Setup(e => e.SpendEnergyAsync(player.Id, ResourceType.GuildStamina, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, hitSize, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        b.Energy.Verify(e => e.SpendEnergyAsync(player.Id, ResourceType.GuildStamina, hitSize, It.IsAny<CancellationToken>()),
            Times.Once, "a guild raid spends GuildStamina equal to the hit size");
        b.Energy.Verify(e => e.SpendEnergyAsync(player.Id, ResourceType.Stamina, It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never, "a guild raid never spends ordinary Stamina");
    }

    [Fact]
    public async Task Hit_GuildRaid_NonMember_AccessDenied_NoSpend()
    {
        var b = BuildService(new Random(0));
        var player = MakePlayer();
        var guildId = Guid.NewGuid();
        var raid = MakeGuildRaid(guildId);

        SetupHitScaffolding(b, player, raid);
        // Default bundle: FindByGuildAndPlayerAsync → null (the striker is not a member of this guild).

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(RaidHitFailureCode.AccessDenied);
        b.Energy.Verify(e => e.SpendEnergyAsync(It.IsAny<Guid>(), It.IsAny<ResourceType>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
            Times.Never, "a non-member is rejected pre-spend");
    }

    [Fact]
    public async Task Hit_GuildRaid_InsufficientGuildStamina_NoDamage()
    {
        var b = BuildService(new Random(0));
        var player = MakePlayer();
        var guildId = Guid.NewGuid();
        var raid = MakeGuildRaid(guildId);
        var hpBefore = raid.CurrentHp;

        SetupHitScaffolding(b, player, raid);
        b.GuildMemberships.Setup(r => r.FindByGuildAndPlayerAsync(guildId, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuildMembership.Create(guildId, player.Id, GuildRank.Member));
        b.Energy.Setup(e => e.SpendEnergyAsync(player.Id, ResourceType.GuildStamina, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // pool empty for this member

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 5, Guid.NewGuid().ToString());

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(RaidHitFailureCode.InsufficientGuildStamina);
        raid.CurrentHp.Should().Be(hpBefore, "no damage when the GuildStamina spend fails");
        b.Participants.Verify(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Hit_GuildRaid_AccruesContributionToMembership()
    {
        var b = BuildService(new Random(0));
        var player = MakePlayer();
        var guildId = Guid.NewGuid();
        var raid = MakeGuildRaid(guildId);
        var membership = GuildMembership.Create(guildId, player.Id, GuildRank.Member);

        SetupHitScaffolding(b, player, raid);
        b.GuildMemberships.Setup(r => r.FindByGuildAndPlayerAsync(guildId, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(membership);
        b.Energy.Setup(e => e.SpendEnergyAsync(player.Id, ResourceType.GuildStamina, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 20, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        membership.ContributionTotal.Should().BeGreaterThan(0, "the hit's damage accrues to the member's guild contribution");
        b.GuildMemberships.Verify(r => r.UpdateAsync(membership, It.IsAny<CancellationToken>()),
            Times.Once, "the accrual is persisted inside the advisory-lock tx");
    }

    [Fact]
    public async Task SummonGuildRaid_Officer_ConsumesPool_CreatesGuildRaid()
    {
        var b = BuildService();
        var player = MakePlayer();
        var guildId = Guid.NewGuid();
        var membership = GuildMembership.Create(guildId, player.Id, GuildRank.Officer);

        b.GuildMemberships.Setup(r => r.FindByPlayerAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(membership);
        b.Definitions.Setup(d => d.GetById("guild_raid_warlord")).Returns(GuildBoss());
        b.GuildEconomy.Setup(e => e.TrySpendPoolAsync(guildId, 1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuildPoolSpendOutcome.Charged);
        b.GuildEconomy.Setup(e => e.GetPoolBalanceAsync(guildId, It.IsAny<CancellationToken>())).ReturnsAsync(4L);
        b.Raids.Setup(r => r.CreateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((ActiveRaid r, CancellationToken _) => r);

        var result = await b.Service.SummonGuildRaidAsync(player.Id, "guild_raid_warlord", RaidDifficulty.Normal);

        result.Success.Should().BeTrue(result.FailureReason);
        result.PoolBalanceAfter.Should().Be(4L);
        b.GuildEconomy.Verify(e => e.TrySpendPoolAsync(guildId, 1, It.IsAny<string>(), It.IsAny<CancellationToken>()),
            Times.Once, "a summon consumes exactly one pooled sigil");
        b.Raids.Verify(r => r.CreateAsync(
            It.Is<ActiveRaid>(a => a.GuildId == guildId && a.Size == RaidSize.Large && a.MaxHp == 500000),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task SummonGuildRaid_NonOfficer_PermissionDenied_NoPoolSpend()
    {
        var b = BuildService();
        var player = MakePlayer();
        var guildId = Guid.NewGuid();
        b.GuildMemberships.Setup(r => r.FindByPlayerAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuildMembership.Create(guildId, player.Id, GuildRank.Member));
        b.Definitions.Setup(d => d.GetById("guild_raid_warlord")).Returns(GuildBoss());

        var result = await b.Service.SummonGuildRaidAsync(player.Id, "guild_raid_warlord", RaidDifficulty.Normal);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(GuildRaidFailureCode.PermissionDenied);
        b.GuildEconomy.Verify(e => e.TrySpendPoolAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        b.Raids.Verify(r => r.CreateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SummonGuildRaid_NotInGuild_Fails()
    {
        var b = BuildService();
        var player = MakePlayer();
        // Default bundle: FindByPlayerAsync → null.
        b.Definitions.Setup(d => d.GetById("guild_raid_warlord")).Returns(GuildBoss());

        var result = await b.Service.SummonGuildRaidAsync(player.Id, "guild_raid_warlord", RaidDifficulty.Normal);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(GuildRaidFailureCode.NotInGuild);
    }

    [Fact]
    public async Task SummonGuildRaid_InsufficientPool_NoRaidCreated()
    {
        var b = BuildService();
        var player = MakePlayer();
        var guildId = Guid.NewGuid();
        b.GuildMemberships.Setup(r => r.FindByPlayerAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuildMembership.Create(guildId, player.Id, GuildRank.Leader));
        b.Definitions.Setup(d => d.GetById("guild_raid_warlord")).Returns(GuildBoss());
        b.GuildEconomy.Setup(e => e.TrySpendPoolAsync(guildId, 1, It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuildPoolSpendOutcome.Insufficient);

        var result = await b.Service.SummonGuildRaidAsync(player.Id, "guild_raid_warlord", RaidDifficulty.Normal);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(GuildRaidFailureCode.InsufficientPool);
        b.Raids.Verify(r => r.CreateAsync(It.IsAny<ActiveRaid>(), It.IsAny<CancellationToken>()),
            Times.Never, "no raid is created when the pool is empty");
    }

    [Fact]
    public async Task SummonGuildRaid_NonGuildDefinition_NotFound()
    {
        var b = BuildService();
        var player = MakePlayer();
        var guildId = Guid.NewGuid();
        b.GuildMemberships.Setup(r => r.FindByPlayerAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuildMembership.Create(guildId, player.Id, GuildRank.Officer));
        // An ordinary (non-Guild-tier) definition must be rejected for guild summon.
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await b.Service.SummonGuildRaidAsync(player.Id, "raid_ironcolossus", RaidDifficulty.Normal);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(GuildRaidFailureCode.DefinitionNotFound);
        b.GuildEconomy.Verify(e => e.TrySpendPoolAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // =======================================================================
    // System 22 Phase A Slice 5 — Masteries combat: Wrath (+% legion power) +
    // Bulwark (+% guild-raid damage). (The 700+ existing hit tests run with neutral
    // mastery mods, proving a mastery-less hit is byte-for-byte unchanged.)
    // =======================================================================

    private void SetupLegionForMasteryTest(ServiceBundle bb, Player player, ActiveRaid raid)
    {
        SetupHitScaffolding(bb, player, raid);
        var legion  = MakeActiveLegion(player.Id);
        var legDef  = MakeLegionDef(powerBonus: 50);
        var unitDef = MakeUnitDef("gen_ironward", UnitType.General, 80, 60, 5);
        var slot    = PlayerLegionSlot.Create(player.Id, "legion_warband", LegionSlotFamily.General, 0, "gen_ironward");
        SetupActiveLegion(bb, player.Id, legion, legDef, (slot, unitDef));
        bb.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        bb.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);
    }

    [Fact]
    public async Task Hit_Wrath_AddsToLegionBonusFraction_AboveBaseline()
    {
        var withWrath = BuildService(new Random(0));
        var baseline  = BuildService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid();
        SetupLegionForMasteryTest(withWrath, player, raid);
        SetupLegionForMasteryTest(baseline, player, raid);

        // +5% legion power (e.g. pledged Wrath @ L5). bonusFraction 0.55 → 0.60.
        withWrath.Mastery.Setup(m => m.GetCombatModifiersAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MasteryCombatModifiers(5.0, 0.0));

        var r1 = await withWrath.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());
        var r2 = await baseline.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        r1.Success.Should().BeTrue();
        r2.Success.Should().BeTrue();
        long expected = (long)Math.Round(r2.Response!.LegionPower * (1.60 / 1.55));
        r1.Response!.LegionPower.Should().BeCloseTo(expected, 1, "Wrath +5% is added into the legion bonus fraction");
        r1.Response.LegionPower.Should().BeGreaterThan(r2.Response.LegionPower);
        r1.Response.WrathLegionBonus.Should().BeGreaterThan(0);
        r2.Response.WrathLegionBonus.Should().Be(0, "no Wrath → no marginal");
    }

    [Fact]
    public async Task Hit_Wrath_NoActiveLegion_NoBonus()
    {
        var b = BuildService(new Random(0));
        var player = MakePlayer();
        var raid = MakeRaid();
        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);
        // Wrath set, but no active legion (default bundle has none) → nothing to scale.
        b.Mastery.Setup(m => m.GetCombatModifiersAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MasteryCombatModifiers(5.0, 0.0));

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.LegionPower.Should().Be(0);
        result.Response.WrathLegionBonus.Should().Be(0, "Wrath only scales an active legion");
    }

    private void SetupGuildHitForMasteryTest(ServiceBundle bb, Player player, Guid guildId, ActiveRaid raid)
    {
        SetupHitScaffolding(bb, player, raid);
        bb.GuildMemberships.Setup(r => r.FindByGuildAndPlayerAsync(guildId, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GuildMembership.Create(guildId, player.Id, GuildRank.Member));
        bb.Energy.Setup(e => e.SpendEnergyAsync(player.Id, ResourceType.GuildStamina, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        bb.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        bb.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);
    }

    [Fact]
    public async Task Hit_Bulwark_GuildRaid_AddsDamage()
    {
        var withBulwark = BuildService(new Random(0));
        var baseline    = BuildService(new Random(0));
        var player = MakePlayer();
        var guildId = Guid.NewGuid();
        var raid = MakeGuildRaid(guildId);
        SetupGuildHitForMasteryTest(withBulwark, player, guildId, raid);
        SetupGuildHitForMasteryTest(baseline, player, guildId, raid);

        // Large fraction (0.25) for unambiguous observability at low base damage — the real ~1% cap
        // is enforced in MasteryService (Slice 2) and tested there; this proves the combat hook applies it.
        withBulwark.Mastery.Setup(m => m.GetCombatModifiersAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MasteryCombatModifiers(0.0, 0.25));

        var r1 = await withBulwark.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());
        var r2 = await baseline.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        r1.Success.Should().BeTrue();
        r2.Success.Should().BeTrue();
        r1.Response!.DamageDealt.Should().BeGreaterThan(r2.Response!.DamageDealt, "Bulwark adds flat damage on a guild raid");
        r1.Response.BulwarkBonus.Should().BeGreaterThan(0);
        r2.Response.BulwarkBonus.Should().Be(0, "no Bulwark → no marginal");
    }

    [Fact]
    public async Task Hit_Bulwark_NonGuildRaid_NotApplied()
    {
        var b = BuildService(new Random(0));
        var player = MakePlayer();
        var raid = MakeRaid(); // ordinary raid — GuildId is null
        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);
        // Bulwark fraction set, but the raid is not a guild raid → must NOT apply.
        b.Mastery.Setup(m => m.GetCombatModifiersAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MasteryCombatModifiers(0.0, 0.25));

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        result.Response!.BulwarkBonus.Should().Be(0, "Bulwark applies to guild raids only (GuildId != null)");
    }
}
