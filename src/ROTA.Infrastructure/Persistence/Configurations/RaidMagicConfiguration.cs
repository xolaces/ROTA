using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class RaidMagicConfiguration : IEntityTypeConfiguration<RaidMagic>
{
    public void Configure(EntityTypeBuilder<RaidMagic> builder)
    {
        builder.ToTable("raid_magics");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.ActiveRaidId)
            .HasColumnName("active_raid_id")
            .IsRequired();

        builder.Property(e => e.MagicDefinitionId)
            .HasColumnName("magic_definition_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.AppliedByPlayerId)
            .HasColumnName("applied_by_player_id")
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

        // No duplicate magic per raid.
        builder.HasIndex(e => new { e.ActiveRaidId, e.MagicDefinitionId })
            .IsUnique()
            .HasDatabaseName("ix_raid_magics_raid_magic_def");

        // FK index on active_raid_id for fast lookups.
        builder.HasIndex(e => e.ActiveRaidId)
            .HasDatabaseName("ix_raid_magics_active_raid_id");

        builder.HasIndex(e => e.AppliedByPlayerId)
            .HasDatabaseName("ix_raid_magics_applied_by_player_id");
    }
}
