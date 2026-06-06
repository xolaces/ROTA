namespace ROTA.Shared.DTOs;

// --- requests ---

/// <summary>Friend request / removal target (username or player id).</summary>
public class FriendTargetRequest { public string Target { get; set; } = string.Empty; }

/// <summary>Block / unblock target.</summary>
public class BlockRequest { public string Target { get; set; } = string.Empty; }

/// <summary>Send a private message to a friend.</summary>
public class SendMessageRequest
{
    public string Target { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
}

/// <summary>Report a player (T37) — routes to a PlayerReport operator email.</summary>
public class ReportPlayerRequest
{
    public string Target { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string? Description { get; set; }
}

// --- responses ---

public class FriendDto
{
    public Guid FriendshipId { get; init; }
    public Guid PlayerId { get; init; }
    public string Username { get; init; } = string.Empty;
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>"Pending" or "Accepted".</summary>
    public string Status { get; init; } = string.Empty;

    /// <summary>True when this is an incoming pending request the player can accept.</summary>
    public bool IncomingRequest { get; init; }
}

public class BlockDto
{
    public Guid BlockedId { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
}

public class PrivateMessageDto
{
    public Guid Id { get; init; }
    public Guid SenderId { get; init; }
    public Guid RecipientId { get; init; }
    public string Body { get; init; } = string.Empty;
    public DateTimeOffset SentAt { get; init; }
    public bool IsRead { get; init; }
}

public class SendMessageResult
{
    public bool Success { get; init; }
    public string? FailureReason { get; init; }
    public PrivateMessageDto? Message { get; init; }

    public static SendMessageResult Ok(PrivateMessageDto m) => new() { Success = true, Message = m };
    public static SendMessageResult Fail(string reason) => new() { Success = false, FailureReason = reason };
}
