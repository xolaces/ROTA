using ROTA.Domain.Enums;

namespace ROTA.Application.Models;

public class MagicDefinition
{
    public string          Id          { get; set; } = string.Empty;
    public string          Name        { get; set; } = string.Empty;
    public string          Description { get; set; } = string.Empty;
    public ItemRarity      Rarity      { get; set; }
    public MagicCategory   Category    { get; set; }
    public MagicEffectType EffectType  { get; set; }
    public double          ProcChance  { get; set; }   // 0..1; ignored for CritChanceFlat (always-on)
    public double          ProcAmount  { get; set; }   // fraction/multiplier depending on EffectType
    public List<ConditionalBonus> Conditions { get; set; } = new();  // ownership scaling (empty for now)
    public bool            Stacks      { get; set; } = true;
    public string          IconPath    { get; set; } = string.Empty;
    public string          Acquisition { get; set; } = string.Empty;
    public int             GemPrice    { get; set; } = 0;  // 0 = not available in gem shop

    // BETA (System 16 Slice 1) — marks an off-cap Gauntlet aura (Wrath/Blessing) that
    // resolves OUTSIDE the shared five-magic MaxAggregateProcBonus cap. Combat does NOT
    // read this yet (wired in Slice 4); content + Gauntlet provider validation only.
    public bool            OffCap      { get; set; } = false;
}
