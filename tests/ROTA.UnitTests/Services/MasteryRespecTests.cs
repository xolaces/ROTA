using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Services;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Services;

/// <summary>System 22 Phase A Slice 3 — re-spec / pledge economy (free unlock → free monthly → paid weekly).</summary>
public class MasteryRespecTests
{
    private static readonly IMasteryDefinitionProvider Defs = new MasteryDefinitionProvider(FindApiContentRoot());

    private sealed class Harness
    {
        public required MasteryService Svc { get; init; }
        public required Mock<IMasteryRespecRepository> Respec { get; init; }
        public required Mock<IGemService> Gems { get; init; }
        public required Mock<IMasteryRespecCapStore> CapStore { get; init; }
        public required Mock<IPlayerRepository> Players { get; init; }
        public required Mock<IPlayerMasteryRepository> MasteryRepo { get; init; }
        public required Mock<IAuditLogRepository> AuditLog { get; init; }
    }

    private static Harness Build(Player player, MasteryConfig? config = null)
    {
        var respec      = new Mock<IMasteryRespecRepository>();
        var gems        = new Mock<IGemService>();
        var capStore    = new Mock<IMasteryRespecCapStore>();
        var players     = new Mock<IPlayerRepository>();
        var masteryRepo = new Mock<IPlayerMasteryRepository>();
        var activityRepo = new Mock<IPlayerMasteryActivityRepository>();
        var leaderboard = new Mock<ILeaderboardEntryRepository>();
        var auditLog    = new Mock<IAuditLogRepository>();

        players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        respec.Setup(r => r.ReferenceExistsAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);                       // default: nothing consumed → first path (unlock) is free
        respec.Setup(r => r.CreateAsync(It.IsAny<MasteryRespecTransaction>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        capStore.Setup(c => c.IsPaidWeeklyUsedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var svc = new MasteryService(
            Defs, masteryRepo.Object, activityRepo.Object, players.Object, leaderboard.Object,
            respec.Object, gems.Object, capStore.Object, auditLog.Object,
            Options.Create(config ?? new MasteryConfig()));

        return new Harness
        {
            Svc = svc, Respec = respec, Gems = gems, CapStore = capStore,
            Players = players, MasteryRepo = masteryRepo, AuditLog = auditLog,
        };
    }

    private static Player NewPlayer() => Player.CreateWithId(Guid.NewGuid(), "u", "u@x.io", "h");

    // ── Free first pledge (NewAncientUnlock) ──────────────────────────────────

    [Fact]
    public async Task FirstPledge_IsFreeUnlock_NoGemSpend()
    {
        var player = NewPlayer();
        var h = Build(player);

        var result = await h.Svc.RespecAsync(player.Id, MasteryAncient.Wrath);

        result.Success.Should().BeTrue();
        result.Kind.Should().Be("NewAncientUnlock");
        result.GemSpent.Should().Be(0);
        result.NewPledge.Should().Be("Wrath");
        player.ActivePledgeAncient.Should().Be(MasteryAncient.Wrath);
        h.Gems.Verify(g => g.SpendGemsAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<GemTransactionType>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
        h.Players.Verify(p => p.UpdateAsync(player, It.IsAny<CancellationToken>()), Times.Once);
    }

    // ── Free monthly ───────────────────────────────────────────────────────────

    [Fact]
    public async Task Swap_UsesFreeMonthly_WhenUnlockConsumedButMonthlyAvailable()
    {
        var player = NewPlayer();
        var h = Build(player);
        // Unlock for the target already consumed; monthly + paid refs still free.
        h.Respec.Setup(r => r.ReferenceExistsAsync(player.Id,
                It.Is<string>(s => s.StartsWith("respec:unlock:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);

        var result = await h.Svc.RespecAsync(player.Id, MasteryAncient.Bulwark);

        result.Success.Should().BeTrue();
        result.Kind.Should().Be("FreeMonthly");
        result.GemSpent.Should().Be(0);
        h.Gems.Verify(g => g.SpendGemsAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<GemTransactionType>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Paid weekly ─────────────────────────────────────────────────────────────

    [Fact]
    public async Task Swap_PaidWeekly_WhenFreePathsConsumed_ChargesGems()
    {
        var player = NewPlayer();
        var h = Build(player, new MasteryConfig { RespecGemCost = 150 });
        // Unlock + monthly consumed → paid path. Cap free, gems Charged.
        h.Respec.Setup(r => r.ReferenceExistsAsync(player.Id,
                It.Is<string>(s => s.StartsWith("respec:unlock:") || s.StartsWith("respec:free:")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        h.Gems.Setup(g => g.SpendGemsAsync(player.Id, 150, GemTransactionType.MasteryRespec,
                It.Is<string>(s => s.StartsWith("respec:paid:")), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GemSpendOutcome.Charged);

        var result = await h.Svc.RespecAsync(player.Id, MasteryAncient.Hoard);

        result.Success.Should().BeTrue();
        result.Kind.Should().Be("Paid");
        result.GemSpent.Should().Be(150);
        player.ActivePledgeAncient.Should().Be(MasteryAncient.Hoard);
        h.CapStore.Verify(c => c.MarkPaidWeeklyUsedAsync(player.Id, It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Swap_PaidWeekly_RedisCapReached_NoCharge()
    {
        var player = NewPlayer();
        var h = Build(player);
        h.Respec.Setup(r => r.ReferenceExistsAsync(player.Id,
                It.Is<string>(s => s.StartsWith("respec:unlock:") || s.StartsWith("respec:free:")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        h.CapStore.Setup(c => c.IsPaidWeeklyUsedAsync(player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true); // weekly slot already used

        var result = await h.Svc.RespecAsync(player.Id, MasteryAncient.Hoard);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(MasteryRespecFailureCode.WeeklyCapReached);
        h.Gems.Verify(g => g.SpendGemsAsync(It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<GemTransactionType>(),
            It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Swap_PaidWeekly_GemAlreadyProcessed_IsWeeklyCapReached()
    {
        var player = NewPlayer();
        var h = Build(player);
        h.Respec.Setup(r => r.ReferenceExistsAsync(player.Id,
                It.Is<string>(s => s.StartsWith("respec:unlock:") || s.StartsWith("respec:free:")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        h.Gems.Setup(g => g.SpendGemsAsync(It.IsAny<Guid>(), It.IsAny<long>(), GemTransactionType.MasteryRespec,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GemSpendOutcome.AlreadyProcessed); // gem ledger says this week is charged

        var result = await h.Svc.RespecAsync(player.Id, MasteryAncient.Hoard);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(MasteryRespecFailureCode.WeeklyCapReached);
        player.ActivePledgeAncient.Should().BeNull(); // no swap applied
    }

    [Fact]
    public async Task Swap_PaidWeekly_InsufficientGems_Returns422Code_NoCapMark()
    {
        var player = NewPlayer();
        var h = Build(player);
        h.Respec.Setup(r => r.ReferenceExistsAsync(player.Id,
                It.Is<string>(s => s.StartsWith("respec:unlock:") || s.StartsWith("respec:free:")),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        h.Gems.Setup(g => g.SpendGemsAsync(It.IsAny<Guid>(), It.IsAny<long>(), GemTransactionType.MasteryRespec,
                It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(GemSpendOutcome.InsufficientBalance);

        var result = await h.Svc.RespecAsync(player.Id, MasteryAncient.Hoard);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(MasteryRespecFailureCode.InsufficientGems);
        h.CapStore.Verify(c => c.MarkPaidWeeklyUsedAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    // ── Guards + lossless ───────────────────────────────────────────────────────

    [Fact]
    public async Task Swap_ToAlreadyPledgedAncient_IsRejected_NoCharge()
    {
        var player = NewPlayer();
        player.SetPledge(MasteryAncient.Wrath);
        var h = Build(player);

        var result = await h.Svc.RespecAsync(player.Id, MasteryAncient.Wrath);

        result.Success.Should().BeFalse();
        result.FailureCode.Should().Be(MasteryRespecFailureCode.AlreadyPledged);
        h.Respec.Verify(r => r.CreateAsync(It.IsAny<MasteryRespecTransaction>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Swap_PlayerNotFound_IsRejected()
    {
        var player = NewPlayer();
        var h = Build(player);
        h.Players.Setup(p => p.FindByIdAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync((Player?)null);

        var result = await h.Svc.RespecAsync(player.Id, MasteryAncient.Wrath);

        result.FailureCode.Should().Be(MasteryRespecFailureCode.PlayerNotFound);
    }

    [Fact]
    public async Task Swap_IsLossless_NeverTouchesMasteryLevels()
    {
        var player = NewPlayer();
        var h = Build(player);

        await h.Svc.RespecAsync(player.Id, MasteryAncient.Discernment);

        // A re-spec only flips the pledge; it must never read/write mastery level rows.
        h.MasteryRepo.Verify(m => m.UpsertAsync(It.IsAny<PlayerMastery>(), It.IsAny<CancellationToken>()), Times.Never);
        h.MasteryRepo.Verify(m => m.EnsureAllAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Swap_WritesAuditRow()
    {
        var player = NewPlayer();
        var h = Build(player);

        await h.Svc.RespecAsync(player.Id, MasteryAncient.Wrath);

        h.AuditLog.Verify(a => a.AppendAsync(
            It.Is<AuditLog>(l => l.Action == "MasteryRespec:NewAncientUnlock"), It.IsAny<CancellationToken>()), Times.Once);
    }

    private static string FindApiContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "ROTA.Api");
            if (Directory.Exists(Path.Combine(candidate, "content")))
                return candidate;
            dir = dir.Parent;
        }
        return AppContext.BaseDirectory;
    }
}
