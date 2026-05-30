using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class PlayerEquipmentConfiguration : IEntityTypeConfiguration<PlayerEquipment>
{
    public void Configure(EntityTypeBuilder<PlayerEquipment> builder)
    {
        builder.ToTable("player_equipment");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(e => e.Slot)
            .HasColumnName("slot")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.GearDefinitionId)
            .HasColumnName("gear_definition_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.EquippedAt)
            .HasColumnName("equipped_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        // One row per (player, slot) — equip upserts into this row; unequip soft-deletes it.
        builder.HasIndex(e => new { e.PlayerId, e.Slot })
            .IsUnique()
            .HasDatabaseName("ix_player_equipment_player_slot");

        builder.HasIndex(e => e.PlayerId)
            .HasDatabaseName("ix_player_equipment_player_id");
    }
}
