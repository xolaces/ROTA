using ROTA.Domain.Enums;

namespace ROTA.Domain.Entities;

// BETA (System 16 Slice 2) — append-only Token + Pitchfork ledger (separate from gems).
// Mirrors GemTransaction: created_at only (NO updated_at / is_deleted). Balance per currency =
// SUM(amount) WHERE currency = X. Idempotency is enforced by a unique partial index on
// (player_id, currency, transaction_type, reference_id) WHERE reference_id IS NOT NULL.
public class GauntletCurrencyTransaction
{
    // Required by EF Core
    private GauntletCurrencyTransaction() { }

    public static GauntletCurrencyTransaction Create(
        Guid playerId,
        GauntletCurrency currency,
        int amount,
        GauntletCurrencyTransactionType transactionType,
        string? referenceId)
        => new GauntletCurrencyTransaction
        {
            Id              = Guid.NewGuid(),
            PlayerId        = playerId,
            Currency        = currency,
            Amount          = amount,
            TransactionType = transactionType,
            ReferenceId     = referenceId,
            CreatedAt       = DateTimeOffset.UtcNow,
        };

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }

    /// <summary>Token or Pitchfork — the balance discriminator.</summary>
    public GauntletCurrency Currency { get; private set; }

    /// <summary>+credit / −debit.</summary>
    public int Amount { get; private set; }

    public GauntletCurrencyTransactionType TransactionType { get; private set; }

    /// <summary>Idempotency key; null when the row need not be deduplicated.</summary>
    public string? ReferenceId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
