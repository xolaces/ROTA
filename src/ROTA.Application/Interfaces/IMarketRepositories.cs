using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IMarketListingRepository
{
    Task CreateAsync(MarketListing listing, CancellationToken ct = default);
    Task UpdateAsync(MarketListing listing, CancellationToken ct = default);

    /// <summary>Reads one listing by id regardless of status. Null when it does not exist.</summary>
    Task<MarketListing?> FindByIdAsync(Guid listingId, CancellationToken ct = default);

    /// <summary>Active, unexpired listings, newest first, paged.</summary>
    Task<IReadOnlyList<MarketListing>> BrowseAsync(
        string? kind, string? definitionId, int page, int pageSize, CancellationToken ct = default);

    /// <summary>Every listing this player has, any status, newest first.</summary>
    Task<IReadOnlyList<MarketListing>> GetForSellerAsync(Guid sellerId, CancellationToken ct = default);

    Task<int> CountActiveForSellerAsync(Guid sellerId, CancellationToken ct = default);

    /// <summary>
    /// Active listings whose clock has run out, oldest first. Drives the settlement sweep — an expiry
    /// that returns nothing is the recurring defect this codebase has already been bitten by twice.
    /// </summary>
    Task<IReadOnlyList<MarketListing>> GetExpiredActiveAsync(
        DateTimeOffset asOf, int max, CancellationToken ct = default);

    /// <summary>
    /// Claims a listing for a buyer with a single conditional UPDATE guarded on status = Active.
    /// Returns true for exactly one caller however many press at once; everyone else gets false and
    /// must be told the listing is gone. Nothing else in the sale may run before this succeeds.
    /// </summary>
    Task<bool> TryClaimForSaleAsync(
        Guid listingId, Guid buyerId, long salePrice, long saleFee, CancellationToken ct = default);

    /// <summary>
    /// Withdraws a listing with a single conditional UPDATE guarded on status = Active AND ownership,
    /// so a cancel racing a sale cannot return goods that have already been handed over.
    /// </summary>
    Task<bool> TryCancelAsync(Guid listingId, Guid sellerId, CancellationToken ct = default);

    /// <summary>Lapses a listing, guarded on status = Active. Same latch, different terminal state.</summary>
    Task<bool> TryExpireAsync(Guid listingId, CancellationToken ct = default);
}

public interface IMarketTransactionRepository
{
    Task AppendAsync(MarketTransaction transaction, CancellationToken ct = default);

    /// <summary>Gold this player RECEIVED from sales since <paramref name="since"/>. Feeds the daily cap.</summary>
    Task<long> SumProceedsSinceAsync(Guid sellerId, DateTimeOffset since, CancellationToken ct = default);

    /// <summary>Gold this player SPENT on purchases since <paramref name="since"/>. Feeds the daily cap.</summary>
    Task<long> SumSpendSinceAsync(Guid buyerId, DateTimeOffset since, CancellationToken ct = default);
}
