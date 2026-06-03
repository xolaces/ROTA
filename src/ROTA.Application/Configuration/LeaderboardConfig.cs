using ROTA.Domain.Enums;

namespace ROTA.Application.Configuration;

// BETA — leaderboard configuration bound from appsettings "LeaderboardConfig" section.
// Startup-validated by IPeriodKeyResolver registration: throws InvalidOperationException
// on any invalid combination (Timezone != "UTC", MinLevel < 1, PageSize < 1).
public class LeaderboardConfig
{
    // Q1 — LOCKED Calendar
    public LeaderboardWindowMode WindowMode { get; set; } = LeaderboardWindowMode.Calendar;

    // Q2 — LOCKED Monday (ISO week start)
    public LeaderboardWeekStart WeekStartsOn { get; set; } = LeaderboardWeekStart.Monday;

    // Q2 — LOCKED UTC; v1 only supports "UTC" — any other value throws at startup
    public string Timezone { get; set; } = "UTC";

    // Q4 — three per-stat live ladders (ATK / DEF / Discernment)
    public LeaderboardStatMetric[] StatMetric { get; set; } =
        [LeaderboardStatMetric.RawAttack, LeaderboardStatMetric.RawDefense, LeaderboardStatMetric.Discernment];

    // Q5 — stored live snapshot rows (Period=Live) in leaderboard_entry table
    public bool StatBoardEnabled { get; set; } = true;

    // Q6 — LOCKED Energy only (Stamina/GuildStamina excluded)
    public LeaderboardEnergyCount[] EnergyCounts { get; set; } =
        [LeaderboardEnergyCount.Energy];

    // Q7 — LOCKED all raids (World/Event/Standard/Guild)
    public LeaderboardMaxHitScope MaxHitRaidScope { get; set; } = LeaderboardMaxHitScope.AllRaids;

    // Q14 — LOCKED 200 entries per page
    public int PageSize { get; set; } = 200;

    // Q12 — LOCKED earliest-to-reach (LastProgressAt ASC on tie)
    public LeaderboardTieBreak TieBreak { get; set; } = LeaderboardTieBreak.EarliestToReach;

    // Q13 — LOCKED on-read ORDER BY (no precomputed rank column required in v1)
    public LeaderboardRankRefresh RankRefresh { get; set; } = LeaderboardRankRefresh.OnRead;

    // Q3 — LOCKED keep closed periods forever
    public bool RetainClosedPeriods { get; set; } = true;

    // Q10 — LOCKED global only (no per-league split)
    public bool Global { get; set; } = true;

    // Q11 — LOCKED L20 floor
    public int MinLevel { get; set; } = 20;

    // Q11 — LOCKED exclude Admin accounts; Moderators appear as regular players
    public bool ExcludeAdmins { get; set; } = true;
}

// ── Config-local supporting enums ───────────────────────────────────────────

public enum LeaderboardWindowMode
{
    Calendar = 0,   // LOCKED — calendar day/week/month buckets in UTC
}

public enum LeaderboardWeekStart
{
    Monday = 1,     // LOCKED — ISO week; DayOfWeek.Monday = 1
    Sunday = 0,     // Not used in v1 but present to keep enum meaningful
}

public enum LeaderboardStatMetric
{
    RawAttack    = 0,
    RawDefense   = 1,
    Discernment  = 2,
}

public enum LeaderboardEnergyCount
{
    Energy       = 0,   // ResourceType.Energy only
    Stamina      = 1,   // PHASE-2
    GuildStamina = 2,   // PHASE-2
}

public enum LeaderboardMaxHitScope
{
    AllRaids = 0,   // LOCKED — World + Event + Standard + Guild
}

public enum LeaderboardTieBreak
{
    EarliestToReach = 0,  // LOCKED — LastProgressAt ASC on equal value
}

public enum LeaderboardRankRefresh
{
    OnRead = 0,   // LOCKED — ORDER BY on read; no precomputed rank column
}
