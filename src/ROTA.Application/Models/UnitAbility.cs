namespace ROTA.Application.Models;

// Proc ability on a unit — same shape as MagicDefinition's proc fields.
// Fires in the hit proc phase (DamageProc-style only; passive/stat abilities
// are accounted for inside legionPower and must NOT roll here).
public class UnitAbility
{
    public double              ProcChance  { get; set; }   // 0..1
    public double              ProcAmount  { get; set; }   // fraction of preProc added as bonus damage
    public List<ConditionalBonus> Conditions { get; set; } = new();
}
