using System.Collections;
using System.Reflection;
using FluentAssertions;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Maintenance;

namespace ROTA.UnitTests.Infrastructure;

/// <summary>
/// The beta reset deletes rows across 44 tables on a live server, so its table classification is
/// load-bearing in a way ordinary code is not: a mistake here is silent, destructive and discovered
/// by a player rather than by a stack trace.
/// </summary>
/// <remarks>
/// These are static invariants over the classification itself — they need no database and they run
/// on every build. The behaviour that needs a database (the actual wipe, the kept-account exemption,
/// the post-condition rollback) was verified against a throwaway clone of the dev database; both the
/// exemption bug and the guild-membership FK violation were found that way.
/// </remarks>
public class BetaResetClassificationTests
{
    private static HashSet<string> Set(string field)
    {
        var f = typeof(BetaResetService).GetField(field, BindingFlags.NonPublic | BindingFlags.Static)!;
        var value = f.GetValue(null)!;
        return value switch
        {
            IEnumerable<string> strings => new HashSet<string>(strings, StringComparer.OrdinalIgnoreCase),
            IDictionary dict => new HashSet<string>(dict.Keys.Cast<string>(), StringComparer.OrdinalIgnoreCase),
            _ => throw new InvalidOperationException($"unexpected shape for {field}"),
        };
    }

    private static HashSet<string> Wiped => Set("Wiped");
    private static HashSet<string> Kept => Set("Kept");
    private static HashSet<string> Special => Set("Special");
    private static HashSet<string> Character => Set("CharacterTables");

    /// <summary>
    /// The architecture rule says audit_log is append-only and takes no DELETE, ever. A reset that
    /// wiped it would destroy the record of the reset itself.
    /// </summary>
    [Fact]
    public void AuditLogIsNeverWiped()
    {
        Kept.Should().Contain("audit_log");
        Wiped.Should().NotContain("audit_log");
    }

    /// <summary>
    /// Players and beta keys need per-row logic — players are reset in place and only UNREDEEMED
    /// keys are deleted. A blanket delete on either would destroy the logins the owner asked to keep
    /// and the record of who was admitted in the previous wave.
    /// </summary>
    [Fact]
    public void PlayersAndBetaKeysAreHandledSpecially()
    {
        Special.Should().Contain("players").And.Contain("beta_keys");
        Wiped.Should().NotContain("players").And.NotContain("beta_keys");
    }

    /// <summary>A table cannot be both preserved and destroyed; whichever ran last would win.</summary>
    [Fact]
    public void NoTableIsClassifiedTwice()
    {
        Wiped.Overlaps(Kept).Should().BeFalse("a table cannot be both kept and wiped");
        Wiped.Overlaps(Special).Should().BeFalse("a table cannot be both special-cased and wiped");
        Kept.Overlaps(Special).Should().BeFalse("a table cannot be both kept and special-cased");
    }

    /// <summary>
    /// --keep spares a character table from the wipe. A character table that is not wiped in the
    /// first place would make that exemption meaningless and hide the mistake.
    /// </summary>
    [Fact]
    public void EveryCharacterTableIsAlsoWiped()
        => Character.Except(Wiped).Should().BeEmpty(
            "CharacterTables only ever narrows Wiped — an entry outside it is a typo");

    /// <summary>
    /// The rows that make an account loadable. If either escaped the wipe, a reset account would
    /// keep its old stats or pools and would not be a fresh player at all.
    /// </summary>
    [Fact]
    public void TheRowsThatMakeAnAccountLoadableAreWipedAndReseeded()
    {
        Wiped.Should().Contain("player_stats").And.Contain("player_resources");
        Character.Should().Contain("player_stats").And.Contain("player_resources");
    }

    /// <summary>
    /// Guild membership and raid participation carry a player_id but describe the world, not the
    /// character. Sparing guild_memberships for a kept account left a row pointing at a guild being
    /// deleted, and PostgreSQL correctly refused the whole transaction. This pins that lesson.
    /// </summary>
    [Theory]
    [InlineData("guild_memberships")]
    [InlineData("guild_join_requests")]
    [InlineData("raid_participants")]
    [InlineData("market_listings")]
    [InlineData("friendships")]
    [InlineData("leaderboard_entry")]
    public void WorldStateIsNeverSparedEvenThoughItCarriesAPlayerId(string table)
    {
        Wiped.Should().Contain(table);
        Character.Should().NotContain(table,
            "sparing world state for a kept account leaves rows pointing at deleted parents");
    }

    /// <summary>
    /// ResetProgress must leave a player exactly where CreateWithId does, or a wiped account is not
    /// a fresh account. Levelling and enrichment are applied first so the assertions mean something.
    /// </summary>
    [Fact]
    public void ResetProgressReturnsAPlayerToFreshlyRegisteredValues()
    {
        var p = Player.CreateWithId(Guid.NewGuid(), "tester", "t@example.com", "hash");
        p.AddExperience(500_000, _ => 100);
        p.SetClass(PlayerClass.Luminary);

        p.ResetProgress();

        p.Level.Should().Be(1);
        p.Experience.Should().Be(0);
        p.Gold.Should().Be(0);
        p.Class.Should().Be(PlayerClass.Conscript);
        p.GuildId.Should().BeNull();
        p.DaysPlayed.Should().Be(0);
        p.LastLoginDate.Should().BeNull();
    }

    /// <summary>
    /// The identity a returning tester logs in with, plus the two things a wipe must not quietly
    /// undo: an admin's roles (or nobody can run the next wave) and a ban (a wipe is not an amnesty).
    /// </summary>
    [Fact]
    public void ResetProgressKeepsIdentityRolesAndPunishment()
    {
        var id = Guid.NewGuid();
        var p = Player.CreateWithId(id, "tester", "T@Example.com", "hash");
        p.GrantRole(PlayerRoles.Admin);
        p.Ban("cheating in wave 1");

        p.ResetProgress();

        p.Id.Should().Be(id);
        p.Username.Should().Be("tester");
        p.Email.Should().Be("t@example.com");
        p.PasswordHash.Should().Be("hash");
        p.HasRole(PlayerRoles.Admin).Should().BeTrue("a wipe must not demote the admins who run it");
        p.BanIssued.Should().BeTrue("a wipe is not an amnesty");
    }
}
