namespace ROTA.Shared.DTOs;

// System 22 Phase A — Masteries read model. Ancient/ActivePledge/ActivityType are strings (the
// ROTA.Shared convention — DTOs never depend on Domain enums).

public class MasteryOverviewResponse
{
    public IReadOnlyList<MasteryAncientDto> Ancients { get; set; } = [];
    /// <summary>The currently-pledged Ancient name, or null if none pledged yet.</summary>
    public string? ActivePledge { get; set; }
    public MasteryRatingDto Rating { get; set; } = new();
    public MasteryTitlesDto Titles { get; set; } = new();
    public MasteryRespecStatusDto RespecStatus { get; set; } = new();
}

public class MasteryAncientDto
{
    public string Ancient { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Theme { get; set; } = string.Empty;
    public int Level { get; set; }
    public bool IsPledged { get; set; }
    /// <summary>The always-on global modifier percent at the current level.</summary>
    public double GlobalPercent { get; set; }
    /// <summary>The effective modifier percent (global, or pledged-doubled when this Ancient is pledged).</summary>
    public double EffectivePercent { get; set; }
    /// <summary>Progress toward the next level; null at max level.</summary>
    public MasteryTierProgressDto? NextTier { get; set; }
}

public class MasteryTierProgressDto
{
    public int FromLevel { get; set; }
    public int ToLevel { get; set; }
    public IReadOnlyList<MasteryChecklistItemDto> Items { get; set; } = [];
    /// <summary>True when every checklist item is satisfied (the next read/evaluation will level up).</summary>
    public bool Complete { get; set; }
}

public class MasteryChecklistItemDto
{
    public string ActivityType { get; set; } = string.Empty;
    /// <summary>Current cumulative count, capped at the threshold for display.</summary>
    public long Current { get; set; }
    public long Threshold { get; set; }
}

public class MasteryRatingDto
{
    /// <summary>Live rating computed from current levels (drives unlocks).</summary>
    public int Active { get; set; }
    /// <summary>High-water tenure marker. Equal to Active in Phase A (levels are monotonic).</summary>
    public int Lifetime { get; set; }
}

public class MasteryTitlesDto
{
    /// <summary>Highest earned breadth title, or null.</summary>
    public string? Breadth { get; set; }
    /// <summary>"Master of &lt;Ancient&gt;" for each maxed Ancient.</summary>
    public IReadOnlyList<string> Masteries { get; set; } = [];
}

public class MasteryRespecStatusDto
{
    public bool FreeMonthlyAvailable { get; set; }
    public bool PaidWeeklyAvailable { get; set; }
    public int GemCost { get; set; }
}

public class MasteryRatingRefreshResponse
{
    public int PlayersSnapshotted { get; set; }
    public DateTimeOffset SnapshotAt { get; set; }
}

// ADMIN/DEV force tool (System 22 Phase A) — set one or more Ancients' levels (1..5, up or down).
// Target null = the calling admin's own account (the common dev-test path).
public class MasteryForceRequest
{
    public string? TargetPlayerIdOrUsername { get; set; }
    /// <summary>Ancient name → level (1..5). Only listed Ancients are changed.</summary>
    public Dictionary<string, int> Levels { get; set; } = new();
}

// ── Re-spec / pledge (Slice 3) ────────────────────────────────────────────────

public enum MasteryRespecFailureCode
{
    None = 0,
    AncientNotFound,
    AlreadyPledged,
    WeeklyCapReached,
    InsufficientGems,
    PlayerNotFound,
}

public class PledgeRequest
{
    /// <summary>The Ancient to pledge (Wrath/Bulwark/Hoard/Discernment).</summary>
    public string Ancient { get; set; } = string.Empty;
}

public class MasteryRespecResult
{
    public bool Success { get; init; }
    public MasteryRespecFailureCode FailureCode { get; init; }
    public string? FailureReason { get; init; }
    /// <summary>How the swap was paid for (Paid / FreeMonthly / NewAncientUnlock).</summary>
    public string? Kind { get; init; }
    public int GemSpent { get; init; }
    public string? NewPledge { get; init; }

    public static MasteryRespecResult Ok(string kind, int gemSpent, string newPledge)
        => new() { Success = true, Kind = kind, GemSpent = gemSpent, NewPledge = newPledge };

    public static MasteryRespecResult Fail(MasteryRespecFailureCode code, string reason)
        => new() { Success = false, FailureCode = code, FailureReason = reason };
}
