using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class GemTransactionRepository : IGemTransactionRepository
{
    private readonly RotaDbContext _db;

    public GemTransactionRepository(RotaDbContext db)
    {
        _db = db;
    }

    public async Task<int> GetBalanceAsync(Guid playerId, CancellationToken ct = default)
        => await _db.GemTransactions
            .Where(t => t.PlayerId == playerId)
            .SumAsync(t => t.Amount, ct);

    public async Task<bool> ReferenceExistsAsync(
        Guid playerId, GemTransactionType type, string referenceId,
        CancellationToken ct = default)
        => await _db.GemTransactions
            .AnyAsync(t => t.PlayerId == playerId
                        && t.TransactionType == type
                        && t.ReferenceId == referenceId, ct);

    public async Task CreateAsync(GemTransaction transaction, CancellationToken ct = default)
    {
        _db.GemTransactions.Add(transaction);
        await _db.SaveChangesAsync(ct);
    }
}
