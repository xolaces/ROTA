using ROTA.Application.Models;

namespace ROTA.Application.Interfaces;

public interface IMagicDefinitionProvider
{
    MagicDefinition? GetById(string id);
    IReadOnlyList<MagicDefinition> GetAll();
}
