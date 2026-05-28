using ROTA.Domain.Enums;

namespace ROTA.Application.Models;

public class QuestDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Chapter { get; set; }
    public string NodeType { get; set; } = "Battle";   // Story | Battle | Boss
    public int BaseEnergyCost { get; set; }
    public float BaseXpRatio { get; set; } = 1.2f;
    public string? LootTableId { get; set; }
    public float SigilDropChance { get; set; }
    public Dictionary<string, string>? Sigils { get; set; }  // difficulty -> itemDefinitionId
    public int GoldReward { get; set; }
    public int ExperienceReward { get; set; }
    public int GemReward { get; set; }
    public string? PrerequisiteQuestId { get; set; }

    public bool IsBoss => NodeType.Equals("Boss", StringComparison.OrdinalIgnoreCase);
}
