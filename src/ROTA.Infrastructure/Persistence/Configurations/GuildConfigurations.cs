using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class GuildConfiguration : IEntityTypeConfiguration<Guild>
{
    public void Configure(EntityTypeBuilder<Guild> builder)
    {
        builder.ToTable("guilds");

        builder.HasKey(g => g.Id);
        builder.Property(g => g.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(g => g.Name).HasColumnName("name").HasMaxLength(32).IsRequired();
        builder.Property(g => g.NameNormalized).HasColumnName("name_normalized").HasMaxLength(32).IsRequired();
        builder.Property(g => g.Tag).HasColumnName("tag").HasMaxLength(5).IsRequired();
        builder.Property(g => g.TagNormalized).HasColumnName("tag_normalized").HasMaxLength(5).IsRequired();
        builder.Property(g => g.Description).HasColumnName("description").HasMaxLength(500).IsRequired();
        builder.Property(g => g.CrestId).HasColumnName("crest_id").HasMaxLength(64);
        builder.Property(g => g.LeaderId).HasColumnName("leader_id").IsRequired();
        builder.Property(g => g.Motd).HasColumnName("motd").HasMaxLength(500).IsRequired();
        builder.Property(g => g.MemberCap).HasColumnName("member_cap").IsRequired();
        builder.Property(g => g.Level).HasColumnName("level").IsRequired();
        builder.Property(g => g.Xp).HasColumnName("xp").IsRequired();
        builder.Property(g => g.MemberCount).HasColumnName("member_count").IsRequired();
        // Enum-as-int. NO HasDefaultValue: JoinPolicy.Open=0 is a legitimate written value and the
        // factory always sets it explicitly, so there is no store default to fight (no sentinel needed).
        builder.Property(g => g.JoinPolicy).HasColumnName("join_policy").HasConversion<int>().IsRequired();
        builder.Property(g => g.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(g => g.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(g => g.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);

        // Case-insensitive uniqueness via lowercase shadow columns under partial unique indexes.
        // Partial on is_deleted=false so a disbanded guild's name/tag can be reused (matches §5.11 reuse).
        builder.HasIndex(g => g.NameNormalized)
            .IsUnique()
            .HasDatabaseName("ix_guilds_name_normalized")
            .HasFilter("is_deleted = false");
        builder.HasIndex(g => g.TagNormalized)
            .IsUnique()
            .HasDatabaseName("ix_guilds_tag_normalized")
            .HasFilter("is_deleted = false");

        // FK index on the leader (arch rule: every FK has an index).
        builder.HasIndex(g => g.LeaderId).HasDatabaseName("ix_guilds_leader_id");
        builder.HasOne<Player>().WithMany().HasForeignKey(g => g.LeaderId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class GuildMembershipConfiguration : IEntityTypeConfiguration<GuildMembership>
{
    public void Configure(EntityTypeBuilder<GuildMembership> builder)
    {
        builder.ToTable("guild_memberships");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(m => m.GuildId).HasColumnName("guild_id").IsRequired();
        builder.Property(m => m.PlayerId).HasColumnName("player_id").IsRequired();
        // Enum-as-int. NO HasDefaultValue: GuildRank starts at Member=1 (no 0 value) and the factory
        // always sets it, so do NOT set a store default (per spec — the HasSentinel rule only applies
        // to a non-zero store default colliding with the CLR zero-default).
        builder.Property(m => m.Rank).HasColumnName("rank").HasConversion<int>().IsRequired();
        builder.Property(m => m.ContributionTotal).HasColumnName("contribution_total").HasDefaultValue(0L);
        builder.Property(m => m.JoinedAt).HasColumnName("joined_at").HasDefaultValueSql("NOW()");
        builder.Property(m => m.LastActiveAt).HasColumnName("last_active_at").HasDefaultValueSql("NOW()");
        builder.Property(m => m.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(m => m.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);

        // One ACTIVE guild per player: partial unique on player_id where is_deleted=false.
        // Re-join after leaving works because the soft-deleted row is excluded from the index.
        builder.HasIndex(m => m.PlayerId)
            .IsUnique()
            .HasDatabaseName("ix_guild_memberships_player_active")
            .HasFilter("is_deleted = false");
        // FK index on guild_id (arch rule). Used by roster/count reads.
        builder.HasIndex(m => m.GuildId).HasDatabaseName("ix_guild_memberships_guild_id");

        builder.HasOne<Guild>().WithMany().HasForeignKey(m => m.GuildId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Player>().WithMany().HasForeignKey(m => m.PlayerId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class GuildJoinRequestConfiguration : IEntityTypeConfiguration<GuildJoinRequest>
{
    public void Configure(EntityTypeBuilder<GuildJoinRequest> builder)
    {
        builder.ToTable("guild_join_requests");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(r => r.GuildId).HasColumnName("guild_id").IsRequired();
        builder.Property(r => r.PlayerId).HasColumnName("player_id").IsRequired();
        // Enum-as-int, both zero-values (Application=0, Pending=0) are legitimate; factory sets them.
        builder.Property(r => r.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(r => r.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(r => r.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(r => r.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(r => r.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);

        // FK indexes (arch rule).
        builder.HasIndex(r => r.GuildId).HasDatabaseName("ix_guild_join_requests_guild_id");
        builder.HasIndex(r => r.PlayerId).HasDatabaseName("ix_guild_join_requests_player_id");

        builder.HasOne<Guild>().WithMany().HasForeignKey(r => r.GuildId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Player>().WithMany().HasForeignKey(r => r.PlayerId).OnDelete(DeleteBehavior.Restrict);
    }
}
