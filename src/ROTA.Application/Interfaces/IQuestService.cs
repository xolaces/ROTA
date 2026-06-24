using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IQuestService
{
    // The depletion/clear/unlock state is per-difficulty (triage node-depletion-per-difficulty), so
    // the availability view reflects ONE difficulty's progress (default Normal for back-compat).
    Task<IReadOnlyList<QuestAvailabilityResponse>> GetAvailableQuestsAsync(
        Guid playerId, QuestDifficulty difficulty = QuestDifficulty.Normal, CancellationToken ct = default);

    Task<QuestResultResponse> AttemptQuestAsync(
        Guid playerId, string questId, QuestDifficulty difficulty, CancellationToken ct = default);
}
