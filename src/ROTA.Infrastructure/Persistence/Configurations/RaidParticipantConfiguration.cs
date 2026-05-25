using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class RaidParticipantConfiguration : IEntityTypeConfiguration<RaidParticipant>
{
    public void Configure(EntityTypeBuilder<RaidParticipant> builder)
    {
        builder.ToTable("raid_participants");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(p => p.ActiveRaidId)
            .HasColumnName("active_raid_id")
            .IsRequired();

        builder.Property(p => p.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(p => p.TotalDamageDealt)
            .HasColumnName("total_damage_dealt")
            .HasDefaultValue(0L);

        builder.Property(p => p.HitCount)
            .HasColumnName("hit_count")
            .HasDefaultValue(0);

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(p => p.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        // Cascade delete: removing a raid removes all its participants
        builder.HasOne<ActiveRaid>()
            .WithMany()
            .HasForeignKey(p => p.ActiveRaidId)
            .OnDelete(DeleteBehavior.Cascade);

        // Player FK — restrict: keep participation records if player is soft-deleted
        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(p => p.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        // One row per player per raid
        builder.HasIndex(p => new { p.ActiveRaidId, p.PlayerId })
            .IsUnique()
            .HasDatabaseName("ix_raid_participants_raid_player");

        builder.HasIndex(p => p.ActiveRaidId)
            .HasDatabaseName("ix_raid_participants_raid_id");

        builder.HasIndex(p => p.PlayerId)
            .HasDatabaseName("ix_raid_participants_player_id");
    }
}
