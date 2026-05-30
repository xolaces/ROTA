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

    public StatService(
        IPlayerRepository players,
        IEnergyService energy,
        IGemService gems,
        IAuditLogRepository auditLog,
        IOptions<LevelingConfig> levelingConfig,
        IOptions<CombatConfig> combatConfig,
        IClassService classService)
    {
        _players        = players;
        _energy         = energy;
        _gems           = gems;
        _auditLog       = auditLog;
        _levelingConfig = levelingConfig;
        _combatConfig   = combatConfig;
        _classService   = classService;
    }

    public async Task<AllocateStatResponse> AllocateStatPointAsync(
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

        // Update resource max values when energy or stamina investment changes
        if (statType == StatType.Energy)
            await _energy.UpdateMaxAsync(playerId, ResourceType.Energy, stats.ComputeMaxEnergy(), ct);

        if (statType == StatType.Stamina)
            await _energy.UpdateMaxAsync(playerId, ResourceType.Stamina, stats.ComputeMaxStamina(), ct);

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
        await _players.UpdateStatsAsync(player.Stats, ct);

        if (newLevel % 5 == 0)
        {
            var referenceId = $"levelup:gems:{playerId}:{newLevel}";
            await _gems.GrantGemsAsync(
                playerId, 5, GemTransactionType.LevelUpReward, referenceId, ct);
        }

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
            $"Level {newLevel} reward: +10 SkillPoints{(newLevel % 5 == 0 ? ", +5 Gems" : "")}{(classChanged ? $", class → {advanced}" : "")}",
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
        if (player?.Stats is null) return null;

        var stats = player.Stats;
        return new PlayerStatsResponse
        {
            SkillPoints           = stats.SkillPoints,
            EnergyInvestment      = stats.EnergyInvestment,
            StaminaInvestment     = stats.StaminaInvestment,
            DiscernmentInvestment = stats.DiscernmentInvestment,
            CurrentLsi            = (decimal)stats.ComputeLSI(player.Level),
            MaxEnergy             = stats.ComputeMaxEnergy(),
            MaxStamina            = stats.ComputeMaxStamina(),
            MaxGuildStamina       = player.Level,
            BaseAttack            = stats.BaseAttack,
            BaseDefense           = stats.BaseDefense,
            BaseMaxHealth         = stats.BaseMaxHealth,
            CurrentHealth         = stats.CurrentHealth,
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
