using System.Text.Json;
using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;

namespace ROTA.Infrastructure.Services;

/// <summary>
/// System 29 — loads and validates <c>content/daily_chest.json</c>. Deliberately NOT a
/// <c>loot_tables.json</c> entry: those are built around difficulties, contribution thresholds and on-hit
/// drops, none of which a pick-one chest has.
/// </summary>
public sealed class DailyChestPoolProvider : IDailyChestPoolProvider
{
    // Gear has its own grant path and stays out (spec §6.1). Stat bags feed the stat-point cost curve,
    // which is balanced against play, so a free daily source of them is an owner call, not content's.
    private static readonly HashSet<ItemType> AllowedTypes = new() { ItemType.Consumable, ItemType.Material, ItemType.Sigil };

    public IReadOnlyList<DailyChestPoolEntry> Pool { get; }
    public int TotalWeight { get; }

    public DailyChestPoolProvider(string contentRootPath, IItemDefinitionProvider items)
    {
        var path = Path.Combine(contentRootPath, "content", "daily_chest.json");
        if (!File.Exists(path))
            throw new InvalidOperationException("content/daily_chest.json is missing — the daily chest has nothing to roll.");

        var file = JsonSerializer.Deserialize<PoolFile>(File.ReadAllText(path), new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
        }) ?? throw new InvalidOperationException("daily_chest.json deserialized to null.");

        if (file.Pool is null || file.Pool.Count == 0)
            throw new InvalidOperationException("daily_chest.json: the pool is empty.");

        var pool = new List<DailyChestPoolEntry>();
        long total = 0;
        foreach (var e in file.Pool)
        {
            var def = items.GetById(e.ItemDefinitionId ?? string.Empty)
                ?? throw new InvalidOperationException($"daily_chest.json: unknown item '{e.ItemDefinitionId}'.");
            if (!AllowedTypes.Contains(def.Type))
                throw new InvalidOperationException(
                    $"daily_chest.json: '{def.Id}' is {def.Type}; the chest may only hold {string.Join(", ", AllowedTypes)}.");
            if (e.Quantity <= 0)
                throw new InvalidOperationException($"daily_chest.json: '{def.Id}' quantity must be positive.");
            if (e.Weight <= 0)
                throw new InvalidOperationException($"daily_chest.json: '{def.Id}' weight must be positive.");

            pool.Add(new DailyChestPoolEntry(def.Id, e.Quantity, e.Weight));
            total += e.Weight;
        }

        if (total > int.MaxValue)
            throw new InvalidOperationException("daily_chest.json: total weight overflows.");

        Pool = pool;
        TotalWeight = (int)total;
    }

    private sealed class PoolFile
    {
        public List<PoolEntryJson>? Pool { get; set; }
    }

    private sealed class PoolEntryJson
    {
        public string? ItemDefinitionId { get; set; }
        public int Quantity { get; set; }
        public int Weight { get; set; }
    }
}
