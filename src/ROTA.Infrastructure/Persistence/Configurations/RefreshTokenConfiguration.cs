using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class RefreshTokenConfiguration : IEntityTypeConfiguration<RefreshToken>
{
    public void Configure(EntityTypeBuilder<RefreshToken> builder)
    {
        // Revoked and the reason move together: a revoked row always says why, a live one never does.
        builder.ToTable("refresh_tokens", t => t.HasCheckConstraint(
            "ck_refresh_tokens_revoked_reason",
            "(is_revoked AND revoked_reason IS NOT NULL) OR (NOT is_revoked AND revoked_reason IS NULL)"));

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(t => t.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(t => t.TokenHash)
            .HasColumnName("token_hash")
            .IsRequired();

        builder.Property(t => t.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(t => t.IsRevoked)
            .HasColumnName("is_revoked")
            .HasDefaultValue(false);

        builder.Property(t => t.RevokedReason)
            .HasColumnName("revoked_reason")
            .HasConversion<int?>();

        builder.Property(t => t.RevokedAt)
            .HasColumnName("revoked_at");

        builder.Property(t => t.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(45); // Supports IPv6

        builder.Property(t => t.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        // SECURITY: token hash must be unique — prevents token collision
        builder.HasIndex(t => t.TokenHash).IsUnique();
        builder.HasIndex(t => t.PlayerId);
    }
}