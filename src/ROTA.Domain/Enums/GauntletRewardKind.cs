namespace ROTA.Domain.Enums;

/// <summary>What a Gauntlet prize-band entry grants at settlement (System 16).</summary>
public enum GauntletRewardKind
{
    /// <summary>Gauntlet Tokens credited to the currency ledger.</summary>
    Tokens = 0,

    /// <summary>Pitchfork Tokens (top-rank-only second currency).</summary>
    Pitchfork = 1,

    /// <summary>A permanent legion-power trophy.</summary>
    Trophy = 2,

    /// <summary>A per-event rank magic (Wrath / Blessing).</summary>
    Magic = 3,
}
