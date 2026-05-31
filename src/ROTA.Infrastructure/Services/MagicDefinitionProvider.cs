using System.Text.Json;
using System.Text.Json.Serialization;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;

namespace ROTA.Infrastructure.Services;

public sealed class MagicDefinitionProvider : IMagicDefinitionProvider
{
    private readonly IReadOnlyDictionary<string, MagicDefinition> _magics;

    public MagicDefinitionProvider(string contentRootPath)
    {
        var path = Path.Combine(contentRootPath, "content", "magics.json");
        if (!File.Exists(path))
        {
            _magics = new Dictionary<string, MagicDefinition>();
            return;
        }

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };
        var list = JsonSerializer.Deserialize<List<MagicDefinition>>(json, options)
            ?? throw new InvalidOperationException("magics.json deserialized to null.");

        Validate(list);

        _magics = list.ToDictionary(m => m.Id, m => m);
    }

    public MagicDefinition? GetById(string id)
        => _magics.TryGetValue(id, out var m) ? m : null;

    public IReadOnlyList<MagicDefinition> GetAll()
        => _magics.Values.ToList();

    private static void Validate(List<MagicDefinition> list)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var m in list)
        {
            if (!seen.Add(m.Id))
                throw new InvalidOperationException(
                    $"magics.json: duplicate id '{m.Id}'.");
            if (m.ProcChance < 0.0 || m.ProcChance > 1.0)
                throw new InvalidOperationException(
                    $"magics.json: '{m.Id}' has procChance {m.ProcChance} outside [0,1].");
            if (m.ProcAmount < 0.0)
                throw new InvalidOperationException(
                    $"magics.json: '{m.Id}' has negative procAmount {m.ProcAmount}.");
        }
    }
}
