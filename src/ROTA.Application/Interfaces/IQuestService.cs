using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IQuestService
{
    Task<IReadOnlyList<QuestAvailabilityResponse>> GetAvailableQuestsAsync(Guid playerId, CancellationToken ct = default);

    Task<QuestResultResponse> AttemptQuestAsync(
        Guid playerId, string questId, QuestDifficulty difficulty, CancellationToken ct = default);
}
