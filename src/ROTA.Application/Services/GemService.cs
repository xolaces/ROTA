using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Services;

// Audit fix (was: "A concurrent double-spend is possible under high contention. Phase 2: advisory
// lock"): spends now run through IGemTransactionRepository.TrySpendAsync — idempotency + balance +
// debit under a per-player advisory lock — so concurrent spends serialize and the SUM balance can
// never go negative. Grants use TryCreateAsync so a concurrent duplicate reference returns false
// (already granted) instead of throwing on the unique index.
public sealed class GemService : IGemService
{
    private const int DailyRefillAmount = 5;

    private readonly IGemTransactionRepository _transactions;
    private readonly IAuditLogRepository _auditLog;

    public GemService(IGemTransactionRepository transactions, IAuditLogRepository auditLog)
    {
        _transactions = transactions;
        _auditLog = auditLog;
    }

    public Task<long> GetBalanceAsync(Guid playerId, CancellationToken ct = default)
        => _transactions.GetBalanceAsync(playerId, ct);

    public async Task<bool> GrantGemsAsync(
        Guid playerId, long amount, GemTransactionType type, string? referenceId,
        CancellationToken ct = default)
    {
        if (referenceId is not null
            && await _transactions.ReferenceExistsAsync(playerId, type, referenceId, ct))
            return false;

        // TryCreateAsync: a concurrent duplicate that slipped past the pre-check above hits the
        // unique index and reports "already granted" instead of throwing.
        if (!await _transactions.TryCreateAsync(GemTransaction.Create(playerId, amount, type, referenceId), ct))
            return false;

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, $"GemGrant:{type}", null,
            $"Granted {amount} gems (ref={referenceId})", null), ct);

        return true;
    }

    public async Task<GemSpendOutcome> SpendGemsAsync(
        Guid playerId, long amount, GemTransactionType type, string? referenceId,
        CancellationToken ct = default)
    {
        // Atomic in the repository: idempotency (AlreadyProcessed = the original charge committed;
        // caller proceeds with its idempotent grant step — the lost-purchase recovery), balance
        // check, and the −amount insert all run under a per-player advisory lock.
        var outcome = await _transactions.TrySpendAsync(playerId, amount, type, referenceId, ct);

        if (outcome == GemSpendOutcome.Charged)
            await _auditLog.AppendAsync(AuditLog.Create(
                playerId, $"GemSpend:{type}", null,
                $"Spent {amount} gems (ref={referenceId})", null), ct);

        return outcome;
    }

    public Task<bool> DailyRefillAsync(Guid playerId, CancellationToken ct = default)
    {
        var referenceId = $"daily:{DateTimeOffset.UtcNow:yyyy-MM-dd}";
        return GrantGemsAsync(playerId, DailyRefillAmount, GemTransactionType.DailyReward, referenceId, ct);
    }
}
