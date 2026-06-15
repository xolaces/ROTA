using FluentAssertions;
using Moq;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.UnitTests.Services;

public class GemServiceTests
{
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

    // GetBalanceAsync — ledger sum

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

    // GrantGemsAsync — idempotency

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
        repo.Verify(r => r.TryCreateAsync(It.IsAny<GemTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task GrantGems_Succeeds_WhenReferenceIdIsNew()
    {
        var (service, repo, _) = BuildService();
        var playerId = Guid.NewGuid();
        const string refId = "daily:2026-05-24";

        repo.Setup(r => r.ReferenceExistsAsync(playerId, GemTransactionType.DailyReward, refId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repo.Setup(r => r.TryCreateAsync(It.IsAny<GemTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await service.GrantGemsAsync(playerId, 5, GemTransactionType.DailyReward, refId);

        result.Should().BeTrue();
        repo.Verify(r => r.TryCreateAsync(It.IsAny<GemTransaction>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task GrantGems_ReturnsFalse_WhenConcurrentDuplicateHitsUniqueIndex()
    {
        // Audit fix — two concurrent grants with the same reference both pass the pre-check; the
        // loser hits the unique index inside TryCreateAsync and must get a clean false, not a 500.
        var (service, repo, auditLog) = BuildService();
        var playerId = Guid.NewGuid();
        const string refId = "levelup:gems:p:5";

        repo.Setup(r => r.ReferenceExistsAsync(playerId, GemTransactionType.LevelUpReward, refId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // pre-check passed (the race window)
        repo.Setup(r => r.TryCreateAsync(It.IsAny<GemTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false); // unique-index violation absorbed by the repository

        var result = await service.GrantGemsAsync(playerId, 5, GemTransactionType.LevelUpReward, refId);

        result.Should().BeFalse("the unique-index loser reports already-granted");
        auditLog.Verify(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never,
            "no grant audit entry when nothing was granted");
    }

    // SpendGemsAsync — delegates to the atomic repository spend (audit fix)

    [Fact]
    public async Task SpendGems_ReturnsInsufficientBalance_WhenBalanceTooLow()
    {
        var (service, repo, auditLog) = BuildService();
        var playerId = Guid.NewGuid();

        repo.Setup(r => r.TrySpendAsync(playerId, 10, GemTransactionType.EnergyRefill, "refill:001", It.IsAny<CancellationToken>()))
            .ReturnsAsync(GemSpendOutcome.InsufficientBalance);

        var result = await service.SpendGemsAsync(playerId, 10, GemTransactionType.EnergyRefill, "refill:001");

        result.Should().Be(GemSpendOutcome.InsufficientBalance,
            "cannot spend more gems than the current balance");
        auditLog.Verify(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Never,
            "no spend audit entry when nothing was charged");
    }

    [Fact]
    public async Task SpendGems_ReturnsAlreadyProcessed_WhenReferenceIdAlreadyExists()
    {
        // Crash-recovery semantics: the ledger row committed on the first call but the grant step
        // was lost. The retry must get AlreadyProcessed (not InsufficientBalance) so callers can
        // re-run the idempotent grant without double-charging the player.
        var (service, repo, _) = BuildService();
        var playerId = Guid.NewGuid();
        const string refId = "unitbuy:player1:gen_ironward";

        repo.Setup(r => r.TrySpendAsync(playerId, 50, GemTransactionType.UnitPurchase, refId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GemSpendOutcome.AlreadyProcessed);

        var result = await service.SpendGemsAsync(playerId, 50, GemTransactionType.UnitPurchase, refId);

        result.Should().Be(GemSpendOutcome.AlreadyProcessed,
            "an existing referenceId indicates the charge already committed — return AlreadyProcessed for idempotent replay");
    }

    [Fact]
    public async Task SpendGems_ReturnsCharged_AndAudits_WhenSuccessful()
    {
        var (service, repo, auditLog) = BuildService();
        var playerId = Guid.NewGuid();
        const string refId = "unitbuy:player1:gen_ironward";

        repo.Setup(r => r.TrySpendAsync(playerId, 50, GemTransactionType.UnitPurchase, refId, It.IsAny<CancellationToken>()))
            .ReturnsAsync(GemSpendOutcome.Charged);

        var result = await service.SpendGemsAsync(playerId, 50, GemTransactionType.UnitPurchase, refId);

        result.Should().Be(GemSpendOutcome.Charged, "successful spend returns Charged");
        repo.Verify(r => r.TrySpendAsync(playerId, 50, GemTransactionType.UnitPurchase, refId, It.IsAny<CancellationToken>()),
            Times.Once, "the spend must go through the atomic repository path exactly once");
        auditLog.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == $"GemSpend:{GemTransactionType.UnitPurchase}"),
            It.IsAny<CancellationToken>()), Times.Once);
    }

    // DailyRefillAsync — once-per-day enforcement

    [Fact]
    public async Task DailyRefill_Succeeds_WhenNotYetClaimedToday()
    {
        var (service, repo, _) = BuildService();
        var playerId = Guid.NewGuid();
        var expectedRef = $"daily:{DateTimeOffset.UtcNow:yyyy-MM-dd}";

        repo.Setup(r => r.ReferenceExistsAsync(playerId, GemTransactionType.DailyReward, expectedRef, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);
        repo.Setup(r => r.TryCreateAsync(It.IsAny<GemTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await service.DailyRefillAsync(playerId);

        result.Should().BeTrue();
        repo.Verify(r => r.TryCreateAsync(It.IsAny<GemTransaction>(), It.IsAny<CancellationToken>()), Times.Once);
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
        repo.Verify(r => r.TryCreateAsync(It.IsAny<GemTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }
}
