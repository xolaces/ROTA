using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class PinnacleFirstClaimConfiguration : IEntityTypeConfiguration<PinnacleFirstClaim>
{
    public void Configure(EntityTypeBuilder<PinnacleFirstClaim> builder)
    {
        builder.ToTable("pinnacle_first_claims");

        builder.HasKey(c => c.Id);
        builder.Property(c => c.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(c => c.PinnacleLevel)
            .HasColumnName("pinnacle_level")
            .IsRequired();

        builder.Property(c => c.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(c => c.ClaimedAt)
            .HasColumnName("claimed_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(c => c.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(c => c.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(c => c.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        // One claim per level — enforces "first" atomically.
        builder.HasIndex(c => c.PinnacleLevel).IsUnique();
        builder.HasIndex(c => c.PlayerId);

        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(c => c.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
