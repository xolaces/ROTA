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

        // System 16 Slice 2 (additive) — nullable link to a Gauntlet event. Stamped at summon on
        // Gauntlet-eligible raids (Slice 4); null on ordinary raids.
        builder.Property(r => r.GauntletEventId)
            .HasColumnName("gauntlet_event_id");

        // System 21 Slice 3b (additive) — nullable link to a guild. Stamped at the officer-gated
        // guild-raid summon; null on ordinary/Gauntlet raids.
        builder.Property(r => r.GuildId)
            .HasColumnName("guild_id");

        builder.Property(r => r.Difficulty)
            .HasColumnName("difficulty")
            .HasDefaultValue(RaidDifficulty.Normal);

        builder.Property(r => r.Size)
            .HasColumnName("size")
            // Default = Large (3 after ExpandRaidSizeSet). Sentinel prevents EF from
            // omitting the column for Personal raids (CLR default 0) and writing Large.
            .HasDefaultValue(RaidSize.Large)
            .HasSentinel(RaidSize.Large);

        // Visibility — summons start private. No HasDefaultValue: the store default (false) equals
        // the CLR default (false), so the app always writes the column and no sentinel is needed
        // (unlike Size, whose store default Large differs from the CLR default Personal).
        builder.Property(r => r.IsPublic)
            .HasColumnName("is_public");

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

        // System 16 Slice 2 (additive) — nullable FK → gauntlet_events. Restrict so we keep raid
        // history if an event is soft-deleted. Indexed (architecture rule: every FK indexed) +
        // used by the per-event scoring filter.
        builder.HasOne<GauntletEvent>()
            .WithMany()
            .HasForeignKey(r => r.GauntletEventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.GauntletEventId)
            .HasDatabaseName("ix_active_raids_gauntlet_event_id");

        // System 21 Slice 3b (additive) — nullable FK → guilds. Restrict so we keep raid history if a
        // guild is soft-deleted. Indexed (architecture rule: every FK indexed) + used by the guild-raid list.
        builder.HasOne<Guild>()
            .WithMany()
            .HasForeignKey(r => r.GuildId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(r => r.GuildId)
            .HasDatabaseName("ix_active_raids_guild_id");
    }
}
