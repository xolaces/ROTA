using FluentAssertions;
using FluentValidation;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Application.Validators;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Services;

public class PlayerServiceTests
{
    // -----------------------------------------------------------------------
    // HELPERS
    // -----------------------------------------------------------------------

    private static (PlayerService service,
                    Mock<IPlayerRepository> players,
                    Mock<IEnergyService> energy,
                    Mock<IAuditLogRepository> auditLog)
        BuildService()
    {
        var players   = new Mock<IPlayerRepository>();
        var energy    = new Mock<IEnergyService>();
        var auditLog  = new Mock<IAuditLogRepository>();
        var equipment = new Mock<IEquipmentService>();

        // Default: pass-through — no gear bonus
        equipment.Setup(e => e.GetEffectiveCombatDataAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, int atk, int def, CancellationToken _) =>
                new EffectiveCombatData(atk, def, null, 0.0));

        var service = new PlayerService(players.Object, energy.Object, auditLog.Object, equipment.Object);
        return (service, players, energy, auditLog);
    }

    private static Player MakePlayer(string username = "testuser")
        => Player.Create(username, $"{username}@rota.test", "hash");

    // -----------------------------------------------------------------------
    // GET /api/players/me — live resource values
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetProfile_ReturnsLiveEnergyValues_NotStoredCheckpoints()
    {
        var (service, players, energy, _) = BuildService();
        var player = MakePlayer();

        players.Setup(p => p.FindByIdWithResourcesAsync(player.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);

        // Return live values that differ from the default MaxValue (25 energy, 5 stamina, 1 guildstamina)
        energy.Setup(e => e.GetCurrentEnergyAsync(player.Id, ResourceType.Energy, It.IsAny<CancellationToken>()))
              .ReturnsAsync(17);
        energy.Setup(e => e.GetCurrentEnergyAsync(player.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
              .ReturnsAsync(3);
        energy.Setup(e => e.GetCurrentEnergyAsync(player.Id, ResourceType.GuildStamina, It.IsAny<CancellationToken>()))
              .ReturnsAsync(1);

        var result = await service.GetProfileAsync(player.Id);

        result.Should().NotBeNull();
        var energyResource = result!.Resources.Single(r => r.Type == nameof(ResourceType.Energy));
        energyResource.LiveValue.Should().Be(17, "live value must come from IEnergyService, not the stored checkpoint");
        energyResource.MaxValue.Should().Be(25);
    }

    [Fact]
    public async Task GetProfile_RegenMinutesPerPoint_ComesFromClassConfig_NotStoredField()
    {
        // Arrange — Conscript defaults: Energy=5.0, Stamina=5.0, GuildStamina=2.0
        // The stored PlayerResource.RegenPerMinute is vestigial (int=2 for Energy, 1 for Stamina, 0 for GuildStamina).
        // RegenMinutesPerPoint must NOT equal those stored values.
        var (service, players, energy, _) = BuildService();
        var player = MakePlayer(); // Class = Conscript

        players.Setup(p => p.FindByIdWithResourcesAsync(player.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);

        energy.Setup(e => e.GetCurrentEnergyAsync(player.Id, It.IsAny<ResourceType>(), It.IsAny<CancellationToken>()))
              .ReturnsAsync(1); // below max so SecondsToNextPoint is non-trivial

        // Conscript regen rates per ClassConfig defaults
        energy.Setup(e => e.GetRegenMinutesPerPoint(PlayerClass.Conscript, ResourceType.Energy))
              .Returns(5.0);
        energy.Setup(e => e.GetRegenMinutesPerPoint(PlayerClass.Conscript, ResourceType.Stamina))
              .Returns(5.0);
        energy.Setup(e => e.GetRegenMinutesPerPoint(PlayerClass.Conscript, ResourceType.GuildStamina))
              .Returns(2.0);

        // Act
        var result = await service.GetProfileAsync(player.Id);

        // Assert — class-based rates returned
        result.Should().NotBeNull();
        var e2 = result!.Resources.Single(r => r.Type == nameof(ResourceType.Energy));
        var s  = result.Resources.Single(r => r.Type == nameof(ResourceType.Stamina));
        var gs = result.Resources.Single(r => r.Type == nameof(ResourceType.GuildStamina));

        e2.RegenMinutesPerPoint.Should().Be(5.0, "Conscript Energy regen is 5.0 min/point from ClassConfig");
        s.RegenMinutesPerPoint.Should().Be(5.0, "Conscript Stamina regen is 5.0 min/point from ClassConfig");
        gs.RegenMinutesPerPoint.Should().Be(2.0, "GuildStamina regen is 2.0 min/point from ClassConfig");

        // Assert — stored vestigial field is NOT accidentally promoted to RegenMinutesPerPoint
        e2.RegenMinutesPerPoint.Should().NotBe(e2.RegenPerMinute,
            "RegenMinutesPerPoint must be the class-based rate (5.0), not the stored int RegenPerMinute (2)");
    }

    [Fact]
    public async Task GetProfile_SecondsToNextPoint_IsZero_WhenResourceFull()
    {
        var (service, players, energy, _) = BuildService();
        var player = MakePlayer();

        players.Setup(p => p.FindByIdWithResourcesAsync(player.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);

        // Energy at max (25), Stamina at max (5), GuildStamina at max (1)
        energy.Setup(e => e.GetCurrentEnergyAsync(player.Id, ResourceType.Energy, It.IsAny<CancellationToken>()))
              .ReturnsAsync(25);
        energy.Setup(e => e.GetCurrentEnergyAsync(player.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
              .ReturnsAsync(5);
        energy.Setup(e => e.GetCurrentEnergyAsync(player.Id, ResourceType.GuildStamina, It.IsAny<CancellationToken>()))
              .ReturnsAsync(1);

        energy.Setup(e => e.GetRegenMinutesPerPoint(It.IsAny<PlayerClass>(), It.IsAny<ResourceType>()))
              .Returns(5.0);

        var result = await service.GetProfileAsync(player.Id);

        result.Should().NotBeNull();
        foreach (var r in result!.Resources)
            r.SecondsToNextPoint.Should().Be(0, $"{r.Type} is full — SecondsToNextPoint must be 0");
    }

    [Fact]
    public async Task GetProfile_SecondsToNextPoint_IsPositive_WhenResourceNotFull()
    {
        var (service, players, energy, _) = BuildService();
        var player = MakePlayer();

        players.Setup(p => p.FindByIdWithResourcesAsync(player.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);

        // Energy below max
        energy.Setup(e => e.GetCurrentEnergyAsync(player.Id, ResourceType.Energy, It.IsAny<CancellationToken>()))
              .ReturnsAsync(10); // 10 of 25 — not full
        energy.Setup(e => e.GetCurrentEnergyAsync(player.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
              .ReturnsAsync(5);
        energy.Setup(e => e.GetCurrentEnergyAsync(player.Id, ResourceType.GuildStamina, It.IsAny<CancellationToken>()))
              .ReturnsAsync(1);

        energy.Setup(e => e.GetRegenMinutesPerPoint(It.IsAny<PlayerClass>(), It.IsAny<ResourceType>()))
              .Returns(5.0);

        var result = await service.GetProfileAsync(player.Id);

        result.Should().NotBeNull();
        var energyResource = result!.Resources.Single(r => r.Type == nameof(ResourceType.Energy));
        energyResource.SecondsToNextPoint.Should().BeInRange(1, 300,
            "Energy is not full — SecondsToNextPoint must be between 1 and 300 (5-min regen cycle = 300 s)");
    }

    [Fact]
    public async Task GetProfile_ReturnsNull_WhenPlayerNotFoundOrDeleted()
    {
        var (service, players, _, _) = BuildService();
        var playerId = Guid.NewGuid();

        players.Setup(p => p.FindByIdWithResourcesAsync(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync((Player?)null);

        var result = await service.GetProfileAsync(playerId);

        result.Should().BeNull("deleted or missing players are filtered by the repository and return null → 404");
    }

    // -----------------------------------------------------------------------
    // PUT /api/players/me — username update
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UpdateUsername_Succeeds_WithValidAvailableUsername()
    {
        var (service, players, _, _) = BuildService();
        var player = MakePlayer("oldname");

        players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);
        players.Setup(p => p.UsernameExistsAsync("newname", It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);
        players.Setup(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);

        var result = await service.UpdateUsernameAsync(player.Id, new UpdateUsernameRequest { Username = "newname" });

        result.Status.Should().Be(PlayerUpdateStatus.Success);
        result.NewUsername.Should().Be("newname");
        players.Verify(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateUsername_ReturnsUsernameTaken_WhenUsernameAlreadyExists()
    {
        var (service, players, _, _) = BuildService();
        var player = MakePlayer("myname");

        players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);
        players.Setup(p => p.UsernameExistsAsync("takenname", It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);

        var result = await service.UpdateUsernameAsync(player.Id, new UpdateUsernameRequest { Username = "takenname" });

        result.Status.Should().Be(PlayerUpdateStatus.UsernameTaken);
        players.Verify(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task UpdateUsername_ReturnsNotFound_WhenPlayerDoesNotExist()
    {
        var (service, players, _, _) = BuildService();
        var playerId = Guid.NewGuid();

        players.Setup(p => p.FindByIdAsync(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync((Player?)null);

        var result = await service.UpdateUsernameAsync(playerId, new UpdateUsernameRequest { Username = "validname" });

        result.Status.Should().Be(PlayerUpdateStatus.NotFound);
    }

    // -----------------------------------------------------------------------
    // UpdateUsernameRequestValidator
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("ab")]                                 // 2 chars — too short
    [InlineData("")]                                   // empty
    [InlineData("has spaces")]                         // spaces not allowed
    [InlineData("has-hyphens")]                        // hyphens not allowed (spec: alphanumeric + underscores only)
    [InlineData("this_username_is_way_too_long_33c")]  // 33 chars — exceeds 32
    public async Task UpdateUsernameValidator_Rejects_InvalidUsernames(string username)
    {
        var validator = new UpdateUsernameRequestValidator();
        var result = await validator.ValidateAsync(new UpdateUsernameRequest { Username = username });
        result.IsValid.Should().BeFalse($"'{username}' should fail validation");
    }

    [Theory]
    [InlineData("abc")]            // minimum length
    [InlineData("Valid_User_123")] // alphanumeric + underscores
    [InlineData("ALLCAPS")]        // uppercase allowed
    public async Task UpdateUsernameValidator_Accepts_ValidUsernames(string username)
    {
        var validator = new UpdateUsernameRequestValidator();
        var result = await validator.ValidateAsync(new UpdateUsernameRequest { Username = username });
        result.IsValid.Should().BeTrue($"'{username}' should pass validation");
    }

    // -----------------------------------------------------------------------
    // PUT /api/players/me/display-name — display name update
    // -----------------------------------------------------------------------

    [Fact]
    public async Task UpdateDisplayName_Succeeds_WithValidDisplayName()
    {
        var (service, players, _, auditLog) = BuildService();
        var player = MakePlayer("testuser");

        players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>()))
               .ReturnsAsync(player);
        players.Setup(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()))
               .Returns(Task.CompletedTask);
        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
                .Returns(Task.CompletedTask);

        var result = await service.UpdateDisplayNameAsync(player.Id, new UpdateDisplayNameRequest("NewName"));

        result.Status.Should().Be(DisplayNameUpdateStatus.Success);
        result.NewDisplayName.Should().Be("NewName");
        players.Verify(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task UpdateDisplayName_ReturnsNotFound_WhenPlayerDoesNotExist()
    {
        var (service, players, _, _) = BuildService();
        var playerId = Guid.NewGuid();

        players.Setup(p => p.FindByIdAsync(playerId, It.IsAny<CancellationToken>()))
               .ReturnsAsync((Player?)null);

        var result = await service.UpdateDisplayNameAsync(playerId, new UpdateDisplayNameRequest("AnyName"));

        result.Status.Should().Be(DisplayNameUpdateStatus.NotFound);
        players.Verify(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // -----------------------------------------------------------------------
    // UpdateDisplayNameRequestValidator
    // -----------------------------------------------------------------------

    [Theory]
    [InlineData("John_Doe-99")]                                              // letters, digits, underscore, hyphen
    [InlineData("A")]                                                        // single char — valid
    [InlineData("Hello World")]                                              // space allowed
    public async Task UpdateDisplayNameValidator_Accepts_ValidDisplayNames(string name)
    {
        var validator = new UpdateDisplayNameRequestValidator();
        var result = await validator.ValidateAsync(new UpdateDisplayNameRequest(name));
        result.IsValid.Should().BeTrue($"'{name}' should pass validation");
    }

    [Theory]
    [InlineData("")]                                                         // empty
    [InlineData("bad!name")]                                                 // special char not allowed
    [InlineData("this_display_name_is_way_too_long_to_fit_in_48_chars_xx")] // > 48 chars
    public async Task UpdateDisplayNameValidator_Rejects_InvalidDisplayNames(string name)
    {
        var validator = new UpdateDisplayNameRequestValidator();
        var result = await validator.ValidateAsync(new UpdateDisplayNameRequest(name));
        result.IsValid.Should().BeFalse($"'{name}' should fail validation");
    }
}
