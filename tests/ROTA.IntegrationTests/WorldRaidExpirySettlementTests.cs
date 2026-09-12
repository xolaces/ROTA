using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using ROTA.Shared.DTOs;
using Testcontainers.PostgreSql;
using Testcontainers.Redis;

namespace ROTA.IntegrationTests;

// A World raid has no collective health. It runs on a timer and pays on an ABSOLUTE damage ladder, so
// its clock running out is not a failure — it is the only ending it has.
//
// THE DEFECT THESE PIN. Nothing ran at that moment. MarkDefeated() was the only thing that set
// Lootable and its sole production call site was the kill branch, which a MaxHp-0 raid can never reach.
// So a World raid hit day seven, HitRaidAsync began rejecting hits, and every participant's banked
// ladder damage sat on their row unpaid forever — LootRaidAsync refuses anything that is not Lootable.
//
// The fix is a sweeper that computes-and-stashes exactly as the kill path does, then flips the raid to
// Lootable so the ordinary per-participant claim grants it.
public class WorldRaidExpirySettlementTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;
    private RedisContainer _redis = null!;
    private WebApplicationFactory<Program> _factory = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_worldraid_settle_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        _redis = new RedisBuilder(TestContainerImages.Redis).Build();

        await Task.WhenAll(_postgres.StartAsync(), _redis.StartAsync());

        _factory = new WebApplicationFactory<Program>()
            .WithWebHostBuilder(host =>
            {
                host.UseContentRoot(FindApiContentRoot());
                host.ConfigureAppConfiguration((_, cfg) => cfg.AddInMemoryCollection(
                    new Dictionary<string, string?>
                    {
                        ["ConnectionStrings:DefaultConnection"] = _postgres.GetConnectionString(),
                        ["ConnectionStrings:Redis"]             = _redis.GetConnectionString(),
                        ["Seed:AdminPassword"]                  = "",
                    }));
            });

        using var scope = _factory.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<RotaDbContext>().Database.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        _factory?.Dispose();
        if (_redis is not null) await _redis.DisposeAsync();
        if (_postgres is not null) await _postgres.DisposeAsync();
    }

    private static string FindApiContentRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "ROTA.slnx")))
            dir = dir.Parent;
        if (dir is null) throw new InvalidOperationException("Could not locate the repo root (ROTA.slnx).");
        return Path.Combine(dir.FullName, "src", "ROTA.Api");
    }

    private static void LevelUpTo(Player player, int targetLevel, IStatService stats)
    {
        while (player.Level < targetLevel)
            player.AddExperience(stats.XpToNextLevel(player.Level), lvl => stats.XpToNextLevel(lvl));
    }

    // raid_ironcolossus is a real World raid: baseHp 0 and a populated damage ladder whose first rung is
    // 500 damage for 1 unassigned stat point. Stat points are the assertion of choice below because they
    // accrue per rung with NO chance roll — item and magic drops are RNG and would make the test flaky.
    private async Task<(Player player, ActiveRaid raid)> SeedAsync(
        string username, long maxHp, string definitionId = "raid_ironcolossus")
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        var stats = scope.ServiceProvider.GetRequiredService<IStatService>();

        var player = Player.Create(username, $"{username}@rota.test", "hash");
        LevelUpTo(player, 100, stats);
        db.Players.Add(player);

        var raid = ActiveRaid.Create(
            definitionId, player.Id, maxHp,
            expiresAt: DateTimeOffset.UtcNow.AddHours(168),
            difficulty: RaidDifficulty.Normal);
        db.ActiveRaids.Add(raid);

        await db.SaveChangesAsync();
        return (player, raid);
    }

    // Hits are rejected once a raid is past ExpiresAt, so a participant has to bank their damage while the
    // raid is still live and the clock is wound forward afterwards. ExpiresAt has a private setter by
    // design; reaching it through the EF property entry keeps that encapsulation intact rather than
    // opening a public setter for the benefit of a test.
    private async Task ExpireAsync(Guid raidId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        var raid = await db.ActiveRaids.FirstAsync(r => r.Id == raidId);
        db.Entry(raid).Property(nameof(ActiveRaid.ExpiresAt)).CurrentValue =
            DateTimeOffset.UtcNow.AddMinutes(-1);
        await db.SaveChangesAsync();
    }

    private async Task<int> SweepAsync()
    {
        using var scope = _factory.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IRaidService>().SettleExpiredRaidsAsync();
    }

    // Banks damage on the participant row directly rather than driving HitRaidAsync. Settlement reads
    // TotalDamageDealt and nothing else, and a real hit loop would bottom out on stamina long before it
    // cleared the higher rungs. That the hit path banks damage correctly is WorldRaidTimerTests' job.
    private async Task BankDamageAsync(Guid raidId, Guid playerId, long damage)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();

        var participant = RaidParticipant.Create(raidId, playerId);
        participant.RecordHit(damage);
        db.RaidParticipants.Add(participant);

        await db.SaveChangesAsync();
    }

    private async Task<ActiveRaid> ReloadAsync(Guid raidId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        return await db.ActiveRaids.AsNoTracking().FirstAsync(r => r.Id == raidId);
    }

    private async Task<RaidParticipant> ParticipantAsync(Guid raidId, Guid playerId)
    {
        using var scope = _factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        return await db.RaidParticipants.AsNoTracking()
            .FirstAsync(p => p.ActiveRaidId == raidId && p.PlayerId == playerId);
    }

    // ── the defect ────────────────────────────────────────────────────────────

    [Fact]
    public async Task AnExpiredWorldRaid_IsSettled_AndBecomesClaimable()
    {
        var (player, raid) = await SeedAsync("settle_becomes_lootable", maxHp: 0);
        await BankDamageAsync(raid.Id, player.Id, 50_000);
        await ExpireAsync(raid.Id);

        (await SweepAsync()).Should().Be(1, "one raid was due for settlement");

        var after = await ReloadAsync(raid.Id);
        after.LifecycleState.Should().Be(RaidLifecycleState.Lootable,
            "expiry is a World raid's only ending — until this ran the raid sat in Active forever and "
            + "nobody could claim the ladder they had banked");
        after.IsDefeated.Should().BeFalse(
            "nobody killed it; IsDefeated would be a lie, and nothing in the claim path reads it");
    }

    [Fact]
    public async Task AnExpiredWorldRaid_StashesTheDamageLadder_ThenPaysItOnClaim()
    {
        var (player, raid) = await SeedAsync("settle_pays_ladder", maxHp: 0);
        await BankDamageAsync(raid.Id, player.Id, 50_000);
        await ExpireAsync(raid.Id);
        await SweepAsync();

        var stashed = await ParticipantAsync(raid.Id, player.Id);
        stashed.TotalDamageDealt.Should().Be(50_000, "the banked damage is what the ladder pays on");
        stashed.StatPointsEarned.Should().BeGreaterThan(0,
            "every rung crossed banks its unassigned stat points, and settlement stashes them");
        stashed.RewardedAt.Should().BeNull(
            "settlement COMPUTES and STASHES; the grant happens on the player's own Loot claim");

        using var scope = _factory.Services.CreateScope();
        var raids = scope.ServiceProvider.GetRequiredService<IRaidService>();
        var loot = await raids.LootRaidAsync(player.Id, raid.Id);

        loot.Success.Should().BeTrue("a settled raid is Lootable, so the ordinary claim path works");
        loot.FailureCode.Should().Be(LootRaidFailureCode.None);
        ((long)loot.Rewards!.UnassignedStatPointsGranted).Should().Be(stashed.StatPointsEarned, "the claim pays what was stashed");

        // A looted raid is not stored: the claim took the participation, and, being the only one,
        // the raid with it.
        var db = scope.ServiceProvider.GetRequiredService<RotaDbContext>();
        (await db.RaidParticipants.AsNoTracking().AnyAsync(p => p.ActiveRaidId == raid.Id)).Should().BeFalse();
        (await db.ActiveRaids.AsNoTracking().AnyAsync(r => r.Id == raid.Id)).Should().BeFalse();
    }

    [Fact]
    public async Task SettlingTwice_PaysTheLadderOnlyOnce()
    {
        var (player, raid) = await SeedAsync("settle_idempotent", maxHp: 0);
        await BankDamageAsync(raid.Id, player.Id, 50_000);
        await ExpireAsync(raid.Id);

        (await SweepAsync()).Should().Be(1);
        var afterFirst = await ParticipantAsync(raid.Id, player.Id);

        (await SweepAsync()).Should().Be(0,
            "the Active to Lootable transition is the latch; a settled raid is no longer selected");

        var afterSecond = await ParticipantAsync(raid.Id, player.Id);
        afterSecond.StatPointsEarned.Should().Be(afterFirst.StatPointsEarned,
            "a re-swept raid must not re-stash — that would double the ladder payout");
        afterSecond.GemsEarned.Should().Be(afterFirst.GemsEarned);
    }

    [Fact]
    public async Task AWorldRaid_WithNoParticipants_StillSettles_SoItIsNotResweptForever()
    {
        var (_, raid) = await SeedAsync("settle_empty", maxHp: 0);
        await ExpireAsync(raid.Id);

        (await SweepAsync()).Should().Be(1);
        (await ReloadAsync(raid.Id)).LifecycleState.Should().Be(RaidLifecycleState.Lootable);
        (await SweepAsync()).Should().Be(0, "it is settled; it must not come back on every tick");
    }

    // ── the counterparts: everything else must be left alone ──────────────────

    [Fact]
    public async Task AnExpiredOrdinaryRaid_IsNotSettled_BecauseExpiryIsItsFailure()
    {
        // The scope must key on "has no health pool", NOT merely on "expired" — otherwise every raid the
        // players failed to kill would quietly pay out as though they had.
        var (player, raid) = await SeedAsync("settle_skips_ordinary", maxHp: 100_000_000_000);
        await BankDamageAsync(raid.Id, player.Id, 50_000);
        await ExpireAsync(raid.Id);

        (await SweepAsync()).Should().Be(0, "a health-pool raid that ran out of time was not beaten");

        var after = await ReloadAsync(raid.Id);
        after.LifecycleState.Should().Be(RaidLifecycleState.Active);
        (await ParticipantAsync(raid.Id, player.Id)).StatPointsEarned.Should().Be(0,
            "a failed raid pays no ladder");
    }

    [Fact]
    public async Task AWorldRaid_StillWithinItsTimer_IsNotSettledEarly()
    {
        var (player, raid) = await SeedAsync("settle_not_early", maxHp: 0);
        await BankDamageAsync(raid.Id, player.Id, 50_000);

        (await SweepAsync()).Should().Be(0, "the clock has six more days to run");
        (await ReloadAsync(raid.Id)).LifecycleState.Should().Be(RaidLifecycleState.Active);
    }
}
