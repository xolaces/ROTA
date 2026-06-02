using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class PlayerCommanderGearConfiguration : IEntityTypeConfiguration<PlayerCommanderGear>
{
    public void Configure(EntityTypeBuilder<PlayerCommanderGear> builder)
    {
        builder.ToTable("player_commander_gear");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(e => e.GearDefinitionId)
            .HasColumnName("gear_definition_id")
            .HasMaxLength(100)
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

        // One active commander row per player (upsert in place).
        builder.HasIndex(e => e.PlayerId)
            .IsUnique()
            .HasDatabaseName("ix_player_commander_gear_player_id");
    }
}
