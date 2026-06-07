using ROTA.Domain.Enums;

namespace ROTA.Domain.Entities;

// BETA (System 16 Slice 2) — append-only Strike ledger. Mirrors GemTransaction exactly:
// created_at only (NO updated_at / is_deleted). Balance = SUM(amount). Amount is +credit
// (RaidDefeat/GemPurchase/SpecialRaidDrop) or −debit (HitSpend). Idempotency is enforced by a
// unique partial index on (player_id, transaction_type, reference_id) WHERE reference_id IS NOT NULL.
// Strikes carry over across events and are never reset.
public class StrikeTransaction
{
    // Required by EF Core
    private StrikeTransaction() { }

    public static StrikeTransaction Create(
        Guid playerId,
        int amount,
        StrikeTransactionType transactionType,
        string? referenceId)
        => new StrikeTransaction
        {
            Id              = Guid.NewGuid(),
            PlayerId        = playerId,
            Amount          = amount,
            TransactionType = transactionType,
            ReferenceId     = referenceId,
            CreatedAt       = DateTimeOffset.UtcNow,
        };

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }

    /// <summary>+credit / −debit.</summary>
    public int Amount { get; private set; }

    public StrikeTransactionType TransactionType { get; private set; }

    /// <summary>Idempotency key; null when the row need not be deduplicated.</summary>
    public string? ReferenceId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
