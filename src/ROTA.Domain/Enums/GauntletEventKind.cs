namespace ROTA.Domain.Enums;

/// <summary>
/// T76 — the two Gauntlet event families (owner-locked 2026-06-10). Seasonal rank rewards are
/// removed when the NEXT event of the SAME kind opens (DotD "removed each time X Gauntlet is
/// summoned" semantics).
/// </summary>
public enum GauntletEventKind
{
    /// <summary>The standard Gauntlet: neck-slot rank gear + rank magics. The common run.</summary>
    Neck = 0,

    /// <summary>The rare Gauntlet (~every 3rd run): ring-slot rank gear, NO rank magics.</summary>
    Ring = 1,
}
