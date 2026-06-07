namespace ROTA.Shared.DTOs;

/// <summary>
/// A chat message (T35 raid / T36 world) or a delivered PM mirror. The client maps <see cref="SenderRole"/>
/// to a colour/marker (reserved-name scheme: Moderator orange, Dev/Admin red).
/// </summary>
public class ChatMessageDto
{
    public Guid Id { get; init; }

    /// <summary>"World", "Raid", or "Guild".</summary>
    public string Scope { get; init; } = "World";

    /// <summary>The active raid id when <see cref="Scope"/> is "Raid".</summary>
    public string? RaidId { get; init; }

    public Guid SenderId { get; init; }
    public string SenderName { get; init; } = string.Empty;

    /// <summary>"Player" | "Moderator" | "Admin" — drives reserved-name colouring on the client.</summary>
    public string SenderRole { get; init; } = "Player";

    public string Body { get; init; } = string.Empty;
    public DateTimeOffset SentAt { get; init; }
}
