namespace ROTA.Shared.DTOs;

public class PlayerProfileResponse
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int Level { get; set; }
    public string Class { get; set; } = string.Empty;
    // Current-level XP (carry-over remainder) — the numerator for the header's "x/xxxx TNL" display.
    public long Experience { get; set; }
    // XP required to reach the next level — the denominator for "x/xxxx TNL".
    public long XpToNextLevel { get; set; }
    public long Gold { get; set; }
    // Gem balance — never stored; SUMMED from the gem_transactions ledger (System 7) for header display.
    // long: the ledger SUM can exceed int32 over a no-reset lifetime (int32-overflow-audit).
    public long Gems { get; set; }
    public Guid? GuildId { get; set; }
    public string? GuildRank { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public IReadOnlyList<ResourceValueResponse> Resources { get; set; } = [];
    // int32-overflow-audit Unit 2 — effective combat stats widened to long (EffectiveCombatData is long).
    public long EffectiveAttack  { get; set; }
    public long EffectiveDefense { get; set; }
    // Masteries (System 22 Phase A) — the active pledge name (null if unpledged) + live Overall Mastery Rating.
    public string? ActivePledge { get; set; }
    public int MasteryRatingActive { get; set; }
    // Achievements (TICKET 46) — total Achievement Points, SUMMED from the award ledger.
    public int TotalAchievementPoints { get; set; }
}

public class ResourceValueResponse
{
    public string Type { get; set; } = string.Empty;
    public int LiveValue { get; set; }
    public int MaxValue { get; set; }
    /// <summary>Legacy stored field — vestigial, do not use for regen timers.</summary>
    public int RegenPerMinute { get; set; }
    /// <summary>
    /// Class-based regen rate: minutes required to regenerate one point.
    /// E.g. Conscript = 5.0 (one Energy/Stamina point every 5 minutes).
    /// Use this for client-side refill countdown timers.
    /// </summary>
    public double RegenMinutesPerPoint { get; set; }
    /// <summary>
    /// Seconds until the next point regenerates. 0 when the resource is already full.
    /// Derived from RegenMinutesPerPoint and the fractional elapsed time at snapshot.
    /// </summary>
    public int SecondsToNextPoint { get; set; }
}

public class UpdateUsernameRequest
{
    public string Username { get; set; } = string.Empty;
}

public class UpdateUsernameResponse
{
    public string Username { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}

public record UpdateDisplayNameRequest(string DisplayName);

public record UpdateDisplayNameResponse(string NewDisplayName, DateTimeOffset UpdatedAt);
