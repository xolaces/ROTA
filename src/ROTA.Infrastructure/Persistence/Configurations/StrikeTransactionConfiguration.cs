using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

// Append-only Strike ledger — mirrors GemTransactionConfiguration (created_at only; no
// updated_at / is_deleted; unique partial idempotency index).
public class StrikeTransactionConfiguration : IEntityTypeConfiguration<StrikeTransaction>
{
    public void Configure(EntityTypeBuilder<StrikeTransaction> builder)
    {
        builder.ToTable("strike_transactions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(t => t.PlayerId)
            .HasColumnName("player_id")
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
            .HasDatabaseName("ix_strike_transactions_player_id");

        // Idempotency: unique on (player_id, transaction_type, reference_id) WHERE reference_id IS NOT NULL.
        // PostgreSQL allows multiple NULLs, so non-idempotent rows (referenceId null) are unconstrained.
        builder.HasIndex(t => new { t.PlayerId, t.TransactionType, t.ReferenceId })
            .IsUnique()
            .HasDatabaseName("ix_strike_transactions_idempotency")
            .HasFilter("reference_id IS NOT NULL");
    }
}
