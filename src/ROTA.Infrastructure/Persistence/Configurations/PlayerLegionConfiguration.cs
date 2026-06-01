using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class PlayerLegionConfiguration : IEntityTypeConfiguration<PlayerLegion>
{
    public void Configure(EntityTypeBuilder<PlayerLegion> builder)
    {
        builder.ToTable("player_legions");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(e => e.LegionDefinitionId)
            .HasColumnName("legion_definition_id")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(e => e.IsActive)
            .HasColumnName("is_active")
            .HasDefaultValue(false);

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        builder.HasIndex(e => new { e.PlayerId, e.LegionDefinitionId })
            .IsUnique()
            .HasDatabaseName("ix_player_legions_player_legion_def");

        builder.HasIndex(e => e.PlayerId)
            .HasDatabaseName("ix_player_legions_player_id");
    }
}
