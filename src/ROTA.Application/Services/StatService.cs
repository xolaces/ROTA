using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

public sealed class StatService : IStatService
{
    private const double LsiCap = 9.0;

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

        if (stats.SkillPoints < amount)
            return Fail($"Insufficient SkillPoints. Have {stats.SkillPoints}, need {amount}.");

        // LSI cap check — only for Energy and Stamina investment
        if (statType is StatType.Energy or StatType.Stamina)
        {
            int projectedEnergy  = stats.EnergyInvestment  + (statType == StatType.Energy  ? amount : 0);
            int projectedStamina = stats.StaminaInvestment + (statType == StatType.Stamina ? amount : 0);
            double newLsi = player.Level > 0
                ? (projectedEnergy + projectedStamina * 2.0) / player.Level
                : 0;

            if (newLsi > LsiCap)
                return Fail($"Allocation would exceed LSI cap of {LsiCap:F1}. Current LSI: {stats.ComputeLSI(player.Level):F2}");
        }

        // Apply the investment to the correct stat field
        switch (statType)
        {
            case StatType.Energy:      stats.AllocateToEnergy(amount);      break;
            case StatType.Stamina:     stats.AllocateToStamina(amount);     break;
            case StatType.Discernment: stats.AllocateToDiscernment(amount); break;
            case StatType.Attack:      stats.AllocateToAttack(amount);      break;
            case StatType.Defense:     stats.AllocateToDefense(amount);     break;
            case StatType.Health:      stats.AllocateToHealth(amount);      break;
            default: return Fail($"Unknown stat type: {statType}");
        }

        await _players.UpdateStatsAsync(stats, ct);

        // Update resource max values when energy or stamina investment changes.
        // T30 — raising the cap by N also credits +N to the *current* pool (the gained delta, capped
        // at the new max by RefillEnergyAsync) so the spend has an immediate effect. This is NOT a
        // full refill — contrast GrantLevelUpPointsAsync, which calls RefillToMaxAsync.
        if (statType == StatType.Energy)
        {
            await _energy.UpdateMaxAsync(playerId, ResourceType.Energy, stats.ComputeMaxEnergy(), ct);
            await _energy.RefillEnergyAsync(playerId, ResourceType.Energy, amount, ct);
        }

        if (statType == StatType.Stamina)
        {
            await _energy.UpdateMaxAsync(playerId, ResourceType.Stamina, stats.ComputeMaxStamina(), ct);
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
        };
    }

    public async Task GrantLevelUpPointsAsync(Guid playerId, int newLevel, CancellationToken ct = default)
    {
        var player = await _players.FindByIdWithStatsAsync(playerId, ct);
        if (player?.Stats is null) return;

        player.Stats.AddSkillPoints(10);

        // T22 — restore health to full on level-up (forward-compatible; health is PHASE-2 in combat).
        player.Stats.RestoreFullHealth();
        await _players.UpdateStatsAsync(player.Stats, ct);

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

    public async Task AddUnassignedPointsAsync(Guid playerId, int amount, CancellationToken ct = default)
    {
        var player = await _players.FindByIdWithStatsAsync(playerId, ct);
        if (player?.Stats is null) return;

        player.Stats.AddSkillPoints(amount);
        await _players.UpdateStatsAsync(player.Stats, ct);

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

    public int XpToNextLevel(int level)
    {
        var cfg = _levelingConfig.Value;
        double baseXp = cfg.XpBaseMultiplier * Math.Pow(level, cfg.XpExponent);
        int floor = cfg.GetFloor(level);
        return Math.Max(floor, (int)Math.Round(baseXp));
    }

    public CritProfile GetCritProfile(int discernment)
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

    private static AllocateStatResponse Fail(string reason)
        => new() { Success = false, FailureReason = reason };
}
