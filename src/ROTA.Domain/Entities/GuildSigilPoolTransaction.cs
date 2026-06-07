using ROTA.Domain.Enums;

namespace ROTA.Domain.Entities;

// Append-only guild sigil-pool ledger (System 21 Slice 3a). One pool per guild; pool balance =
// SUM(amount) WHERE guild_id = X. Fed by member Donations (Slice 3a), drained by RaidSummon (Slice 3b).
// created_at only (NO updated_at / is_deleted). Idempotency is enforced by a unique partial index on
// (guild_id, transaction_type, reference_id) WHERE reference_id IS NOT NULL. The donor is encoded in the
// reference_id (guilddonate:{playerId}:{date}:{slot}) so no player_id column is needed here.
public class GuildSigilPoolTransaction
{
    // Required by EF Core
    private GuildSigilPoolTransaction() { }

    public static GuildSigilPoolTransaction Create(
        Guid guildId,
        int amount,
        GuildSigilPoolTransactionType transactionType,
        string? referenceId)
        => new GuildSigilPoolTransaction
        {
            Id              = Guid.NewGuid(),
            GuildId         = guildId,
            Amount          = amount,
            TransactionType = transactionType,
            ReferenceId     = referenceId,
            CreatedAt       = DateTimeOffset.UtcNow,
        };

    public Guid Id { get; private set; }
    public Guid GuildId { get; private set; }

    /// <summary>+credit / −debit.</summary>
    public int Amount { get; private set; }

    public GuildSigilPoolTransactionType TransactionType { get; private set; }

    /// <summary>Idempotency key; null when the row need not be deduplicated.</summary>
    public string? ReferenceId { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
}
