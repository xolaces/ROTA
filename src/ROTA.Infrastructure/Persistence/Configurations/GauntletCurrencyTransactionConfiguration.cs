using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

// Append-only Token + Pitchfork ledger — mirrors GemTransactionConfiguration but with a Currency
// discriminator folded into the idempotency key (so the same referenceId can credit Token AND
// Pitchfork in one settlement without colliding).
public class GauntletCurrencyTransactionConfiguration : IEntityTypeConfiguration<GauntletCurrencyTransaction>
{
    public void Configure(EntityTypeBuilder<GauntletCurrencyTransaction> builder)
    {
        builder.ToTable("gauntlet_currency_transactions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(t => t.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        // Enum stored as int — always set by Create (Token=0). No default/sentinel.
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
            .HasDatabaseName("ix_gauntlet_currency_transactions_player_id");

        // Idempotency: unique on (player_id, currency, transaction_type, reference_id)
        // WHERE reference_id IS NOT NULL.
        builder.HasIndex(t => new { t.PlayerId, t.Currency, t.TransactionType, t.ReferenceId })
            .IsUnique()
            .HasDatabaseName("ix_gauntlet_currency_transactions_idempotency")
            .HasFilter("reference_id IS NOT NULL");
    }
}
