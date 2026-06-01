using System.Text.Json;
using System.Text.Json.Serialization;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Enums;

namespace ROTA.Infrastructure.Services;

public sealed class LegionDefinitionProvider : ILegionDefinitionProvider
{
    private readonly IReadOnlyDictionary<string, LegionDefinition> _legions;

    public LegionDefinitionProvider(string contentRootPath)
    {
        var path = Path.Combine(contentRootPath, "content", "legions.json");
        if (!File.Exists(path))
        {
            _legions = new Dictionary<string, LegionDefinition>();
            return;
        }

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var list = JsonSerializer.Deserialize<List<LegionDefinition>>(json, options)
            ?? throw new InvalidOperationException("legions.json deserialized to null.");

        Validate(list);

        _legions = list.ToDictionary(l => l.Id, l => l);
    }

    public LegionDefinition? GetById(string id)
        => _legions.TryGetValue(id, out var l) ? l : null;

    public IReadOnlyList<LegionDefinition> GetAll()
        => _legions.Values.ToList();

    private static void Validate(List<LegionDefinition> list)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var l in list)
        {
            if (!seen.Add(l.Id))
                throw new InvalidOperationException(
                    $"legions.json: duplicate id '{l.Id}'.");

            // Validate slot constraint values parse to the correct enum type
            foreach (var (slots, family) in new[] {
                (l.GeneralSlots, "generalSlots"),
                (l.TroopSlots,   "troopSlots") })
            {
                foreach (var slot in slots)
                {
                    if (slot.ConstraintType == SlotConstraintType.None) continue;
                    if (slot.ConstraintValue is null)
                        throw new InvalidOperationException(
                            $"legions.json: '{l.Id}' {family} slot constraintType={slot.ConstraintType} requires constraintValue.");

                    bool valid = slot.ConstraintType switch
                    {
                        SlotConstraintType.Race      => Enum.TryParse<UnitRace>(slot.ConstraintValue, out _),
                        SlotConstraintType.Role      => Enum.TryParse<UnitRole>(slot.ConstraintValue, out _),
                        SlotConstraintType.Attribute => Enum.TryParse<UnitAttribute>(slot.ConstraintValue, out _),
                        _                            => false,
                    };
                    if (!valid)
                        throw new InvalidOperationException(
                            $"legions.json: '{l.Id}' {family} slot constraintValue '{slot.ConstraintValue}' " +
                            $"is not a valid {slot.ConstraintType} enum member.");
                }
            }
        }
    }
}
