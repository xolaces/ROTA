using System.Text.Json;
using System.Text.Json.Serialization;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;

namespace ROTA.Infrastructure.Services;

public sealed class GearDefinitionProvider : IGearDefinitionProvider
{
    private readonly IReadOnlyDictionary<string, GearDefinition> _gear;

    public GearDefinitionProvider(string contentRootPath)
    {
        var path = Path.Combine(contentRootPath, "content", "gear.json");
        if (!File.Exists(path))
            throw new InvalidOperationException("gear.json not found — required for startup.");

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var list = JsonSerializer.Deserialize<List<GearDefinition>>(json, options)
            ?? throw new InvalidOperationException("gear.json deserialized to null.");

        // Startup validation: no duplicate IDs
        var seen = new HashSet<string>();
        foreach (var g in list)
        {
            if (!seen.Add(g.Id))
                throw new InvalidOperationException($"gear.json has duplicate id '{g.Id}'.");
        }

        _gear = list.ToDictionary(g => g.Id, g => g);

        // validate the Discernment quality-upgrade ladder (resolves +
        // strictly higher rarity). The gear-drop upgrade roll is wired with the deferred raid-threshold work.
        foreach (var g in list)
        {
            if (string.IsNullOrEmpty(g.UpgradesTo)) continue;
            if (!_gear.TryGetValue(g.UpgradesTo, out var target))
                throw new InvalidOperationException(
                    $"gear.json: '{g.Id}' upgradesTo '{g.UpgradesTo}' which does not exist.");
            if (target.Rarity <= g.Rarity)
                throw new InvalidOperationException(
                    $"gear.json: '{g.Id}' ({g.Rarity}) upgradesTo '{g.UpgradesTo}' ({target.Rarity}) must be strictly higher rarity.");
        }

        // Sets. Nothing reads SetId in combat yet (set bonuses are PHASE-2), so these catch the two
        // mistakes that would make a set impossible to WEAR long before any bonus depends on it:
        // a set that claims one slot twice can never be completed, and a set of mixed rarity is not
        // a set, it is a naming accident.
        foreach (var grp in list.Where(g => !string.IsNullOrWhiteSpace(g.SetId))
                                .GroupBy(g => g.SetId!, StringComparer.Ordinal))
        {
            var slots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var piece in grp)
                if (!slots.Add(piece.Slot))
                    throw new InvalidOperationException(
                        $"gear.json: set '{grp.Key}' has two pieces in the '{piece.Slot}' slot "
                        + $"('{piece.Id}' is the second) — the set could never be completed.");

            var rarities = grp.Select(p => p.Rarity).Distinct().ToList();
            if (rarities.Count > 1)
                throw new InvalidOperationException(
                    $"gear.json: set '{grp.Key}' mixes rarities ({string.Join(", ", rarities)}). "
                    + "A set is one tier; a mixed one is a naming accident.");
        }

        // The starter kit is granted and WORN at registration, one piece per slot; two pieces in a
        // slot would make the second grant fail on every new account.
        var starterSlots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var piece in list.Where(g => g.Starter))
            if (!starterSlots.Add(piece.Slot))
                throw new InvalidOperationException(
                    $"gear.json: two starter pieces claim the '{piece.Slot}' slot ('{piece.Id}' is the second).");
    }

    public GearDefinition? GetById(string id)
        => _gear.TryGetValue(id, out var g) ? g : null;

    public IReadOnlyList<GearDefinition> GetAll()
        => _gear.Values.ToList();

    public IReadOnlyList<GearDefinition> GetBySlot(string slot)
        => _gear.Values.Where(g => string.Equals(g.Slot, slot, StringComparison.OrdinalIgnoreCase)).ToList();
}
