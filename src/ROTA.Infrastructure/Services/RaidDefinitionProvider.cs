using System.Text.Json;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;

namespace ROTA.Infrastructure.Services;

// BETA — raids.json is read once at application start and held in memory for the lifetime
//        of the process. Adding raids requires a redeploy.
public sealed class RaidDefinitionProvider : IRaidDefinitionProvider
{
    private readonly IReadOnlyDictionary<string, RaidDefinition> _raids;

    public RaidDefinitionProvider(string contentRootPath)
    {
        var path = Path.Combine(contentRootPath, "content", "raids.json");
        var json = File.ReadAllText(path);
        var list = JsonSerializer.Deserialize<List<RaidDefinition>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("raids.json deserialized to null.");
        _raids = list.ToDictionary(r => r.Id, r => r);
    }

    public IReadOnlyList<RaidDefinition> GetAll()
        => _raids.Values.ToList();

    public RaidDefinition? GetById(string id)
        => _raids.TryGetValue(id, out var r) ? r : null;
}
