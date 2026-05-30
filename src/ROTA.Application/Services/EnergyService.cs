using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Services;

public sealed class EnergyService : IEnergyService
{
    private readonly IPlayerResourceRepository _resources;
    private readonly IAuditLogRepository _auditLog;
    private readonly IPlayerRepository _players;
    private readonly IClassService _classService;

    public EnergyService(
        IPlayerResourceRepository resources,
        IAuditLogRepository auditLog,
        IPlayerRepository players,
        IClassService classService)
    {
        _resources    = resources;
        _auditLog     = auditLog;
        _players      = players;
        _classService = classService;
    }

    public async Task<int> GetCurrentEnergyAsync(Guid playerId, ResourceType type, CancellationToken ct = default)
    {
        var resource = await _resources.GetAsync(playerId, type, ct)
            ?? throw new InvalidOperationException($"Resource {type} not found for player {playerId}.");

        var minutesPerPoint = await ResolveMinutesPerPointAsync(playerId, type, ct);
        return ComputeLiveValue(resource, minutesPerPoint);
    }

    public async Task<bool> SpendEnergyAsync(Guid playerId, ResourceType type, int amount, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var minutesPerPoint = await ResolveMinutesPerPointAsync(playerId, type, ct);

        var success = await _resources.AtomicUpdateAsync(playerId, type, resource =>
        {
            var live = ComputeLiveValue(resource, minutesPerPoint, now);
            if (live < amount) return false;

            resource.SaveCheckpoint(live - amount, now);
            return true;
        }, ct);

        if (success)
        {
            await _auditLog.AppendAsync(AuditLog.Create(
                playerId,
                $"EnergySpend:{type}",
                null,
                $"Spent {amount} {type}",
                null), ct);
        }

        return success;
    }

    public async Task RefillEnergyAsync(Guid playerId, ResourceType type, int amount, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;
        var minutesPerPoint = await ResolveMinutesPerPointAsync(playerId, type, ct);

        await _resources.AtomicUpdateAsync(playerId, type, resource =>
        {
            var live = ComputeLiveValue(resource, minutesPerPoint, now);
            resource.SaveCheckpoint(Math.Min(live + amount, resource.MaxValue), now);
            return true;
        }, ct);
    }

    public async Task UpdateMaxAsync(Guid playerId, ResourceType type, int newMax, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        await _resources.AtomicUpdateAsync(playerId, type, resource =>
        {
            resource.SetMaxValue(newMax, now);
            return true;
        }, ct);
    }

    // -------------------------------------------------------------------
    // DOMAIN LOGIC — live value computation
    // -------------------------------------------------------------------

    // BEHAVIOR CHANGE (v0.2.2): GuildStamina previously had RegenPerMinute=0 (no regen).
    // Now all resource types regen at the class-configured rate from ClassConfig.
    // GuildStamina now regenerates at ClassConfig.GuildStaminaRegenMinutes (default 2.0 min/point).

    private static int ComputeLiveValue(PlayerResource resource, double minutesPerPoint, DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.UtcNow;

        if (minutesPerPoint <= 0)
            return Math.Min(resource.CurrentValue, resource.MaxValue);

        var elapsed = (reference - resource.LastRegenAt).TotalMinutes;
        var regenerated = (int)(elapsed / minutesPerPoint);
        return Math.Min(resource.CurrentValue + regenerated, resource.MaxValue);
    }

    private async Task<double> ResolveMinutesPerPointAsync(Guid playerId, ResourceType type, CancellationToken ct)
    {
        var player = await _players.FindByIdAsync(playerId, ct);
        if (player is null) return 0; // no regen if player not found

        var rates = _classService.GetRegenRates(player.Class);
        return type switch
        {
            ResourceType.Energy       => rates.EnergyRegenMinutesPerPoint,
            ResourceType.Stamina      => rates.StaminaRegenMinutesPerPoint,
            ResourceType.GuildStamina => rates.GuildStaminaRegenMinutesPerPoint,
            _                         => 0,
        };
    }
}
