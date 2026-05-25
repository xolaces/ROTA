using ROTA.Application.Models;

namespace ROTA.Application.Interfaces;

public interface IRaidDefinitionProvider
{
    IReadOnlyList<RaidDefinition> GetAll();
    RaidDefinition? GetById(string id);
}
