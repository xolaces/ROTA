namespace ROTA.Domain.Entities;

public class PlayerQuestProgress
{
    // Required by EF Core
    private PlayerQuestProgress() { }

    public static PlayerQuestProgress Create(Guid playerId, string questId)
        => new PlayerQuestProgress
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            QuestId = questId,
            CompletionCount = 0,
            LastCompletedAt = null,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public string QuestId { get; private set; } = string.Empty;
    public int CompletionCount { get; private set; }
    public DateTimeOffset? LastCompletedAt { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void RecordCompletion()
    {
        CompletionCount++;
        LastCompletedAt = DateTimeOffset.UtcNow;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
