using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

public sealed class ClassService : IClassService
{
    private readonly IPlayerRepository _players;
    private readonly IAuditLogRepository _auditLog;
    private readonly IOptions<ClassConfig> _config;

    private static readonly IReadOnlyList<PlayerClass> Tier2Choices =
        new[] { PlayerClass.Ironguard, PlayerClass.Arcanist, PlayerClass.Sentinel };

    private static readonly IReadOnlyDictionary<PlayerClass, IReadOnlyList<PlayerClass>> Tier3Choices =
        new Dictionary<PlayerClass, IReadOnlyList<PlayerClass>>
        {
            [PlayerClass.Ironguard] = new[] { PlayerClass.Stormguard, PlayerClass.Bloodguard, PlayerClass.Siegebreaker },
            [PlayerClass.Arcanist]  = new[] { PlayerClass.HighArcanist, PlayerClass.ShadowArcanist, PlayerClass.Runecaller },
            [PlayerClass.Sentinel]  = new[] { PlayerClass.IroncladSentinel, PlayerClass.Voidwalker, PlayerClass.Dawnblade },
        };

    public ClassService(
        IPlayerRepository players,
        IAuditLogRepository auditLog,
        IOptions<ClassConfig> config)
    {
        _players  = players;
        _auditLog = auditLog;
        _config   = config;
    }

    public bool IsConvergedClass(PlayerClass playerClass)
        => (int)playerClass >= 300;

    public ClassRegenRates GetRegenRates(PlayerClass playerClass)
    {
        var (energy, stamina) = _config.Value.GetRegenRates(playerClass);
        return new ClassRegenRates
        {
            CurrentClass                   = playerClass.ToString(),
            EnergyRegenMinutesPerPoint     = energy,
            StaminaRegenMinutesPerPoint    = stamina,
            GuildStaminaRegenMinutesPerPoint = _config.Value.GuildStaminaRegenMinutes,
            IsConverged                    = IsConvergedClass(playerClass),
        };
    }

    public IReadOnlyList<PlayerClass> GetAvailableChoices(int level, PlayerClass current)
    {
        // Converged classes and high-level players auto-advance only — no manual choices
        if (IsConvergedClass(current) || level >= 500)
            return Array.Empty<PlayerClass>();

        // Tier 2 unlock: level 5+, still Conscript
        if (level >= 5 && level < 100 && current == PlayerClass.Conscript)
            return Tier2Choices;

        // Tier 3 unlock: level 100+, currently a Tier 2 class
        if (level >= 100 && Tier3Choices.TryGetValue(current, out var tier3))
            return tier3;

        return Array.Empty<PlayerClass>();
    }

    public PlayerClass ComputeAutoAdvance(int level, PlayerClass current)
    {
        // Check convergence milestones first — these override path-based advances
        var convergenceName = _config.Value.GetConvergenceClass(level);
        if (convergenceName != null && Enum.TryParse<PlayerClass>(convergenceName, out var converged))
            return converged;

        // Path-based auto-advances below convergence
        if (level == 1000)
        {
            var baseName = current.ToString()
                .Replace("Legendary", "")
                .Replace("Ascendant", "");
            if (Enum.TryParse<PlayerClass>("Ascendant" + baseName, out var ascendant))
                return ascendant;
        }

        if (level == 500)
        {
            var baseName = current.ToString()
                .Replace("Legendary", "")
                .Replace("Ascendant", "");
            if (Enum.TryParse<PlayerClass>("Legendary" + baseName, out var legendary))
                return legendary;
        }

        return current;
    }

    public async Task<ClassRegenRates?> AssignClassAsync(
        Guid playerId, PlayerClass requested, CancellationToken ct = default)
    {
        var player = await _players.FindByIdAsync(playerId, ct);
        if (player is null) return null;

        var available = GetAvailableChoices(player.Level, player.Class);
        if (!available.Contains(requested))
            return null; // caller should treat null as forbidden

        player.SetClass(requested);
        await _players.UpdateAsync(player, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "ClassAssigned", null,
            $"Class set to {requested}",
            null), ct);

        var rates = GetRegenRates(requested);
        rates.AvailableChoices = GetAvailableChoices(player.Level, requested)
            .Select(c => c.ToString()).ToList();
        return rates;
    }

    public async Task<ClassRegenRates?> GetClassInfoAsync(Guid playerId, CancellationToken ct = default)
    {
        var player = await _players.FindByIdAsync(playerId, ct);
        if (player is null) return null;

        var rates = GetRegenRates(player.Class);
        rates.AvailableChoices = GetAvailableChoices(player.Level, player.Class)
            .Select(c => c.ToString()).ToList();
        return rates;
    }
}
