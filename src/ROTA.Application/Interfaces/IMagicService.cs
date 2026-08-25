using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IMagicService
{
    Task<IReadOnlyList<OwnedMagicResponse>> GetOwnedMagicsAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Full magic catalogue (every definition in content/magics.json) tagged with the caller's
    /// owned-state and gem price. The Bazaar uses this to show purchasable magics, not just owned ones.
    /// </summary>
    Task<MagicCatalogueResponse> GetCatalogueAsync(Guid playerId, CancellationToken ct = default);

    Task<MagicApplyResult> ApplyMagicAsync(
        Guid playerId, Guid raidId, string magicDefinitionId, bool isAdmin,
        CancellationToken ct = default);

    Task<MagicApplyResult> RemoveMagicAsync(
        Guid playerId, Guid raidId, string magicDefinitionId, bool isAdmin,
        CancellationToken ct = default);

    /// <summary>Idempotent magic grant. Safe to call multiple times — duplicate = no-op.</summary>
    Task GrantMagicAsync(Guid playerId, string magicDefinitionId, CancellationToken ct = default);

    /// <summary>
    /// Spend gems to purchase a magic. A duplicate purchase is refused with AlreadyOwned BEFORE any
    /// charge; the spend is also keyed by an idempotent referenceId, so a retried request cannot
    /// double-charge. (This comment used to say a duplicate charges again -- that was the v0.2.6.1
    /// bug, fixed by the ownership pre-check.)
    /// </summary>
    Task<BuyMagicResult> BuyMagicAsync(Guid playerId, string magicDefinitionId, CancellationToken ct = default);
}
