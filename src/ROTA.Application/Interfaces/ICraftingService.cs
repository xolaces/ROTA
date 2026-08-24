using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

/// <summary>
/// System 26 — crafting (D-018). The read-only catalogue plus the consuming transaction.
/// </summary>
public interface ICraftingService
{
    /// <summary>
    /// Currently-offered recipes, each hydrated with what the caller holds and why a craft is blocked.
    /// Advisory: the craft call re-checks everything authoritatively.
    /// </summary>
    Task<CraftCatalogueResponse> GetCatalogueAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>
    /// Performs one craft: verify, charge gold, consume every ingredient and grant the output, all in a
    /// single transaction under the player's mutation lock. Never throws for a player-caused refusal —
    /// those come back as a <see cref="CraftFailureCode"/> so the client can explain them.
    /// </summary>
    Task<CraftResponse> CraftAsync(Guid playerId, string recipeId, CancellationToken ct = default);
}
