namespace ROTA.Domain.Enums;

/// <summary>
/// Discriminator for Gauntlet currency-ledger rows (System 16). Parallel to
/// <see cref="GemTransactionType"/>; does NOT touch the gem ledger.
/// </summary>
public enum GauntletCurrencyTransactionType
{
    /// <summary>Rank-band bonus credited at settlement.</summary>
    RankReward = 0,

    /// <summary>Per-Gauntlet-raid-defeat credit.</summary>
    RaidDefeatReward = 1,

    /// <summary>Debit when buying from the token shop.</summary>
    ShopPurchase = 2,

    /// <summary>Credit from a gem-funded purchase of this currency.</summary>
    GemPurchase = 3,
}
