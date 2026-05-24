using ROTA.Application.Models;

namespace ROTA.Application.Interfaces;

/// <summary>
/// Provides access to the static quest definitions loaded from quests.json at startup.
/// Registered as singleton — file is read once at application start.
/// </summary>
public interface IQuestDefinitionProvider
{
    IReadOnlyList<QuestDefinition> GetAll();
    QuestDefinition? GetById(string id);
}
