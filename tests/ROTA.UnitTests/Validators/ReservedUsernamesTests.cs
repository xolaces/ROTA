using FluentAssertions;
using ROTA.Application.Validators;

namespace ROTA.UnitTests.Validators;

/// <summary>
/// Reserved-username guard: public registration and self-service rename must not be able to
/// claim staff / system handles. Staff names are created only via the admin CLI / seeding.
/// </summary>
public sealed class ReservedUsernamesTests
{
    [Theory]
    [InlineData("admin")]
    [InlineData("Administrator")]
    [InlineData("MOD")]
    [InlineData("moderator")]
    [InlineData("staff")]
    [InlineData("dev")]
    [InlineData("developer")]
    [InlineData("gm")]
    [InlineData("owner")]
    [InlineData("official")]
    [InlineData("support")]
    [InlineData("system")]
    [InlineData("rota")]
    [InlineData("ancient")]
    public void IsReserved_BlocksExactStaffHandles_CaseInsensitive(string name)
    {
        ReservedUsernames.IsReserved(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("DEV_Owner")]
    [InlineData("dev_hacker")]
    [InlineData("Mod_Smith")]
    [InlineData("admin_root")]
    [InlineData("STAFF_jones")]
    [InlineData("rota_official")]
    public void IsReserved_BlocksReservedPrefixes_CaseInsensitive(string name)
    {
        ReservedUsernames.IsReserved(name).Should().BeTrue();
    }

    [Theory]
    [InlineData("BlueZBear")]
    [InlineData("VoidWalker99")]
    [InlineData("development_studio")]  // not a reserved prefix ("dev_" requires the underscore right after dev)
    [InlineData("modern_warrior")]      // not "mod_" prefix
    [InlineData("admiral")]             // not "admin"
    [InlineData("devon")]               // not "dev" exact, not "dev_" prefix
    public void IsReserved_AllowsOrdinaryNames(string name)
    {
        ReservedUsernames.IsReserved(name).Should().BeFalse();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void IsReserved_EmptyOrNull_IsNotReserved(string? name)
    {
        // Charset/length rules handle empties; this guard only judges reserved content.
        ReservedUsernames.IsReserved(name).Should().BeFalse();
    }
}
