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

    /// <summary>
    /// Settlement payout counts (System 16 Slice 5). Populated only on the settle path; null for
    /// open/close. Carried alongside the event so the existing controller/CLI shape is unchanged.
    /// </summary>
    public GauntletSettlementSummaryResponse? Settlement { get; init; }

    public static GauntletEventActionResult Ok(GauntletEventResponse ev)
        => new() { Success = true, Event = ev };
    public static GauntletEventActionResult Ok(
        GauntletEventResponse ev, GauntletSettlementSummaryResponse settlement)
        => new() { Success = true, Event = ev, Settlement = settlement };
    public static GauntletEventActionResult Fail(string reason)
        => new() { Success = false, FailureReason = reason };
}

/// <summary>
/// Settlement payout summary (System 16 Slice 5). Returned by <c>SettleEventAsync</c> and surfaced
/// by the admin settle endpoint + CLI. Counts are the number of grant ACTIONS performed this run —
/// because settlement is idempotent, a re-settle of an already-Settled event reports zeros (nothing
/// new was paid).
/// </summary>
public class GauntletSettlementSummaryResponse
{
    /// <summary>Number of prize-eligible entries (LastRank within PrizeRankCount) processed.</summary>
    public int RanksSettled { get; init; }

    /// <summary>Total Token amount credited across all bands this run.</summary>
    public long TokensGranted { get; init; }

    /// <summary>Total Pitchfork amount credited across all bands this run.</summary>
    public long PitchforkGranted { get; init; }

    /// <summary>Number of trophy upserts performed this run.</summary>
    public int TrophiesGranted { get; init; }

    /// <summary>Number of honor-echo records written for revoked event-magic holders this run.</summary>
    public int HonorsWritten { get; init; }
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

// ──────────────────────────────────────────────────────────────────────────────
// System 16 Slice 6 — token-shop DTOs (catalogue + purchase result).
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>One catalogue entry, hydrated with a caller-specific <see cref="AlreadyOwned"/> flag.</summary>
public class GauntletShopEntryResponse
{
    public string Id { get; init; } = string.Empty;

    /// <summary>Reward kind name (Unit/Legion/Gear/GemBundle/StrikeRefill).</summary>
    public string RewardKind { get; init; } = string.Empty;

    /// <summary>Definition id for Unit/Legion/Gear; empty for GemBundle/StrikeRefill.</summary>
    public string PayloadId { get; init; } = string.Empty;

    /// <summary>Gems (GemBundle) or Strikes (StrikeRefill) granted; 0 for own-once kinds.</summary>
    public int Amount { get; init; }

    /// <summary>Currency name (Token/Pitchfork) that funds the purchase.</summary>
    public string Currency { get; init; } = string.Empty;

    /// <summary>Price in <see cref="Currency"/>.</summary>
    public int Price { get; init; }

    /// <summary>Ownership cap; 1 for own-once kinds, null for repeatable bundles/refills.</summary>
    public int? MaxOwned { get; init; }

    /// <summary>
    /// True when the caller already owns this own-once payload (so a buy would return AlreadyOwned).
    /// Always false for repeatable kinds (GemBundle/StrikeRefill).
    /// </summary>
    public bool AlreadyOwned { get; init; }
}

/// <summary>GET /api/gauntlet/shop — the catalogue plus the caller's live Token + Pitchfork balances.</summary>
public class GauntletShopResponse
{
    public List<GauntletShopEntryResponse> Entries { get; init; } = new();
    public long TokenBalance { get; init; }
    public long PitchforkBalance { get; init; }
}

/// <summary>
/// Result of <c>BuyFromShopAsync</c>. The tri-state spend underneath is the explicit safeguard
/// against the magic/gem money-bug: <see cref="InsufficientTokens"/> (no charge, no grant) and
/// <see cref="AlreadyCharged"/> (idempotent re-grant, no re-charge) are never conflated.
/// </summary>
public class BuyShopResult
{
    /// <summary>True on a first-time purchase OR an idempotent replay (AlreadyCharged re-grant).</summary>
    public bool Success { get; init; }

    /// <summary>Own-once payload was already owned → no charge, no grant.</summary>
    public bool AlreadyOwned { get; init; }

    /// <summary>Balance in the entry's currency was insufficient → no charge, no grant.</summary>
    public bool InsufficientTokens { get; init; }

    /// <summary>
    /// The spend row already existed (lost-purchase replay): the original charge committed, so the
    /// payload was re-granted idempotently without re-charging. Reported alongside Success = true.
    /// </summary>
    public bool AlreadyCharged { get; init; }

    /// <summary>Human-readable reason on the unknown-entry (NotFound-style) failure path.</summary>
    public string? FailureReason { get; init; }

    public static BuyShopResult Ok(bool alreadyCharged = false)
        => new() { Success = true, AlreadyCharged = alreadyCharged };

    public static BuyShopResult OwnedAlready()
        => new() { Success = false, AlreadyOwned = true };

    public static BuyShopResult Insufficient()
        => new() { Success = false, InsufficientTokens = true };

    public static BuyShopResult Fail(string reason)
        => new() { Success = false, FailureReason = reason };
}
