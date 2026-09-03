using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using ROTA.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ROTA.IntegrationTests;

// A quest attempt spends energy and grants its reward atomically, and concurrent attempts cannot
// spend energy the player does not have.
//
// WHY THIS SEAM NEEDED ITS OWN TESTS. The lock DOMAIN differs from the raid seam: quests serialize on
// the PLAYER (PlayerMutationLock), raid loot serializes on the PARTICIPANT row. That mismatch is
// precisely what silently lost skill-point grants (251fec1), so neither seam's proof carries to the
// other and the gem/loot tests say nothing about this one.
//
// These reproduce AttemptQuestAsync's composition: the whole attempt runs inside
// PlayerMutationLock.RunAsync, which opens a transaction, takes a per-player advisory lock, and
// commits at the end. Everything the attempt touches -- the energy spend's SELECT ... FOR UPDATE, the
// gem grant -- enlists in that ambient transaction rather than owning its own.
public class QuestRewardConcurrencyTests : IAsyncLifetime
{
    private const int  StartingEnergy = 10;
    private const int  EnergyPerAttempt = 1;
    private const long GemsPerAttempt = 5;

    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_questreward_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        await _postgres.StartAsync();

        await using var db = NewDbContext();
        await db.Database.MigrateAsync();
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private RotaDbContext NewDbContext()
        => new(new DbContextOptionsBuilder<RotaDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options);

    /// <summary>
    /// A player whose Energy pool holds exactly <paramref name="energy"/>. Player.Create already
    /// materialises the resource rows, so this UPDATES the existing Energy row -- adding a second one
    /// violates the unique (player_id, resource_type) index.
    /// </summary>
    private async Task<Guid> SeedPlayerWithEnergyAsync(int energy)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var player = Player.Create($"quest_{suffix}", $"{suffix}@test.dev", "hash");

        await using var db = NewDbContext();
        db.Players.Add(player);
        await db.SaveChangesAsync();

        var now = DateTimeOffset.UtcNow;
        var resource = await db.PlayerResources
            .FirstAsync(r => r.PlayerId == player.Id && r.ResourceType == ResourceType.Energy);
        resource.SetMaxValue(Math.Max(energy, 1), now);
        resource.SaveCheckpoint(energy, now);
        await db.SaveChangesAsync();

        return player.Id;
    }

    private async Task<int> EnergyAsync(Guid playerId)
    {
        await using var db = NewDbContext();
        return await db.PlayerResources.AsNoTracking()
            .Where(r => r.PlayerId == playerId && r.ResourceType == ResourceType.Energy)
            .Select(r => r.CurrentValue)
            .FirstAsync();
    }

    private async Task<long> GemsAsync(Guid playerId)
    {
        await using var db = NewDbContext();
        return await db.GemTransactions.AsNoTracking()
            .Where(t => t.PlayerId == playerId).SumAsync(t => t.Amount);
    }

    /// <summary>
    /// One quest attempt, assembled as AttemptQuestAsync assembles it: everything inside
    /// PlayerMutationLock.RunAsync. <paramref name="failAfterSpend"/> simulates a reward step throwing.
    /// </summary>
    private async Task<bool> AttemptAsync(Guid playerId, string reference, bool failAfterSpend = false)
    {
        await using var db = NewDbContext();
        var mutationLock = new PlayerMutationLock(db);
        var resources    = new PlayerResourceRepository(db);
        var gems         = new GemTransactionRepository(db);

        return await mutationLock.RunAsync(playerId, async () =>
        {
            // The energy spend, as EnergyService performs it: a conditional mutation under
            // SELECT ... FOR UPDATE, refusing when the pool is short.
            var spent = await resources.AtomicUpdateAsync(playerId, ResourceType.Energy, resource =>
            {
                if (resource.CurrentValue < EnergyPerAttempt) return false;
                resource.SaveCheckpoint(resource.CurrentValue - EnergyPerAttempt, DateTimeOffset.UtcNow);
                return true;
            });
            if (!spent) return false;

            await gems.CreateAsync(GemTransaction.Create(
                playerId, GemsPerAttempt, GemTransactionType.QuestReward, reference));

            if (failAfterSpend)
                throw new InvalidOperationException("reward step failed");

            return true;
        });
    }

    [Fact]
    public async Task ConcurrentAttempts_CannotSpendEnergyThePlayerDoesNotHave()
    {
        // Twenty attempts in flight against ten energy. A stale read would let extra attempts through
        // and hand out free quest rewards -- the pool is the only thing rationing play.
        var playerId = await SeedPlayerWithEnergyAsync(StartingEnergy);

        var results = await Task.WhenAll(Enumerable.Range(0, 20)
            .Select(i => AttemptAsync(playerId, $"quest:{i}")));

        results.Count(ok => ok).Should().Be(StartingEnergy,
            "ten energy funds exactly ten one-energy attempts, however many are in flight");

        (await EnergyAsync(playerId)).Should().Be(0,
            "energy below zero means attempts were granted that the pool never paid for");

        (await GemsAsync(playerId)).Should().Be(StartingEnergy * GemsPerAttempt,
            "rewards must match the attempts that actually succeeded, not the ones that were tried");
    }

    [Fact]
    public async Task AFailedRewardStep_RollsBackTheEnergySpend()
    {
        // THIS SETTLES A STALE COMMENT. QuestService's header says "Reward steps after the energy spend
        // are not wrapped in an explicit transaction -- a server crash mid-reward would be unfair but
        // acceptable. PHASE-2: wrap quest reward steps in an explicit transaction."
        //
        // That predates PlayerMutationLock. AttemptQuestAsync now runs the WHOLE core inside
        // RunAsync's transaction, so the reward steps are already covered and PHASE-2 is done. If this
        // test ever fails, the comment has become true again and the seam has genuinely regressed.
        var playerId = await SeedPlayerWithEnergyAsync(StartingEnergy);

        var act = async () => await AttemptAsync(playerId, "quest:doomed", failAfterSpend: true);
        await act.Should().ThrowAsync<InvalidOperationException>();

        (await EnergyAsync(playerId)).Should().Be(StartingEnergy,
            "a quest that failed to deliver its reward must not have charged the player");
        (await GemsAsync(playerId)).Should().Be(0,
            "and must not have paid one either -- both sides roll back together or neither does");
    }

    [Fact]
    public async Task AnAttemptAfterAFailedOne_StillSucceeds()
    {
        // The rollback must release the lock and leave the pool usable, not wedge the player out.
        var playerId = await SeedPlayerWithEnergyAsync(StartingEnergy);

        var act = async () => await AttemptAsync(playerId, "quest:doomed", failAfterSpend: true);
        await act.Should().ThrowAsync<InvalidOperationException>();

        (await AttemptAsync(playerId, "quest:next")).Should().BeTrue();
        (await EnergyAsync(playerId)).Should().Be(StartingEnergy - EnergyPerAttempt);
        (await GemsAsync(playerId)).Should().Be(GemsPerAttempt);
    }

    [Fact]
    public async Task AnEmptyPool_RefusesTheAttempt_AndPaysNothing()
    {
        // Boundary: the pool is the gate, so an empty one must refuse before any reward is written.
        var playerId = await SeedPlayerWithEnergyAsync(0);

        (await AttemptAsync(playerId, "quest:broke")).Should().BeFalse();
        (await EnergyAsync(playerId)).Should().Be(0);
        (await GemsAsync(playerId)).Should().Be(0);
    }

    [Fact]
    public async Task ConcurrentAttemptsByDifferentPlayers_DoNotBlockEachOther()
    {
        // The lock is keyed per player. One keyed too coarsely would still be "safe" while serializing
        // every quest attempt in the game behind a single key.
        var a = await SeedPlayerWithEnergyAsync(StartingEnergy);
        var b = await SeedPlayerWithEnergyAsync(StartingEnergy);

        var results = await Task.WhenAll(
            AttemptAsync(a, "quest:a"), AttemptAsync(b, "quest:b"));

        results.Should().AllSatisfy(ok => ok.Should().BeTrue());
        (await EnergyAsync(a)).Should().Be(StartingEnergy - EnergyPerAttempt);
        (await EnergyAsync(b)).Should().Be(StartingEnergy - EnergyPerAttempt);
    }
}
