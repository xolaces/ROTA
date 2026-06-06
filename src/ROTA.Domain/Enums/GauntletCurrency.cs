namespace ROTA.Domain.Enums;

/// <summary>
/// Discriminator on the shared Gauntlet currency ledger (System 16). Separate from gems;
/// balance is computed per currency as SUM(amount) WHERE currency = X.
/// </summary>
public enum GauntletCurrency
{
    /// <summary>Gauntlet Tokens — earned per raid defeat + rank-band bonus; spent in the token shop.</summary>
    Token = 0,

    /// <summary>Pitchfork Tokens — top-rank-only second currency.</summary>
    Pitchfork = 1,
}
