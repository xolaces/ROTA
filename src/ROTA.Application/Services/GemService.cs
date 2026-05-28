using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Services;

//        A concurrent double-spend is possible under high contention. Phase 2: advisory lock.
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

    public Task<int> GetBalanceAsync(Guid playerId, CancellationToken ct = default)
        => _transactions.GetBalanceAsync(playerId, ct);

    public async Task<bool> GrantGemsAsync(
        Guid playerId, int amount, GemTransactionType type, string? referenceId,
        CancellationToken ct = default)
    {
        if (referenceId is not null
            && await _transactions.ReferenceExistsAsync(playerId, type, referenceId, ct))
            return false;

        await _transactions.CreateAsync(GemTransaction.Create(playerId, amount, type, referenceId), ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, $"GemGrant:{type}", null,
            $"Granted {amount} gems (ref={referenceId})", null), ct);

        return true;
    }

    public async Task<bool> SpendGemsAsync(
        Guid playerId, int amount, GemTransactionType type, string? referenceId,
        CancellationToken ct = default)
    {
        if (referenceId is not null
            && await _transactions.ReferenceExistsAsync(playerId, type, referenceId, ct))
            return false;

        var balance = await _transactions.GetBalanceAsync(playerId, ct);
        if (balance < amount) return false;

        await _transactions.CreateAsync(GemTransaction.Create(playerId, -amount, type, referenceId), ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, $"GemSpend:{type}", null,
            $"Spent {amount} gems (ref={referenceId})", null), ct);

        return true;
    }

    public Task<bool> DailyRefillAsync(Guid playerId, CancellationToken ct = default)
    {
        var referenceId = $"daily:{DateTimeOffset.UtcNow:yyyy-MM-dd}";
        return GrantGemsAsync(playerId, DailyRefillAmount, GemTransactionType.DailyReward, referenceId, ct);
    }
}
