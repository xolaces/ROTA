using System.Text.Json;
using System.Text.Json.Serialization;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;

namespace ROTA.Infrastructure.Services;

public sealed class ItemDefinitionProvider : IItemDefinitionProvider
{
    private readonly IReadOnlyDictionary<string, ItemDefinition> _items;

    public ItemDefinitionProvider(string contentRootPath)
    {
        var path = Path.Combine(contentRootPath, "content", "items.json");
        if (!File.Exists(path))
        {
            _items = new Dictionary<string, ItemDefinition>();
            return;
        }

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() }, // ItemRarity and ItemType are stored as strings
        };
        var list = JsonSerializer.Deserialize<List<ItemDefinition>>(json, options)
            ?? throw new InvalidOperationException("items.json deserialized to null.");

        var byId = new Dictionary<string, ItemDefinition>(StringComparer.Ordinal);
        foreach (var i in list)
        {
            if (!byId.TryAdd(i.Id, i))
                throw new InvalidOperationException($"items.json: duplicate id '{i.Id}'.");
        }

        // System 22 Phase A (Slice 7) — validate the Discernment quality-upgrade ladder.
        foreach (var i in list)
        {
            if (string.IsNullOrEmpty(i.UpgradesTo)) continue;
            if (!byId.TryGetValue(i.UpgradesTo, out var target))
                throw new InvalidOperationException(
                    $"items.json: '{i.Id}' upgradesTo '{i.UpgradesTo}' which does not exist.");
            if (target.Rarity <= i.Rarity)
                throw new InvalidOperationException(
                    $"items.json: '{i.Id}' ({i.Rarity}) upgradesTo '{i.UpgradesTo}' ({target.Rarity}) must be strictly higher rarity.");
        }

        _items = byId;
    }

    public ItemDefinition? GetById(string id)
        => _items.TryGetValue(id, out var i) ? i : null;

    public IReadOnlyList<ItemDefinition> GetAll()
        => _items.Values.ToList();
}
