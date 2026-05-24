using System.Text.Json;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;

namespace ROTA.Infrastructure.Services;

// BETA — quests.json is read once at application start and held in memory for the lifetime
//        of the process. Adding quests requires a redeploy.
public sealed class QuestDefinitionProvider : IQuestDefinitionProvider
{
    private readonly IReadOnlyDictionary<string, QuestDefinition> _quests;

    public QuestDefinitionProvider(string contentRootPath)
    {
        var path = Path.Combine(contentRootPath, "content", "quests.json");
        var json = File.ReadAllText(path);
        var list = JsonSerializer.Deserialize<List<QuestDefinition>>(json,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true })
            ?? throw new InvalidOperationException("quests.json deserialized to null.");
        _quests = list.ToDictionary(q => q.Id, q => q);
    }

    public IReadOnlyList<QuestDefinition> GetAll()
        => _quests.Values.ToList();

    public QuestDefinition? GetById(string id)
        => _quests.TryGetValue(id, out var q) ? q : null;
}
