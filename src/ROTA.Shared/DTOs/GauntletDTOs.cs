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

    // T76 — event identity for the event page.
    /// <summary>Event family ("Neck" standard / "Ring" rare) — drives the prize set.</summary>
    public string Kind { get; init; } = "Neck";
    /// <summary>Sequential run number within this kind ("the 3rd Neck Gauntlet").</summary>
    public int RunNumber { get; init; }
    public string? LoreBlurb { get; init; }
    public string? BannerKey { get; init; }
    /// <summary>Server-computed seconds until EndsAt (0 when ended) — drives the countdown.</summary>
    public long SecondsRemaining { get; init; }

    /// <summary>
    /// T76 — server-computed seconds until StartsAt (0 once started). Non-zero means the event is
    /// visible but not yet playable — the Home CTA's "Coming Soon" state.
    /// </summary>
    public long SecondsUntilStart { get; init; }
}

/// <summary>A player's standing in an event.</summary>
public class GauntletEntryResponse
{
    public Guid Id { get; init; }
    public Guid GauntletEventId { get; init; }
    public Guid PlayerId { get; init; }
    /// <summary>League name (Whelpling/Wyrm/Dragon/Ancient) — locked at first join.</summary>
    public string League { get; init; } = string.Empty;
    public long Score { get; init; }
    /// <summary>T76 — highest ladder stage defeated this event (the primary ranking metric).</summary>
    public int HighestStage { get; init; }
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

    /// <summary>
    /// T76 — the caller's result in the most recently SETTLED event (any kind), or null if no event
    /// has settled or the caller had no entry in it. Drives the "you placed #N" settlement card and
    /// the Home CTA's Settled state.
    /// </summary>
    public GauntletPlayerSettlementResponse? LastSettlement { get; init; }
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

    // T76 — event identity. Kind: "Neck" (default, standard run) or "Ring" (rare run).
    public string Kind { get; set; } = "Neck";
    public string? LoreBlurb { get; set; }
    public string? BannerKey { get; set; }
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
// System 16 Slice 7 — Gauntlet ladder (auto-advance climb) DTO.
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// GET /api/gauntlet/ladder — the player's current Gauntlet ladder target. The ladder auto-advances:
/// after defeating the current stage, the next call lazily spawns the next (tankier) stage. Exactly
/// one of {NoActiveEvent, JoinedRequired, Complete, ActiveRaid present} describes the state.
/// </summary>
public class GauntletLadderResponse
{
    /// <summary>The current target stage as a standard raid (null when none — see the flags below).</summary>
    public ActiveRaidResponse? ActiveRaid { get; init; }

    /// <summary>
    /// 1-based ladder position of <see cref="ActiveRaid"/> (0 when no current stage is targetable —
    /// no event / not joined / ladder complete).
    /// </summary>
    public int CurrentStage { get; init; }

    /// <summary>Total stages in the ladder (the finite ceiling; tunable via content/gauntlet_raids.json).</summary>
    public int StageCount { get; init; }

    /// <summary>True when the player has cleared the final stage — nothing left to climb this event.</summary>
    public bool Complete { get; init; }

    /// <summary>True when there is an active event but the player must join it before climbing.</summary>
    public bool JoinedRequired { get; init; }

    /// <summary>True when no Gauntlet event is currently active.</summary>
    public bool NoActiveEvent { get; init; }

    /// <summary>
    /// T76 — true when an event exists but its StartsAt is still in the future (Coming Soon): no
    /// stage is spawned and the ladder is not climbable until the event window opens.
    /// </summary>
    public bool NotStarted { get; init; }
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

    /// <summary>T76 — the caller's highest defeated stage, or null if they have no entry.</summary>
    public int? YourHighestStage { get; init; }

    /// <summary>Total ranked entries in this league (entries with a non-null snapshot rank).</summary>
    public int TotalRanked { get; init; }
}

/// <summary>One row on the Gauntlet leaderboard.</summary>
public class GauntletLeaderboardEntryDto
{
    public int Rank { get; init; }
    public Guid PlayerId { get; init; }
    public string DisplayName { get; init; } = string.Empty;
    /// <summary>T76 — highest ladder stage defeated (the primary ranking metric).</summary>
    public int HighestStage { get; init; }
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

// ──────────────────────────────────────────────────────────────────────────────
// T76 — prize preview table + per-player settlement summary (System 24).
// ──────────────────────────────────────────────────────────────────────────────

/// <summary>
/// GET /api/gauntlet/prizes — the kind-aware rank-prize bands for the event page's prize preview.
/// Ring events without an authored ring set serve the Neck bands with the rank magic stripped.
/// </summary>
public class GauntletPrizeTableResponse
{
    /// <summary>Event family the bands apply to ("Neck"/"Ring").</summary>
    public string Kind { get; init; } = "Neck";
    public List<GauntletPrizeBandResponse> Bands { get; init; } = new();
}

/// <summary>One rank band of the prize table, hydrated with display names for the client.</summary>
public class GauntletPrizeBandResponse
{
    public int RankFrom { get; init; }
    public int RankTo { get; init; }
    public int Tokens { get; init; }
    public int Pitchfork { get; init; }
    public string? TrophyId { get; init; }
    public string? TrophyName { get; init; }
    /// <summary>Seasonal rank magic (Neck only) — held until the next Neck Gauntlet opens.</summary>
    public string? MagicId { get; init; }
    public string? MagicName { get; init; }
}

/// <summary>
/// The caller's result in the most recently settled event — "you placed #N — won X". Prize fields
/// are the band the final rank landed in (zeros/nulls when the rank fell outside every band).
/// </summary>
public class GauntletPlayerSettlementResponse
{
    public Guid EventId { get; init; }
    public string EventName { get; init; } = string.Empty;
    /// <summary>Event family ("Neck"/"Ring") the result belongs to.</summary>
    public string Kind { get; init; } = "Neck";
    public int RunNumber { get; init; }
    public DateTimeOffset? SettledAt { get; init; }

    /// <summary>League name (Whelpling/Wyrm/Dragon/Ancient) the caller competed in.</summary>
    public string League { get; init; } = string.Empty;
    /// <summary>Final snapshot rank within the league (null only if settlement never ranked the entry).</summary>
    public int? FinalRank { get; init; }
    public int HighestStage { get; init; }
    public long Score { get; init; }

    /// <summary>True when the final rank landed inside a prize band (something below was awarded).</summary>
    public bool WonPrizes { get; init; }
    public int TokensAwarded { get; init; }
    public int PitchforkAwarded { get; init; }
    public string? TrophyId { get; init; }
    public string? TrophyName { get; init; }
    /// <summary>Rank magic earned (Neck only) — granted when the NEXT Neck Gauntlet opens, held for that run.</summary>
    public string? MagicId { get; init; }
    public string? MagicName { get; init; }
}
