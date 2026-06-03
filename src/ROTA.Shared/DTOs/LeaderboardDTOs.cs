namespace ROTA.Shared.DTOs;

// BETA — read DTOs for System 17 Global Leaderboards.
// Board and Period are serialised as their enum name strings (matching the convention used
// by all other DTOs in ROTA.Shared — e.g. RaidDTOs uses string Difficulty / Tier).
// ROTA.Shared has no reference to ROTA.Domain (Domain→Shared is one-way).

/// <summary>A single ranked entry on a leaderboard page.</summary>
public sealed class LeaderboardEntryDto
{
    /// <summary>1-based rank computed on read (ORDER BY value DESC, last_progress_at ASC).</summary>
    public int Rank { get; init; }

    public Guid PlayerId { get; init; }

    /// <summary>Player.DisplayName; falls back to Player.Username if DisplayName is empty.</summary>
    public string DisplayName { get; init; } = string.Empty;

    /// <summary>Accumulated or best value for this board (energy spent, damage dealt, largest hit, etc.).</summary>
    public long Value { get; init; }
}

/// <summary>A page of ranked entries for a board + period window.</summary>
public sealed class LeaderboardPageResponse
{
    /// <summary>Board name, e.g. "StatAttack", "DamageDealt".</summary>
    public string Board { get; init; } = string.Empty;

    /// <summary>Period granularity, e.g. "Live", "Daily", "Weekly", "Monthly".</summary>
    public string Period { get; init; } = string.Empty;

    /// <summary>Canonical period key, e.g. "week:2026-W23", "day:2026-06-02", "month:2026-06", "live".</summary>
    public string PeriodKey { get; init; } = string.Empty;

    /// <summary>1-based page number that was returned.</summary>
    public int Page { get; init; }

    /// <summary>Max entries per page (from LeaderboardConfig.PageSize).</summary>
    public int PageSize { get; init; }

    /// <summary>Total number of ELIGIBLE ranked entries for this board + period_key.</summary>
    public int TotalRanked { get; init; }

    public List<LeaderboardEntryDto> Entries { get; init; } = new();

    /// <summary>Caller's own rank+value on this board, or null when the caller has no entry or is ineligible.</summary>
    public LeaderboardEntryDto? You { get; init; }
}

/// <summary>Discovery item returned by GET /api/leaderboards.</summary>
public sealed class LeaderboardSummary
{
    /// <summary>Board name, e.g. "StatAttack", "DamageDealt".</summary>
    public string Board { get; init; } = string.Empty;

    /// <summary>Human-readable board title.</summary>
    public string Title { get; init; } = string.Empty;

    /// <summary>Valid period granularity names for this board, e.g. ["Weekly", "Monthly"].</summary>
    public List<string> Periods { get; init; } = new();

    /// <summary>Current period_key for this board's active period (for client display).</summary>
    public string CurrentPeriodKey { get; init; } = string.Empty;
}
