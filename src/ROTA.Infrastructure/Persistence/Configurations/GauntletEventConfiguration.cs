using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class GauntletEventConfiguration : IEntityTypeConfiguration<GauntletEvent>
{
    public void Configure(EntityTypeBuilder<GauntletEvent> builder)
    {
        builder.ToTable("gauntlet_events");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.Name)
            .HasColumnName("name")
            .HasMaxLength(100)
            .IsRequired();

        // Enum stored as int — always set by the Create factory (Scheduled=0). No HasDefaultValue,
        // no sentinel needed (same precedent as LeaderboardEntry.Board/Period).
        builder.Property(e => e.State)
            .HasColumnName("state")
            .IsRequired();

        builder.Property(e => e.StartsAt)
            .HasColumnName("starts_at")
            .IsRequired();

        builder.Property(e => e.EndsAt)
            .HasColumnName("ends_at")
            .IsRequired();

        builder.Property(e => e.SettledAt)
            .HasColumnName("settled_at");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        // Read index for "the active event" lookup (≤1 active enforced by the service/repo).
        builder.HasIndex(e => e.State)
            .HasDatabaseName("ix_gauntlet_events_state");
    }
}
