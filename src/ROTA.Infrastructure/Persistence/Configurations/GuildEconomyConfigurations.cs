using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

// Append-only per-player guild-currency ledger (System 21 Slice 3a) — mirrors
// GauntletCurrencyTransactionConfiguration. The currency is folded into the idempotency key so a single
// referenceId (a buy) can debit ShopTicket AND credit Sigil without colliding. Like the other ledgers
// (gems, gauntlet currency, strikes), player_id is an indexed column with no declared FK constraint.
public class GuildCurrencyTransactionConfiguration : IEntityTypeConfiguration<GuildCurrencyTransaction>
{
    public void Configure(EntityTypeBuilder<GuildCurrencyTransaction> builder)
    {
        builder.ToTable("guild_currency_transactions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(t => t.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        // Always set by Create (Sigil=0). No default/sentinel.
        builder.Property(t => t.Currency)
            .HasColumnName("currency")
            .IsRequired();

        builder.Property(t => t.Amount)
            .HasColumnName("amount")
            .IsRequired();

        builder.Property(t => t.TransactionType)
            .HasColumnName("transaction_type")
            .IsRequired();

        builder.Property(t => t.ReferenceId)
            .HasColumnName("reference_id")
            .HasMaxLength(200);

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        // FK index on player_id (architecture rule).
        builder.HasIndex(t => t.PlayerId)
            .HasDatabaseName("ix_guild_currency_transactions_player_id");

        builder.HasIndex(t => new { t.PlayerId, t.Currency, t.TransactionType, t.ReferenceId })
            .IsUnique()
            .HasDatabaseName("ix_guild_currency_transactions_idempotency")
            .HasFilter("reference_id IS NOT NULL");
    }
}

// Append-only guild sigil-pool ledger (System 21 Slice 3a). Pool balance per guild = SUM(amount).
// Donations credit it (Slice 3a); raid summons debit it (Slice 3b). Idempotency on
// (guild_id, transaction_type, reference_id) WHERE reference_id IS NOT NULL — the donor is encoded in
// the reference_id, so concurrent donors never collide.
public class GuildSigilPoolTransactionConfiguration : IEntityTypeConfiguration<GuildSigilPoolTransaction>
{
    public void Configure(EntityTypeBuilder<GuildSigilPoolTransaction> builder)
    {
        builder.ToTable("guild_sigil_pool_transactions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(t => t.GuildId)
            .HasColumnName("guild_id")
            .IsRequired();

        builder.Property(t => t.Amount)
            .HasColumnName("amount")
            .IsRequired();

        builder.Property(t => t.TransactionType)
            .HasColumnName("transaction_type")
            .IsRequired();

        builder.Property(t => t.ReferenceId)
            .HasColumnName("reference_id")
            .HasMaxLength(200);

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        // FK index on guild_id (architecture rule).
        builder.HasIndex(t => t.GuildId)
            .HasDatabaseName("ix_guild_sigil_pool_transactions_guild_id");

        builder.HasIndex(t => new { t.GuildId, t.TransactionType, t.ReferenceId })
            .IsUnique()
            .HasDatabaseName("ix_guild_sigil_pool_transactions_idempotency")
            .HasFilter("reference_id IS NOT NULL");
    }
}
