using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class PlayerMagicConfiguration : IEntityTypeConfiguration<PlayerMagic>
{
    public void Configure(EntityTypeBuilder<PlayerMagic> builder)
    {
        builder.ToTable("player_magics");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(e => e.MagicDefinitionId)
            .HasColumnName("magic_definition_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.Quantity)
            .HasColumnName("quantity")
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        // Unique ownership: one row per (player, magic).
        builder.HasIndex(e => new { e.PlayerId, e.MagicDefinitionId })
            .IsUnique()
            .HasDatabaseName("ix_player_magics_player_magic_def");

        builder.HasIndex(e => e.PlayerId)
            .HasDatabaseName("ix_player_magics_player_id");
    }
}
