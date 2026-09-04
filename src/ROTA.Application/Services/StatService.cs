using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

public sealed class StatService : IStatService
{
    private const double LsiCap = 7.45;   // tuned down 2026-06-23 (was 9.0 canonical / 8.0 original) — owner pacing call "for now"

    private readonly IPlayerRepository _players;
    private readonly IEnergyService _energy;
    private readonly IGemService _gems;
    private readonly IAuditLogRepository _auditLog;
    private readonly IOptions<LevelingConfig> _levelingConfig;
    private readonly IOptions<CombatConfig> _combatConfig;
    private readonly IClassService _classService;
    private readonly IEquipmentService _equipment;
    private readonly IPinnacleService _pinnacle;
    private readonly IPlayerMutationLock _mutationLock;   // exploit audit 2026-06-14 (D)

    public StatService(
        IPlayerRepository players,
        IEnergyService energy,
        IGemService gems,
        IAuditLogRepository auditLog,
        IOptions<LevelingConfig> levelingConfig,
        IOptions<CombatConfig> combatConfig,
        IClassService classService,
        IEquipmentService equipment,
        IPinnacleService pinnacle,
        IPlayerMutationLock mutationLock)
    {
        _players        = players;
        _energy         = energy;
        _gems           = gems;
        _auditLog       = auditLog;
        _levelingConfig = levelingConfig;
        _combatConfig   = combatConfig;
        _classService   = classService;
        _equipment      = equipment;
        _pinnacle       = pinnacle;
        _mutationLock   = mutationLock;
    }

    // SECURITY (exploit audit 2026-06-14, finding D): serialize per-player so concurrent allocations can't
    // each pass the SkillPoints check and last-writer-wins extra stats (player_stats has no concurrency token).
    public Task<AllocateStatResponse> AllocateStatPointAsync(
        Guid playerId, StatType statType, int amount, CancellationToken ct = default)
        => _mutationLock.RunAsync(playerId, () => AllocateStatPointCoreAsync(playerId, statType, amount, ct), ct);

    private async Task<AllocateStatResponse> AllocateStatPointCoreAsync(
        Guid playerId, StatType statType, int amount, CancellationToken ct = default)
    {
        var player = await _players.FindByIdWithStatsAsync(playerId, ct);
        if (player?.Stats is null)
            return Fail("Player or stats not found.");

        var stats = player.Stats;

        // The PRICE is per stat point and is not always 1. Stamina costs 2, because it counts double
        // toward the LSI cap below; charging 1 for it let a stamina build reach the same ceiling for
        // half the skill points and bank the rest in Attack/Defense/Discernment.
        int costPerPoint = _levelingConfig.Value.SkillPointCost(statType.ToString());
        long totalCost   = (long)amount * costPerPoint;

        if (stats.SkillPoints < totalCost)
            return Fail(costPerPoint == 1
                ? $"Insufficient SkillPoints. Have {stats.SkillPoints}, need {totalCost}."
                : $"Insufficient SkillPoints. Have {stats.SkillPoints}, need {totalCost} " +
                  $"({amount} x {costPerPoint} per {statType} point).");

        // LSI cap check — only for Energy and Stamina investment
        if (statType is StatType.Energy or StatType.Stamina)
        {
            long projectedEnergy  = stats.EnergyInvestment  + (statType == StatType.Energy  ? amount : 0);
            long projectedStamina = stats.StaminaInvestment + (statType == StatType.Stamina ? amount : 0);
            double newLsi = player.Level > 0
                ? (projectedEnergy + projectedStamina * 2.0) / player.Level
                : 0;

            if (newLsi > LsiCap)
                return Fail(DescribeLsiRefusal(statType, player.Level,
                                               stats.EnergyInvestment, stats.StaminaInvestment));
        }

        int charge = (int)totalCost;   // bounded: amount is validator-capped at 100,000,000 and cost is small
        switch (statType)
        {
            case StatType.Energy:      stats.AllocateToEnergy(amount, charge);      break;
            case StatType.Stamina:     stats.AllocateToStamina(amount, charge);     break;
            case StatType.Discernment: stats.AllocateToDiscernment(amount, charge); break;
            case StatType.Attack:      stats.AllocateToAttack(amount, charge);      break;
            case StatType.Defense:     stats.AllocateToDefense(amount, charge);     break;
            case StatType.Health:      stats.AllocateToHealth(amount, charge);      break;
            default: return Fail($"Unknown stat type: {statType}");
        }

        await _players.UpdateStatsAsync(stats, ct);

        // Update resource max values when energy or stamina investment changes.
        // T30 — raising the cap by N also credits +N to the *current* pool (the gained delta, capped
        // at the new max by RefillEnergyAsync) so the spend has an immediate effect. This is NOT a
        // full refill — contrast GrantLevelUpPointsAsync, which calls RefillToMaxAsync.
        // int32-overflow-audit Unit 2: ComputeMax* are now long (uncapped investment), but the resource
        // pool max (PlayerResource) is deliberately int and out of Unit-2 scope; the LSI cap bounds the
        // investable energy/stamina well within int32, so the (int) narrowing here is safe.
        if (statType == StatType.Energy)
        {
            await _energy.UpdateMaxAsync(playerId, ResourceType.Energy, (int)stats.ComputeMaxEnergy(), ct);
            await _energy.RefillEnergyAsync(playerId, ResourceType.Energy, amount, ct);
        }

        if (statType == StatType.Stamina)
        {
            await _energy.UpdateMaxAsync(playerId, ResourceType.Stamina, (int)stats.ComputeMaxStamina(), ct);
            await _energy.RefillEnergyAsync(playerId, ResourceType.Stamina, amount, ct);
        }

        // T56 — Health investment raises BaseMaxHealth, so grow the Health pool max to match and credit
        // the gained delta to the current pool (same immediate-effect rule as Energy/Stamina above).
        if (statType == StatType.Health)
        {
            await _energy.UpdateMaxAsync(playerId, ResourceType.Health, stats.BaseMaxHealth, ct);
            await _energy.RefillEnergyAsync(playerId, ResourceType.Health, amount, ct);
        }

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "AllocateStat", null,
            $"Allocated {amount} points to {statType}. SkillPoints remaining: {stats.SkillPoints}",
            null), ct);

        // atk-def-chip-stale-on-alloc — return the gear-inclusive effective ATK/DEF so the client can patch
        // its stat chips inline; without this the chips only refresh on a post-allocate profile round-trip,
        // which goes stale if that fetch fails.
        var combat = await _equipment.GetEffectiveCombatDataAsync(
            playerId, stats.BaseAttack, stats.BaseDefense, ct);

        return new AllocateStatResponse
        {
            Success                  = true,
            StatType                 = statType.ToString(),
            AmountAllocated          = amount,
            NewSkillPointsRemaining  = stats.SkillPoints,
            NewEnergyInvestment      = stats.EnergyInvestment,
            NewStaminaInvestment     = stats.StaminaInvestment,
            NewDiscernmentInvestment = stats.DiscernmentInvestment,
            NewMaxEnergy             = stats.ComputeMaxEnergy(),
            NewMaxStamina            = stats.ComputeMaxStamina(),
            NewMaxGuildStamina       = player.Level,
            CurrentLsi               = (decimal)stats.ComputeLSI(player.Level),
            EffectiveAttack          = combat.EffectiveAttack,
            EffectiveDefense         = combat.EffectiveDefense,
        };
    }

    public async Task GrantLevelUpPointsAsync(Guid playerId, int newLevel, CancellationToken ct = default)
    {
        var player = await _players.FindByIdWithStatsAsync(playerId, ct);
        if (player?.Stats is null) return;

        // T22 — restore health to full on level-up (forward-compatible; health is PHASE-2 in combat).
        player.Stats.RestoreFullHealth();
        await _players.UpdateStatsAsync(player.Stats, ct);

        // The +10 is an ATOMIC increment, not a read-modify-write. This runs for every participant of a
        // raid kill while only the RAID's advisory lock is held, so the same player may be levelling from
        // a quest in another request at the same instant; an entity write would silently drop one of the
        // two grants. Done after the health save on purpose — the save above must not carry a stale
        // skill_points value over the top of it.
        await _players.IncrementSkillPointsAsync(playerId, 10, ct);

        // T24 — GuildStamina scales 1:1 with level. Sync the stored pool max to the new level before
        // the refill below (Energy/Stamina max depend on investment, not level, so they need no resync).
        await _energy.UpdateMaxAsync(playerId, ResourceType.GuildStamina, newLevel, ct);

        // T22 — fully refill all resource pools on level-up.
        await _energy.RefillToMaxAsync(playerId, ResourceType.Energy, ct);
        await _energy.RefillToMaxAsync(playerId, ResourceType.Stamina, ct);
        await _energy.RefillToMaxAsync(playerId, ResourceType.GuildStamina, ct);

        // T56 — sync the Health pool max to BaseMaxHealth (grows via stat allocation) and refill it on
        // level-up too (owner decision: level-up still tops up health, alongside its passive regen).
        // RestoreFullHealth() above keeps the vestigial PlayerStats.CurrentHealth consistent.
        await _energy.UpdateMaxAsync(playerId, ResourceType.Health, player.Stats.BaseMaxHealth, ct);
        await _energy.RefillToMaxAsync(playerId, ResourceType.Health, ct);

        if (newLevel % 5 == 0)
        {
            var referenceId = $"levelup:gems:{playerId}:{newLevel}";
            await _gems.GrantGemsAsync(
                playerId, 5, GemTransactionType.LevelUpReward, referenceId, ct);
        }

        // T32 — pinnacle / milestone gem reward (data-driven via LevelingConfig.PinnacleGemRewards).
        // Idempotent by referenceId; separate from the every-5 LevelUpReward above.
        var pinnacleGems = _levelingConfig.Value.GetPinnacleGems(newLevel);
        if (pinnacleGems > 0)
        {
            await _gems.GrantGemsAsync(
                playerId, pinnacleGems, GemTransactionType.PinnacleReward,
                $"pinnacle:gems:{playerId}:{newLevel}", ct);
        }

        // T33 — first-claimant logging + operator email (idempotent; only the first player at a given
        // pinnacle level triggers it). Same "pinnacle level" set as the gem reward above.
        if (_levelingConfig.Value.IsPinnacleLevel(newLevel))
            await _pinnacle.RecordFirstClaimAsync(playerId, newLevel, ct);

        // Auto-advance class on milestone levels (500, 1000, 2000, 5000, etc.)
        var advanced    = _classService.ComputeAutoAdvance(newLevel, player.Class);
        var classChanged = advanced != player.Class;
        if (classChanged)
        {
            player.SetClass(advanced);
            await _players.UpdateAsync(player, ct);
        }

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "LevelUpReward", null,
            $"Level {newLevel} reward: +10 SkillPoints{(newLevel % 5 == 0 ? ", +5 Gems" : "")}{(pinnacleGems > 0 ? $", +{pinnacleGems} Pinnacle Gems" : "")}{(classChanged ? $", class → {advanced}" : "")}",
            null), ct);
    }

    public async Task AddUnassignedPointsAsync(Guid playerId, long amount, CancellationToken ct = default)
    {
        // Atomic increment rather than read-modify-write. Raid loot claims serialize on the PARTICIPANT
        // row, so a player claiming two lootable raids at once runs both grants concurrently — both used
        // to read the same starting total and one grant vanished, unrecoverably, because the claim latch
        // had already marked both participant rows rewarded.
        var granted = await _players.IncrementSkillPointsAsync(playerId, amount, ct);
        if (granted == 0 && amount != 0) return;   // no stats row → nothing to grant

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "StatBagUsed", null,
            $"Added {amount} unassigned SkillPoints from item.",
            null), ct);
    }

    public async Task<PlayerStatsResponse?> GetStatsAsync(Guid playerId, CancellationToken ct = default)
    {
        var player = await _players.FindByIdWithStatsAsync(playerId, ct);
        if (player is null || player.IsDeleted) return null;
        var stats = player.Stats!;

        // Effective stats include gear bonuses.
        var combat = await _equipment.GetEffectiveCombatDataAsync(
            playerId, stats.BaseAttack, stats.BaseDefense, ct);

        return new PlayerStatsResponse
        {
            SkillPoints            = stats.SkillPoints,
            EnergyInvestment       = stats.EnergyInvestment,
            StaminaInvestment      = stats.StaminaInvestment,
            DiscernmentInvestment  = stats.DiscernmentInvestment,
            CurrentLsi             = (decimal)stats.ComputeLSI(player.Level),
            MaxEnergy              = stats.ComputeMaxEnergy(),
            MaxStamina             = stats.ComputeMaxStamina(),
            MaxGuildStamina        = player.Level,
            BaseAttack             = stats.BaseAttack,
            BaseDefense            = stats.BaseDefense,
            BaseMaxHealth          = stats.BaseMaxHealth,
            CurrentHealth          = stats.CurrentHealth,
            EffectiveAttack        = combat.EffectiveAttack,
            EffectiveDefense       = combat.EffectiveDefense,
        };
    }

    /// <summary>
    /// XP required to advance from <paramref name="level"/> to the next.
    ///
    /// The linear floor is what stops a full stamina dump from being worth a whole level: the stamina
    /// pool grows linearly with level (LSI-capped), the power curve does not, so without it the two
    /// cross and never uncross. See LevelingConfig.XpLinearPerLevel.
    /// </summary>
    public int XpToNextLevel(int level)
    {
        var cfg = _levelingConfig.Value;
        double baseXp = cfg.XpBaseMultiplier * Math.Pow(level, cfg.XpExponent);
        double linearFloor = cfg.XpLinearPerLevel * level;
        int milestoneFloor = cfg.GetFloor(level);
        return Math.Max(milestoneFloor, Math.Max((int)Math.Round(linearFloor), (int)Math.Round(baseXp)));
    }

    public CritProfile GetCritProfile(long discernment)
    {
        var cfg = _combatConfig.Value;
        double chance = cfg.BaseCritChance
            + Math.Min(cfg.MaxCritChanceBonus, discernment * cfg.CritChancePerDiscernment);
        double multiplier = cfg.BaseCritMultiplier
            + Math.Min(cfg.MaxCritDamageBonus, discernment * cfg.CritDamagePerDiscernment);
        return new CritProfile(
            Math.Clamp(chance, 0.0, cfg.BaseCritChance + cfg.MaxCritChanceBonus),
            Math.Clamp(multiplier, cfg.BaseCritMultiplier, cfg.BaseCritMultiplier + cfg.MaxCritDamageBonus));
    }

    /// <summary>
    /// Why the pool cap refused, in terms the player can act on.
    ///
    /// This is the FIRST rule a player meets with no tutorial running — the opening sequence ends at
    /// level 3 and the cap does not refuse a point until level 4 — so this message is the only
    /// explanation they get. It previously read "Allocation would exceed LSI cap of 7.45. Current
    /// LSI: 5.00": a number, a threshold, and nothing to do about either. It never said that Stamina
    /// counts double, how many points WOULD fit, or that the ceiling rises with level, all of which
    /// are known right here.
    ///
    /// House voice: say the fact, not the explanation. The actionable fact is how many fit.
    /// </summary>
    private static string DescribeLsiRefusal(StatType statType, int level, long energy, long stamina)
    {
        // Allowed iff  energy + 2 x stamina  <=  LsiCap x level.  So the room left, in cap units, is:
        double headroom = LsiCap * level - (energy + stamina * 2.0);

        // Stamina spends that room twice as fast, which is the part players do not guess.
        long fits = (long)Math.Floor(statType == StatType.Stamina ? headroom / 2.0 : headroom);
        if (fits < 0) fits = 0;

        string room = fits == 0
            ? $"No more {statType} will fit at level {level}."
            : $"Only {fits:N0} more {statType} will fit at level {level}.";

        // Only raise the shared-ceiling rule when it is actually what is biting. Telling a player
        // with no Stamina that Stamina counts double is noise, and noise is what made the old
        // message useless.
        string why =
            statType == StatType.Stamina ? " Stamina counts twice against the ceiling."
            : stamina > 0                ? " Energy and Stamina share one ceiling, and Stamina counts twice."
            : "";

        return room + why + " The ceiling rises with every level.";
    }

    private static AllocateStatResponse Fail(string reason)
        => new() { Success = false, FailureReason = reason };
}
