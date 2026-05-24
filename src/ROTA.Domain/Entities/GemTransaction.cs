using ROTA.Domain.Enums;

namespace ROTA.Domain.Entities;

/// <summary>
/// Append-only ledger row for the gem economy.
/// Positive Amount = credit; negative Amount = debit.
/// Balance is always computed as SUM(amount) — never stored as a field.
/// The DB role has no UPDATE or DELETE permission on this table.
/// </summary>
public class GemTransaction
{
    // Required by EF Core
    private GemTransaction() { }

    public static GemTransaction Create(
        Guid playerId,
        int amount,
        GemTransactionType transactionType,
        string? referenceId)
        => new GemTransaction
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            Amount = amount,
            TransactionType = transactionType,
            ReferenceId = referenceId,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }

    /// <summary>Positive = credit, negative = debit.</summary>
    public int Amount { get; private set; }

    public GemTransactionType TransactionType { get; private set; }

    /// <summary>
    /// Idempotency key — unique per (PlayerId, TransactionType).
    /// Prevents duplicate grants (e.g. daily reward claimed twice).
    /// Nullable: some admin grants may not require idempotency.
    /// </summary>
    public string? ReferenceId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
