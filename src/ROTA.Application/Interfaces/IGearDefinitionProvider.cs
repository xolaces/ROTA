using ROTA.Application.Models;

namespace ROTA.Application.Interfaces;

public interface IGearDefinitionProvider
{
    GearDefinition? GetById(string id);
    IReadOnlyList<GearDefinition> GetAll();
    IReadOnlyList<GearDefinition> GetBySlot(string slot);
}
