using Microsoft.Extensions.Logging;
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
    private readonly ILeaderboardService _leaderboards;
    private readonly ILogger<EnergyService> _logger;

    public EnergyService(
        IPlayerResourceRepository resources,
        IAuditLogRepository auditLog,
        IPlayerRepository players,
        IClassService classService,
        ILeaderboardService leaderboards,
        ILogger<EnergyService> logger)
    {
        _resources    = resources;
        _auditLog     = auditLog;
        _players      = players;
        _classService = classService;
        _leaderboards = leaderboards;
        _logger       = logger;
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
        // Defense-in-depth (audit fix): a negative spend would CREDIT the pool (live − (−n) = live + n).
        // All callers pass server-computed costs today, but this guard makes the invariant local.
        if (amount <= 0) return false;

        var now = DateTimeOffset.UtcNow;
        var minutesPerPoint = await ResolveMinutesPerPointAsync(playerId, type, ct);

        var success = await _resources.AtomicUpdateAsync(playerId, type, resource =>
        {
            var (live, checkpointAt) = ComputeLiveValueWithCarry(resource, minutesPerPoint, now);
            if (live < amount) return false;

            resource.SaveCheckpoint(live - amount, checkpointAt);
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

            // Slice 4 — leaderboard write hook (EnergySpent boards).
            // Only Energy counts toward the Questing leaderboard (Q6 — Stamina/GuildStamina excluded).
            // The spend has no ambient DB transaction at this point (AtomicUpdateAsync is its own
            // self-contained PostgreSQL FOR UPDATE, already committed).  Any leaderboard failure must
            // never roll back or fail the spend — swallow + log, mirroring the audit-log discipline
            // in AuditLogMiddleware.
            if (type == ResourceType.Energy)
            {
                try
                {
                    await _leaderboards.RecordEnergySpendAsync(playerId, amount, now, ct);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "Leaderboard write failed for EnergySpent (player {PlayerId}, amount {Amount}). " +
                        "Energy spend committed; board update skipped.", playerId, amount);
                }
            }
        }

        return success;
    }

    public async Task RefillEnergyAsync(Guid playerId, ResourceType type, int amount, CancellationToken ct = default)
    {
        // Defense-in-depth (audit fix): a negative refill would silently drain the pool.
        if (amount <= 0) return;

        var now = DateTimeOffset.UtcNow;
        var minutesPerPoint = await ResolveMinutesPerPointAsync(playerId, type, ct);

        await _resources.AtomicUpdateAsync(playerId, type, resource =>
        {
            var (live, checkpointAt) = ComputeLiveValueWithCarry(resource, minutesPerPoint, now);
            var newValue = Math.Min(live + amount, resource.MaxValue);
            // At the cap the fractional carry is meaningless (no banking past max) — restart the clock.
            resource.SaveCheckpoint(newValue, newValue >= resource.MaxValue ? now : checkpointAt);
            return true;
        }, ct);
    }

    // T56 — deduct up to `amount` from a pool, clamping at 0 (never fails). Used for health damage,
    // which should drain the pool rather than be rejected like a spend. Returns the amount drained.
    public async Task<int> DrainAsync(Guid playerId, ResourceType type, int amount, CancellationToken ct = default)
    {
        if (amount <= 0) return 0;
        var now = DateTimeOffset.UtcNow;
        var minutesPerPoint = await ResolveMinutesPerPointAsync(playerId, type, ct);

        int drained = 0;
        await _resources.AtomicUpdateAsync(playerId, type, resource =>
        {
            var (live, checkpointAt) = ComputeLiveValueWithCarry(resource, minutesPerPoint, now);
            drained = Math.Min(live, amount);
            resource.SaveCheckpoint(live - drained, checkpointAt);
            return true;
        }, ct);
        return drained;
    }

    public async Task RefillToMaxAsync(Guid playerId, ResourceType type, CancellationToken ct = default)
    {
        var now = DateTimeOffset.UtcNow;

        await _resources.AtomicUpdateAsync(playerId, type, resource =>
        {
            resource.SaveCheckpoint(resource.MaxValue, now);
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

    // Audit fix — fractional-regen carry. ComputeLiveValue truncates elapsed/minutesPerPoint to whole
    // points; the old write path then checkpointed at `now`, silently destroying the fractional
    // remainder on EVERY spend/refill/drain (a player acting every 30s with a 5-min rate would regen
    // ~nothing). This variant also returns the timestamp to checkpoint at: backdated by the remainder
    // so progress toward the next point survives the write. At/above the cap the clock restarts at
    // `reference` instead — elapsed time at the cap is not regen progress (no banking past max).
    private static (int Live, DateTimeOffset CheckpointAt) ComputeLiveValueWithCarry(
        PlayerResource resource, double minutesPerPoint, DateTimeOffset reference)
    {
        if (minutesPerPoint <= 0)
            return (Math.Min(resource.CurrentValue, resource.MaxValue), reference);

        var elapsed = (reference - resource.LastRegenAt).TotalMinutes;
        if (elapsed < 0) elapsed = 0; // clock-skew safety: never let a future checkpoint inflate regen

        var regenerated = (int)(elapsed / minutesPerPoint);
        var live = resource.CurrentValue + regenerated;

        if (live >= resource.MaxValue)
            return (resource.MaxValue, reference);

        var remainderMinutes = elapsed - regenerated * minutesPerPoint;
        return (live, reference - TimeSpan.FromMinutes(remainderMinutes));
    }

    public double GetRegenMinutesPerPoint(PlayerClass playerClass, ResourceType type)
    {
        var rates = _classService.GetRegenRates(playerClass);
        return type switch
        {
            ResourceType.Energy       => rates.EnergyRegenMinutesPerPoint,
            ResourceType.Stamina      => rates.StaminaRegenMinutesPerPoint,
            ResourceType.GuildStamina => rates.GuildStaminaRegenMinutesPerPoint,
            ResourceType.Health       => rates.HealthRegenMinutesPerPoint,   // T56
            _                         => 0,
        };
    }

    private async Task<double> ResolveMinutesPerPointAsync(Guid playerId, ResourceType type, CancellationToken ct)
    {
        var player = await _players.FindByIdAsync(playerId, ct);
        if (player is null) return 0; // no regen if player not found

        return GetRegenMinutesPerPoint(player.Class, type);
    }
}
