namespace ROTA.Domain.Enums;

/// <summary>
/// Tri-state result of spending Strikes (System 16). Mirrors <see cref="GemSpendOutcome"/>'s
/// idempotency discipline: <see cref="AlreadyCharged"/> (referenceId already debited) must NEVER
/// be conflated with <see cref="Insufficient"/> (balance too low), so a retry never double-debits.
/// </summary>
public enum StrikeSpendOutcome
{
    /// <summary>The −debit row was written; balance was sufficient.</summary>
    Charged = 1,

    /// <summary>Balance was below the requested amount; no row written.</summary>
    Insufficient = 2,

    /// <summary>The referenceId already exists — the original debit committed (idempotent replay).</summary>
    AlreadyCharged = 3,
}
