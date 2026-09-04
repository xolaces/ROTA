using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

// TICKET 46 — Achievement state tables. Progress rows are permanent (never soft-deleted in this
// phase), so the unique index is NON-partial (a valid ON CONFLICT target). The award ledger mirrors
// the gem ledger: append-only, one row per achievement enforced by a unique index.

public class AchievementProgressConfiguration : IEntityTypeConfiguration<AchievementProgress>
{
    public void Configure(EntityTypeBuilder<AchievementProgress> builder)
    {
        builder.ToTable("achievement_progress");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(p => p.PlayerId).HasColumnName("player_id").IsRequired();
        builder.Property(p => p.AchievementId).HasColumnName("achievement_id").HasMaxLength(100).IsRequired();
        builder.Property(p => p.ProgressValue).HasColumnName("progress_value").HasDefaultValue(0L);
        builder.Property(p => p.IsCompleted).HasColumnName("is_completed").HasDefaultValue(false);
        builder.Property(p => p.CompletedAt).HasColumnName("completed_at");
        builder.Property(p => p.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(p => p.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(p => p.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);

        // One row per (player, achievement). NON-partial — the ON CONFLICT target for the race-safe
        // increment / recount. Leading player_id column also serves the FK index.
        builder.HasIndex(p => new { p.PlayerId, p.AchievementId })
            .IsUnique()
            .HasDatabaseName("ix_achievement_progress_player_achievement");

        // R5d — the sweep index, PARTIAL on the two flags the hot-path query filters by.
        //
        // GetIncompleteForPlayerAsync runs on every quest click and every profile read. The unique
        // index above cannot serve it: it leads on player_id but carries every row that player has,
        // and the rows that matter are a shrinking minority as an account completes its ladder.
        //
        // Measured rather than argued, in docs/eval/PARTIAL_INDEX_BENCHMARK.md against a 932,000-row
        // table: reads 1.662 ms -> 0.096 ms (about 14 to 1), writes +10.9 us per incremented row, and
        // the index is 432 kB against 56 MB for the composite it sits beside — because it indexes only
        // the incomplete rows, and "incomplete" is the small side of the split.
        //
        // The predicate must stay EXACTLY the query's predicate. PostgreSQL only uses a partial index
        // when it can prove the query's WHERE implies the index's, so widening either one silently
        // drops back to the sequential scan this exists to remove.
        builder.HasIndex(p => p.PlayerId)
            .HasDatabaseName("ix_ap_player_incomplete")
            .HasFilter("NOT is_completed AND NOT is_deleted");
    }
}

// Append-only AP ledger (no updated_at / is_deleted). Total AP = SUM(points). One award per
// achievement enforced by the unique index (the gem-ledger discipline).
public class AchievementAwardConfiguration : IEntityTypeConfiguration<AchievementAward>
{
    public void Configure(EntityTypeBuilder<AchievementAward> builder)
    {
        builder.ToTable("achievement_awards");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.PlayerId).HasColumnName("player_id").IsRequired();
        builder.Property(a => a.AchievementId).HasColumnName("achievement_id").HasMaxLength(100).IsRequired();
        builder.Property(a => a.Points).HasColumnName("points").IsRequired();
        builder.Property(a => a.ReferenceId).HasColumnName("reference_id").HasMaxLength(200).IsRequired();
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        // FK index on player_id (the balance SUM read filters on it).
        builder.HasIndex(a => a.PlayerId)
            .HasDatabaseName("ix_achievement_awards_player_id");

        // Exactly one award per achievement per player (idempotency target).
        builder.HasIndex(a => new { a.PlayerId, a.AchievementId })
            .IsUnique()
            .HasDatabaseName("ix_achievement_awards_player_achievement");
    }
}

// Append-only progress-event idempotency ledger (no updated_at/is_deleted). Mirrors
// MasteryActivityEvent: a row is written before a REFERENCED progress increment, so a replay never
// double-counts. Unique on (player_id, achievement_id, reference_id) is the exactly-once target.
public class AchievementProgressEventConfiguration : IEntityTypeConfiguration<AchievementProgressEvent>
{
    public void Configure(EntityTypeBuilder<AchievementProgressEvent> builder)
    {
        builder.ToTable("achievement_progress_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.PlayerId).HasColumnName("player_id").IsRequired();
        builder.Property(e => e.AchievementId).HasColumnName("achievement_id").HasMaxLength(100).IsRequired();
        builder.Property(e => e.ReferenceId).HasColumnName("reference_id").HasMaxLength(200).IsRequired();
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(e => e.PlayerId)
            .HasDatabaseName("ix_achievement_progress_events_player_id");

        // Exactly one progress event per (player, achievement, source ref) — the idempotency target.
        builder.HasIndex(e => new { e.PlayerId, e.AchievementId, e.ReferenceId })
            .IsUnique()
            .HasDatabaseName("ix_achievement_progress_events_idempotency");
    }
}
