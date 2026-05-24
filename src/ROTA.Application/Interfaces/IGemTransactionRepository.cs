using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IGemTransactionRepository
{
    /// <summary>Returns the current gem balance as SUM of all amounts — never a stored field.</summary>
    Task<int> GetBalanceAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>Returns true if a transaction with the given referenceId already exists for this player+type.</summary>
    Task<bool> ReferenceExistsAsync(Guid playerId, GemTransactionType type, string referenceId, CancellationToken ct = default);

    /// <summary>Appends a new transaction. The DB role has no UPDATE or DELETE on this table.</summary>
    Task CreateAsync(GemTransaction transaction, CancellationToken ct = default);
}
