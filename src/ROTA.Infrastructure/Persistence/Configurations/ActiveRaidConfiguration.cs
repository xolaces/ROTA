using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class ActiveRaidConfiguration : IEntityTypeConfiguration<ActiveRaid>
{
    public void Configure(EntityTypeBuilder<ActiveRaid> builder)
    {
        builder.ToTable("active_raids");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(r => r.RaidDefinitionId)
            .HasColumnName("raid_definition_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(r => r.SummonedByPlayerId)
            .HasColumnName("summoned_by_player_id")
            .IsRequired();

        builder.Property(r => r.CurrentHp)
            .HasColumnName("current_hp");

        builder.Property(r => r.MaxHp)
            .HasColumnName("max_hp");

        builder.Property(r => r.IsDefeated)
            .HasColumnName("is_defeated")
            .HasDefaultValue(false);

        builder.Property(r => r.ExpiresAt)
            .HasColumnName("expires_at");

        builder.Property(r => r.Difficulty)
            .HasColumnName("difficulty")
            .HasDefaultValue(RaidDifficulty.Normal);

        builder.Property(r => r.Size)
            .HasColumnName("size")
            .HasDefaultValue(RaidSize.Large);

        builder.Property(r => r.ParticipantCount)
            .HasColumnName("participant_count")
            .HasDefaultValue(0);

        builder.Property(r => r.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(r => r.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        // Summoner FK — restrict delete so we keep raid history even if player is soft-deleted
        builder.HasOne(r => r.SummonedByPlayer)
            .WithMany()
            .HasForeignKey(r => r.SummonedByPlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.SummonedByPlayerId)
            .HasDatabaseName("ix_active_raids_summoned_by_player_id");

        // Covering index for the active-raid query (non-defeated, non-expired, non-deleted)
        builder.HasIndex(r => new { r.IsDefeated, r.IsDeleted, r.ExpiresAt })
            .HasDatabaseName("ix_active_raids_status");
    }
}
