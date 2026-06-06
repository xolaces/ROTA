using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class FriendshipConfiguration : IEntityTypeConfiguration<Friendship>
{
    public void Configure(EntityTypeBuilder<Friendship> builder)
    {
        builder.ToTable("friendships");

        builder.HasKey(f => f.Id);
        builder.Property(f => f.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(f => f.RequesterId).HasColumnName("requester_id").IsRequired();
        builder.Property(f => f.AddresseeId).HasColumnName("addressee_id").IsRequired();
        builder.Property(f => f.Status).HasColumnName("status").HasConversion<int>().IsRequired();
        builder.Property(f => f.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(f => f.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(f => f.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);

        // Partial unique index: only ACTIVE rows are unique on the pair, so re-friending after an
        // unfriend (soft-deleted row lingers) can insert a fresh row without colliding.
        builder.HasIndex(f => new { f.RequesterId, f.AddresseeId })
            .IsUnique()
            .HasFilter("is_deleted = false");
        builder.HasIndex(f => f.AddresseeId);

        builder.HasOne<Player>().WithMany().HasForeignKey(f => f.RequesterId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Player>().WithMany().HasForeignKey(f => f.AddresseeId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PlayerBlockConfiguration : IEntityTypeConfiguration<PlayerBlock>
{
    public void Configure(EntityTypeBuilder<PlayerBlock> builder)
    {
        builder.ToTable("player_blocks");

        builder.HasKey(b => b.Id);
        builder.Property(b => b.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(b => b.BlockerId).HasColumnName("blocker_id").IsRequired();
        builder.Property(b => b.BlockedId).HasColumnName("blocked_id").IsRequired();
        builder.Property(b => b.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(b => b.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(b => b.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);

        builder.HasIndex(b => new { b.BlockerId, b.BlockedId }).IsUnique();
        builder.HasIndex(b => b.BlockedId);

        builder.HasOne<Player>().WithMany().HasForeignKey(b => b.BlockerId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Player>().WithMany().HasForeignKey(b => b.BlockedId).OnDelete(DeleteBehavior.Restrict);
    }
}

public class PrivateMessageConfiguration : IEntityTypeConfiguration<PrivateMessage>
{
    public void Configure(EntityTypeBuilder<PrivateMessage> builder)
    {
        builder.ToTable("private_messages");

        builder.HasKey(m => m.Id);
        builder.Property(m => m.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");
        builder.Property(m => m.SenderId).HasColumnName("sender_id").IsRequired();
        builder.Property(m => m.RecipientId).HasColumnName("recipient_id").IsRequired();
        builder.Property(m => m.Body).HasColumnName("body").HasMaxLength(2000).IsRequired();
        builder.Property(m => m.SentAt).HasColumnName("sent_at").HasDefaultValueSql("NOW()");
        builder.Property(m => m.IsRead).HasColumnName("is_read").HasDefaultValue(false);
        builder.Property(m => m.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(m => m.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(m => m.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);

        builder.HasIndex(m => m.SenderId);
        builder.HasIndex(m => new { m.RecipientId, m.SentAt });

        builder.HasOne<Player>().WithMany().HasForeignKey(m => m.SenderId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Player>().WithMany().HasForeignKey(m => m.RecipientId).OnDelete(DeleteBehavior.Restrict);
    }
}
