using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class PlayerGauntletTrophyConfiguration : IEntityTypeConfiguration<PlayerGauntletTrophy>
{
    public void Configure(EntityTypeBuilder<PlayerGauntletTrophy> builder)
    {
        builder.ToTable("player_gauntlet_trophies");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(t => t.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(t => t.GauntletTrophyId)
            .HasColumnName("gauntlet_trophy_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(t => t.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(t => t.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(t => t.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(t => t.PlayerId)
            .HasDatabaseName("ix_player_gauntlet_trophies_player_id");

        builder.HasIndex(t => new { t.PlayerId, t.GauntletTrophyId })
            .IsUnique()
            .HasDatabaseName("ix_player_gauntlet_trophies_player_trophy");
    }
}
