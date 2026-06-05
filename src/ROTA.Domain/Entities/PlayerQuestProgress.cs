namespace ROTA.Domain.Entities;

public class PlayerQuestProgress
{
    // Required by EF Core
    private PlayerQuestProgress() { }

    public static PlayerQuestProgress Create(Guid playerId, string questId, double startProgress = 100.0)
        => new PlayerQuestProgress
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            QuestId = questId,
            CompletionCount = 0,
            Progress = startProgress,
            IsCleared = false,
            LastCompletedAt = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public string QuestId { get; private set; } = string.Empty;
    public int CompletionCount { get; private set; }
    // Node depletion (System 20). Starts at the configured node value and counts down each attempt;
    // when it reaches 0 the node is permanently Cleared, which unlocks the next node in the chain.
    public double Progress { get; private set; }
    public bool IsCleared { get; private set; }
    public DateTimeOffset? LastCompletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void RecordCompletion()
    {
        CompletionCount++;
        LastCompletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    // Drains the node by `amount`, floored at 0. Reaching 0 latches IsCleared (one-way — the node
    // stays cleared and remains replayable for XP/drops/sigils).
    public void Deplete(double amount)
    {
        if (amount < 0) amount = 0;
        Progress = Math.Max(0, Progress - amount);
        if (Progress <= 0) IsCleared = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
