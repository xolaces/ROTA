using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IQuestDifficultyProgressRepository
{
    Task<PlayerQuestDifficultyProgress?> GetAsync(
        Guid playerId, string questId, QuestDifficulty difficulty, CancellationToken ct = default);

    Task CreateAsync(PlayerQuestDifficultyProgress progress, CancellationToken ct = default);
    Task UpdateAsync(PlayerQuestDifficultyProgress progress, CancellationToken ct = default);
}
