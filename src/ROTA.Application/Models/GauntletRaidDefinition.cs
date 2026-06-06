namespace ROTA.Application.Models;

// BETA (System 16 Slice 1) — a Gauntlet raid stage, loaded from
// content/gauntlet_raids.json. The escalating ladder is a sequence of boss entries
// ordered by LadderStage (rising BaseHp); defeating stage N unlocks stage N+1 for the
// same player. Tier is always "Event" and GauntletScored is always true.
//
// Modeled as a DEDICATED type (not the shared RaidDefinition) so the ladder's required
// LadderStage ordering + Gauntlet-only validation stay isolated and the existing
// RaidDefinitionProvider / RaidService / raids.json are untouched. The summon/combat
// wiring that turns a stage into an ActiveRaid is Slice 2/4.
public class GauntletRaidDefinition
{
    public string Id   { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;

    // Always "Event" for Gauntlet raids.
    public string Tier { get; set; } = "Event";

    // 1-based ladder position; ascending, contiguous, unique within the file.
    public int LadderStage { get; set; }

    // Boss HP for this stage; must rise monotonically with LadderStage.
    public long BaseHp { get; set; }

    public int    TimerHours        { get; set; }
    public int    StaminaCostPerHit { get; set; } = 1;
    public string LootTableId       { get; set; } = string.Empty;
    public long   BaseGoldReward    { get; set; }
    public int    BaseExperienceReward { get; set; }
    public int    BaseGemReward     { get; set; }
    public string ArtKey            { get; set; } = string.Empty;

    // Always true — marks the raid's damage as Gauntlet-scored.
    public bool GauntletScored { get; set; } = true;
}
