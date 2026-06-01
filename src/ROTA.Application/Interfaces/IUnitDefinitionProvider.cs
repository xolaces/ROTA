using ROTA.Application.Models;

namespace ROTA.Application.Interfaces;

public interface IUnitDefinitionProvider
{
    UnitDefinition?              GetById(string id);
    IReadOnlyList<UnitDefinition> GetAll();
}
