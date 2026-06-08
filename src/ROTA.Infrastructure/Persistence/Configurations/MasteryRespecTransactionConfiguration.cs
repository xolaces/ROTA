using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

// Append-only mastery re-spec ledger (System 22 Phase A, Slice 3). The unique (player_id, reference_id)
// index is the hard backstop for the period caps + idempotency (the referenceId encodes the period/scope).
public class MasteryRespecTransactionConfiguration : IEntityTypeConfiguration<MasteryRespecTransaction>
{
    public void Configure(EntityTypeBuilder<MasteryRespecTransaction> builder)
    {
        builder.ToTable("mastery_respec_transactions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(t => t.PlayerId).HasColumnName("player_id").IsRequired();
        builder.Property(t => t.Kind).HasColumnName("kind").IsRequired();
        builder.Property(t => t.FromAncient).HasColumnName("from_ancient");
        builder.Property(t => t.ToAncient).HasColumnName("to_ancient").IsRequired();
        builder.Property(t => t.GemCost).HasColumnName("gem_cost").HasDefaultValue(0);
        builder.Property(t => t.ReferenceId).HasColumnName("reference_id").HasMaxLength(200).IsRequired();
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(t => t.PlayerId)
            .HasDatabaseName("ix_mastery_respec_transactions_player_id");

        builder.HasIndex(t => new { t.PlayerId, t.ReferenceId })
            .IsUnique()
            .HasDatabaseName("ix_mastery_respec_transactions_idempotency");
    }
}
