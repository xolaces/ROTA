namespace ROTA.Domain.Entities;

/// <summary>A direct message from one player to another (T37). Persisted so conversation history survives.</summary>
public class PrivateMessage
{
    private PrivateMessage() { }

    public static PrivateMessage Create(Guid senderId, Guid recipientId, string body)
        => new PrivateMessage
        {
            Id = Guid.NewGuid(),
            SenderId = senderId,
            RecipientId = recipientId,
            Body = body,
            SentAt = DateTimeOffset.UtcNow,
            IsRead = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
            IsDeleted = false,
        };

    public Guid Id { get; private set; }
    public Guid SenderId { get; private set; }
    public Guid RecipientId { get; private set; }
    public string Body { get; private set; } = string.Empty;
    public DateTimeOffset SentAt { get; private set; }
    public bool IsRead { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
    public bool IsDeleted { get; private set; }

    public void MarkRead()
    {
        if (IsRead) return;
        IsRead = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
