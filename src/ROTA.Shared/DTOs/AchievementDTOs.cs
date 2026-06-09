namespace ROTA.Shared.DTOs;

/// <summary>Response for GET /api/achievements (TICKET 46): the player's total AP + per-achievement state.</summary>
public class AchievementOverviewResponse
{
    /// <summary>Total Achievement Points = SUM over the award ledger.</summary>
    public int TotalPoints { get; set; }

    public IReadOnlyList<AchievementEntryDto> Achievements { get; set; } = [];
}

/// <summary>One achievement's definition + the caller's progress toward it.</summary>
public class AchievementEntryDto
{
    public string Id { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Metric { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Points { get; set; }
    public long Threshold { get; set; }

    /// <summary>Current metric counter, clamped to the threshold for display.</summary>
    public long Progress { get; set; }

    public bool IsCompleted { get; set; }
    public DateTimeOffset? CompletedAt { get; set; }
    public string? IconKey { get; set; }
}
