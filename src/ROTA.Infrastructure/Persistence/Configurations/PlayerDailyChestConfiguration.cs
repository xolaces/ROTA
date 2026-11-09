using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

// System 29 — one row per (player, UTC day). The unique index is NON-partial so it can serve as the
// ON CONFLICT target for the race-safe first roll; its leading player_id column is also the FK index.
public class PlayerDailyChestConfiguration : IEntityTypeConfiguration<PlayerDailyChest>
{
    public void Configure(EntityTypeBuilder<PlayerDailyChest> builder)
    {
        builder.ToTable("player_daily_chests");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(c => c.PlayerId).HasColumnName("player_id").IsRequired();
        builder.Property(c => c.ChestDay).HasColumnName("chest_day").IsRequired();
        builder.Property(c => c.ContentsJson).HasColumnName("contents").HasColumnType("jsonb").IsRequired();
        builder.Property(c => c.PickedIndex).HasColumnName("picked_index");
        builder.Property(c => c.PickedAt).HasColumnName("picked_at");
        builder.Property(c => c.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(c => c.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(c => c.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);

        builder.Ignore(c => c.IsPicked);

        builder.HasIndex(c => new { c.PlayerId, c.ChestDay })
            .IsUnique()
            .HasDatabaseName("ix_player_daily_chests_player_day");

        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(c => c.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        // A pick is 0–5 or nothing. The service validates first; this is the last line.
        builder.ToTable(t => t.HasCheckConstraint(
            "ck_player_daily_chests_picked_index",
            "picked_index IS NULL OR (picked_index >= 0 AND picked_index < 6)"));
    }
}
