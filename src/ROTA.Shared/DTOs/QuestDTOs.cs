namespace ROTA.Shared.DTOs;

public class QuestAvailabilityResponse
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int Chapter { get; set; }
    public string NodeType { get; set; } = string.Empty;
    public int BaseEnergyCost { get; set; }
    public int GoldReward { get; set; }
    public int ExperienceReward { get; set; }
    public int GemReward { get; set; }
    public string? PrerequisiteQuestId { get; set; }
    public int CompletionCount { get; set; }
    public DateTimeOffset? LastCompletedAt { get; set; }
    // Node depletion (System 20): Progress counts down from 100→0 over repeated attempts; the node
    // is Cleared at 0, which unlocks the next node. Client renders the depletion bar + cleared state.
    public double Progress { get; set; }
    public bool IsCleared { get; set; }
    public bool IsBossNode { get; set; }
    /// <summary>
    /// True when the player may attempt this node. The service only returns nodes whose
    /// prerequisite is satisfied, so every returned node is unlocked. The client gates the
    /// Attempt button on this flag — it must be sent explicitly (a missing field deserializes
    /// to false on the client and disables every Attempt button).
    /// </summary>
    public bool IsUnlocked { get; set; }
}

public class QuestAttemptRequest
{
    public string Difficulty { get; set; } = "Normal";
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
    public string Difficulty { get; set; } = string.Empty;
    public string DifficultyColor { get; set; } = string.Empty;
    public List<ItemGrantDTO> ItemsGranted { get; set; } = new();

    // XP progression detail
    public int XpGained { get; set; }
    public long CurrentLevelXp { get; set; }
    public int XpToNextLevel { get; set; }
    public int LevelsGained { get; set; }

    // Node depletion (System 20): the node's remaining Progress after this attempt, whether it is
    // now fully Cleared, and whether THIS attempt is the one that cleared it (for a client callout).
    public double NodeProgress { get; set; }
    public bool NodeCleared { get; set; }
    public bool NodeJustCleared { get; set; }
    // T26: true when this attempt completed a chapter boss and reset the whole chapter's nodes to
    // fresh (the deplete→clear→boss→reset farming cycle).
    public bool ChapterReset { get; set; }
}

public enum QuestFailureCode
{
    None               = 0,
    QuestNotFound      = 1,
    PrerequisiteNotMet = 2,
    InsufficientEnergy = 3,
    PlayerNotFound     = 4,
    PlayerBanned       = 5,
    DifficultyLocked   = 6,
    // T26: the node is cleared (locked) and can't be attempted until the chapter boss resets it.
    NodeCleared        = 7,
}
