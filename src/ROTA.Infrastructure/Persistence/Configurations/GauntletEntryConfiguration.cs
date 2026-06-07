using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class GauntletEntryConfiguration : IEntityTypeConfiguration<GauntletEntry>
{
    public void Configure(EntityTypeBuilder<GauntletEntry> builder)
    {
        builder.ToTable("gauntlet_entries");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.GauntletEventId)
            .HasColumnName("gauntlet_event_id")
            .IsRequired();

        builder.Property(e => e.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        // Enum stored as int — always set by Create (the locked league). No default/sentinel.
        builder.Property(e => e.League)
            .HasColumnName("league")
            .IsRequired();

        builder.Property(e => e.Score)
            .HasColumnName("score")
            .HasDefaultValue(0L);

        builder.Property(e => e.TieBreakAt)
            .HasColumnName("tie_break_at")
            .IsRequired();

        builder.Property(e => e.LastRank)
            .HasColumnName("last_rank");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        // FK → gauntlet_events; restrict so we keep entries if an event is soft-deleted.
        builder.HasOne<GauntletEvent>()
            .WithMany()
            .HasForeignKey(e => e.GauntletEventId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK → players; restrict so we keep entries if a player is soft-deleted.
        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(e => e.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK indexes (architecture rule: every FK indexed).
        builder.HasIndex(e => e.GauntletEventId)
            .HasDatabaseName("ix_gauntlet_entries_gauntlet_event_id");

        builder.HasIndex(e => e.PlayerId)
            .HasDatabaseName("ix_gauntlet_entries_player_id");

        // One entry per player per event — the join idempotency key.
        builder.HasIndex(e => new { e.GauntletEventId, e.PlayerId })
            .IsUnique()
            .HasDatabaseName("ix_gauntlet_entries_event_player");

        // Read index for the per-league ranked snapshot (Slice 3): ORDER BY score DESC, tie_break_at ASC.
        builder.HasIndex(e => new { e.GauntletEventId, e.League, e.Score })
            .HasDatabaseName("ix_gauntlet_entries_event_league_score");
    }
}
