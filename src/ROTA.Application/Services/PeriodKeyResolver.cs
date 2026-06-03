using System.Globalization;
using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;

namespace ROTA.Application.Services;

// BETA — singleton; validates LeaderboardConfig at construction so misconfiguration
// surfaces at startup rather than at first board read/write.
public sealed class PeriodKeyResolver : IPeriodKeyResolver
{
    // Kept for potential future Timezone support; v1 enforces UTC.
    private readonly LeaderboardConfig _config;

    public PeriodKeyResolver(IOptions<LeaderboardConfig> options)
    {
        _config = options.Value;
        Validate(_config);
    }

    /// <inheritdoc />
    public string Resolve(DateTimeOffset utcNow, LeaderboardPeriod period)
    {
        // Always operate on the UTC component — callers may pass a local-offset value.
        var utc = utcNow.UtcDateTime;

        return period switch
        {
            LeaderboardPeriod.Live    => "live",
            LeaderboardPeriod.Daily   => $"day:{utc:yyyy-MM-dd}",
            LeaderboardPeriod.Weekly  => FormatIsoWeekKey(utc),
            LeaderboardPeriod.Monthly => $"month:{utc:yyyy-MM}",
            _ => throw new ArgumentOutOfRangeException(nameof(period), period,
                     $"Unknown LeaderboardPeriod '{period}'.")
        };
    }

    // ── Private helpers ──────────────────────────────────────────────────────

    private static string FormatIsoWeekKey(DateTime utc)
    {
        // System.Globalization.ISOWeek correctly handles the year-boundary case:
        // e.g. 2025-12-29 is in ISO week 2026-W01, not 2025-W53.
        var isoYear = ISOWeek.GetYear(utc);
        var isoWeek = ISOWeek.GetWeekOfYear(utc);
        return $"week:{isoYear}-W{isoWeek:D2}";
    }

    private static void Validate(LeaderboardConfig cfg)
    {
        // v1 only supports UTC. Other timezones would require converting period boundaries,
        // which is deferred. Throw clearly at startup so misconfiguration is visible.
        if (!string.Equals(cfg.Timezone, "UTC", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException(
                $"LeaderboardConfig.Timezone must be \"UTC\" in v1. Got: \"{cfg.Timezone}\". " +
                "Non-UTC timezone support is deferred.");

        if (cfg.MinLevel < 1)
            throw new InvalidOperationException(
                $"LeaderboardConfig.MinLevel must be >= 1. Got: {cfg.MinLevel}.");

        if (cfg.PageSize < 1)
            throw new InvalidOperationException(
                $"LeaderboardConfig.PageSize must be >= 1. Got: {cfg.PageSize}.");

        // WeekStartsOn must be a valid enum member (guards against garbage appsettings values).
        if (!Enum.IsDefined(typeof(LeaderboardWeekStart), cfg.WeekStartsOn))
            throw new InvalidOperationException(
                $"LeaderboardConfig.WeekStartsOn has an unrecognised value: {cfg.WeekStartsOn}. " +
                "Valid values are Monday or Sunday.");
    }
}
