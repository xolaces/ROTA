using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class PlayerLegionSlotConfiguration : IEntityTypeConfiguration<PlayerLegionSlot>
{
    public void Configure(EntityTypeBuilder<PlayerLegionSlot> builder)
    {
        builder.ToTable("player_legion_slots");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(e => e.LegionDefinitionId)
            .HasColumnName("legion_definition_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.SlotFamily)
            .HasColumnName("slot_family")
            .HasConversion<string>()
            .IsRequired();

        builder.Property(e => e.SlotIndex)
            .HasColumnName("slot_index")
            .IsRequired();

        builder.Property(e => e.UnitDefinitionId)
            .HasColumnName("unit_definition_id")
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

        // Unique slot per player/legion/family/index — one unit per slot.
        builder.HasIndex(e => new { e.PlayerId, e.LegionDefinitionId, e.SlotFamily, e.SlotIndex })
            .IsUnique()
            .HasDatabaseName("ix_player_legion_slots_player_legion_slot");

        builder.HasIndex(e => e.PlayerId)
            .HasDatabaseName("ix_player_legion_slots_player_id");
    }
}
