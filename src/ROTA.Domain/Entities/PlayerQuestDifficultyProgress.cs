using ROTA.Domain.Enums;

namespace ROTA.Domain.Entities;

public class PlayerQuestDifficultyProgress
{
    private PlayerQuestDifficultyProgress() { }

    public static PlayerQuestDifficultyProgress Create(Guid playerId, string questId, QuestDifficulty difficulty)
        => new()
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            QuestId = questId,
            Difficulty = difficulty,
            CompletionCount = 0,
            FirstCompletedAt = null,
            LastCompletedAt = null,
            FirstSigilDropped = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public string QuestId { get; private set; } = string.Empty;
    public QuestDifficulty Difficulty { get; private set; }
    public int CompletionCount { get; private set; }
    public DateTimeOffset? FirstCompletedAt { get; private set; }
    public DateTimeOffset? LastCompletedAt { get; private set; }
    public bool FirstSigilDropped { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void RecordCompletion()
    {
        CompletionCount++;
        if (FirstCompletedAt is null)
            FirstCompletedAt = DateTimeOffset.UtcNow;
        LastCompletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void MarkSigilDropped()
    {
        FirstSigilDropped = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
