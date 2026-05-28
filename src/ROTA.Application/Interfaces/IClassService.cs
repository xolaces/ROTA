using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IClassService
{
    ClassRegenRates GetRegenRates(PlayerClass playerClass);
    IReadOnlyList<PlayerClass> GetAvailableChoices(int level, PlayerClass current);
    Task<ClassRegenRates?> AssignClassAsync(Guid playerId, PlayerClass requested, CancellationToken ct = default);
    PlayerClass ComputeAutoAdvance(int level, PlayerClass current);
    bool IsConvergedClass(PlayerClass playerClass);
    Task<ClassRegenRates?> GetClassInfoAsync(Guid playerId, CancellationToken ct = default);
}
