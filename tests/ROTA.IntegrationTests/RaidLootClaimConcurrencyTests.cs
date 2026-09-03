using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using ROTA.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ROTA.IntegrationTests;

// A raid's deferred rewards are granted exactly once, however many Loot presses land at once.
//
// WHAT THIS SEAM IS. The kill path COMPUTES rewards and stashes them on the participant row
// (GemsEarned, StatPointsEarned, ItemsEarnedJson, PendingDropsJson); LootRaidAsync GRANTS them later.
// That gap is the exposure: the rewards sit in the database as a claimable number, and the player
// chooses when -- and how many times at once -- to press the button that converts them into balance.
//
// These tests reproduce LootRaidAsync's actual COMPOSITION rather than testing its pieces apart:
// an advisory lock keyed on the participant row, a conditional-UPDATE latch inside it, and the grant
// riding the same transaction. Any of the three alone is insufficient, and it is the assembly that
// has to hold. Each caller gets its OWN DbContext, and therefore its own connection -- sharing one
// would serialize them and the race could never occur.
//
// Note the lock domain: this seam serializes on the PARTICIPANT row, while quests serialize on the
// PLAYER. That mismatch is exactly what silently lost skill-point grants (251fec1), so the two seams
// cannot borrow each other's proof and need separate tests.
public class RaidLootClaimConcurrencyTests : IAsyncLifetime
{
    private const long GemsEarned = 100;

    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_lootclaim_test")
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

    private sealed record Seeded(Guid PlayerId, Guid RaidId, Guid ParticipantId);

    /// <summary>A killed raid with one participant holding unclaimed rewards.</summary>
    private async Task<Seeded> SeedClaimableRaidAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var player = Player.Create($"loot_{suffix}", $"{suffix}@test.dev", "hash");
        var raid   = ActiveRaid.Create("raid_test", player.Id, 1000, DateTimeOffset.UtcNow.AddHours(1));
        var part   = RaidParticipant.Create(raid.Id, player.Id);

        await using var db = NewDbContext();
        db.Players.Add(player);
        db.ActiveRaids.Add(raid);
        db.RaidParticipants.Add(part);
        await db.SaveChangesAsync();

        return new Seeded(player.Id, raid.Id, part.Id);
    }

    private async Task<long> GemBalanceAsync(Guid playerId)
    {
        await using var db = NewDbContext();
        return await db.GemTransactions.AsNoTracking()
            .Where(t => t.PlayerId == playerId)
            .SumAsync(t => t.Amount);
    }

    private async Task<DateTimeOffset?> RewardedAtAsync(Guid participantId)
    {
        await using var db = NewDbContext();
        return await db.RaidParticipants.AsNoTracking()
            .Where(p => p.Id == participantId)
            .Select(p => p.RewardedAt)
            .FirstAsync();
    }

    /// <summary>
    /// One Loot press, assembled exactly as LootRaidAsync assembles it: advisory lock on the
    /// participant, conditional latch inside it, grant on the same transaction.
    /// </summary>
    private async Task<bool> ClaimOnceAsync(Seeded s)
    {
        await using var db = NewDbContext();
        var raids        = new ActiveRaidRepository(db);
        var participants = new RaidParticipantRepository(db);
        var gems         = new GemTransactionRepository(db);

        return await raids.AtomicWithAdvisoryLockAsync(s.ParticipantId, async () =>
        {
            if (!await participants.TryClaimRewardsAsync(s.ParticipantId, DateTimeOffset.UtcNow))
                return false;

            await gems.CreateAsync(GemTransaction.Create(
                s.PlayerId, GemsEarned, GemTransactionType.RaidReward, $"raid:{s.RaidId}:{s.PlayerId}"));
            return true;
        });
    }

    [Fact]
    public async Task ConcurrentLootPresses_GrantTheRewardExactlyOnce()
    {
        // The attack: hold the Loot button, or replay the request, so a dozen claims are in flight
        // before any of them has committed.
        var s = await SeedClaimableRaidAsync();

        var results = await Task.WhenAll(
            Enumerable.Range(0, 12).Select(_ => ClaimOnceAsync(s)));

        results.Count(won => won).Should().Be(1,
            "the latch is a conditional UPDATE, so exactly one presser may convert the reward");

        (await GemBalanceAsync(s.PlayerId)).Should().Be(GemsEarned,
            "twelve simultaneous presses of one 100-gem reward pay 100 gems, once");

        (await RewardedAtAsync(s.ParticipantId)).Should().NotBeNull(
            "the winning claim must leave the durable latch stamped");
    }

    [Fact]
    public async Task ASecondClaim_AfterTheFirstCommitted_GrantsNothing()
    {
        // The sequential case, which is the one a player actually reaches: press Loot, get the
        // reward, press it again a minute later.
        var s = await SeedClaimableRaidAsync();

        (await ClaimOnceAsync(s)).Should().BeTrue();
        (await ClaimOnceAsync(s)).Should().BeFalse("the latch is already stamped");

        (await GemBalanceAsync(s.PlayerId)).Should().Be(GemsEarned);
    }

    [Fact]
    public async Task ALoserOfTheLatch_RollsBackItsOwnGrant()
    {
        // The grant must ride the SAME transaction as the latch. If it did not, a loser could stamp
        // nothing and still write gems -- which is the double-grant the latch exists to prevent, just
        // moved one layer down. Proven by having the loser attempt a grant it is not entitled to:
        // the latch returns false, AtomicWithAdvisoryLockAsync rolls back, and the row must not exist.
        var s = await SeedClaimableRaidAsync();
        (await ClaimOnceAsync(s)).Should().BeTrue();

        await using (var db = NewDbContext())
        {
            var raids        = new ActiveRaidRepository(db);
            var participants = new RaidParticipantRepository(db);
            var gems         = new GemTransactionRepository(db);

            var won = await raids.AtomicWithAdvisoryLockAsync(s.ParticipantId, async () =>
            {
                bool latched = await participants.TryClaimRewardsAsync(s.ParticipantId, DateTimeOffset.UtcNow);

                // Write the grant BEFORE honouring the latch result, then report the truth. Only a
                // shared transaction can undo this.
                await gems.CreateAsync(GemTransaction.Create(
                    s.PlayerId, 999, GemTransactionType.RaidReward, $"rogue:{Guid.NewGuid():N}"));

                return latched;
            });

            won.Should().BeFalse("the latch was already stamped");
        }

        (await GemBalanceAsync(s.PlayerId)).Should().Be(GemsEarned,
            "a rolled-back claim must take its grant with it -- 999 gems reaching the ledger would "
            + "mean the grant does not share the latch's transaction");
    }

    [Fact]
    public async Task ClaimsOnDifferentParticipants_DoNotBlockEachOther()
    {
        // The lock is keyed per participant, so two players looting different raids must both be paid.
        // A lock keyed too coarsely would still be "safe" while quietly serializing the whole game.
        var a = await SeedClaimableRaidAsync();
        var b = await SeedClaimableRaidAsync();

        var results = await Task.WhenAll(ClaimOnceAsync(a), ClaimOnceAsync(b));

        results.Should().AllSatisfy(won => won.Should().BeTrue());
        (await GemBalanceAsync(a.PlayerId)).Should().Be(GemsEarned);
        (await GemBalanceAsync(b.PlayerId)).Should().Be(GemsEarned);
    }
}
