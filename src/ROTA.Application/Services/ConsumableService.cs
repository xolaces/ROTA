using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

/// <summary>
/// D-008 / D-013 — gem-priced instant refills, the premium tier of the northstar §1 escape valve.
/// Gold-priced potions are items (<see cref="ItemService"/>); this is a service purchase.
/// </summary>
public sealed class ConsumableService : IConsumableService
{
    private readonly IEnergyService _energy;
    private readonly IPlayerResourceRepository _resources;
    private readonly IGemService _gems;
    private readonly IPlayerMutationLock _mutationLock;
    private readonly IAuditLogRepository _auditLog;
    private readonly ConsumableConfig _config;

    public ConsumableService(
        IEnergyService energy,
        IPlayerResourceRepository resources,
        IGemService gems,
        IPlayerMutationLock mutationLock,
        IAuditLogRepository auditLog,
        IOptions<ConsumableConfig> config)
    {
        _energy       = energy;
        _resources    = resources;
        _gems         = gems;
        _mutationLock = mutationLock;
        _auditLog     = auditLog;
        _config       = config.Value;
    }

    public async Task<RefillOptionsResponse> GetRefillOptionsAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var gems = await _gems.GetBalanceAsync(playerId, ct);
        var options = new List<RefillOptionResponse>();

        // Only resources the config prices are offered — an unpriced pool (GuildStamina) is simply
        // absent rather than shown-and-refused.
        foreach (var (name, cost) in _config.InstantRefillGemCost.OrderBy(kv => kv.Key, StringComparer.Ordinal))
        {
            if (!Enum.TryParse<ResourceType>(name, out var type)) continue;

            var pool = await _resources.GetAsync(playerId, type, ct);
            if (pool is null) continue;

            var live = await _energy.GetCurrentEnergyAsync(playerId, type, ct);
            options.Add(new RefillOptionResponse
            {
                ResourceType = type.ToString(),
                GemCost      = cost,
                CurrentValue = live,
                MaxValue     = pool.MaxValue,
                CanRefill    = live < pool.MaxValue,
                CanAfford    = gems >= cost,
            });
        }

        return new RefillOptionsResponse { Options = options, PlayerGems = gems };
    }

    public Task<RefillResourceResponse> RefillAsync(
        Guid playerId, ResourceType resourceType, CancellationToken ct = default)
        // Serialized per player so a double-tap can't charge twice: the first fills the pool, the
        // second then sees it full and is rejected before spending.
        => _mutationLock.RunAsync(playerId, () => RefillCoreAsync(playerId, resourceType, ct), ct);

    private async Task<RefillResourceResponse> RefillCoreAsync(
        Guid playerId, ResourceType resourceType, CancellationToken ct)
    {
        if (!_config.InstantRefillGemCost.TryGetValue(resourceType.ToString(), out var gemCost) || gemCost <= 0)
            return Fail(RefillFailureCode.NotRefillable,
                $"{resourceType} cannot be refilled with gems.");

        var pool = await _resources.GetAsync(playerId, resourceType, ct);
        if (pool is null)
            return Fail(RefillFailureCode.ResourceNotFound, $"You have no {resourceType} pool.");

        // Live value, not the stored checkpoint — regen since the last write counts toward "full".
        var before = await _energy.GetCurrentEnergyAsync(playerId, resourceType, ct);
        if (before >= pool.MaxValue)
            return Fail(RefillFailureCode.AlreadyFull, $"Your {resourceType} is already full.");

        // Refills are REPEATABLE, so unlike a one-time purchase there is no natural idempotency key.
        // A short time bucket gives one: a retry after a dropped response lands on the same
        // referenceId and returns AlreadyProcessed instead of charging again, while a genuine refill
        // after the pool is spent down lands in a later bucket and charges properly. The mutation lock
        // plus the already-full check above cover the double-click; this covers a retry that spans
        // requests (client timeout, crash between charge and fill).
        var bucket = DateTimeOffset.UtcNow.ToUnixTimeSeconds()
                     / Math.Max(1, _config.RefillIdempotencyWindowSeconds);
        var referenceId = $"refill:{playerId}:{resourceType}:{bucket}";

        var outcome = await _gems.SpendGemsAsync(
            playerId, gemCost, GemTransactionType.EnergyRefill, referenceId, ct);

        if (outcome == GemSpendOutcome.InsufficientBalance)
            return Fail(RefillFailureCode.InsufficientGems,
                $"Insufficient gems. Required: {gemCost}.");

        // Charged OR AlreadyProcessed → complete the (idempotent) fill. AlreadyProcessed means the
        // charge committed but the fill may not have — re-running it is how the player gets what they
        // paid for, exactly as the magic/unit/legion shops treat their replay path.
        await _energy.RefillToMaxAsync(playerId, resourceType, ct);

        var after   = await _energy.GetCurrentEnergyAsync(playerId, resourceType, ct);
        var balance = await _gems.GetBalanceAsync(playerId, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "ResourceRefilled", null,
            $"Refilled {resourceType} {before} -> {after} for {gemCost} gems " +
            $"(ref={referenceId}, spend={outcome}).", null), ct);

        return new RefillResourceResponse
        {
            Success        = true,
            ResourceType   = resourceType.ToString(),
            GemsSpent      = gemCost,
            AmountRestored = after - before,
            NewValue       = after,
            MaxValue       = pool.MaxValue,
            NewGemBalance  = balance,
        };
    }

    private static RefillResourceResponse Fail(RefillFailureCode code, string reason)
        => new() { FailureCode = code, FailureReason = reason };
}
