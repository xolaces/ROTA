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

        builder.Property(p => p.CompletionCount)
            .HasColumnName("completion_count")
            .HasDefaultValue(0);

        builder.Property(p => p.LastCompletedAt)
            .HasColumnName("last_completed_at");

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        // One row per player per quest
        builder.HasIndex(p => new { p.PlayerId, p.QuestId })
            .IsUnique()
            .HasDatabaseName("ix_player_quest_progress_player_quest");

        // FK index
        builder.HasIndex(p => p.PlayerId)
            .HasDatabaseName("ix_player_quest_progress_player_id");
    }
}
