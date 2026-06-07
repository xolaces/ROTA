namespace ROTA.Domain.Enums;

/// <summary>
/// Discriminator for Strike-ledger rows (System 16). Strikes are an earned-first,
/// persistent action currency; balance is SUM(amount). Strikes carry over across events
/// and are never reset.
/// </summary>
public enum StrikeTransactionType
{
    /// <summary>Credit for defeating a Gauntlet raid stage.</summary>
    RaidDefeat = 0,

    /// <summary>Credit from a gem-funded purchase (uncapped).</summary>
    GemPurchase = 1,

    /// <summary>Debit when spending Strikes on a hit (cost scales with hit size).</summary>
    HitSpend = 2,

    /// <summary>Low-rate credit dropped by special raids (Phase 2 earning path).</summary>
    SpecialRaidDrop = 3,

    /// <summary>
    /// Credit from a Gauntlet token-shop StrikeRefill purchase (System 16 Slice 6) — paid for with
    /// Token/Pitchfork, not gems. Idempotent via the shop referenceId guarded by ReferenceExistsAsync.
    /// </summary>
    ShopRefill = 4,
}
