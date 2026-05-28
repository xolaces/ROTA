using System.Text.Json;
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
        var list = JsonSerializer.Deserialize<List<ItemDefinition>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("items.json deserialized to null.");
        _items = list.ToDictionary(i => i.Id, i => i);
    }

    public ItemDefinition? GetById(string id)
        => _items.TryGetValue(id, out var i) ? i : null;

    public IReadOnlyList<ItemDefinition> GetAll()
        => _items.Values.ToList();
}
