using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class AuditLogConfiguration : IEntityTypeConfiguration<AuditLog>
{
    public void Configure(EntityTypeBuilder<AuditLog> builder)
    {
        builder.ToTable("audit_log");

        // BIGSERIAL — auto-incrementing long, not UUID
        // Audit logs are written at very high volume; sequential IDs are
        // more efficient for append-only tables than random UUIDs
        builder.HasKey(a => a.Id);
        builder.Property(a => a.Id)
            .HasColumnName("id")
            .ValueGeneratedOnAdd();

        builder.Property(a => a.PlayerId)
            .HasColumnName("player_id");

        builder.Property(a => a.Action)
            .HasColumnName("action")
            .HasMaxLength(100)
            .IsRequired();

        builder.Property(a => a.InputHash)
            .HasColumnName("input_hash");

        builder.Property(a => a.ResultSummary)
            .HasColumnName("result_summary");

        builder.Property(a => a.IpAddress)
            .HasColumnName("ip_address")
            .HasMaxLength(45);

        builder.Property(a => a.SessionId)
            .HasColumnName("session_id");

        builder.Property(a => a.Flagged)
            .HasColumnName("flagged")
            .HasDefaultValue(false);

        builder.Property(a => a.FlagReason)
            .HasColumnName("flag_reason");

        builder.Property(a => a.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.HasIndex(a => a.PlayerId);
        builder.HasIndex(a => a.CreatedAt);
    }
}