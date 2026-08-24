using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

/// <summary>
/// System 26 — crafting (D-018). Slice 1 is the read-only catalogue; the consuming transaction
/// (CraftAsync) lands in slice 2.
/// </summary>
public interface ICraftingService
{
    /// <summary>
    /// Currently-offered recipes, each hydrated with what the caller holds and why a craft is blocked.
    /// Advisory: the craft call re-checks everything authoritatively.
    /// </summary>
    Task<CraftCatalogueResponse> GetCatalogueAsync(Guid playerId, CancellationToken ct = default);
}
