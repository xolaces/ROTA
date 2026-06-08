using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

// System 22 Phase A — mastery state tables. Enums stored as int (Npgsql default; the factory always
// sets the value, so no store default / sentinel is needed). PlayerMastery rows are permanent (never
// soft-deleted in Phase A), so their unique indexes are NON-partial (also valid ON CONFLICT targets).

public class PlayerMasteryConfiguration : IEntityTypeConfiguration<PlayerMastery>
{
    public void Configure(EntityTypeBuilder<PlayerMastery> builder)
    {
        builder.ToTable("player_masteries");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(m => m.PlayerId).HasColumnName("player_id").IsRequired();
        builder.Property(m => m.Ancient).HasColumnName("ancient").IsRequired();
        builder.Property(m => m.Level).HasColumnName("level").IsRequired();
        builder.Property(m => m.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(m => m.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);

        // One row per (player, ancient). Leading player_id column also serves the FK index.
        builder.HasIndex(m => new { m.PlayerId, m.Ancient })
            .IsUnique()
            .HasDatabaseName("ix_player_masteries_player_ancient");
    }
}

public class PlayerMasteryActivityConfiguration : IEntityTypeConfiguration<PlayerMasteryActivity>
{
    public void Configure(EntityTypeBuilder<PlayerMasteryActivity> builder)
    {
        builder.ToTable("player_mastery_activities");

        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(a => a.PlayerId).HasColumnName("player_id").IsRequired();
        builder.Property(a => a.ActivityType).HasColumnName("activity_type").IsRequired();
        builder.Property(a => a.Counter).HasColumnName("counter").HasDefaultValue(0L);
        builder.Property(a => a.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(a => a.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(a => a.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);

        // Unique (player_id, activity_type) — NON-partial: the ON CONFLICT target for the Slice 4
        // race-safe increment. Leading player_id column also serves the FK index.
        builder.HasIndex(a => new { a.PlayerId, a.ActivityType })
            .IsUnique()
            .HasDatabaseName("ix_player_mastery_activities_player_type");
    }
}

// Append-only idempotency ledger (no updated_at/is_deleted). Mirrors the other ledgers' idempotency
// index. ReferenceId is always set in code, but the partial filter matches the house pattern.
public class MasteryActivityEventConfiguration : IEntityTypeConfiguration<MasteryActivityEvent>
{
    public void Configure(EntityTypeBuilder<MasteryActivityEvent> builder)
    {
        builder.ToTable("mastery_activity_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(e => e.PlayerId).HasColumnName("player_id").IsRequired();
        builder.Property(e => e.ActivityType).HasColumnName("activity_type").IsRequired();
        builder.Property(e => e.ReferenceId).HasColumnName("reference_id").HasMaxLength(200);
        builder.Property(e => e.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");

        builder.HasIndex(e => e.PlayerId)
            .HasDatabaseName("ix_mastery_activity_events_player_id");

        builder.HasIndex(e => new { e.PlayerId, e.ActivityType, e.ReferenceId })
            .IsUnique()
            .HasDatabaseName("ix_mastery_activity_events_idempotency")
            .HasFilter("reference_id IS NOT NULL");
    }
}
