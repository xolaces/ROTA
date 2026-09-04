using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Infrastructure.Persistence.Configurations;

public class MarketListingConfiguration : IEntityTypeConfiguration<MarketListing>
{
    public void Configure(EntityTypeBuilder<MarketListing> builder)
    {
        builder.ToTable("market_listings");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(l => l.SellerId).HasColumnName("seller_id").IsRequired();
        builder.Property(l => l.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(l => l.DefinitionId).HasColumnName("definition_id").HasMaxLength(100).IsRequired();
        builder.Property(l => l.Quantity).HasColumnName("quantity").HasDefaultValue(1);
        builder.Property(l => l.UnitPrice).HasColumnName("unit_price").IsRequired();
        builder.Property(l => l.Status).HasColumnName("status").HasConversion<int>().HasDefaultValue(MarketListingStatus.Active);
        builder.Property(l => l.ListedAt).HasColumnName("listed_at").HasDefaultValueSql("NOW()");
        builder.Property(l => l.ExpiresAt).HasColumnName("expires_at").IsRequired();
        builder.Property(l => l.BuyerId).HasColumnName("buyer_id");
        builder.Property(l => l.SoldAt).HasColumnName("sold_at");
        builder.Property(l => l.SalePrice).HasColumnName("sale_price").HasDefaultValue(0L);
        builder.Property(l => l.SaleFeePaid).HasColumnName("sale_fee_paid").HasDefaultValue(0L);
        builder.Property(l => l.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(l => l.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(l => l.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);

        builder.Ignore(l => l.TotalPrice);

        builder.HasIndex(l => l.SellerId).HasDatabaseName("ix_market_listings_seller_id");
        builder.HasIndex(l => l.BuyerId).HasDatabaseName("ix_market_listings_buyer_id");

        // The board's own query: active, unexpired, filtered by kind/definition, ordered by price.
        // Partial so the index carries only rows that can still be bought — sold and cancelled
        // listings accumulate forever and would otherwise dominate it.
        builder.HasIndex(l => new { l.Kind, l.DefinitionId, l.UnitPrice })
            .HasDatabaseName("ix_market_listings_board")
            .HasFilter("status = 0 AND is_deleted = false");

        // The expiry sweep's query, likewise partial: only Active rows can lapse.
        builder.HasIndex(l => l.ExpiresAt)
            .HasDatabaseName("ix_market_listings_expiry")
            .HasFilter("status = 0 AND is_deleted = false");
    }
}

public class MarketTransactionConfiguration : IEntityTypeConfiguration<MarketTransaction>
{
    public void Configure(EntityTypeBuilder<MarketTransaction> builder)
    {
        builder.ToTable("market_transactions");

        builder.HasKey(t => t.Id);
        builder.Property(t => t.Id).HasColumnName("id").HasDefaultValueSql("gen_random_uuid()");

        builder.Property(t => t.ListingId).HasColumnName("listing_id").IsRequired();
        builder.Property(t => t.SellerId).HasColumnName("seller_id").IsRequired();
        builder.Property(t => t.BuyerId).HasColumnName("buyer_id").IsRequired();
        builder.Property(t => t.Kind).HasColumnName("kind").HasConversion<int>().IsRequired();
        builder.Property(t => t.DefinitionId).HasColumnName("definition_id").HasMaxLength(100).IsRequired();
        builder.Property(t => t.Quantity).HasColumnName("quantity").HasDefaultValue(1);
        builder.Property(t => t.UnitPrice).HasColumnName("unit_price").IsRequired();
        builder.Property(t => t.TotalPrice).HasColumnName("total_price").IsRequired();
        builder.Property(t => t.SaleFee).HasColumnName("sale_fee").HasDefaultValue(0L);
        builder.Property(t => t.ListingFee).HasColumnName("listing_fee").HasDefaultValue(0L);
        builder.Property(t => t.SellerProceeds).HasColumnName("seller_proceeds").IsRequired();
        builder.Property(t => t.OccurredAt).HasColumnName("occurred_at").HasDefaultValueSql("NOW()");
        builder.Property(t => t.CreatedAt).HasColumnName("created_at").HasDefaultValueSql("NOW()");
        builder.Property(t => t.UpdatedAt).HasColumnName("updated_at").HasDefaultValueSql("NOW()");
        builder.Property(t => t.IsDeleted).HasColumnName("is_deleted").HasDefaultValue(false);

        // One ledger row per listing, enforced by the database rather than by the service being
        // careful. If a sale path is ever made retryable, this is what stops the retry inventing a
        // second sale of the same listing.
        builder.HasIndex(t => t.ListingId)
            .IsUnique()
            .HasDatabaseName("ux_market_transactions_listing_id");

        // The two daily-cap queries: proceeds by seller since a timestamp, spend by buyer since one.
        builder.HasIndex(t => new { t.SellerId, t.OccurredAt })
            .HasDatabaseName("ix_market_transactions_seller_occurred");
        builder.HasIndex(t => new { t.BuyerId, t.OccurredAt })
            .HasDatabaseName("ix_market_transactions_buyer_occurred");
    }
}
