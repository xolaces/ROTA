using System.Text.Json;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;

namespace ROTA.Infrastructure.Services;

public sealed class LootTableProvider : ILootTableProvider
{
    private readonly IReadOnlyDictionary<string, LootTableDefinition> _tables;

    public LootTableProvider(string contentRootPath, IRaidDefinitionProvider raidDefinitions)
    {
        var path = Path.Combine(contentRootPath, "content", "loot_tables.json");
        if (!File.Exists(path))
        {
            _tables = new Dictionary<string, LootTableDefinition>();
            return;
        }

        var json = File.ReadAllText(path);
        var list = JsonSerializer.Deserialize<List<LootTableDefinition>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("loot_tables.json deserialized to null.");

        // Startup validation
        foreach (var table in list.Where(t => t.Type == "Raid"))
        {
            if (table.Difficulties is null) continue;
            var raidDef = raidDefinitions.GetById(table.Id.Replace("lt_raid_", "raid_"));

            foreach (var (diffName, diff) in table.Difficulties)
            {
                if (diff.OnHitDrops is { Count: > 0 })
                {
                    if (raidDef is null || !raidDef.HasOnHitDrops)
                        throw new InvalidOperationException(
                            $"Loot table '{table.Id}' [{diffName}] has onHitDrops but raid definition does not have hasOnHitDrops=true.");
                }

                if (diff.ThresholdRewards is null) continue;
                foreach (var threshold in diff.ThresholdRewards)
                {
                    if (threshold.AttackPoints > 0 || threshold.DefensePoints > 0 || threshold.DiscernmentPoints > 0)
                    {
                        if (raidDef is null || (raidDef.Tier != "World" && raidDef.Tier != "Event"))
                            throw new InvalidOperationException(
                                $"Loot table '{table.Id}' [{diffName}] has attack/defense/discernment points but raid tier is '{raidDef?.Tier}' (only World/Event allowed).");
                    }
                }
            }
        }

        _tables = list.ToDictionary(t => t.Id, t => t);
    }

    public LootTableDefinition? GetById(string id)
        => _tables.TryGetValue(id, out var t) ? t : null;
}
