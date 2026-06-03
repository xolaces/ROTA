using FluentAssertions;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Domain;

public class LeaderboardEntryTests
{
    private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 6, 3, 0, 0, 0, TimeSpan.Zero);

    // ── Create factory ───────────────────────────────────────────────────────

    [Fact]
    public void Create_SetsAllFields_Correctly()
    {
        var playerId = Guid.NewGuid();
        var entry = LeaderboardEntry.Create(
            playerId,
            LeaderboardBoard.DamageDealt,
            LeaderboardPeriod.Weekly,
            "week:2026-W23",
            1000L,
            T0);

        entry.Id.Should().NotBe(Guid.Empty);
        entry.PlayerId.Should().Be(playerId);
        entry.Board.Should().Be(LeaderboardBoard.DamageDealt);
        entry.Period.Should().Be(LeaderboardPeriod.Weekly);
        entry.PeriodKey.Should().Be("week:2026-W23");
        entry.Value.Should().Be(1000L);
        entry.LastProgressAt.Should().Be(T0);
        entry.Rank.Should().BeNull();
        entry.CreatedAt.Should().Be(T0);
        entry.UpdatedAt.Should().Be(T0);
        entry.IsDeleted.Should().BeFalse();
    }

    // ── AddValue ─────────────────────────────────────────────────────────────

    [Fact]
    public void AddValue_PositiveDelta_AccumulatesAndMovesTimestamp()
    {
        var entry = MakeEntry(initialValue: 100L, at: T0);
        var t1 = T0.AddSeconds(10);

        entry.AddValue(50L, t1);

        entry.Value.Should().Be(150L);
        entry.LastProgressAt.Should().Be(t1);
        entry.UpdatedAt.Should().Be(t1);
    }

    [Fact]
    public void AddValue_MultipleIncrements_SumCorrectly()
    {
        var entry = MakeEntry(initialValue: 0L, at: T0);

        entry.AddValue(300L, T0.AddSeconds(1));
        entry.AddValue(200L, T0.AddSeconds(2));
        entry.AddValue(100L, T0.AddSeconds(3));

        entry.Value.Should().Be(600L);
    }

    [Fact]
    public void AddValue_ZeroDelta_IsNoOp()
    {
        var entry = MakeEntry(initialValue: 500L, at: T0);

        entry.AddValue(0L, T0.AddSeconds(1));

        // Value and timestamps must not change — zero is explicitly guarded
        entry.Value.Should().Be(500L);
        entry.LastProgressAt.Should().Be(T0);
        entry.UpdatedAt.Should().Be(T0);
    }

    [Fact]
    public void AddValue_NegativeDelta_IsIgnored()
    {
        var entry = MakeEntry(initialValue: 500L, at: T0);

        // Negative delta is silently ignored — it must never lower the value.
        entry.AddValue(-100L, T0.AddSeconds(1));

        entry.Value.Should().Be(500L, "negative delta must not lower the value");
        entry.LastProgressAt.Should().Be(T0, "timestamp must not move when delta is ignored");
    }

    // ── MaxValue ─────────────────────────────────────────────────────────────

    [Fact]
    public void MaxValue_LargerCandidate_RaisesAndReturnsTrue()
    {
        var entry = MakeEntry(initialValue: 1000L, at: T0);
        var t1 = T0.AddSeconds(5);

        var raised = entry.MaxValue(2000L, t1);

        raised.Should().BeTrue();
        entry.Value.Should().Be(2000L);
        entry.LastProgressAt.Should().Be(t1);
        entry.UpdatedAt.Should().Be(t1);
    }

    [Fact]
    public void MaxValue_SmallerCandidate_DoesNotLowerAndReturnsFalse()
    {
        var entry = MakeEntry(initialValue: 2000L, at: T0);
        var t1 = T0.AddSeconds(5);

        var raised = entry.MaxValue(500L, t1);

        raised.Should().BeFalse();
        entry.Value.Should().Be(2000L, "a smaller candidate must not lower the stored value");
        entry.LastProgressAt.Should().Be(T0, "timestamp must not move when value did not rise");
    }

    [Fact]
    public void MaxValue_EqualCandidate_DoesNotUpdateAndReturnsFalse()
    {
        var entry = MakeEntry(initialValue: 1500L, at: T0);
        var t1 = T0.AddSeconds(5);

        var raised = entry.MaxValue(1500L, t1);

        raised.Should().BeFalse();
        entry.Value.Should().Be(1500L);
        // Timestamp must NOT move on an equal candidate — the row already holds the
        // "earliest to reach" record (T0 came first), and we must not clobber it.
        entry.LastProgressAt.Should().Be(T0);
    }

    [Fact]
    public void MaxValue_SequenceOfHits_OnlyBestSurvives()
    {
        var entry = MakeEntry(initialValue: 100L, at: T0);

        entry.MaxValue(500L,  T0.AddSeconds(1));  // raises → 500
        entry.MaxValue(300L,  T0.AddSeconds(2));  // does not lower
        entry.MaxValue(1200L, T0.AddSeconds(3));  // raises → 1200
        entry.MaxValue(900L,  T0.AddSeconds(4));  // does not lower

        entry.Value.Should().Be(1200L);
        entry.LastProgressAt.Should().Be(T0.AddSeconds(3));
    }

    // ── SetRank ───────────────────────────────────────────────────────────────

    [Fact]
    public void SetRank_StoresTheRank()
    {
        var entry = MakeEntry(initialValue: 100L, at: T0);

        entry.SetRank(7);

        entry.Rank.Should().Be(7);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private static LeaderboardEntry MakeEntry(long initialValue, DateTimeOffset at)
        => LeaderboardEntry.Create(
            Guid.NewGuid(),
            LeaderboardBoard.LargestHit,
            LeaderboardPeriod.Daily,
            "day:2026-06-03",
            initialValue,
            at);
}
