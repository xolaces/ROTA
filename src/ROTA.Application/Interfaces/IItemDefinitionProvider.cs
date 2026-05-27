using ROTA.Application.Models;

namespace ROTA.Application.Interfaces;

public interface IItemDefinitionProvider
{
    ItemDefinition? GetById(string id);
    IReadOnlyList<ItemDefinition> GetAll();
}
