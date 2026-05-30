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
                new EffectiveCombatData(atk, def, null));

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
}
