namespace ROTA.Domain.Enums;

/// <summary>
/// What a Gauntlet token-shop entry grants (System 16 Slice 6). For
/// <see cref="Unit"/>/<see cref="Legion"/>/<see cref="Gear"/> the entry's <c>payloadId</c> is the
/// definition id; for <see cref="GemBundle"/>/<see cref="StrikeRefill"/> the entry's <c>amount</c>
/// is the gems/strikes granted (payloadId may be empty).
/// </summary>
public enum GauntletShopRewardKind
{
    /// <summary>Grants a unit (own-once; payloadId = unit definition id).</summary>
    Unit = 0,

    /// <summary>Grants a legion (own-once; payloadId = legion definition id).</summary>
    Legion = 1,

    /// <summary>Grants one gear piece (own-once in the shop; payloadId = gear definition id).</summary>
    Gear = 2,

    /// <summary>Grants <c>amount</c> gems (repeatable; idempotent via the gem ledger).</summary>
    GemBundle = 3,

    /// <summary>Grants <c>amount</c> Strikes (repeatable; idempotent via the strike ledger).</summary>
    StrikeRefill = 4,
}
