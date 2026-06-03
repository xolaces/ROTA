using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

// BETA — deterministic, singleton period-key resolver.
// All leaderboard writes and reads use this to agree on the current calendar bucket.
// ISO week logic uses System.Globalization.ISOWeek to handle the year-boundary case
// correctly (ISO year can differ from calendar year in late Dec / early Jan).
public interface IPeriodKeyResolver
{
    /// <summary>
    /// Returns the canonical period_key string for the given UTC instant and period granularity.
    /// <list type="bullet">
    ///   <item><see cref="LeaderboardPeriod.Live"/>    → "live"</item>
    ///   <item><see cref="LeaderboardPeriod.Daily"/>   → "day:yyyy-MM-dd"  (UTC date)</item>
    ///   <item><see cref="LeaderboardPeriod.Weekly"/>  → "week:yyyy-Www"   e.g. "week:2026-W23"</item>
    ///   <item><see cref="LeaderboardPeriod.Monthly"/> → "month:yyyy-MM"   (UTC calendar month)</item>
    /// </list>
    /// Input is converted to UTC before computation.
    /// </summary>
    string Resolve(DateTimeOffset utcNow, LeaderboardPeriod period);
}
