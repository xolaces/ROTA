using ROTA.Application.Models;

namespace ROTA.Application.Interfaces;

public interface ILegionDefinitionProvider
{
    LegionDefinition?              GetById(string id);
    IReadOnlyList<LegionDefinition> GetAll();
}
