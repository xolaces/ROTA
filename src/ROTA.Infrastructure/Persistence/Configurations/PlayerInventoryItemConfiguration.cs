using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class PlayerInventoryItemConfiguration : IEntityTypeConfiguration<PlayerInventoryItem>
{
    public void Configure(EntityTypeBuilder<PlayerInventoryItem> builder)
    {
        builder.ToTable("player_inventory_items");

        builder.HasKey(i => i.Id);
        builder.Property(i => i.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(i => i.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(i => i.ItemDefinitionId)
            .HasColumnName("item_definition_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(i => i.Quantity)
            .HasColumnName("quantity")
            .HasDefaultValue(1);

        builder.Property(i => i.AcquiredAt)
            .HasColumnName("acquired_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(i => i.IsUsed)
            .HasColumnName("is_used")
            .HasDefaultValue(false);

        builder.Property(i => i.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(i => i.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        // One row per item type per player
        builder.HasIndex(i => new { i.PlayerId, i.ItemDefinitionId })
            .IsUnique()
            .HasDatabaseName("ix_player_inventory_items_player_item");

        builder.HasIndex(i => i.PlayerId)
            .HasDatabaseName("ix_player_inventory_items_player_id");
    }
}
