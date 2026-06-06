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
    // when it reaches 0 the node is Cleared (locked). A chapter-boss completion resets it (T26).
    public double Progress { get; private set; }
    // Current depletion state — gates attemptability. Resettable: a chapter-boss completion clears it
    // back to false (the node becomes fresh and attemptable again).
    public bool IsCleared { get; private set; }
    // Permanent latch — set the first time the node is ever cleared and NEVER reset. Gates unlock of
    // the next node (forward progression), so a chapter reset never re-locks already-earned content.
    public bool HasEverCleared { get; private set; }
    public DateTimeOffset? LastCompletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void RecordCompletion()
    {
        CompletionCount++;
        LastCompletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    // Drains the node by `amount`, floored at 0. Reaching 0 marks the node Cleared (locked) and
    // latches HasEverCleared permanently (the latter is never undone, even by a chapter reset).
    public void Deplete(double amount)
    {
        if (amount < 0) amount = 0;
        Progress = Math.Max(0, Progress - amount);
        if (Progress <= 0)
        {
            IsCleared = true;
            HasEverCleared = true;
        }
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    // Chapter-boss reset (T26): restore the node to fresh so it's attemptable again. HasEverCleared
    // is intentionally preserved so forward progression (next-node unlock) is never lost.
    public void Reset(double startProgress)
    {
        Progress = startProgress;
        IsCleared = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
