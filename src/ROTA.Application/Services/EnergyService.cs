using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Services;

// BETA — all writes go through AtomicUpdateAsync (row-level lock) to prevent double-spend.
public sealed class EnergyService : IEnergyService
{
    private readonly IPlayerResourceRepository _resources;
    private readonly IAuditLogRepository _auditLog;

    public EnergyService(IPlayerResourceRepository resources, IAuditLogRepository auditLog)
    {
        _resources = resources;
        _auditLog = auditLog;
    }

    /// <inheritdoc />
    public async Task<int> GetCurrentEnergyAsync(Guid playerId, ResourceType type, CancellationToken ct = default)
    {
        var resource = await _resources.GetAsync(playerId, type, ct)
            ?? throw new InvalidOperationException($"Resource {type} not found for player {playerId}.");

        return ComputeLiveValue(resource);
    }

    /// <inheritdoc />
    public async Task<bool> SpendEnergyAsync(Guid playerId, ResourceType type, int amount, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        var success = await _resources.AtomicUpdateAsync(playerId, type, resource =>
        {
            var live = ComputeLiveValue(resource, now);
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

    /// <inheritdoc />
    public async Task RefillEnergyAsync(Guid playerId, ResourceType type, int amount, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        await _resources.AtomicUpdateAsync(playerId, type, resource =>
        {
            var live = ComputeLiveValue(resource, now);
            resource.SaveCheckpoint(Math.Min(live + amount, resource.MaxValue), now);
            return true;
        }, ct);
    }

    /// <inheritdoc />
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

    private static int ComputeLiveValue(PlayerResource resource, DateTimeOffset? now = null)
    {
        var reference = now ?? DateTimeOffset.UtcNow;

        if (resource.RegenPerMinute <= 0)
            return Math.Min(resource.CurrentValue, resource.MaxValue);

        var elapsed = (reference - resource.LastRegenAt).TotalMinutes;
        var regenerated = (int)(elapsed * resource.RegenPerMinute);
        return Math.Min(resource.CurrentValue + regenerated, resource.MaxValue);
    }
}
