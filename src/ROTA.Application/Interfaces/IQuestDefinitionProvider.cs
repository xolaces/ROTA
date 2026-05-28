using ROTA.Application.Models;

namespace ROTA.Application.Interfaces;

public interface IQuestDefinitionProvider
{
    IReadOnlyList<QuestDefinition> GetAll();
    QuestDefinition? GetById(string id);
}
