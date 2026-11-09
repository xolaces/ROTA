using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.UnitTests.TestSupport;

namespace ROTA.UnitTests.Services;

/// <summary>
/// System 29 S1 — the check-in seam. The auth-path behavior (login + refresh both check in, failures
/// never break auth) is covered in AuthServiceTests; these pin the result contract System 30 builds on.
/// </summary>
public class CheckInServiceTests
{
    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static (CheckInService service, Mock<IAchievementService> achievements, Player player) Build()
    {
        var players = new Mock<IPlayerRepository>();
        var achievements = new Mock<IAchievementService>();
        var player = Player.Create("checkin", "checkin@rota.test", "hash");
        players.Setup(r => r.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        players.SetupMutatePassThrough<CheckInResult>();
        return (new CheckInService(players.Object, achievements.Object), achievements, player);
    }

    [Fact]
    public async Task FirstEverCheckIn_CountsTheDay_WithNoPreviousDay()
    {
        var (service, achievements, player) = Build();

        var result = await service.CheckInAsync(player.Id);

        result.Should().Be(new CheckInResult(NewDay: true, PreviousDay: null));
        player.DaysPlayed.Should().Be(1);
        achievements.Verify(a => a.EvaluateCompletionsAsync(player.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task CheckInAfterAnAbsence_ReportsTheLastCountedDay()
    {
        var (service, _, player) = Build();
        var lastSeen = Today.AddDays(-9);
        player.RecordLogin(lastSeen);

        var result = await service.CheckInAsync(player.Id);

        result.Should().Be(new CheckInResult(NewDay: true, PreviousDay: lastSeen),
            "System 30 derives the comeback gap from PreviousDay");
        player.LastLoginDate.Should().Be(Today);
    }

    [Fact]
    public async Task SecondCheckInSameDay_CountsNothing_AndSkipsAchievements()
    {
        var (service, achievements, player) = Build();
        await service.CheckInAsync(player.Id);
        achievements.Invocations.Clear();

        var result = await service.CheckInAsync(player.Id);

        result.Should().Be(new CheckInResult(NewDay: false, PreviousDay: Today));
        player.DaysPlayed.Should().Be(1);
        achievements.Verify(a => a.RecordProgressAsync(
            It.IsAny<Guid>(), It.IsAny<AchievementMetric>(), It.IsAny<long>(), It.IsAny<string?>(),
            It.IsAny<CancellationToken>()), Times.Never);
        achievements.Verify(a => a.EvaluateCompletionsAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
