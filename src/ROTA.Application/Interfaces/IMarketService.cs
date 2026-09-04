using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

/// <summary>System 27 — the player market (prototype). Consignment only; there is no direct transfer.</summary>
public interface IMarketService
{
    Task<MarketBrowseResponse> BrowseAsync(
        Guid playerId, string? kind, string? definitionId, int page, CancellationToken ct = default);

    Task<MarketBrowseResponse> GetMyListingsAsync(Guid playerId, CancellationToken ct = default);

    Task<CreateListingResponse> CreateListingAsync(
        Guid playerId, CreateListingRequest request, CancellationToken ct = default);

    Task<BuyListingResponse> BuyAsync(Guid playerId, Guid listingId, CancellationToken ct = default);

    Task<CancelListingResponse> CancelAsync(Guid playerId, Guid listingId, CancellationToken ct = default);

    /// <summary>
    /// Returns the goods on every listing whose clock has run out. Driven by a hosted service, and
    /// idempotent under the status latch so a double-fire or a second app instance settles each
    /// listing exactly once. Returns how many it settled.
    /// </summary>
    Task<int> SettleExpiredListingsAsync(int max = 50, CancellationToken ct = default);
}
