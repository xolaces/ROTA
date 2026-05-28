using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class GemTransactionConfiguration : IEntityTypeConfiguration<GemTransaction>
{
    public void Configure(EntityTypeBuilder<GemTransaction> builder)
    {
        builder.ToTable("gem_transactions");

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

        // FK index
        builder.HasIndex(t => t.PlayerId)
            .HasDatabaseName("ix_gem_transactions_player_id");

        // Unique index on (player_id, transaction_type, reference_id) prevents duplicate grants.
        // PostgreSQL allows multiple NULLs in unique indexes — nullable referenceIds are not constrained,
        // which is correct for admin grants that don't require idempotency.
        builder.HasIndex(t => new { t.PlayerId, t.TransactionType, t.ReferenceId })
            .IsUnique()
            .HasDatabaseName("ix_gem_transactions_idempotency")
            .HasFilter("reference_id IS NOT NULL");
    }
}
