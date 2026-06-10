using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Infrastructure.Persistence.Configurations;

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

        builder.Property(p => p.Class)
            .HasColumnName("class")
            .HasConversion<int>()
            .HasDefaultValue(PlayerClass.Conscript);

        builder.Property(p => p.Gold)
            .HasColumnName("gold")
            .HasDefaultValue(0L);

        // Role system — stored as a single int using bitwise flags
        builder.Property(p => p.Roles)
            .HasColumnName("roles")
            .HasConversion<int>()
            .HasDefaultValue(PlayerRoles.Player)
            // Sentinel = the DB default so non-default role sets (e.g. Player|Admin) are sent
            // explicitly and the column is omitted only when Roles == Player. Without this a
            // CLR-default value would be wrongly replaced by the store default. (EF 20601)
            .HasSentinel(PlayerRoles.Player);

        // Display name — shown in UI, populated from username at registration
        builder.Property(p => p.DisplayName)
            .HasColumnName("display_name")
            .HasMaxLength(48)
            .IsRequired();

        builder.Property(p => p.GuildId)
            .HasColumnName("guild_id");

        builder.Property(p => p.GuildRank)
            .HasColumnName("guild_rank")
            .HasMaxLength(20);

        // Masteries (System 22) — denormalized active pledge; nullable enum stored as nullable int
        // (Npgsql default, like the ledger enums). Null until the player pledges an Ancient.
        builder.Property(p => p.ActivePledgeAncient)
            .HasColumnName("active_pledge_ancient");

        builder.Property(p => p.IsBanned)
            .HasColumnName("is_banned")
            .HasDefaultValue(false);

        builder.Property(p => p.BanReason)
            .HasColumnName("ban_reason");

        builder.Property(p => p.MuteExpiresAt)
            .HasColumnName("mute_expires_at");

        // IsMuted is derived from MuteExpiresAt — never a column.
        builder.Ignore(p => p.IsMuted);

        // Achievements (TICKET 46) — days-played tracking. LastLoginDate maps to a DATE column;
        // DaysPlayed defaults to 0 for existing rows.
        builder.Property(p => p.LastLoginDate)
            .HasColumnName("last_login_date");

        builder.Property(p => p.DaysPlayed)
            .HasColumnName("days_played")
            .HasDefaultValue(0);

        builder.Property(p => p.CreatedAt)
            .HasColumnName("created_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(p => p.UpdatedAt)
            .HasColumnName("updated_at")
            .HasDefaultValueSql("NOW()");

        builder.Property(p => p.IsDeleted)
            .HasColumnName("is_deleted")
            .HasDefaultValue(false);

        // T59 — optimistic concurrency on the players row via the PostgreSQL xmin system column.
        // Concurrent quest/raid reward writes were last-write-wins (silent XP/gold loss); with xmin
        // as the token a stale write throws DbUpdateConcurrencyException and the reward chokepoints
        // retry via IPlayerRepository.MutateWithRetryAsync. No schema change (xmin is a system column).
        builder.Property<uint>("xmin")
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Unique constraints — PARTIAL (index-hardening, audit ticket): existence checks all filter
        // is_deleted, so a soft-deleted account's email/username must not block re-registration
        // (the non-partial index 500'd it — the friendship lesson applied to players).
        builder.HasIndex(p => p.Username).IsUnique().HasFilter("is_deleted = false");
        builder.HasIndex(p => p.Email).IsUnique().HasFilter("is_deleted = false");

        // Relationships
        builder.HasOne(p => p.Stats)
            .WithOne(s => s.Player)
            .HasForeignKey<PlayerStats>(s => s.PlayerId);

        builder.HasMany(p => p.Resources)
            .WithOne(r => r.Player)
            .HasForeignKey(r => r.PlayerId);
    }
}
