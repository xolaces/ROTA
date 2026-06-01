using System.Text.Json;
using System.Text.Json.Serialization;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Enums;

namespace ROTA.Infrastructure.Services;

public sealed class UnitDefinitionProvider : IUnitDefinitionProvider
{
    private readonly IReadOnlyDictionary<string, UnitDefinition> _units;

    public UnitDefinitionProvider(string contentRootPath)
    {
        var path = Path.Combine(contentRootPath, "content", "units.json");
        if (!File.Exists(path))
        {
            _units = new Dictionary<string, UnitDefinition>();
            return;
        }

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var list = JsonSerializer.Deserialize<List<UnitDefinition>>(json, options)
            ?? throw new InvalidOperationException("units.json deserialized to null.");

        Validate(list);

        _units = list.ToDictionary(u => u.Id, u => u);
    }

    public UnitDefinition? GetById(string id)
        => _units.TryGetValue(id, out var u) ? u : null;

    public IReadOnlyList<UnitDefinition> GetAll()
        => _units.Values.ToList();

    private static void Validate(List<UnitDefinition> list)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var u in list)
        {
            if (!seen.Add(u.Id))
                throw new InvalidOperationException(
                    $"units.json: duplicate id '{u.Id}'.");

            if (u.Ability is not null)
            {
                if (u.Ability.ProcChance < 0.0 || u.Ability.ProcChance > 1.0)
                    throw new InvalidOperationException(
                        $"units.json: '{u.Id}' ability.procChance {u.Ability.ProcChance} outside [0,1].");
                if (u.Ability.ProcAmount < 0.0)
                    throw new InvalidOperationException(
                        $"units.json: '{u.Id}' ability.procAmount {u.Ability.ProcAmount} is negative.");
            }

            // Troops must not carry legionBonus (it's a general-only field)
            if (u.UnitType == UnitType.Troop && u.LegionBonus != 0)
                throw new InvalidOperationException(
                    $"units.json: '{u.Id}' is a Troop but has legionBonus {u.LegionBonus} (must be 0).");
        }
    }
}
