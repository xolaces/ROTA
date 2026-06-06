using FluentAssertions;
using ROTA.Domain.Entities;

namespace ROTA.UnitTests.Domain;

/// <summary>Domain tests for the chat-mute state added in T40.</summary>
public class PlayerMuteTests
{
    private static Player NewPlayer() => Player.Create("u", "u@rota.test", "hash");

    [Fact]
    public void NewPlayer_IsNotMuted()
    {
        var p = NewPlayer();
        p.IsMuted.Should().BeFalse();
        p.MuteExpiresAt.Should().BeNull();
    }

    [Fact]
    public void Mute_SetsExpiry_AndIsMutedTrue()
    {
        var p = NewPlayer();
        p.Mute(DateTimeOffset.UtcNow.AddMinutes(30));
        p.MuteExpiresAt.Should().NotBeNull();
        p.IsMuted.Should().BeTrue();
    }

    [Fact]
    public void IsMuted_False_WhenMuteAlreadyExpired()
    {
        var p = NewPlayer();
        p.Mute(DateTimeOffset.UtcNow.AddMinutes(-1)); // already in the past
        p.IsMuted.Should().BeFalse("an expired mute is no longer in effect");
    }

    [Fact]
    public void Unmute_ClearsExpiry()
    {
        var p = NewPlayer();
        p.Mute(DateTimeOffset.UtcNow.AddHours(1));
        p.Unmute();
        p.MuteExpiresAt.Should().BeNull();
        p.IsMuted.Should().BeFalse();
    }
}
