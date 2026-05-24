using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IQuestProgressRepository
{
    Task<IReadOnlyList<PlayerQuestProgress>> GetAllForPlayerAsync(Guid playerId, CancellationToken ct = default);
    Task<PlayerQuestProgress?> GetAsync(Guid playerId, string questId, CancellationToken ct = default);
    Task CreateAsync(PlayerQuestProgress progress, CancellationToken ct = default);
    Task UpdateAsync(PlayerQuestProgress progress, CancellationToken ct = default);
}
