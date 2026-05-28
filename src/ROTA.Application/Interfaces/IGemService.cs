using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IGemService
{
    Task<int> GetBalanceAsync(Guid playerId, CancellationToken ct = default);

    Task<bool> GrantGemsAsync(Guid playerId, int amount, GemTransactionType type, string? referenceId, CancellationToken ct = default);

    Task<bool> SpendGemsAsync(Guid playerId, int amount, GemTransactionType type, string? referenceId, CancellationToken ct = default);

    Task<bool> DailyRefillAsync(Guid playerId, CancellationToken ct = default);
}
