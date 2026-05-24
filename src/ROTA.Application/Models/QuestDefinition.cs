namespace ROTA.Application.Models;

/// <summary>
/// Represents a quest loaded from content/quests.json at startup.
/// This is static game content — not stored in the database.
/// </summary>
public class QuestDefinition
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Chapter { get; set; }
    public int EnergyCost { get; set; }
    public int GoldReward { get; set; }
    public int ExperienceReward { get; set; }
    public int GemReward { get; set; }
    public string? PrerequisiteQuestId { get; set; }
}
