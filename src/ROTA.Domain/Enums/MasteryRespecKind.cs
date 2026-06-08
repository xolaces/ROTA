namespace ROTA.Domain.Enums;

/// <summary>
/// How a mastery re-spec (pledge change) was paid for (System 22 Phase A). Recorded on each
/// <c>MasteryRespecTransaction</c> for audit/analytics. Stored as int — append, never renumber.
/// </summary>
public enum MasteryRespecKind
{
    /// <summary>Paid with gems (weekly-capped).</summary>
    Paid = 0,

    /// <summary>Free monthly swap (once per calendar month).</summary>
    FreeMonthly = 1,

    /// <summary>Free first pledge to an Ancient (one per Ancient — also covers a future awakened Ancient).</summary>
    NewAncientUnlock = 2,
}
