using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class PasswordResetTokenConfiguration : IEntityTypeConfiguration<PasswordResetToken>
{
    public void Configure(EntityTypeBuilder<PasswordResetToken> builder)
    {
        builder.ToTable("password_reset_tokens");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(t => t.PlayerId)
            .HasColumnName("player_id")
            .IsRequired();

        builder.Property(t => t.CodeHash)
            .HasColumnName("code_hash")
            .HasMaxLength(64) // SHA256 hex
            .IsRequired();

        builder.Property(t => t.ExpiresAt)
            .HasColumnName("expires_at")
            .IsRequired();

        builder.Property(t => t.UsedAt)
            .HasColumnName("used_at");

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
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(t => t.PlayerId)
            .HasDatabaseName("ix_password_reset_tokens_player_id");

        // The consume UPDATE filters on (player_id, code_hash) — covered by this pair index.
        builder.HasIndex(t => new { t.PlayerId, t.CodeHash })
            .HasDatabaseName("ix_password_reset_tokens_player_code");
    }
}
