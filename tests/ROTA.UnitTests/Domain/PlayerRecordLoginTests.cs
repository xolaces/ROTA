using FluentAssertions;
using ROTA.Domain.Entities;

namespace ROTA.UnitTests.Domain;

public class PlayerRecordLoginTests
{
    private static Player NewPlayer() => Player.Create("u", "u@rota.test", "hash");

    [Fact]
    public void RecordLogin_FirstLogin_CountsOneDay()
    {
        var p = NewPlayer();
        var today = new DateOnly(2026, 6, 8);

        var counted = p.RecordLogin(today);

        counted.Should().BeTrue();
        p.DaysPlayed.Should().Be(1);
        p.LastLoginDate.Should().Be(today);
    }

    [Fact]
    public void RecordLogin_SameDayAgain_DoesNotDoubleCount()
    {
        var p = NewPlayer();
        var today = new DateOnly(2026, 6, 8);

        p.RecordLogin(today);
        var second = p.RecordLogin(today);

        second.Should().BeFalse();
        p.DaysPlayed.Should().Be(1);
    }

    [Fact]
    public void RecordLogin_NextDay_IncrementsAgain()
    {
        var p = NewPlayer();
        p.RecordLogin(new DateOnly(2026, 6, 8));

        var counted = p.RecordLogin(new DateOnly(2026, 6, 9));

        counted.Should().BeTrue();
        p.DaysPlayed.Should().Be(2);
        p.LastLoginDate.Should().Be(new DateOnly(2026, 6, 9));
    }

    [Fact]
    public void RecordLogin_EarlierDate_DoesNotDecrementOrCount()
    {
        var p = NewPlayer();
        p.RecordLogin(new DateOnly(2026, 6, 8));

        var counted = p.RecordLogin(new DateOnly(2026, 6, 1)); // clock skew / replay

        counted.Should().BeFalse();
        p.DaysPlayed.Should().Be(1);
        p.LastLoginDate.Should().Be(new DateOnly(2026, 6, 8));
    }
}
