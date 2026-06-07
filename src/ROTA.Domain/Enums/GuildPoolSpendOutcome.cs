namespace ROTA.Domain.Enums;

/// <summary>
/// Tri-state result of spending from the guild sigil pool (System 21 Slice 3b). Mirrors
/// <see cref="StrikeSpendOutcome"/>'s idempotency discipline: <see cref="AlreadyCharged"/> (referenceId
/// already debited) must NEVER be conflated with <see cref="Insufficient"/> (pool too low), so a retry
/// never double-debits a summon.
/// </summary>
public enum GuildPoolSpendOutcome
{
    /// <summary>The −debit row was written; the pool balance was sufficient.</summary>
    Charged = 1,

    /// <summary>Pool balance was below the requested amount; no row written.</summary>
    Insufficient = 2,

    /// <summary>The referenceId already exists — the original debit committed (idempotent replay).</summary>
    AlreadyCharged = 3,
}
