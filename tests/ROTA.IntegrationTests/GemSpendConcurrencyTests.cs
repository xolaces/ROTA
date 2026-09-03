using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using ROTA.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ROTA.IntegrationTests;

// A gem balance cannot be spent twice, however many spends are in flight at once.
//
// THE DEFECT THESE PIN. The original flow read the SUM balance, checked it, then inserted a negative
// row. Two concurrent spends both read the same balance, both passed the check, and both inserted --
// so a player with one purchase worth of gems got two, and the balance went negative. That is the
// worst shape of economy bug: it mints value, it is invisible in a single-threaded test, and the
// player controls the timing by firing parallel requests.
//
// TrySpendAsync now takes a per-player advisory lock and re-checks the balance inside the INSERT
// itself, so spends for one player serialize and every guard sees committed truth. That is a sound
// argument; these are the test. Each spend gets its OWN DbContext, and therefore its own connection
// -- otherwise they would serialize on a single connection and the race could never occur.
public class GemSpendConcurrencyTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder(TestContainerImages.Postgres)
            .WithDatabase("rota_gemspend_test")
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

    /// <summary>A player funded with exactly <paramref name="gems"/> gems.</summary>
    private async Task<Guid> SeedFundedPlayerAsync(long gems)
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var player = Player.Create($"gem_{suffix}", $"{suffix}@test.dev", "hash");

        await using var db = NewDbContext();
        db.Players.Add(player);
        await db.SaveChangesAsync();

        if (gems > 0)
        {
            db.GemTransactions.Add(GemTransaction.Create(
                player.Id, gems, GemTransactionType.AdminGrant, $"seed:{suffix}"));
            await db.SaveChangesAsync();
        }
        return player.Id;
    }

    private async Task<long> BalanceAsync(Guid playerId)
    {
        await using var db = NewDbContext();
        return await db.GemTransactions.AsNoTracking()
            .Where(t => t.PlayerId == playerId)
            .SumAsync(t => t.Amount);
    }

    /// <summary>Fires <paramref name="count"/> spends at once, each on its own connection.</summary>
    private async Task<GemSpendOutcome[]> SpendConcurrentlyAsync(
        Guid playerId, int count, long amount, Func<int, string?> reference)
    {
        // A barrier is not enough on its own, but starting every task before awaiting any of them,
        // each with its own connection, is what actually puts them in flight together.
        var tasks = Enumerable.Range(0, count).Select(async i =>
        {
            await using var db = NewDbContext();
            return await new GemTransactionRepository(db).TrySpendAsync(
                playerId, amount, GemTransactionType.EnergyRefill, reference(i));
        }).ToArray();

        return await Task.WhenAll(tasks);
    }

    [Fact]
    public async Task ConcurrentSpends_NeverOverspendTheBalance()
    {
        // 10 gems, twenty simultaneous 1-gem spends: exactly ten may be charged.
        const long funded = 10;
        const int attempts = 20;

        var playerId = await SeedFundedPlayerAsync(funded);

        var outcomes = await SpendConcurrentlyAsync(
            playerId, attempts, amount: 1, reference: i => $"spend:{i}");

        outcomes.Count(o => o == GemSpendOutcome.Charged).Should().Be((int)funded,
            "the balance funds exactly ten one-gem spends, no matter how many are in flight");
        outcomes.Count(o => o == GemSpendOutcome.InsufficientBalance).Should().Be(attempts - (int)funded);

        (await BalanceAsync(playerId)).Should().Be(0,
            "a balance that goes negative means value was minted out of a race");
    }

    [Fact]
    public async Task ConcurrentSpends_LargerThanBalance_ChargeAtMostOnce()
    {
        // The nastiest shape: several spends that are individually affordable but collectively are
        // not, so a stale read lets more than one through and the balance lands far below zero.
        var playerId = await SeedFundedPlayerAsync(100);

        var outcomes = await SpendConcurrentlyAsync(
            playerId, count: 8, amount: 60, reference: i => $"big:{i}");

        outcomes.Count(o => o == GemSpendOutcome.Charged).Should().Be(1,
            "100 gems funds one 60-gem spend and cannot fund two");

        (await BalanceAsync(playerId)).Should().Be(40);
    }

    [Fact]
    public async Task ConcurrentSpends_OnOneReferenceId_ChargeExactlyOnce()
    {
        // Idempotency under contention. A client retrying a purchase -- or an attacker replaying one
        // deliberately -- fires the same referenceId many times at once. Exactly one row may be
        // written; every other caller must be told the original charge already committed, NOT that it
        // failed, because AlreadyProcessed is a SUCCESS that callers follow with the grant step.
        var playerId = await SeedFundedPlayerAsync(1000);

        var outcomes = await SpendConcurrentlyAsync(
            playerId, count: 12, amount: 50, reference: _ => "purchase:same-reference");

        outcomes.Count(o => o == GemSpendOutcome.Charged).Should().Be(1);
        outcomes.Count(o => o == GemSpendOutcome.AlreadyProcessed).Should().Be(11);
        outcomes.Should().NotContain(GemSpendOutcome.InsufficientBalance,
            "a replay of an affordable purchase must never read as unaffordable");

        (await BalanceAsync(playerId)).Should().Be(950,
            "twelve replays of one 50-gem purchase cost fifty gems, once");
    }

    [Fact]
    public async Task ASpend_WithNoReferenceId_IsNotDeduplicated()
    {
        // The counterpart to the test above, so the idempotency guard is not mistaken for a blanket
        // one-spend-per-player rule. Unreferenced spends are ordinary repeatable purchases and each
        // must charge, up to the balance.
        var playerId = await SeedFundedPlayerAsync(5);

        var outcomes = await SpendConcurrentlyAsync(
            playerId, count: 5, amount: 1, reference: _ => null);

        outcomes.Should().AllSatisfy(o => o.Should().Be(GemSpendOutcome.Charged));
        (await BalanceAsync(playerId)).Should().Be(0);
    }

    [Fact]
    public async Task AnExactBalanceSpend_Succeeds_AndTheNextOneDoesNot()
    {
        // Boundary: the guard is >= cost, so spending the whole balance must succeed and leave zero.
        // An off-by-one to > would silently strand every player's last purchase.
        var playerId = await SeedFundedPlayerAsync(250);

        await using (var db = NewDbContext())
        {
            var repo = new GemTransactionRepository(db);
            (await repo.TrySpendAsync(playerId, 250, GemTransactionType.EnergyRefill, "exact"))
                .Should().Be(GemSpendOutcome.Charged, "a player may spend their entire balance");
        }

        (await BalanceAsync(playerId)).Should().Be(0);

        await using (var db = NewDbContext())
        {
            (await new GemTransactionRepository(db)
                .TrySpendAsync(playerId, 1, GemTransactionType.EnergyRefill, "one-more"))
                .Should().Be(GemSpendOutcome.InsufficientBalance);
        }
    }
}
