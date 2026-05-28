using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class PlayerStatsConfiguration : IEntityTypeConfiguration<PlayerStats>
{
    public void Configure(EntityTypeBuilder<PlayerStats> builder)
    {
        builder.ToTable("player_stats");

        builder.HasKey(s => s.Id);
        builder.Property(s => s.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(s => s.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(s => s.BaseAttack)
            .HasColumnName("base_attack")
            .HasDefaultValue(10);

        builder.Property(s => s.BaseDefense)
            .HasColumnName("base_defense")
            .HasDefaultValue(10);

        builder.Property(s => s.BaseMaxHealth)
            .HasColumnName("base_max_health")
            .HasDefaultValue(100);

        builder.Property(s => s.CurrentHealth)
            .HasColumnName("current_health")
            .HasDefaultValue(100);

        // Investable stat fields
        builder.Property(s => s.EnergyInvestment)
            .HasColumnName("energy_investment")
            .HasDefaultValue(0);

        builder.Property(s => s.StaminaInvestment)
            .HasColumnName("stamina_investment")
            .HasDefaultValue(0);

        builder.Property(s => s.DiscernmentInvestment)
            .HasColumnName("discernment_investment")
            .HasDefaultValue(0);

        builder.Property(s => s.SkillPoints)
            .HasColumnName("skill_points")
            .HasDefaultValue(0);

        builder.Property(s => s.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(s => s.PlayerId);
    }
}
