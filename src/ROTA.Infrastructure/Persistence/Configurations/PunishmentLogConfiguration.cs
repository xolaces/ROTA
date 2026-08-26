using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class PunishmentLogConfiguration : IEntityTypeConfiguration<PunishmentLog>
{
    public void Configure(EntityTypeBuilder<PunishmentLog> builder)
    {
        builder.ToTable("punishment_log");

        // BIGSERIAL, matching audit_log: an append-only table wants sequential keys, and the ordering
        // doubles as the tie-break when two entries share a timestamp.
        builder.HasKey(p => p.Id);
        builder.Property(p => p.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(p => p.ActorPlayerId)
            .HasColumnName("actor_player_id");

        builder.Property(p => p.ActorRole)
            .HasColumnName("actor_role")
            .HasMaxLength(32)
            .IsRequired();

        builder.Property(p => p.TargetPlayerId)
            .HasColumnName("target_player_id")
            .IsRequired();

        builder.Property(p => p.TargetUsername)
            .HasColumnName("target_username")
            .HasMaxLength(64)
            .IsRequired();

        // Stored as the int enum value, matching how PlayerRoles and the other enums persist here.
        builder.Property(p => p.Type)
            .HasColumnName("type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(p => p.Reason)
            .HasColumnName("reason")
            .HasMaxLength(1000)
            .IsRequired();

        builder.Property(p => p.ExpiresAt)
            .HasColumnName("expires_at");

        builder.Property(p => p.ReversalOfId)
            .HasColumnName("reversal_of_id");

        builder.Property(p => p.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(45);

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        // No FK to players on EITHER side, deliberately. A punishment record has to outlive the
        // account it describes -- a deleted account must not be able to erase its own moderation
        // history, and a deleted staff account must not erase what they did.
        builder.HasIndex(p => new { p.TargetPlayerId, p.CreatedAt })
            .HasDatabaseName("ix_punishment_log_target_created");
        builder.HasIndex(p => p.ActorPlayerId)
            .HasDatabaseName("ix_punishment_log_actor");
    }
}
