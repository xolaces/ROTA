using FluentAssertions;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Domain;

public class RefreshTokenTests
{
    private static RefreshToken NewToken()
        => new(Guid.NewGuid(), "hash", DateTimeOffset.UtcNow.AddDays(7), null);

    [Fact]
    public void Revoke_RecordsReasonAndTime()
    {
        var token = NewToken();

        token.Revoke(RefreshTokenRevokeReason.LoggedOut);

        token.IsRevoked.Should().BeTrue();
        token.IsActive.Should().BeFalse();
        token.RevokedReason.Should().Be(RefreshTokenRevokeReason.LoggedOut);
        token.RevokedAt.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromSeconds(5));
        token.WasRotated.Should().BeFalse("only a rotation makes a later presentation a theft signal");
    }

    [Fact]
    public void Revoke_FirstReasonSticks()
    {
        // A rotated token caught by a later revoke-all must still read as Rotated, or presenting it
        // again would stop being treated as a replay.
        var token = NewToken();
        token.Revoke(RefreshTokenRevokeReason.Rotated);
        var firstAt = token.RevokedAt;

        token.Revoke(RefreshTokenRevokeReason.ReplayDetected);

        token.RevokedReason.Should().Be(RefreshTokenRevokeReason.Rotated);
        token.RevokedAt.Should().Be(firstAt);
        token.WasRotated.Should().BeTrue();
    }

    [Fact]
    public void NewToken_IsLive_WithNoReason()
    {
        var token = NewToken();

        token.IsActive.Should().BeTrue();
        token.RevokedReason.Should().BeNull();
        token.RevokedAt.Should().BeNull();
    }
}
