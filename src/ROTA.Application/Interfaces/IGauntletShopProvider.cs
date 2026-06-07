using ROTA.Application.Models;

namespace ROTA.Application.Interfaces;

// BETA (System 16 Slice 6) — singleton provider for the Gauntlet token-shop catalogue, loaded
// from content/gauntlet_shop.json and validated at startup (mirrors IGauntletContentProvider).
// Validation throws on: duplicate ids; price <= 0; invalid currency; Unit/Legion/Gear payloadId
// that does not resolve in the matching def provider; GemBundle/StrikeRefill amount <= 0.
public interface IGauntletShopProvider
{
    /// <summary>The full catalogue (insertion order).</summary>
    IReadOnlyList<GauntletShopEntry> GetAll();

    /// <summary>A catalogue entry by id, or null if no such entry exists.</summary>
    GauntletShopEntry? GetById(string id);
}
