using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IQuestService
{
    /// <summary>
    /// Returns all quests the player has unlocked (prerequisite met or null).
    /// Includes the player's completion count per quest.
    /// </summary>
    Task<IReadOnlyList<QuestAvailabilityResponse>> GetAvailableQuestsAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Attempts a quest: spends energy, grants rewards, records progress.
    /// Energy is spent before rewards are granted — if energy is insufficient, no side effects occur.
    /// </summary>
    Task<QuestResultResponse> AttemptQuestAsync(Guid playerId, string questId, CancellationToken ct = default);
}
