using ROTA.Domain.Enums;

namespace ROTA.Domain.Entities;

// Append-only per-player guild-currency ledger (System 21 Slice 3a). Mirrors GauntletCurrencyTransaction:
// created_at only (NO updated_at / is_deleted). Balance per currency = SUM(amount) WHERE currency = X.
// Idempotency is enforced by a unique partial index on
// (player_id, currency, transaction_type, reference_id) WHERE reference_id IS NOT NULL.
public class GuildCurrencyTransaction
{
    // Required by EF Core
    private GuildCurrencyTransaction() { }

    public static GuildCurrencyTransaction Create(
        Guid playerId,
        GuildCurrency currency,
        int amount,
        GuildCurrencyTransactionType transactionType,
        string? referenceId)
        => new GuildCurrencyTransaction
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

    /// <summary>Sigil or ShopTicket — the balance discriminator.</summary>
    public GuildCurrency Currency { get; private set; }

    /// <summary>+credit / −debit.</summary>
    public int Amount { get; private set; }

    public GuildCurrencyTransactionType TransactionType { get; private set; }

    /// <summary>Idempotency key; null when the row need not be deduplicated.</summary>
    public string? ReferenceId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
