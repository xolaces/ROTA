using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class PlayerMagicHonorConfiguration : IEntityTypeConfiguration<PlayerMagicHonor>
{
    public void Configure(EntityTypeBuilder<PlayerMagicHonor> builder)
    {
        builder.ToTable("player_magic_honors");

        builder.HasKey(h => h.Id);
        builder.Property(h => h.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(h => h.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(h => h.MagicDefinitionId)
            .HasColumnName("magic_definition_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(h => h.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(h => h.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(h => h.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(h => h.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(h => h.PlayerId)
            .HasDatabaseName("ix_player_magic_honors_player_id");

        builder.HasIndex(h => new { h.PlayerId, h.MagicDefinitionId })
            .IsUnique()
            .HasDatabaseName("ix_player_magic_honors_player_magic");
    }
}
