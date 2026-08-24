using System.Text.Json;
using System.Text.Json.Serialization;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Enums;

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

        // D-008 consumables — a misconfigured potion must fail the BOOT, not fail silently in a
        // player's hands. ItemService rejects these at use time too; this makes bad content unshippable.
        foreach (var i in list)
        {
            if (i.Type != ItemType.Consumable) continue;

            if (string.IsNullOrWhiteSpace(i.RestoreResourceType))
                throw new InvalidOperationException(
                    $"items.json: consumable '{i.Id}' must set restoreResourceType.");
            if (!Enum.TryParse<ResourceType>(i.RestoreResourceType, out _))
                throw new InvalidOperationException(
                    $"items.json: consumable '{i.Id}' restoreResourceType '{i.RestoreResourceType}' is not a valid ResourceType.");
            if (!i.RestoreToMax && i.RestoreAmount <= 0)
                throw new InvalidOperationException(
                    $"items.json: consumable '{i.Id}' must restore a positive amount or set restoreToMax.");
            if (i.GoldPrice < 0)
                throw new InvalidOperationException(
                    $"items.json: consumable '{i.Id}' has a negative goldPrice.");
        }

        _items = byId;
    }

    public ItemDefinition? GetById(string id)
        => _items.TryGetValue(id, out var i) ? i : null;

    public IReadOnlyList<ItemDefinition> GetAll()
        => _items.Values.ToList();
}
