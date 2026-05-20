using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

/// <summary>
/// EF Core Fluent API mapping for the Player entity.
/// Defines table name, column constraints, and indexes.
/// No data annotations on the domain entity — all config lives here.
/// </summary>
public class PlayerConfiguration : IEntityTypeConfiguration<Player>
{
    public void Configure(EntityTypeBuilder<Player> builder)
    {
        builder.ToTable("players");

        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(p => p.Username)
            .HasColumnName("username")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(p => p.Email)
            .HasColumnName("email")
            .HasMaxLength(255)
            .IsRequired();

        builder.Property(p => p.PasswordHash)
            .HasColumnName("password_hash")
            .IsRequired();

        builder.Property(p => p.Level)
            .HasColumnName("level")
            .HasDefaultValue(1);

        builder.Property(p => p.Experience)
            .HasColumnName("experience")
            .HasDefaultValue(0L);

        builder.Property(p => p.Gold)
            .HasColumnName("gold")
            .HasDefaultValue(0L);

        builder.Property(p => p.GuildId)
            .HasColumnName("guild_id");

        builder.Property(p => p.GuildRank)
            .HasColumnName("guild_rank")
            .HasMaxLength(20);

        builder.Property(p => p.IsBanned)
            .HasColumnName("is_banned")
            .HasDefaultValue(false);

        builder.Property(p => p.BanReason)
            .HasColumnName("ban_reason");

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(p => p.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        // Unique constraints
        builder.HasIndex(p => p.Username).IsUnique();
        builder.HasIndex(p => p.Email).IsUnique();

        // Relationships
        builder.HasOne(p => p.Stats)
            .WithOne(s => s.Player)
            .HasForeignKey<PlayerStats>(s => s.PlayerId);

        builder.HasMany(p => p.Resources)
            .WithOne(r => r.Player)
            .HasForeignKey(r => r.PlayerId);
    }
}