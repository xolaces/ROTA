using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Services;

// BETA — Slice 4 write-hook tests for System 17 Leaderboards.
// Verifies that:
//   - EnergyService.SpendEnergyAsync → RecordEnergySpendAsync (EnergySpent Weekly + Monthly)
//   - RaidService.HitRaidAsync      → RecordRaidHitAsync     (DamageDealt Weekly + Monthly + LargestHit Daily)
//   - Cached-replay (Redis idempotency) path does NOT call any leaderboard write
//   - Leaderboard failure does NOT fail the energy spend
//   - Period-key boundary tests (day N vs N+1, week/month crossings)
public class LeaderboardWriteHookTests
{
    // ────────────────────────────────────────────────────────────────────────────
    // HELPERS — LeaderboardService (write methods)
    // ────────────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Builds a real <see cref="LeaderboardService"/> with a mocked repo + a real resolver.
    /// Used for write-method unit tests so the resolver produces stable period_keys.
    /// </summary>
    private static (LeaderboardService service,
                    Mock<ILeaderboardEntryRepository> repo,
                    PeriodKeyResolver resolver)
        BuildLeaderboardService()
    {
        var repo     = new Mock<ILeaderboardEntryRepository>();
        var players  = new Mock<IPlayerRepository>();

        repo.Setup(r => r.IncrementAsync(
                It.IsAny<Guid>(), It.IsAny<LeaderboardBoard>(), It.IsAny<LeaderboardPeriod>(),
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        repo.Setup(r => r.MaxUpdateAsync(
                It.IsAny<Guid>(), It.IsAny<LeaderboardBoard>(), It.IsAny<LeaderboardPeriod>(),
                It.IsAny<string>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var cfg      = Options.Create(new LeaderboardConfig());
        var resolver = new PeriodKeyResolver(cfg);
        var service  = new LeaderboardService(repo.Object, players.Object, resolver, cfg);
        return (service, repo, resolver);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 1. RecordEnergySpendAsync — increments both boards with the correct period_keys
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordEnergySpend_IncrementsBothBoards_WithCorrectPeriodKeys()
    {
        var (service, repo, resolver) = BuildLeaderboardService();
        var playerId = Guid.NewGuid();
        var at       = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero); // Tuesday of W24
        long amount  = 25;

        await service.RecordEnergySpendAsync(playerId, amount, at);

        var expectedWeek  = resolver.Resolve(at, LeaderboardPeriod.Weekly);   // "week:2026-W24"
        var expectedMonth = resolver.Resolve(at, LeaderboardPeriod.Monthly);  // "month:2026-06"

        repo.Verify(r => r.IncrementAsync(
            playerId, LeaderboardBoard.EnergySpent, LeaderboardPeriod.Weekly,
            expectedWeek, amount, at, It.IsAny<CancellationToken>()),
            Times.Once, "must increment EnergySpent/Weekly by the spent amount");

        repo.Verify(r => r.IncrementAsync(
            playerId, LeaderboardBoard.EnergySpent, LeaderboardPeriod.Monthly,
            expectedMonth, amount, at, It.IsAny<CancellationToken>()),
            Times.Once, "must increment EnergySpent/Monthly by the spent amount");

        // No MaxUpdateAsync calls for energy boards
        repo.Verify(r => r.MaxUpdateAsync(
            It.IsAny<Guid>(), It.IsAny<LeaderboardBoard>(), It.IsAny<LeaderboardPeriod>(),
            It.IsAny<string>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never, "MaxUpdate must not be called for energy spend");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 2. RecordRaidHitAsync — increments damage boards + max-updates largest-hit
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordRaidHit_IncrementsDamageBoards_AndMaxUpdatesLargestHit()
    {
        var (service, repo, resolver) = BuildLeaderboardService();
        var playerId    = Guid.NewGuid();
        var at          = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);
        long damageFinal = 15000;

        await service.RecordRaidHitAsync(playerId, damageFinal, at);

        var expectedWeek  = resolver.Resolve(at, LeaderboardPeriod.Weekly);
        var expectedMonth = resolver.Resolve(at, LeaderboardPeriod.Monthly);
        var expectedDay   = resolver.Resolve(at, LeaderboardPeriod.Daily);

        repo.Verify(r => r.IncrementAsync(
            playerId, LeaderboardBoard.DamageDealt, LeaderboardPeriod.Weekly,
            expectedWeek, damageFinal, at, It.IsAny<CancellationToken>()),
            Times.Once, "must increment DamageDealt/Weekly by damageFinal");

        repo.Verify(r => r.IncrementAsync(
            playerId, LeaderboardBoard.DamageDealt, LeaderboardPeriod.Monthly,
            expectedMonth, damageFinal, at, It.IsAny<CancellationToken>()),
            Times.Once, "must increment DamageDealt/Monthly by damageFinal");

        repo.Verify(r => r.MaxUpdateAsync(
            playerId, LeaderboardBoard.LargestHit, LeaderboardPeriod.Daily,
            expectedDay, damageFinal, at, It.IsAny<CancellationToken>()),
            Times.Once, "must max-update LargestHit/Daily with damageFinal");
    }

    [Fact]
    public async Task RecordRaidHit_SmallerLaterHit_PassesRawValue_ReposGREATESTLogicHandlesIt()
    {
        // The service always passes the raw damageFinal to MaxUpdateAsync.
        // The repo's ON CONFLICT GREATEST logic ensures the stored value never lowers.
        // This test confirms the service does NOT filter or compare — it passes through.
        var (service, repo, resolver) = BuildLeaderboardService();
        var playerId = Guid.NewGuid();
        var at       = new DateTimeOffset(2026, 6, 10, 12, 0, 0, TimeSpan.Zero);

        await service.RecordRaidHitAsync(playerId, 50000, at);
        await service.RecordRaidHitAsync(playerId, 100,   at); // smaller later hit

        var dayKey = resolver.Resolve(at, LeaderboardPeriod.Daily);

        // Both calls must reach MaxUpdateAsync with their raw values — GREATEST is in SQL
        repo.Verify(r => r.MaxUpdateAsync(
            playerId, LeaderboardBoard.LargestHit, LeaderboardPeriod.Daily,
            dayKey, 50000L, at, It.IsAny<CancellationToken>()),
            Times.Once, "first hit must reach MaxUpdateAsync with its raw value");

        repo.Verify(r => r.MaxUpdateAsync(
            playerId, LeaderboardBoard.LargestHit, LeaderboardPeriod.Daily,
            dayKey, 100L, at, It.IsAny<CancellationToken>()),
            Times.Once, "smaller later hit must still reach MaxUpdateAsync; GREATEST logic is in SQL");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 3. Period-key boundary — hits on day N and day N+1 land in different keys
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RecordRaidHit_DayBoundary_ProducesDifferentDayKeys()
    {
        var (service, repo, resolver) = BuildLeaderboardService();
        var playerId = Guid.NewGuid();

        // 23:59:59 UTC on day N
        var dayN     = new DateTimeOffset(2026, 6, 10, 23, 59, 59, TimeSpan.Zero);
        // 00:00:00 UTC on day N+1 (exactly at midnight)
        var dayNplus1 = new DateTimeOffset(2026, 6, 11, 0, 0, 0, TimeSpan.Zero);

        await service.RecordRaidHitAsync(playerId, 1000, dayN);
        await service.RecordRaidHitAsync(playerId, 2000, dayNplus1);

        var keyN     = resolver.Resolve(dayN,      LeaderboardPeriod.Daily);
        var keyNplus1 = resolver.Resolve(dayNplus1, LeaderboardPeriod.Daily);

        keyN.Should().NotBe(keyNplus1, "different UTC calendar days must produce different day keys");
        keyN.Should().Be("day:2026-06-10");
        keyNplus1.Should().Be("day:2026-06-11");
    }

    [Fact]
    public async Task RecordEnergySpend_WeekBoundary_ProducesDifferentWeekKeys()
    {
        var (service, repo, resolver) = BuildLeaderboardService();
        var playerId = Guid.NewGuid();

        // Sunday 2026-06-07 23:59:59 — last second of ISO week 23
        var endOfWeek23   = new DateTimeOffset(2026, 6, 7, 23, 59, 59, TimeSpan.Zero);
        // Monday 2026-06-08 00:00:00 — first second of ISO week 24
        var startOfWeek24 = new DateTimeOffset(2026, 6, 8, 0, 0, 0, TimeSpan.Zero);

        await service.RecordEnergySpendAsync(playerId, 10, endOfWeek23);
        await service.RecordEnergySpendAsync(playerId, 10, startOfWeek24);

        var weekKey23 = resolver.Resolve(endOfWeek23,   LeaderboardPeriod.Weekly);
        var weekKey24 = resolver.Resolve(startOfWeek24, LeaderboardPeriod.Weekly);

        weekKey23.Should().NotBe(weekKey24, "Sunday end-of-week23 and Monday start-of-week24 must land in different week buckets");
        weekKey23.Should().Be("week:2026-W23");
        weekKey24.Should().Be("week:2026-W24");
    }

    [Fact]
    public async Task RecordEnergySpend_MonthBoundary_ProducesDifferentMonthKeys()
    {
        var (service, repo, resolver) = BuildLeaderboardService();
        var playerId = Guid.NewGuid();

        var lastSecondJune = new DateTimeOffset(2026, 6, 30, 23, 59, 59, TimeSpan.Zero);
        var firstSecondJuly = new DateTimeOffset(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

        var juneKey = resolver.Resolve(lastSecondJune,  LeaderboardPeriod.Monthly);
        var julyKey = resolver.Resolve(firstSecondJuly, LeaderboardPeriod.Monthly);

        juneKey.Should().Be("month:2026-06");
        julyKey.Should().Be("month:2026-07");
        juneKey.Should().NotBe(julyKey, "different calendar months must produce different month keys");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 4. EnergyService hook — leaderboard failure does NOT fail the spend
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SpendEnergy_LeaderboardFailure_DoesNotFailTheSpend()
    {
        // Arrange: leaderboard throws an exception on RecordEnergySpendAsync.
        var resources    = new Mock<IPlayerResourceRepository>();
        var auditLog     = new Mock<IAuditLogRepository>();
        var players      = new Mock<IPlayerRepository>();
        var classService = new Mock<IClassService>();
        var leaderboards = new Mock<ILeaderboardService>();

        classService.Setup(c => c.GetRegenRates(It.IsAny<PlayerClass>()))
            .Returns(new ClassRegenRates
            {
                CurrentClass                     = "Conscript",
                EnergyRegenMinutesPerPoint       = 0,
                StaminaRegenMinutesPerPoint      = 0,
                GuildStaminaRegenMinutesPerPoint = 2,
                IsConverged                      = false,
            });

        players.Setup(p => p.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Player.Create("u", "u@t.com", "h"));

        // Resource with enough energy to spend
        var resource = PlayerResource.Create(Guid.NewGuid(), ResourceType.Energy, 50, regenPerMinute: 0);
        resource.SaveCheckpoint(20, DateTimeOffset.UtcNow);

        resources.Setup(r => r.AtomicUpdateAsync(
                It.IsAny<Guid>(), ResourceType.Energy,
                It.IsAny<Func<PlayerResource, bool>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, ResourceType _, Func<PlayerResource, bool> fn, CancellationToken _)
                => fn(resource));

        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        // The leaderboard throws — must be swallowed
        leaderboards.Setup(l => l.RecordEnergySpendAsync(
                It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("simulated leaderboard failure"));

        var service = new EnergyService(
            resources.Object, auditLog.Object, players.Object, classService.Object,
            leaderboards.Object, NullLogger<EnergyService>.Instance);

        // Act
        var result = await service.SpendEnergyAsync(Guid.NewGuid(), ResourceType.Energy, 5);

        // Assert — spend succeeded despite leaderboard throw
        result.Should().BeTrue("energy spend must succeed even when the leaderboard write throws");
        leaderboards.Verify(l => l.RecordEnergySpendAsync(
            It.IsAny<Guid>(), 5L, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once, "leaderboard was called once and then swallowed its exception");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 5. EnergyService hook — Stamina spend does NOT call the leaderboard
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SpendStamina_DoesNotCallLeaderboard()
    {
        // Q6: only ResourceType.Energy counts toward the Questing board.
        var resources    = new Mock<IPlayerResourceRepository>();
        var auditLog     = new Mock<IAuditLogRepository>();
        var players      = new Mock<IPlayerRepository>();
        var classService = new Mock<IClassService>();
        var leaderboards = new Mock<ILeaderboardService>();

        classService.Setup(c => c.GetRegenRates(It.IsAny<PlayerClass>()))
            .Returns(new ClassRegenRates
            {
                CurrentClass                     = "Conscript",
                EnergyRegenMinutesPerPoint       = 0,
                StaminaRegenMinutesPerPoint      = 0,
                GuildStaminaRegenMinutesPerPoint = 2,
                IsConverged                      = false,
            });

        players.Setup(p => p.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Player.Create("u", "u@t.com", "h"));

        var resource = PlayerResource.Create(Guid.NewGuid(), ResourceType.Stamina, 50, regenPerMinute: 0);
        resource.SaveCheckpoint(20, DateTimeOffset.UtcNow);

        resources.Setup(r => r.AtomicUpdateAsync(
                It.IsAny<Guid>(), ResourceType.Stamina,
                It.IsAny<Func<PlayerResource, bool>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, ResourceType _, Func<PlayerResource, bool> fn, CancellationToken _)
                => fn(resource));

        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new EnergyService(
            resources.Object, auditLog.Object, players.Object, classService.Object,
            leaderboards.Object, NullLogger<EnergyService>.Instance);

        var result = await service.SpendEnergyAsync(Guid.NewGuid(), ResourceType.Stamina, 3);

        result.Should().BeTrue();
        leaderboards.Verify(l => l.RecordEnergySpendAsync(
            It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never, "Stamina spend must NOT call the EnergySpent leaderboard (Q6: Energy only)");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 6. EnergyService hook — successful Energy spend calls leaderboard once
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SpendEnergy_SuccessfulSpend_CallsLeaderboardOnce_WithCorrectAmount()
    {
        var resources    = new Mock<IPlayerResourceRepository>();
        var auditLog     = new Mock<IAuditLogRepository>();
        var players      = new Mock<IPlayerRepository>();
        var classService = new Mock<IClassService>();
        var leaderboards = new Mock<ILeaderboardService>();

        classService.Setup(c => c.GetRegenRates(It.IsAny<PlayerClass>()))
            .Returns(new ClassRegenRates
            {
                CurrentClass                     = "Conscript",
                EnergyRegenMinutesPerPoint       = 0,
                StaminaRegenMinutesPerPoint      = 0,
                GuildStaminaRegenMinutesPerPoint = 2,
                IsConverged                      = false,
            });

        var playerId = Guid.NewGuid();
        players.Setup(p => p.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Player.Create("u", "u@t.com", "h"));

        var resource = PlayerResource.Create(Guid.NewGuid(), ResourceType.Energy, 50, regenPerMinute: 0);
        resource.SaveCheckpoint(30, DateTimeOffset.UtcNow);

        resources.Setup(r => r.AtomicUpdateAsync(
                It.IsAny<Guid>(), ResourceType.Energy,
                It.IsAny<Func<PlayerResource, bool>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, ResourceType _, Func<PlayerResource, bool> fn, CancellationToken _)
                => fn(resource));

        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        leaderboards.Setup(l => l.RecordEnergySpendAsync(
                It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var service = new EnergyService(
            resources.Object, auditLog.Object, players.Object, classService.Object,
            leaderboards.Object, NullLogger<EnergyService>.Instance);

        var result = await service.SpendEnergyAsync(playerId, ResourceType.Energy, 10);

        result.Should().BeTrue();
        leaderboards.Verify(l => l.RecordEnergySpendAsync(
            playerId, 10L, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once, "must call RecordEnergySpendAsync exactly once with the exact spent amount");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 7. EnergyService hook — failed spend does NOT call leaderboard
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task SpendEnergy_InsufficientEnergy_DoesNotCallLeaderboard()
    {
        var resources    = new Mock<IPlayerResourceRepository>();
        var auditLog     = new Mock<IAuditLogRepository>();
        var players      = new Mock<IPlayerRepository>();
        var classService = new Mock<IClassService>();
        var leaderboards = new Mock<ILeaderboardService>();

        classService.Setup(c => c.GetRegenRates(It.IsAny<PlayerClass>()))
            .Returns(new ClassRegenRates
            {
                CurrentClass                     = "Conscript",
                EnergyRegenMinutesPerPoint       = 0,
                StaminaRegenMinutesPerPoint      = 0,
                GuildStaminaRegenMinutesPerPoint = 2,
                IsConverged                      = false,
            });

        players.Setup(p => p.FindByIdAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Player.Create("u", "u@t.com", "h"));

        // Only 2 energy — spend of 10 fails
        var resource = PlayerResource.Create(Guid.NewGuid(), ResourceType.Energy, 50, regenPerMinute: 0);
        resource.SaveCheckpoint(2, DateTimeOffset.UtcNow);

        resources.Setup(r => r.AtomicUpdateAsync(
                It.IsAny<Guid>(), ResourceType.Energy,
                It.IsAny<Func<PlayerResource, bool>>(),
                It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, ResourceType _, Func<PlayerResource, bool> fn, CancellationToken _)
                => fn(resource));

        var service = new EnergyService(
            resources.Object, auditLog.Object, players.Object, classService.Object,
            leaderboards.Object, NullLogger<EnergyService>.Instance);

        var result = await service.SpendEnergyAsync(Guid.NewGuid(), ResourceType.Energy, 10);

        result.Should().BeFalse("insufficient energy");
        leaderboards.Verify(l => l.RecordEnergySpendAsync(
            It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never, "must NOT call leaderboard on a failed spend");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 8. RaidService hook — successful hit calls RecordRaidHitAsync with damageFinal
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Hit_SuccessfulHit_CallsRecordRaidHitAsync_WithDamageFinal()
    {
        // Use seeded RNG so damageFinal is deterministic.
        // With seed=0, ATK=10, DEF=10 (default player), hitSize=1:
        //   multiplier ≈ 0.849 (first NextDouble() from seed 0)
        //   base = (10*4 + 10) * 1 * 0.849 ≈ 42
        var b = BuildRaidService(new Random(0));
        var player = MakePlayer();
        var raid   = MakeRaid();

        SetupHitScaffolding(b, player, raid);
        b.Participants.Setup(p => p.FindByRaidAndPlayerAsync(raid.Id, player.Id, It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant?)null);
        b.Participants.Setup(p => p.CreateAsync(It.IsAny<RaidParticipant>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((RaidParticipant p, CancellationToken _) => p);

        long capturedDamage = -1;
        b.Leaderboards.Setup(l => l.RecordRaidHitAsync(
                player.Id, It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Callback<Guid, long, DateTimeOffset, CancellationToken>((_, dmg, _, _) => capturedDamage = dmg)
            .Returns(Task.CompletedTask);

        var result = await b.Service.HitRaidAsync(player.Id, raid.Id, 1, Guid.NewGuid().ToString());

        result.Success.Should().BeTrue();
        capturedDamage.Should().Be(result.Response!.DamageDealt,
            "RecordRaidHitAsync must be called with the exact damageFinal that was returned in the response");
        b.Leaderboards.Verify(l => l.RecordRaidHitAsync(
            player.Id, result.Response.DamageDealt, It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Once);
    }

    // ────────────────────────────────────────────────────────────────────────────
    // 9. RaidService hook — Redis cached-replay does NOT call the leaderboard
    // ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task Hit_CachedReplay_DoesNotCallLeaderboard()
    {
        // The idempotency early-return (TryAcquireSlotAsync returns false + cached response)
        // fires BEFORE AtomicApplyHitAsync is entered, so RecordRaidHitAsync must never be called.
        var b      = BuildRaidService();
        var key    = Guid.NewGuid().ToString();
        var cached = new RaidHitResponse { Success = true, DamageDealt = 99999 };

        // Audit fix: the service now scopes the cache key to player+raid, so match on the suffix.
        b.HitCache.Setup(c => c.TryAcquireSlotAsync(It.Is<string>(s => s.EndsWith(key)), It.IsAny<CancellationToken>()))
            .ReturnsAsync((false, cached));

        var raid = MakeRaid();
        b.Raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());

        var result = await b.Service.HitRaidAsync(Guid.NewGuid(), raid.Id, 1, key);

        result.Success.Should().BeTrue();
        result.Response!.DamageDealt.Should().Be(99999, "returns the cached response unmodified");

        b.Leaderboards.Verify(l => l.RecordRaidHitAsync(
            It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()),
            Times.Never, "CRITICAL: cached-replay path must never call RecordRaidHitAsync");
    }

    // ────────────────────────────────────────────────────────────────────────────
    // HELPERS — RaidService (mirrors RaidServiceTests.BuildService exactly)
    // ────────────────────────────────────────────────────────────────────────────

    private record RaidBundle(
        RaidService Service,
        Mock<IActiveRaidRepository> Raids,
        Mock<IRaidParticipantRepository> Participants,
        Mock<IPlayerRepository> Players,
        Mock<IPlayerResourceRepository> Resources,
        Mock<IEnergyService> Energy,
        Mock<IAuditLogRepository> AuditLog,
        Mock<IRaidDefinitionProvider> Definitions,
        Mock<IRaidHitCache> HitCache,
        Mock<IEquipmentService> Equipment,
        Mock<IStatService> Stats,
        Mock<ILeaderboardService> Leaderboards);

    private static RaidBundle BuildRaidService(Random? random = null)
    {
        var raids          = new Mock<IActiveRaidRepository>();
        var participants   = new Mock<IRaidParticipantRepository>();
        var players        = new Mock<IPlayerRepository>();
        var resources      = new Mock<IPlayerResourceRepository>();
        var energy         = new Mock<IEnergyService>();
        var gems           = new Mock<IGemService>();
        var stats          = new Mock<IStatService>();
        var inventory      = new Mock<IPlayerInventoryRepository>();
        var itemDefs       = new Mock<IItemDefinitionProvider>();
        var lootTables     = new Mock<ILootTableProvider>();
        var auditLog       = new Mock<IAuditLogRepository>();
        var definitions    = new Mock<IRaidDefinitionProvider>();
        var hitCache       = new Mock<IRaidHitCache>();
        var equipment      = new Mock<IEquipmentService>();
        var raidMagics     = new Mock<IRaidMagicRepository>();
        var magicDefs      = new Mock<IMagicDefinitionProvider>();
        var magicSvc       = new Mock<IMagicService>();
        var playerLegions  = new Mock<IPlayerLegionRepository>();
        var legionSlots    = new Mock<IPlayerLegionSlotRepository>();
        var unitDefs       = new Mock<IUnitDefinitionProvider>();
        var legionDefs     = new Mock<ILegionDefinitionProvider>();
        var commanderGear  = new Mock<IPlayerCommanderGearRepository>();
        var gearDefs       = new Mock<IGearDefinitionProvider>();
        var legionSvc      = new Mock<ILegionService>();
        var leaderboards   = new Mock<ILeaderboardService>();
        // System 16 Slice 4 — Gauntlet amplifier deps. These tests only exercise non-Gauntlet raids
        // (GauntletEventId is null), so trophy lookup returns empty and the rest never fire.
        var trophyRepo        = new Mock<IPlayerGauntletTrophyRepository>();
        var gauntletContent   = new Mock<IGauntletContentProvider>();
        var playerEventMagics = new Mock<IPlayerEventMagicRepository>();
        var playerMagicHonors = new Mock<IPlayerMagicHonorRepository>();
        var strikes           = new Mock<IStrikeRepository>();
        var gauntletScoring   = new Mock<IGauntletScoringService>();
        var gauntletCurrency  = new Mock<IGauntletCurrencyRepository>();   // System 16 Slice 5 — per-defeat Token reward
        // System 21 Slice 3b — guild-raid deps (unused by these leaderboard-hook tests; raids are non-guild).
        var guildMemberships  = new Mock<IGuildMembershipRepository>();
        var guildEconomy      = new Mock<IGuildEconomyRepository>();
        trophyRepo.Setup(r => r.GetForPlayerAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerGauntletTrophy>());

        hitCache.Setup(c => c.TryAcquireSlotAsync(It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((true, (RaidHitResponse?)null));
        hitCache.Setup(c => c.StoreResultAsync(It.IsAny<string>(), It.IsAny<RaidHitResponse>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        auditLog.Setup(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        stats.Setup(s => s.GrantLevelUpPointsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        stats.Setup(s => s.AddUnassignedPointsAsync(It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        stats.Setup(s => s.XpToNextLevel(It.IsAny<int>())).Returns(1000);
        stats.Setup(s => s.GetCritProfile(It.IsAny<int>()))
            .Returns(new CritProfile(Chance: 0.0, Multiplier: 1.5));
        equipment.Setup(e => e.GetEffectiveCombatDataAsync(
                It.IsAny<Guid>(), It.IsAny<int>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((Guid _, int atk, int def, CancellationToken _) =>
                new EffectiveCombatData(atk, def, null, 0.0));
        raidMagics.Setup(r => r.GetForRaidAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<RaidMagic>());
        magicDefs.Setup(d => d.GetAll()).Returns(new List<MagicDefinition>());
        magicSvc.Setup(m => m.GrantMagicAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        playerLegions.Setup(r => r.GetActiveAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerLegion?)null);
        legionSlots.Setup(r => r.GetForLegionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<PlayerLegionSlot>());
        commanderGear.Setup(r => r.FindAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync((PlayerCommanderGear?)null);
        legionSvc.Setup(l => l.GrantUnitAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        legionSvc.Setup(l => l.GrantLegionAsync(It.IsAny<Guid>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        leaderboards.Setup(l => l.RecordRaidHitAsync(
                It.IsAny<Guid>(), It.IsAny<long>(), It.IsAny<DateTimeOffset>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var magicCfg  = Options.Create(new MagicConfig());
        var legionCfg = Options.Create(new LegionConfig());
        var combatCfg = Options.Create(new CombatConfig());
        var gauntletCfg = Options.Create(new GauntletConfig());
        var mastery = new Mock<IMasteryService>();
        mastery.Setup(m => m.GetModifiersAsync(It.IsAny<Guid>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new MasteryModifiers(new MasteryCombatModifiers(0.0, 0.0), new MasteryLootModifiers(1.0, 1.0, 1.0, 0.0)));
        // Ticket 50 — friendship repo for the FriendsOnly visibility tier (no friends by default).
        var friendships = new Mock<IFriendshipRepository>();
        friendships.Setup(r => r.ListForPlayerAsync(
                It.IsAny<Guid>(), It.IsAny<FriendshipStatus?>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<Friendship>());
        var achievements = new Mock<IAchievementService>();

        var service = new RaidService(
            raids.Object, participants.Object, players.Object, resources.Object,
            energy.Object, gems.Object, stats.Object, inventory.Object,
            itemDefs.Object, lootTables.Object, auditLog.Object,
            definitions.Object, hitCache.Object, equipment.Object,
            raidMagics.Object, magicDefs.Object, magicSvc.Object, magicCfg,
            playerLegions.Object, legionSlots.Object, unitDefs.Object, legionDefs.Object,
            legionCfg, commanderGear.Object, gearDefs.Object, legionSvc.Object,
            leaderboards.Object, combatCfg,
            trophyRepo.Object, gauntletContent.Object, playerEventMagics.Object,
            playerMagicHonors.Object, strikes.Object, gauntletScoring.Object, gauntletCfg,
            gauntletCurrency.Object, guildMemberships.Object, guildEconomy.Object, mastery.Object,
            achievements.Object, friendships.Object, random);

        return new RaidBundle(service, raids, participants, players, resources, energy,
            auditLog, definitions, hitCache, equipment, stats, leaderboards);
    }

    private static Player MakePlayer()
        => Player.Create("testuser", "test@rota.test", "hash");

    private static RaidDefinition IronColossus() => new()
    {
        Id                   = "raid_ironcolossus",
        Name                 = "The Iron Colossus",
        Tier                 = "World",
        BaseHp               = 100000,
        PersonalBaseHp       = 500,
        TimerHours           = 48,
        StaminaCostPerHit    = 1,
        GoldPerStamina       = 1,
        LootTableId          = "lt_raid_ironcolossus",
        BaseGoldReward       = 500,
        BaseExperienceReward = 300,
        BaseGemReward        = 2,
        HasOnHitDrops        = false,
    };

    private static ActiveRaid MakeRaid(long currentHp = 100000, bool isDefeated = false)
    {
        var raid = ActiveRaid.Create(
            "raid_ironcolossus", Guid.NewGuid(), 100000,
            DateTimeOffset.UtcNow.AddHours(48), RaidDifficulty.Normal);
        if (currentHp < 100000)
            raid.TakeDamage(100000 - currentHp);
        if (isDefeated)
            raid.MarkDefeated();
        return raid;
    }

    private static void SetupHitScaffolding(RaidBundle b, Player player, ActiveRaid raid, bool staminaSuccess = true)
    {
        b.Raids.Setup(r => r.FindByIdAsync(raid.Id, It.IsAny<CancellationToken>())).ReturnsAsync(raid);
        b.Raids.Setup(r => r.AtomicApplyHitAsync(
                raid.Id,
                It.IsAny<Func<ActiveRaid, Task<bool>>>(),
                It.IsAny<CancellationToken>()))
            .Returns<Guid, Func<ActiveRaid, Task<bool>>, CancellationToken>(
                (_, mutate, _) => mutate(raid));
        b.Definitions.Setup(d => d.GetById("raid_ironcolossus")).Returns(IronColossus());
        b.Energy.Setup(e => e.SpendEnergyAsync(player.Id, ResourceType.Stamina, It.IsAny<int>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(staminaSuccess);
        b.Players.Setup(p => p.FindByIdWithStatsAsync(player.Id, It.IsAny<CancellationToken>())).ReturnsAsync(player);
        b.Players.Setup(p => p.UpdateAsync(It.IsAny<Player>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
        b.Resources.Setup(r => r.GetAsync(player.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
            .ReturnsAsync(PlayerResource.Create(Guid.NewGuid(), ResourceType.Stamina, 5, regenPerMinute: 1));
        b.Energy.Setup(e => e.GetCurrentEnergyAsync(player.Id, ResourceType.Stamina, It.IsAny<CancellationToken>()))
            .ReturnsAsync(4);
    }
}
