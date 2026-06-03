using FluentAssertions;
using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Services;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Services;

public class PeriodKeyResolverTests
{
    // ── Helpers ──────────────────────────────────────────────────────────────

    private static PeriodKeyResolver Build(Action<LeaderboardConfig>? mutate = null)
    {
        var cfg = new LeaderboardConfig();
        mutate?.Invoke(cfg);
        return new PeriodKeyResolver(Options.Create(cfg));
    }

    private static DateTimeOffset Utc(int year, int month, int day,
        int hour = 0, int minute = 0, int second = 0)
        => new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.Zero);

    // ── Live ─────────────────────────────────────────────────────────────────

    [Fact]
    public void Live_AlwaysReturnsLiteral()
    {
        var resolver = Build();
        resolver.Resolve(Utc(2026, 6, 2), LeaderboardPeriod.Live).Should().Be("live");
        resolver.Resolve(Utc(2026, 1, 1), LeaderboardPeriod.Live).Should().Be("live");
        resolver.Resolve(Utc(2026, 12, 31, 23, 59, 59), LeaderboardPeriod.Live).Should().Be("live");
    }

    // ── Daily boundary ───────────────────────────────────────────────────────

    [Fact]
    public void Daily_LastSecondOfDay_IsInThatDay()
    {
        var resolver = Build();
        // 23:59:59 UTC on 2026-06-02 should still be "day:2026-06-02"
        resolver.Resolve(Utc(2026, 6, 2, 23, 59, 59), LeaderboardPeriod.Daily)
                .Should().Be("day:2026-06-02");
    }

    [Fact]
    public void Daily_FirstSecondOfNextDay_IsInNextDay()
    {
        var resolver = Build();
        // 00:00:00 UTC on 2026-06-03 should be "day:2026-06-03"
        resolver.Resolve(Utc(2026, 6, 3, 0, 0, 0), LeaderboardPeriod.Daily)
                .Should().Be("day:2026-06-03");
    }

    [Fact]
    public void Daily_MidnightBoundaryTransitionsCorrectly()
    {
        var resolver = Build();
        var lastSecond = Utc(2026, 6, 2, 23, 59, 59);
        var firstSecond = Utc(2026, 6, 3, 0, 0, 0);

        var keyBefore = resolver.Resolve(lastSecond, LeaderboardPeriod.Daily);
        var keyAfter  = resolver.Resolve(firstSecond, LeaderboardPeriod.Daily);

        keyBefore.Should().NotBe(keyAfter, "adjacent seconds straddle the day boundary");
        keyBefore.Should().Be("day:2026-06-02");
        keyAfter.Should().Be("day:2026-06-03");
    }

    // ── Weekly boundary ──────────────────────────────────────────────────────

    [Fact]
    public void Weekly_Monday_StartsNewWeek()
    {
        var resolver = Build();
        // ISO week 2026-W23 starts on Monday 2026-06-01
        // Sunday 2026-05-31 is the last day of W22
        resolver.Resolve(Utc(2026, 5, 31, 23, 59, 59), LeaderboardPeriod.Weekly)
                .Should().Be("week:2026-W22");
        resolver.Resolve(Utc(2026, 6, 1, 0, 0, 0), LeaderboardPeriod.Weekly)
                .Should().Be("week:2026-W23");
    }

    [Fact]
    public void Weekly_LastSecondOfWeekVsFirstSecondNextWeek_AreAdjacentKeys()
    {
        var resolver = Build();
        // Sunday 2026-06-07 23:59:59 — last second of W23
        // Monday 2026-06-08 00:00:00 — first second of W24
        var lastSecond  = Utc(2026, 6, 7, 23, 59, 59);
        var firstSecond = Utc(2026, 6, 8, 0, 0, 0);

        resolver.Resolve(lastSecond, LeaderboardPeriod.Weekly).Should().Be("week:2026-W23");
        resolver.Resolve(firstSecond, LeaderboardPeriod.Weekly).Should().Be("week:2026-W24");
    }

    // ── ISO year-boundary (the bug-prone case) ───────────────────────────────

    // 2025-12-29 (Mon) is the start of ISO week 2026-W01 because
    // the ISO year is 2026 even though the calendar year is 2025.
    [Fact]
    public void Weekly_IsoYearBoundary_Dec29_2025_IsWeek2026W01()
    {
        var resolver = Build();
        resolver.Resolve(Utc(2025, 12, 29), LeaderboardPeriod.Weekly)
                .Should().Be("week:2026-W01");
    }

    // 2025-12-28 (Sun) is in ISO week 2025-W52
    [Fact]
    public void Weekly_IsoYearBoundary_Dec28_2025_IsWeek2025W52()
    {
        var resolver = Build();
        resolver.Resolve(Utc(2025, 12, 28), LeaderboardPeriod.Weekly)
                .Should().Be("week:2025-W52");
    }

    // 2026-01-04 (Sun) should still be in ISO week 2026-W01
    [Fact]
    public void Weekly_IsoYearBoundary_Jan04_2026_IsWeek2026W01()
    {
        var resolver = Build();
        resolver.Resolve(Utc(2026, 1, 4), LeaderboardPeriod.Weekly)
                .Should().Be("week:2026-W01");
    }

    // 2026-01-05 (Mon) is the start of ISO week 2026-W02
    [Fact]
    public void Weekly_IsoYearBoundary_Jan05_2026_IsWeek2026W02()
    {
        var resolver = Build();
        resolver.Resolve(Utc(2026, 1, 5), LeaderboardPeriod.Weekly)
                .Should().Be("week:2026-W02");
    }

    // Late-December case: 2020-12-31 (Thu) is in ISO week 2020-W53
    [Fact]
    public void Weekly_IsoYearBoundary_Dec31_2020_IsWeek2020W53()
    {
        var resolver = Build();
        // 2020 has 53 ISO weeks; Dec 31 is a Thursday in W53
        resolver.Resolve(Utc(2020, 12, 31), LeaderboardPeriod.Weekly)
                .Should().Be("week:2020-W53");
    }

    // Jan 1 2021 (Friday) is still in ISO week 2020-W53
    [Fact]
    public void Weekly_IsoYearBoundary_Jan01_2021_IsWeek2020W53()
    {
        var resolver = Build();
        resolver.Resolve(Utc(2021, 1, 1), LeaderboardPeriod.Weekly)
                .Should().Be("week:2020-W53");
    }

    // Jan 4 2021 (Mon) starts ISO week 2021-W01
    [Fact]
    public void Weekly_IsoYearBoundary_Jan04_2021_IsWeek2021W01()
    {
        var resolver = Build();
        resolver.Resolve(Utc(2021, 1, 4), LeaderboardPeriod.Weekly)
                .Should().Be("week:2021-W01");
    }

    // ── Monthly boundary ─────────────────────────────────────────────────────

    [Fact]
    public void Monthly_LastSecondOfMonth_IsInThatMonth()
    {
        var resolver = Build();
        // Last second of January 2026
        resolver.Resolve(Utc(2026, 1, 31, 23, 59, 59), LeaderboardPeriod.Monthly)
                .Should().Be("month:2026-01");
    }

    [Fact]
    public void Monthly_FirstSecondOfNextMonth_IsInNextMonth()
    {
        var resolver = Build();
        resolver.Resolve(Utc(2026, 2, 1, 0, 0, 0), LeaderboardPeriod.Monthly)
                .Should().Be("month:2026-02");
    }

    [Fact]
    public void Monthly_Rollover_LastSecondDec_ToFirstSecondJan()
    {
        var resolver = Build();
        resolver.Resolve(Utc(2025, 12, 31, 23, 59, 59), LeaderboardPeriod.Monthly)
                .Should().Be("month:2025-12");
        resolver.Resolve(Utc(2026, 1, 1, 0, 0, 0), LeaderboardPeriod.Monthly)
                .Should().Be("month:2026-01");
    }

    [Fact]
    public void Monthly_BoundaryTransitionsCorrectly()
    {
        var resolver = Build();
        var lastSecond  = Utc(2026, 5, 31, 23, 59, 59);
        var firstSecond = Utc(2026, 6, 1, 0, 0, 0);

        resolver.Resolve(lastSecond, LeaderboardPeriod.Monthly).Should().Be("month:2026-05");
        resolver.Resolve(firstSecond, LeaderboardPeriod.Monthly).Should().Be("month:2026-06");
    }

    // ── Non-UTC input is normalised to UTC ───────────────────────────────────

    [Fact]
    public void Resolve_NonUtcOffset_NormalisesToUtc()
    {
        var resolver = Build();
        // +02:00 offset: 2026-06-03 01:30:00+02:00 = 2026-06-02 23:30:00 UTC
        var localTime = new DateTimeOffset(2026, 6, 3, 1, 30, 0, TimeSpan.FromHours(2));
        resolver.Resolve(localTime, LeaderboardPeriod.Daily)
                .Should().Be("day:2026-06-02", "UTC conversion should land in the previous calendar day");
    }

    // ── Config validation — bad configs throw at construction ─────────────────

    [Fact]
    public void Config_NonUtcTimezone_ThrowsAtConstruction()
    {
        var act = () => Build(c => c.Timezone = "PST");
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*UTC*");
    }

    [Fact]
    public void Config_MinLevelZero_ThrowsAtConstruction()
    {
        var act = () => Build(c => c.MinLevel = 0);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*MinLevel*");
    }

    [Fact]
    public void Config_MinLevelNegative_ThrowsAtConstruction()
    {
        var act = () => Build(c => c.MinLevel = -1);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*MinLevel*");
    }

    [Fact]
    public void Config_PageSizeZero_ThrowsAtConstruction()
    {
        var act = () => Build(c => c.PageSize = 0);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*PageSize*");
    }

    [Fact]
    public void Config_PageSizeNegative_ThrowsAtConstruction()
    {
        var act = () => Build(c => c.PageSize = -1);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*PageSize*");
    }

    [Fact]
    public void Config_SundayWeekStart_ThrowsAtConstruction()
    {
        // v1 supports ISO/Monday weeks only; Sunday must fail loudly, not silently no-op.
        var act = () => Build(c => c.WeekStartsOn = LeaderboardWeekStart.Sunday);
        act.Should().Throw<InvalidOperationException>()
           .WithMessage("*Monday*");
    }

    [Fact]
    public void Config_ValidDefaults_DoNotThrow()
    {
        var act = () => Build();
        act.Should().NotThrow();
    }
}
