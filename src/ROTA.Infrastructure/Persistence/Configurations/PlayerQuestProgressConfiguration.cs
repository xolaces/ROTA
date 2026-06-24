using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class PlayerQuestProgressConfiguration : IEntityTypeConfiguration<PlayerQuestProgress>
{
    public void Configure(EntityTypeBuilder<PlayerQuestProgress> builder)
    {
        builder.ToTable("player_quest_progress");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(p => p.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(p => p.QuestId)
            .HasColumnName("quest_id")
            .HasMaxLength(50)
            .IsRequired();

        // Per-difficulty depletion track (triage node-depletion-per-difficulty). Stored as int.
        builder.Property(p => p.Difficulty)
            .HasColumnName("difficulty")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(p => p.CompletionCount)
            .HasColumnName("completion_count")
            .HasDefaultValue(0);

        builder.Property(p => p.Progress)
            .HasColumnName("progress")
            .HasDefaultValue(100.0);

        builder.Property(p => p.IsCleared)
            .HasColumnName("is_cleared")
            .HasDefaultValue(false);

        builder.Property(p => p.HasEverCleared)
            .HasColumnName("has_ever_cleared")
            .HasDefaultValue(false);

        builder.Property(p => p.LastCompletedAt)
            .HasColumnName("last_completed_at");

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        // One row per player per quest PER DIFFICULTY (triage node-depletion-per-difficulty: each
        // difficulty has its own independent depletion/clear/unlock track).
        builder.HasIndex(p => new { p.PlayerId, p.QuestId, p.Difficulty })
            .IsUnique()
            .HasDatabaseName("ix_player_quest_progress_player_quest_difficulty");

        // FK index
        builder.HasIndex(p => p.PlayerId)
            .HasDatabaseName("ix_player_quest_progress_player_id");
    }
}
