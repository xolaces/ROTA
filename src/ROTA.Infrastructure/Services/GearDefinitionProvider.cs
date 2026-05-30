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
    }

    public GearDefinition? GetById(string id)
        => _gear.TryGetValue(id, out var g) ? g : null;

    public IReadOnlyList<GearDefinition> GetAll()
        => _gear.Values.ToList();

    public IReadOnlyList<GearDefinition> GetBySlot(string slot)
        => _gear.Values.Where(g => string.Equals(g.Slot, slot, StringComparison.OrdinalIgnoreCase)).ToList();
}
