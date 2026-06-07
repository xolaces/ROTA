namespace ROTA.Shared.DTOs;

// ──────────────────────────────────────────────────────────────────────────────
// System 16 Slice 2 — Gauntlet DTOs (event lifecycle, join, strike economy).
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>A Gauntlet event's public shape.</summary>
public class GauntletEventResponse
{
    public Guid Id { get; init; }
    public string Name { get; init; } = string.Empty;
    /// <summary>Lifecycle state name (Scheduled/Active/Closed/Settled).</summary>
    public string State { get; init; } = string.Empty;
    public DateTimeOffset StartsAt { get; init; }
    public DateTimeOffset EndsAt { get; init; }
    public DateTimeOffset? SettledAt { get; init; }
}

/// <summary>A player's standing in an event.</summary>
public class GauntletEntryResponse
{
    public Guid Id { get; init; }
    public Guid GauntletEventId { get; init; }
    public Guid PlayerId { get; init; }
    /// <summary>League name (Whelpling/Wyrm/Dragon) — locked at first join.</summary>
    public string League { get; init; } = string.Empty;
    public long Score { get; init; }
    public DateTimeOffset TieBreakAt { get; init; }
    public int? LastRank { get; init; }
}

/// <summary>Current Strike balance for a player.</summary>
public class StrikeBalanceResponse
{
    public long Balance { get; init; }
}

/// <summary>Balance for a single Gauntlet currency.</summary>
public class GauntletCurrencyBalanceResponse
{
    /// <summary>Currency name (Token/Pitchfork).</summary>
    public string Currency { get; init; } = string.Empty;
    public long Balance { get; init; }
}

/// <summary>
/// GET /api/gauntlet — the current event (null if none active), the caller's entry (null if not
/// joined), and the caller's strike + Token + Pitchfork balances.
/// </summary>
public class GauntletOverviewResponse
{
    public GauntletEventResponse? CurrentEvent { get; init; }
    public GauntletEntryResponse? MyEntry { get; init; }
    public long StrikeBalance { get; init; }
    public long TokenBalance { get; init; }
    public long PitchforkBalance { get; init; }
}

// ── Requests ───────────────────────────────────────────────────────────────────

/// <summary>POST /api/gauntlet/strikes/buy — buy <see cref="Strikes"/> with gems (uncapped).</summary>
public class BuyStrikesRequest
{
    /// <summary>Number of Strikes to buy (must be &gt; 0).</summary>
    public int Strikes { get; set; }

    /// <summary>
    /// Client-supplied idempotency key. The same key replays the purchase without double-charging
    /// or double-crediting (referenceId = strikebuy:{playerId}:{idempotencyKey}).
    /// </summary>
    public string IdempotencyKey { get; set; } = string.Empty;
}

/// <summary>POST /api/admin/gauntlet/events — open (create + activate) a new event.</summary>
public class OpenGauntletEventRequest
{
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset StartsAt { get; set; }
    public DateTimeOffset EndsAt { get; set; }
}

// ── Results ──────────────────────────────────────────────────────────────────

/// <summary>Result of <c>JoinEventAsync</c> — the entry on success, a reason on failure.</summary>
public class JoinGauntletResult
{
    public bool Success { get; init; }
    public string? FailureReason { get; init; }
    public GauntletEntryResponse? Entry { get; init; }

    public static JoinGauntletResult Ok(GauntletEntryResponse entry)
        => new() { Success = true, Entry = entry };
    public static JoinGauntletResult Fail(string reason)
        => new() { Success = false, FailureReason = reason };
}

/// <summary>Result of <c>BuyStrikesAsync</c>.</summary>
public class BuyStrikesResult
{
    public bool Success { get; init; }
    public string? FailureReason { get; init; }
    /// <summary>Gem cost charged (or that would have been charged).</summary>
    public int GemCost { get; init; }
    /// <summary>The player's Strike balance after the purchase (on success).</summary>
    public long NewStrikeBalance { get; init; }

    public static BuyStrikesResult Ok(int gemCost, long newBalance)
        => new() { Success = true, GemCost = gemCost, NewStrikeBalance = newBalance };
    public static BuyStrikesResult Fail(string reason, int gemCost)
        => new() { Success = false, FailureReason = reason, GemCost = gemCost };
}

/// <summary>Result of an admin lifecycle action (open/close/settle).</summary>
public class GauntletEventActionResult
{
    public bool Success { get; init; }
    public string? FailureReason { get; init; }
    public GauntletEventResponse? Event { get; init; }

    public static GauntletEventActionResult Ok(GauntletEventResponse ev)
        => new() { Success = true, Event = ev };
    public static GauntletEventActionResult Fail(string reason)
        => new() { Success = false, FailureReason = reason };
}

// ──────────────────────────────────────────────────────────────────────────────
// System 16 Slice 3 — leaderboard / scoring DTOs.
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// GET /api/gauntlet/leaderboard?league= — the snapshot-ranked page for a single league plus the
/// caller's own standing (regardless of position). Ranks are drawn from the ~60s Postgres snapshot
/// (GauntletEntry.LastRank); they are NOT recomputed on read.
/// </summary>
public class GauntletLeaderboardResponse
{
    /// <summary>League name (Whelpling/Wyrm/Dragon) the board is scoped to.</summary>
    public string League { get; init; } = string.Empty;

    /// <summary>Top <c>LeaderboardPageSize</c> entries for the league, ordered by snapshot rank ASC.</summary>
    public List<GauntletLeaderboardEntryDto> Entries { get; init; } = new();

    /// <summary>The caller's snapshot rank in this league+event, or null if they have no entry.</summary>
    public int? YourRank { get; init; }

    /// <summary>The caller's current score in this league+event, or null if they have no entry.</summary>
    public long? YourScore { get; init; }

    /// <summary>Total ranked entries in this league (entries with a non-null snapshot rank).</summary>
    public int TotalRanked { get; init; }
}

/// <summary>One row on the Gauntlet leaderboard.</summary>
public class GauntletLeaderboardEntryDto
{
    public int Rank { get; init; }
    public Guid PlayerId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    public long Score { get; init; }
}
