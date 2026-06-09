using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class OutboundEmailConfiguration : IEntityTypeConfiguration<OutboundEmail>
{
    public void Configure(EntityTypeBuilder<OutboundEmail> builder)
    {
        builder.ToTable("outbound_emails");

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id)
            .HasColumnName("id")
            .HasDefaultValueSql("gen_random_uuid()");

        builder.Property(e => e.Type)
            .HasColumnName("email_type")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.Subject)
            .HasColumnName("subject")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(e => e.Recipient)
            .HasColumnName("recipient")
            .HasMaxLength(256)
            .IsRequired();

        builder.Property(e => e.TriggeringPlayerId)
            .HasColumnName("triggering_player_id");

        builder.Property(e => e.TriggeringSystem)
            .HasColumnName("triggering_system")
            .HasMaxLength(64);

        builder.Property(e => e.Summary)
            .HasColumnName("summary")
            .HasMaxLength(1024)
            .IsRequired();

        // T52 — operator-triage priority (Low/Normal/High). The DB default (Normal) backfills any legacy
        // row; the sentinel is set out of the enum range so an explicit Low(0) — the CLR default — is
        // still written on INSERT instead of being mistaken for "unset" and falling back to the default.
        builder.Property(e => e.Priority)
            .HasColumnName("priority")
            .HasConversion<int>()
            .HasDefaultValue(EmailPriority.Normal)
            .HasSentinel((EmailPriority)(-1))
            .IsRequired();

        // Structured payloads — jsonb so the dashboard can pretty-print and (future) query them.
        builder.Property(e => e.DetailJson)
            .HasColumnName("detail")
            .HasColumnType("jsonb");

        builder.Property(e => e.MetadataJson)
            .HasColumnName("metadata")
            .HasColumnType("jsonb");

        builder.Property(e => e.SendStatus)
            .HasColumnName("send_status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.SendAttempts)
            .HasColumnName("send_attempts")
            .HasDefaultValue(0);

        builder.Property(e => e.LastSendError)
            .HasColumnName("last_send_error")
            .HasMaxLength(1024);

        builder.Property(e => e.ReviewStatus)
            .HasColumnName("review_status")
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.ReviewedBy)
            .HasColumnName("reviewed_by");

        builder.Property(e => e.ReviewedAt)
            .HasColumnName("reviewed_at");

        builder.Property(e => e.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(e => e.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        // Indexes for the dashboard's filters + the FK column.
        builder.HasIndex(e => e.Type);
        builder.HasIndex(e => e.ReviewStatus);
        builder.HasIndex(e => e.CreatedAt);
        builder.HasIndex(e => e.TriggeringPlayerId);
        builder.HasIndex(e => e.ReviewedBy);
        // T52 — the dashboard filters/sorts on priority.
        builder.HasIndex(e => e.Priority);

        // FK → players (nullable; players are soft-deleted, so SetNull is a defensive default).
        builder.HasOne<Player>()
            .WithMany()
            .HasForeignKey(e => e.TriggeringPlayerId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
