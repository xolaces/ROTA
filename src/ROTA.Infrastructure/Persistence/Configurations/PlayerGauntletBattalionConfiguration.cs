using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class PlayerGauntletBattalionConfiguration : IEntityTypeConfiguration<PlayerGauntletBattalion>
{
    public void Configure(EntityTypeBuilder<PlayerGauntletBattalion> builder)
    {
        builder.ToTable("player_gauntlet_battalions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(e => e.GeneralsJson)
            .HasColumnName("generals_json")
            .HasDefaultValue("[]")
            .IsRequired();

        builder.Property(e => e.TroopsJson)
            .HasColumnName("troops_json")
            .HasDefaultValue("[]")
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        // FK → players; restrict so we keep the battalion if a player is soft-deleted.
        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(e => e.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        // One battalion per player — a unique index (also satisfies the FK-index rule).
        builder.HasIndex(e => e.PlayerId)
            .IsUnique()
            .HasDatabaseName("ix_player_gauntlet_battalions_player");
    }
}
