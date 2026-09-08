namespace ROTA.Domain.Enums;

public enum MagicEffectType
{
    DamageProc,
    CritChanceFlat,
    GoldProc,
    XpProc,

    /// <summary>
    /// Adds FLAT Attack to every hit landed on the raid this magic is applied to, for as long as it
    /// is applied. <c>ProcAmount</c> carries the flat Attack value, not a multiplier, and there is no
    /// roll — it is always on.
    /// </summary>
    /// <remarks>
    /// PINNACLE-ONLY (owner 2026-09-07). Effect types beyond the ordinary four exist only on
    /// milestone-level <c>magic_pinnacle_*</c> magics; no other magic and no item may carry one until
    /// the owner says otherwise. <c>PinnacleEffectExclusivityTests</c> enforces this.
    ///
    /// Being flat rather than proportional is the point: it helps the WEAKEST participant most. A
    /// 500-Attack player gains a quarter of their damage from it; the level-1000 player who applied
    /// it gains about two percent. It narrows the veteran gap the research paper blames for killing
    /// Dawn of the Dragons' new-player acquisition, instead of widening it.
    /// </remarks>
    FlatAttackAura,
}
