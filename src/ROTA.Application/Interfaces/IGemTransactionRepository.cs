using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IGemTransactionRepository
{
    Task<int> GetBalanceAsync(Guid playerId, CancellationToken ct = default);

    Task<bool> ReferenceExistsAsync(Guid playerId, GemTransactionType type, string referenceId, CancellationToken ct = default);

    Task CreateAsync(GemTransaction transaction, CancellationToken ct = default);
}
