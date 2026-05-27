using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class PlayerQuestDifficultyProgressConfiguration : IEntityTypeConfiguration<PlayerQuestDifficultyProgress>
{
    public void Configure(EntityTypeBuilder<PlayerQuestDifficultyProgress> builder)
    {
        builder.ToTable("player_quest_difficulty_progress");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(p => p.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(p => p.QuestId)
            .HasColumnName("quest_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(p => p.Difficulty)
            .HasColumnName("difficulty")
            .IsRequired();

        builder.Property(p => p.CompletionCount)
            .HasColumnName("completion_count")
            .HasDefaultValue(0);

        builder.Property(p => p.FirstCompletedAt)
            .HasColumnName("first_completed_at");

        builder.Property(p => p.LastCompletedAt)
            .HasColumnName("last_completed_at");

        builder.Property(p => p.FirstSigilDropped)
            .HasColumnName("first_sigil_dropped")
            .HasDefaultValue(false);

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        // Unique: one row per (player, quest, difficulty)
        builder.HasIndex(p => new { p.PlayerId, p.QuestId, p.Difficulty })
            .IsUnique()
            .HasDatabaseName("ix_player_quest_difficulty_progress_unique");

        builder.HasIndex(p => p.PlayerId)
            .HasDatabaseName("ix_player_quest_difficulty_progress_player_id");
    }
}
