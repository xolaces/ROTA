using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class PlayerResourceConfiguration : IEntityTypeConfiguration<PlayerResource>
{
    public void Configure(EntityTypeBuilder<PlayerResource> builder)
    {
        builder.ToTable("player_resources");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(r => r.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(r => r.ResourceType)
            .HasColumnName("resource_type")
            .HasConversion<string>()  // Store enum as string in DB for readability
            .HasMaxLength(50)
            .IsRequired();

        builder.Property(r => r.CurrentValue)
            .HasColumnName("current_value")
            .HasDefaultValue(0);

        builder.Property(r => r.MaxValue)
            .HasColumnName("max_value")
            .HasDefaultValue(0);

        builder.Property(r => r.RegenPerMinute)
            .HasColumnName("regen_per_minute")
            .HasDefaultValue(0);

        builder.Property(r => r.LastRegenAt)
            .HasColumnName("last_regen_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(r => r.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        // One row per resource type per player
        builder.HasIndex(r => new { r.PlayerId, r.ResourceType }).IsUnique();
    }
}