using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Services;

public class GemServiceTests
{
    // -----------------------------------------------------------------------
    // HELPERS
    // -----------------------------------------------------------------------

    private static (GemService service,
                    Mock<IGemTransactionRepository> repo,
                    Mock<IAuditLogRepository> auditLog)
        BuildService()
    {
        var repo     = new Mock<IGemTransactionRepository>();
        var auditLog = new Mock<IAuditLogRepository>();
        var service  = new GemService(repo.Object, auditLog.Object);
        return (service, repo, auditLog);
    }

    // -----------------------------------------------------------------------
    // GetBalanceAsync — ledger sum
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GetBalance_ReturnsSumOfAllTransactions()
    {
        var (service, repo, _) = BuildService();
        var playerId = Guid.NewGuid();

        repo.Setup(r => r.GetBalanceAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(15); // e.g. +20 credits − 5 debits

        var balance = await service.GetBalanceAsync(playerId);

        balance.Should().Be(15);
    }

    // -----------------------------------------------------------------------
    // GrantGemsAsync — idempotency
    // -----------------------------------------------------------------------

    [Fact]
    public async Task GrantGems_ReturnsFalse_WhenReferenceIdAlreadyUsed()
    {
        var (service, repo, _) = BuildService();
        var playerId = Guid.NewGuid();
        const string refId = "daily:2026-05-24";

        repo.Setup(r => r.ReferenceExistsAsync(playerId, GemTransactionType.DailyReward, refId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await service.GrantGemsAsync(playerId, 5, GemTransactionType.DailyReward, refId);

        result.Should().BeFalse("duplicate referenceId must be rejected for idempotency");
        repo.Verify(r => r.CreateAsync(It.IsAny<GemTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GrantGems_Succeeds_WhenReferenceIdIsNew()
    {
        var (service, repo, _) = BuildService();
        var playerId = Guid.NewGuid();
        const string refId = "daily:2026-05-24";

        repo.Setup(r => r.ReferenceExistsAsync(playerId, GemTransactionType.DailyReward, refId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repo.Setup(r => r.CreateAsync(It.IsAny<GemTransaction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await service.GrantGemsAsync(playerId, 5, GemTransactionType.DailyReward, refId);

        result.Should().BeTrue();
        repo.Verify(r => r.CreateAsync(It.IsAny<GemTransaction>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // SpendGemsAsync — balance guard
    // -----------------------------------------------------------------------

    [Fact]
    public async Task SpendGems_ReturnsInsufficientBalance_WhenBalanceTooLow()
    {
        var (service, repo, _) = BuildService();
        var playerId = Guid.NewGuid();

        repo.Setup(r => r.ReferenceExistsAsync(It.IsAny<Guid>(), It.IsAny<GemTransactionType>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repo.Setup(r => r.GetBalanceAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(3); // only 3 gems

        var result = await service.SpendGemsAsync(playerId, 10, GemTransactionType.EnergyRefill, "refill:001");

        result.Should().Be(GemSpendOutcome.InsufficientBalance,
            "cannot spend more gems than the current balance");
        repo.Verify(r => r.CreateAsync(It.IsAny<GemTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SpendGems_ReturnsAlreadyProcessed_WhenReferenceIdAlreadyExists()
    {
        // Simulates crash-recovery: the ledger row was committed on the first call but the
        // grant step was lost.  The retry must get AlreadyProcessed (not InsufficientBalance)
        // so callers can re-run the idempotent grant without double-charging the player.
        var (service, repo, _) = BuildService();
        var playerId = Guid.NewGuid();
        const string refId = "unitbuy:player1:gen_ironward";

        repo.Setup(r => r.ReferenceExistsAsync(playerId, GemTransactionType.UnitPurchase, refId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // ledger row already exists

        var result = await service.SpendGemsAsync(playerId, 50, GemTransactionType.UnitPurchase, refId);

        result.Should().Be(GemSpendOutcome.AlreadyProcessed,
            "an existing referenceId indicates the charge already committed — return AlreadyProcessed for idempotent replay");
        repo.Verify(r => r.CreateAsync(It.IsAny<GemTransaction>(), It.IsAny<CancellationToken>()), Times.Never,
            "no second ledger row may be written on replay");
        repo.Verify(r => r.GetBalanceAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never,
            "balance check is skipped entirely when the referenceId already exists");
    }

    [Fact]
    public async Task SpendGems_ReturnsCharged_WhenSuccessful()
    {
        var (service, repo, _) = BuildService();
        var playerId = Guid.NewGuid();
        const string refId = "unitbuy:player1:gen_ironward";

        repo.Setup(r => r.ReferenceExistsAsync(playerId, GemTransactionType.UnitPurchase, refId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repo.Setup(r => r.GetBalanceAsync(playerId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(200);
        repo.Setup(r => r.CreateAsync(It.IsAny<GemTransaction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await service.SpendGemsAsync(playerId, 50, GemTransactionType.UnitPurchase, refId);

        result.Should().Be(GemSpendOutcome.Charged, "successful spend returns Charged");
        repo.Verify(r => r.CreateAsync(It.IsAny<GemTransaction>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    // -----------------------------------------------------------------------
    // DailyRefillAsync — once-per-day enforcement
    // -----------------------------------------------------------------------

    [Fact]
    public async Task DailyRefill_Succeeds_WhenNotYetClaimedToday()
    {
        var (service, repo, _) = BuildService();
        var playerId = Guid.NewGuid();
        var expectedRef = $"daily:{DateTimeOffset.UtcNow:yyyy-MM-dd}";

        repo.Setup(r => r.ReferenceExistsAsync(playerId, GemTransactionType.DailyReward, expectedRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repo.Setup(r => r.CreateAsync(It.IsAny<GemTransaction>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var result = await service.DailyRefillAsync(playerId);

        result.Should().BeTrue();
        repo.Verify(r => r.CreateAsync(It.IsAny<GemTransaction>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task DailyRefill_ReturnsFalse_WhenAlreadyClaimedToday()
    {
        var (service, repo, _) = BuildService();
        var playerId = Guid.NewGuid();
        var expectedRef = $"daily:{DateTimeOffset.UtcNow:yyyy-MM-dd}";

        repo.Setup(r => r.ReferenceExistsAsync(playerId, GemTransactionType.DailyReward, expectedRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // already claimed

        var result = await service.DailyRefillAsync(playerId);

        result.Should().BeFalse("daily refill is idempotent via referenceId — second call on same day is rejected");
        repo.Verify(r => r.CreateAsync(It.IsAny<GemTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
