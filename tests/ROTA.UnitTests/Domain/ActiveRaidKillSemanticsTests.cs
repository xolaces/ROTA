using FluentAssertions;
using ROTA.Domain.Entities;

namespace ROTA.UnitTests.Domain;

// The World-raid kill gate, as a domain invariant. World raids are a timer + damage ladder and carry
// MaxHp 0 as their marker, so CurrentHp == 0 is their RESTING state, not a death. Ordinary raids are
// dead only once a real health pool reaches zero.
public class ActiveRaidKillSemanticsTests
{
    private static ActiveRaid NewRaid(string id, long maxHp)
        => ActiveRaid.Create(id, Guid.NewGuid(), maxHp, DateTimeOffset.UtcNow.AddHours(1));

    [Fact]
    public void WorldRaid_WithNoHealthPool_IsNeverKilledByDamage()
    {
        NewRaid("raid_world", 0).CanBeKilledByDamage()
            .Should().BeFalse("World raids have no health pool and zero HP is a resting state");
    }

    [Fact]
    public void NormalRaid_AtFullHealth_IsNotYetKilled()
    {
        NewRaid("raid_normal", 5).CanBeKilledByDamage()
            .Should().BeFalse("a fresh raid is not yet at zero HP");
    }

    [Fact]
    public void NormalRaid_AtZeroHealth_IsKilled()
    {
        var normal = NewRaid("raid_normal", 5);
        normal.TakeDamage(5);

        normal.CanBeKilledByDamage()
            .Should().BeTrue("ordinary raids are killed by reaching zero health");
    }
}
