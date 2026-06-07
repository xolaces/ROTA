using FluentAssertions;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Domain;

// BETA (System 16 Slice 2) — domain invariant tests for GauntletEvent (guarded lifecycle) and
// GauntletEntry (AddScore tiebreak semantics).
public class GauntletEntityTests
{
    private static GauntletEvent NewEvent()
        => GauntletEvent.Create("Cycle",
            DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));

    // ── GauntletEvent lifecycle ─────────────────────────────────────────────────

    [Fact]
    public void Create_StartsScheduled_WithNoSettledAt()
    {
        var ev = NewEvent();
        ev.State.Should().Be(GauntletEventState.Scheduled);
        ev.SettledAt.Should().BeNull();
    }

    [Fact]
    public void Create_Throws_WhenEndsAtNotAfterStartsAt()
    {
        var now = DateTimeOffset.UtcNow;
        var act = () => GauntletEvent.Create("X", now, now);
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void HappyPath_Scheduled_Active_Closed_Settled()
    {
        var ev = NewEvent();
        ev.Activate();   ev.State.Should().Be(GauntletEventState.Active);
        ev.Close();      ev.State.Should().Be(GauntletEventState.Closed);
        ev.MarkSettled();
        ev.State.Should().Be(GauntletEventState.Settled);
        ev.SettledAt.Should().NotBeNull();
    }

    [Fact]
    public void Activate_Throws_WhenNotScheduled()
    {
        var ev = NewEvent();
        ev.Activate();
        var act = () => ev.Activate();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Close_Throws_WhenNotActive()
    {
        var ev = NewEvent(); // Scheduled
        var act = () => ev.Close();
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void MarkSettled_Throws_WhenNotClosed()
    {
        var ev = NewEvent();
        ev.Activate(); // Active, not Closed
        var act = () => ev.MarkSettled();
        act.Should().Throw<InvalidOperationException>();
    }

    // ── GauntletEntry scoring ────────────────────────────────────────────────────

    [Fact]
    public void Create_StartsWithZeroScore_AndNullRank()
    {
        var e = GauntletEntry.Create(Guid.NewGuid(), Guid.NewGuid(), GauntletLeague.Wyrm);
        e.Score.Should().Be(0);
        e.LastRank.Should().BeNull();
        e.League.Should().Be(GauntletLeague.Wyrm);
    }

    [Fact]
    public void AddScore_AccumulatesAndAdvancesTieBreak_OnPositiveDelta()
    {
        var e = GauntletEntry.Create(Guid.NewGuid(), Guid.NewGuid(), GauntletLeague.Whelpling);
        var t1 = DateTimeOffset.UtcNow.AddMinutes(1);
        e.AddScore(500, t1);
        e.Score.Should().Be(500);
        e.TieBreakAt.Should().Be(t1);
    }

    [Fact]
    public void AddScore_IgnoresZeroAndNegativeDelta_TieBreakUnchanged()
    {
        var e = GauntletEntry.Create(Guid.NewGuid(), Guid.NewGuid(), GauntletLeague.Whelpling);
        var t1 = DateTimeOffset.UtcNow.AddMinutes(1);
        e.AddScore(500, t1);
        var t2 = t1.AddMinutes(5);

        e.AddScore(0, t2);
        e.AddScore(-100, t2);

        e.Score.Should().Be(500, "zero/negative deltas do not change score");
        e.TieBreakAt.Should().Be(t1, "tiebreak only advances on a real increase");
    }

    [Fact]
    public void SetRank_StoresRank()
    {
        var e = GauntletEntry.Create(Guid.NewGuid(), Guid.NewGuid(), GauntletLeague.Dragon);
        e.SetRank(7);
        e.LastRank.Should().Be(7);
    }
}
