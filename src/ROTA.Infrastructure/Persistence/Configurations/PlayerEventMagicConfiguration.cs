using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class PlayerEventMagicConfiguration : IEntityTypeConfiguration<PlayerEventMagic>
{
    public void Configure(EntityTypeBuilder<PlayerEventMagic> builder)
    {
        builder.ToTable("player_event_magics");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(m => m.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(m => m.GauntletEventId)
            .HasColumnName("gauntlet_event_id")
            .IsRequired();

        builder.Property(m => m.MagicDefinitionId)
            .HasColumnName("magic_definition_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(m => m.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(m => m.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(m => m.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(m => m.PlayerId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne<GauntletEvent>()
            .WithMany()
            .HasForeignKey(m => m.GauntletEventId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasIndex(m => m.PlayerId)
            .HasDatabaseName("ix_player_event_magics_player_id");

        builder.HasIndex(m => m.GauntletEventId)
            .HasDatabaseName("ix_player_event_magics_gauntlet_event_id");

        builder.HasIndex(m => new { m.PlayerId, m.GauntletEventId, m.MagicDefinitionId })
            .IsUnique()
            .HasDatabaseName("ix_player_event_magics_player_event_magic");
    }
}
