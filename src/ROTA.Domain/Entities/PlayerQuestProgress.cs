using ROTA.Domain.Enums;

namespace ROTA.Domain.Entities;

public class PlayerQuestProgress
{
    // Required by EF Core
    private PlayerQuestProgress() { }

    public static PlayerQuestProgress Create(
        Guid playerId, string questId, QuestDifficulty difficulty, double startProgress = 100.0)
        => new PlayerQuestProgress
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            QuestId = questId,
            Difficulty = difficulty,
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
    // Per-difficulty depletion track (triage node-depletion-per-difficulty, owner 2026-06-23). Each
    // (player, quest, difficulty) has its OWN independent Progress / IsCleared / HasEverCleared /
    // CompletionCount, so a cheap-difficulty grind can no longer deplete an expensive-difficulty node
    // (the shared-row sigil exploit). Owner-confirmed "per-difficulty ladder": HasEverCleared (the
    // forward map-unlock latch) is also per-difficulty — each tier is progressed in order on its own.
    public QuestDifficulty Difficulty { get; private set; }
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
