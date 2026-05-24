namespace ROTA.Shared.DTOs;

public class QuestAvailabilityResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Chapter { get; set; }
    public int EnergyCost { get; set; }
    public int GoldReward { get; set; }
    public int ExperienceReward { get; set; }
    public int GemReward { get; set; }
    public string? PrerequisiteQuestId { get; set; }
    public int CompletionCount { get; set; }
    public DateTimeOffset? LastCompletedAt { get; set; }
}

public class QuestResultResponse
{
    public bool Success { get; set; }
    public QuestFailureCode FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public int GoldGranted { get; set; }
    public int ExperienceGranted { get; set; }
    public int GemsGranted { get; set; }
    public int NewLevel { get; set; }
    public long NewExperience { get; set; }
    public long NewGold { get; set; }
    public int CompletionCount { get; set; }
}

public enum QuestFailureCode
{
    None               = 0,
    QuestNotFound      = 1,
    PrerequisiteNotMet = 2,
    InsufficientEnergy = 3,
    PlayerNotFound     = 4,
    PlayerBanned       = 5,
}
