using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IQuestDifficultyProgressRepository
{
    Task<PlayerQuestDifficultyProgress?> GetAsync(
        Guid playerId, string questId, QuestDifficulty difficulty, CancellationToken ct = default);

    /// <summary>All difficulty-progress rows for the player (T74 — availability unlock hints).</summary>
    Task<IReadOnlyList<PlayerQuestDifficultyProgress>> GetAllForPlayerAsync(
        Guid playerId, CancellationToken ct = default);

    Task CreateAsync(PlayerQuestDifficultyProgress progress, CancellationToken ct = default);
    Task UpdateAsync(PlayerQuestDifficultyProgress progress, CancellationToken ct = default);
}
