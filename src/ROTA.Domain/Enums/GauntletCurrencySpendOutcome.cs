namespace ROTA.Domain.Enums;

/// <summary>
/// Tri-state result of spending a Gauntlet currency (Token/Pitchfork, System 16). Same
/// idempotency discipline as <see cref="StrikeSpendOutcome"/> / <see cref="GemSpendOutcome"/>.
/// </summary>
public enum GauntletCurrencySpendOutcome
{
    /// <summary>The −debit row was written; balance was sufficient.</summary>
    Charged = 1,

    /// <summary>Balance for the currency was below the requested amount; no row written.</summary>
    Insufficient = 2,

    /// <summary>The referenceId already exists for that currency — the original debit committed.</summary>
    AlreadyCharged = 3,
}
