using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class BetaKeyConfiguration : IEntityTypeConfiguration<BetaKey>
{
    public void Configure(EntityTypeBuilder<BetaKey> builder)
    {
        builder.ToTable("beta_keys");

        builder.HasKey(k => k.Id);
        builder.Property(k => k.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(k => k.Key)
            .HasColumnName("key")
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(k => k.CreatedByPlayerId)
            .HasColumnName("created_by_player_id");

        builder.Property(k => k.IsRedeemed)
            .HasColumnName("is_redeemed")
            .HasDefaultValue(false);

        builder.Property(k => k.RedeemedByPlayerId)
            .HasColumnName("redeemed_by_player_id");

        builder.Property(k => k.RedeemedAt)
            .HasColumnName("redeemed_at");

        builder.Property(k => k.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(k => k.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(k => k.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        // Unique constraint on the key string — prevents duplicate key generation
        builder.HasIndex(k => k.Key)
            .IsUnique()
            .HasDatabaseName("ix_beta_keys_key");

        // Index for querying which player redeemed a given key
        builder.HasIndex(k => k.RedeemedByPlayerId)
            .HasDatabaseName("ix_beta_keys_redeemed_by_player_id");
    }
}
