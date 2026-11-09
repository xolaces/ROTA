using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

/// <summary>
/// System 29 S2 — the daily chest. Always free: there is no path that spends anything for a re-pick or
/// an extra chest, which keeps it out of paid-randomness territory.
/// </summary>
public sealed class DailyChestService : IDailyChestService
{
    private readonly IPlayerDailyChestRepository _chests;
    private readonly IDailyChestPoolProvider _pool;
    private readonly IItemDefinitionProvider _itemDefs;
    private readonly IPlayerInventoryRepository _inventory;
    private readonly IPlayerMutationLock _mutationLock;
    private readonly IAuditLogRepository _auditLog;
    private readonly Random _random;

    public DailyChestService(
        IPlayerDailyChestRepository chests,
        IDailyChestPoolProvider pool,
        IItemDefinitionProvider itemDefs,
        IPlayerInventoryRepository inventory,
        IPlayerMutationLock mutationLock,
        IAuditLogRepository auditLog,
        Random? random = null)
    {
        _chests       = chests;
        _pool         = pool;
        _itemDefs     = itemDefs;
        _inventory    = inventory;
        _mutationLock = mutationLock;
        _auditLog     = auditLog;
        _random       = random ?? Random.Shared;
    }

    public async Task<DailyChestResponse> GetTodayAsync(Guid playerId, CancellationToken ct = default)
    {
        var today = Today();
        var chest = await EnsureRolledAsync(playerId, today, ct);
        return ToResponse(chest.GetContents(), today, chest.PickedIndex);
    }

    public Task<PickDailyChestResponse> PickAsync(Guid playerId, int index, CancellationToken ct = default)
    {
        // The controller validates first (FluentValidation); this guards every other caller.
        if (index < 0 || index >= PlayerDailyChest.ChestCount)
            return Task.FromResult(new PickDailyChestResponse
            {
                FailureCode   = DailyChestFailureCode.InvalidIndex,
                FailureReason = $"Pick a chest from 0 to {PlayerDailyChest.ChestCount - 1}.",
            });

        // One transaction under the player's advisory lock: the pick latch and the item grant commit or
        // roll back together, so a failed grant can never burn the day's pick.
        return _mutationLock.RunAsync(playerId, () => PickCoreAsync(playerId, (short)index, ct), ct);
    }

    private async Task<PickDailyChestResponse> PickCoreAsync(Guid playerId, short index, CancellationToken ct)
    {
        var today = Today();

        // A pick can arrive before any GET today (or straddle midnight): roll first so there is a
        // chest to claim. Across midnight this picks from the NEW day's chest; the old one expired unpicked.
        var chest    = await EnsureRolledAsync(playerId, today, ct);
        var contents = chest.GetContents();

        if (!await _chests.TryPickAsync(chest.Id, playerId, index, ct))
        {
            var current = await _chests.FindAsync(playerId, today, ct) ?? chest;
            return new PickDailyChestResponse
            {
                FailureCode   = DailyChestFailureCode.AlreadyPicked,
                FailureReason = "Already opened today.",
                Chest         = ToResponse(contents, today, current.PickedIndex),
            };
        }

        var slot = contents[index];
        await GrantItemAsync(playerId, slot.ItemDefinitionId, slot.Quantity, ct);

        var granted = ToSlotResponse(slot);
        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "DailyChestPicked", null,
            $"Day {today:yyyy-MM-dd}: picked chest {index}, got {slot.Quantity}x {granted.ItemName} ({slot.ItemDefinitionId}).",
            null), ct);

        return new PickDailyChestResponse
        {
            Success = true,
            Chest   = ToResponse(contents, today, index),
            Granted = granted,
        };
    }

    private async Task<PlayerDailyChest> EnsureRolledAsync(Guid playerId, DateOnly day, CancellationToken ct)
    {
        var existing = await _chests.FindAsync(playerId, day, ct);
        if (existing is not null) return existing;

        var rolled = PlayerDailyChest.Create(playerId, day, Roll());
        if (await _chests.InsertIfAbsentAsync(rolled, ct))
        {
            await _auditLog.AppendAsync(AuditLog.Create(
                playerId, "DailyChestRolled", null, $"Day {day:yyyy-MM-dd}: six chests rolled.", null), ct);
            return rolled;
        }

        // Lost the race to a concurrent first read — use the roll that was stored.
        return await _chests.FindAsync(playerId, day, ct)
            ?? throw new InvalidOperationException($"Daily chest for {playerId} on {day} vanished after insert.");
    }

    private List<DailyChestSlot> Roll()
    {
        // Six independent weighted draws; duplicates allowed (as in Dawn).
        var slots = new List<DailyChestSlot>(PlayerDailyChest.ChestCount);
        for (var i = 0; i < PlayerDailyChest.ChestCount; i++)
        {
            var entry  = Draw();
            var rarity = _itemDefs.GetById(entry.ItemDefinitionId)?.Rarity ?? default;
            slots.Add(new DailyChestSlot(entry.ItemDefinitionId, entry.Quantity, rarity));
        }
        return slots;
    }

    private DailyChestPoolEntry Draw()
    {
        var roll = _random.Next(_pool.TotalWeight);
        foreach (var entry in _pool.Pool)
        {
            if (roll < entry.Weight) return entry;
            roll -= entry.Weight;
        }
        return _pool.Pool[^1];   // unreachable while TotalWeight == Σ weights (provider-enforced)
    }

    private async Task GrantItemAsync(Guid playerId, string itemDefinitionId, int quantity, CancellationToken ct)
    {
        var existing = await _inventory.GetAsync(playerId, itemDefinitionId, ct);
        if (existing is not null)
        {
            existing.AddQuantity(quantity);
            await _inventory.UpdateAsync(existing, ct);
        }
        else
        {
            await _inventory.CreateAsync(PlayerInventoryItem.Create(playerId, itemDefinitionId, quantity), ct);
        }
    }

    private DailyChestResponse ToResponse(IReadOnlyList<DailyChestSlot> contents, DateOnly day, short? pickedIndex)
        => new()
        {
            Day         = day.ToString("yyyy-MM-dd"),
            ResetsAt    = new DateTimeOffset(day.AddDays(1).ToDateTime(TimeOnly.MinValue), TimeSpan.Zero),
            Picked      = pickedIndex.HasValue,
            PickedIndex = pickedIndex,
            Chests      = pickedIndex.HasValue ? contents.Select(ToSlotResponse).ToList() : null,
        };

    private DailyChestSlotResponse ToSlotResponse(DailyChestSlot slot)
        => new()
        {
            ItemDefinitionId = slot.ItemDefinitionId,
            ItemName         = _itemDefs.GetById(slot.ItemDefinitionId)?.Name ?? slot.ItemDefinitionId,
            Quantity         = slot.Quantity,
            Rarity           = slot.Rarity.ToString(),
        };

    private static DateOnly Today() => DateOnly.FromDateTime(DateTime.UtcNow);
}
