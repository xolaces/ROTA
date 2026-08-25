using FluentAssertions;
using ROTA.Domain.Entities;

namespace ROTA.UnitTests.Domain;

/// <summary>
/// Domain tests for temporary bans. The whole design rests on IsBanned being DERIVED rather than
/// stored: there is no background job to sweep expired bans (D-009 puts scheduling at launch), so a
/// temporary ban has to lift itself the moment its instant passes — and every existing IsBanned check
/// across middleware, the chat hub and five services has to get that right without knowing about
/// BannedUntil at all.
/// </summary>
public class PlayerBanTests
{
    private static Player NewPlayer() => Player.Create("u", "u@rota.test", "hash");

    [Fact]
    public void NewPlayer_IsNotBanned()
    {
        var p = NewPlayer();
        p.IsBanned.Should().BeFalse();
        p.BanIssued.Should().BeFalse();
        p.BannedUntil.Should().BeNull();
    }

    [Fact]
    public void Ban_WithNoDuration_IsPermanent()
    {
        var p = NewPlayer();
        p.Ban("cheating");

        p.IsBanned.Should().BeTrue();
        p.BanIssued.Should().BeTrue();
        p.BannedUntil.Should().BeNull("a null expiry is what makes a ban permanent");
        p.BanReason.Should().Be("cheating");
    }

    [Fact]
    public void Ban_WithFutureExpiry_IsInEffect()
    {
        var p = NewPlayer();
        var until = DateTimeOffset.UtcNow.AddDays(3);
        p.Ban("spam", until);

        p.IsBanned.Should().BeTrue();
        p.BannedUntil.Should().BeCloseTo(until, TimeSpan.FromSeconds(1));
    }

    // The property the feature exists for: nothing sweeps expired bans, so the ban must lift itself.
    [Fact]
    public void IsBanned_False_WhenTemporaryBanAlreadyExpired()
    {
        var p = NewPlayer();
        p.Ban("spam", DateTimeOffset.UtcNow.AddSeconds(-1));

        p.IsBanned.Should().BeFalse("an elapsed temporary ban is over, with or without a sweeper");
        p.BanIssued.Should().BeTrue("the record that a ban was issued survives its expiry");
    }

    [Fact]
    public void Unban_ClearsEverything()
    {
        var p = NewPlayer();
        p.Ban("spam", DateTimeOffset.UtcNow.AddDays(2));
        p.Unban();

        p.IsBanned.Should().BeFalse();
        p.BanIssued.Should().BeFalse();
        p.BannedUntil.Should().BeNull();
        p.BanReason.Should().BeNull();
    }

    [Fact]
    public void Ban_CanBeReissuedPermanently_AfterATemporaryOne()
    {
        var p = NewPlayer();
        p.Ban("first offence", DateTimeOffset.UtcNow.AddDays(1));
        p.Ban("did it again");

        p.IsBanned.Should().BeTrue();
        p.BannedUntil.Should().BeNull("re-banning without a duration must not inherit the old expiry");
    }
}
