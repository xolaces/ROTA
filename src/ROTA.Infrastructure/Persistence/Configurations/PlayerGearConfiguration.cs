using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class PlayerGearConfiguration : IEntityTypeConfiguration<PlayerGear>
{
    public void Configure(EntityTypeBuilder<PlayerGear> builder)
    {
        builder.ToTable("player_gear");

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(g => g.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(g => g.GearDefinitionId)
            .HasColumnName("gear_definition_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(g => g.Quantity)
            .HasColumnName("quantity")
            .HasDefaultValue(1);

        builder.Property(g => g.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(g => g.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(g => g.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        // One row per gear definition per player
        builder.HasIndex(g => new { g.PlayerId, g.GearDefinitionId })
            .IsUnique()
            .HasDatabaseName("ix_player_gear_player_gear");

        builder.HasIndex(g => g.PlayerId)
            .HasDatabaseName("ix_player_gear_player_id");
    }
}
