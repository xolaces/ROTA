using ROTA.Domain.Enums;

namespace ROTA.Application.Models;

// BETA (System 16 Slice 6) — a Gauntlet token-shop catalogue entry, loaded from
// content/gauntlet_shop.json and validated at startup by GauntletShopProvider:
//   - no duplicate ids; price > 0; currency valid;
//   - Unit/Legion/Gear payloadId resolves in the matching def provider;
//   - GemBundle/StrikeRefill amount > 0.
// Own-once kinds (Unit/Legion/Gear) carry maxOwned = 1; GemBundle/StrikeRefill are repeatable
// (no maxOwned). The purchase flow (GauntletService.BuyFromShopAsync) spends from the
// gauntlet_currency_transactions ledger with the tri-state SpendAsync discipline.
public class GauntletShopEntry
{
    /// <summary>Stable catalogue id; the spend referenceId is gauntletshop:{playerId}:{id}.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Which kind of reward this entry grants.</summary>
    public GauntletShopRewardKind RewardKind { get; set; }

    /// <summary>
    /// Definition id for Unit/Legion/Gear. May be empty for GemBundle/StrikeRefill (where
    /// <see cref="Amount"/> is the payload instead).
    /// </summary>
    public string PayloadId { get; set; } = string.Empty;

    /// <summary>
    /// Gems (GemBundle) or Strikes (StrikeRefill) granted. Must be &gt; 0 for those kinds; ignored
    /// for Unit/Legion/Gear.
    /// </summary>
    public int Amount { get; set; }

    /// <summary>Which currency funds the purchase (Token or Pitchfork). Never conflated.</summary>
    public GauntletCurrency Currency { get; set; }

    /// <summary>Price in <see cref="Currency"/>. Must be &gt; 0.</summary>
    public int Price { get; set; }

    /// <summary>
    /// Ownership cap. 1 = own-once (the shop refuses to re-sell once owned). Null = repeatable
    /// (GemBundle/StrikeRefill). Unit/Legion/Gear are own-once.
    /// </summary>
    public int? MaxOwned { get; set; }

    /// <summary>True when this entry can only be owned once (maxOwned == 1).</summary>
    public bool IsOwnOnce => MaxOwned == 1;
}
