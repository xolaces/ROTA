using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IQuestProgressRepository
{
    // Returns ALL difficulty rows for the player (callers filter by Difficulty as needed).
    Task<IReadOnlyList<PlayerQuestProgress>> GetAllForPlayerAsync(Guid playerId, CancellationToken ct = default);
    // The depletion track is per-difficulty (triage node-depletion-per-difficulty), so a read is
    // scoped to one difficulty's row.
    Task<PlayerQuestProgress?> GetAsync(Guid playerId, string questId, QuestDifficulty difficulty, CancellationToken ct = default);
    Task CreateAsync(PlayerQuestProgress progress, CancellationToken ct = default);
    Task UpdateAsync(PlayerQuestProgress progress, CancellationToken ct = default);
}
