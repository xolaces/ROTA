using Microsoft.EntityFrameworkCore;
using Npgsql;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

// BETA (System 16 Slice 2) — append-only Strike ledger. Mirrors GemTransactionRepository +
// GemService.SpendGemsAsync, with the atomic-spend discipline of BetaKeyRepository.
public sealed class StrikeRepository : IStrikeRepository
{
    private readonly RotaDbContext _db;

    public StrikeRepository(RotaDbContext db) => _db = db;

    public async Task<long> GetBalanceAsync(Guid playerId, CancellationToken ct = default)
        => await _db.StrikeTransactions
            .Where(t => t.PlayerId == playerId)
            .SumAsync(t => (long)t.Amount, ct);

    public async Task CreateAsync(StrikeTransaction transaction, CancellationToken ct = default)
    {
        _db.StrikeTransactions.Add(transaction);
        await _db.SaveChangesAsync(ct);
    }

    public Task<bool> ReferenceExistsAsync(
        Guid playerId, StrikeTransactionType type, string referenceId, CancellationToken ct = default)
        => _db.StrikeTransactions.AnyAsync(
            t => t.PlayerId == playerId
              && t.TransactionType == type
              && t.ReferenceId == referenceId, ct);

    public async Task<StrikeSpendOutcome> SpendAsync(
        Guid playerId, int amount, string referenceId, CancellationToken ct = default)
    {
        // Idempotency-first (mirrors GemService.SpendGemsAsync): if the HitSpend reference already
        // exists the original debit committed — return AlreadyCharged, never re-debit, never confuse
        // with Insufficient.
        if (await ReferenceExistsAsync(playerId, StrikeTransactionType.HitSpend, referenceId, ct))
            return StrikeSpendOutcome.AlreadyCharged;

        var balance = await GetBalanceAsync(playerId, ct);
        if (balance < amount)
            return StrikeSpendOutcome.Insufficient;

        // Insert the −debit row. The unique partial index on
        // (player_id, transaction_type, reference_id) is the hard backstop: if a concurrent caller
        // inserted the same reference between our check and this write, the insert throws a
        // unique-violation, which we translate to AlreadyCharged (idempotent) rather than surfacing.
        try
        {
            _db.StrikeTransactions.Add(StrikeTransaction.Create(
                playerId, -amount, StrikeTransactionType.HitSpend, referenceId));
            await _db.SaveChangesAsync(ct);
            return StrikeSpendOutcome.Charged;
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            // Detach the failed insert so the context stays usable.
            _db.ChangeTracker.Clear();
            return StrikeSpendOutcome.AlreadyCharged;
        }
    }

    private static bool IsUniqueViolation(DbUpdateException ex)
        => ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation };
}
