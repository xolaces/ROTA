using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class LeaderboardEntryConfiguration : IEntityTypeConfiguration<LeaderboardEntry>
{
    public void Configure(EntityTypeBuilder<LeaderboardEntry> builder)
    {
        builder.ToTable("leaderboard_entry");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        // Enum stored as int — consistent with all other enum columns in this codebase.
        // NO HasDefaultValue on Board or Period: both are always set by the Create factory,
        // so there is no store default to fight. No HasSentinel required (the sentinel rule
        // only applies when HasDefaultValue assigns a non-zero default that collides with the
        // CLR zero-default — e.g. the RaidSize/PlayerRoles bug). Board=0 (StatAttack) and
        // Period=0 (Live) are legitimate values, and the factory always writes them explicitly.
        builder.Property(e => e.Board)
            .HasColumnName("board")
            .IsRequired();

        builder.Property(e => e.Period)
            .HasColumnName("period")
            .IsRequired();

        builder.Property(e => e.PeriodKey)
            .HasColumnName("period_key")
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(e => e.Value)
            .HasColumnName("value")
            .IsRequired();

        builder.Property(e => e.LastProgressAt)
            .HasColumnName("last_progress_at")
            .IsRequired();

        builder.Property(e => e.Rank)
            .HasColumnName("rank");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        // FK → players; restrict so we keep leaderboard history if a player is soft-deleted.
        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(e => e.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK index on player_id (required by architecture rules).
        builder.HasIndex(e => e.PlayerId)
            .HasDatabaseName("ix_leaderboard_entry_player_id");

        // Unique upsert key — guarantees exactly one row per player × board × period_key.
        // The ON CONFLICT clause in IncrementAsync/MaxUpdateAsync targets this index.
        builder.HasIndex(e => new { e.PlayerId, e.Board, e.PeriodKey })
            .IsUnique()
            .HasDatabaseName("ix_leaderboard_entry_upsert_key");

        // Read index for ranked page queries: ORDER BY value DESC, last_progress_at ASC
        // filtered to a specific board+period_key.
        builder.HasIndex(e => new { e.Board, e.PeriodKey, e.Value })
            .HasDatabaseName("ix_leaderboard_entry_board_period_value");
    }
}
