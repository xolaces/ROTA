using ROTA.Application.Models;

namespace ROTA.Application.Interfaces;

public interface ILootTableProvider
{
    LootTableDefinition? GetById(string id);
}
