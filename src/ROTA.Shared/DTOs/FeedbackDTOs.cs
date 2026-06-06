namespace ROTA.Shared.DTOs;

/// <summary>In-game bug report / general feedback submission (T38).</summary>
public class FeedbackRequest
{
    /// <summary>"Bug" or "Feedback".</summary>
    public string Category { get; set; } = "Bug";

    /// <summary>Short subject line.</summary>
    public string Subject { get; set; } = string.Empty;

    /// <summary>Free-text description of the issue / feedback.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>The screen/context the player was on (optional).</summary>
    public string? Screen { get; set; }

    /// <summary>Lightweight game-state snapshot (level, class, resources, …) — optional.</summary>
    public Dictionary<string, string>? Snapshot { get; set; }
}

/// <summary>Acknowledgement returned after a feedback/bug submission.</summary>
public class FeedbackSubmittedResponse
{
    public Guid Id { get; init; }
    public string Message { get; init; } = "Thanks — your submission was received.";
}
