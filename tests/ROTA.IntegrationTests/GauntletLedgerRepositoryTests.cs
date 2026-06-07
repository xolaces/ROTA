using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;
using ROTA.Infrastructure.Persistence.Repositories;
using Testcontainers.PostgreSql;

namespace ROTA.IntegrationTests;

// BETA (System 16 Slice 2) — Postgres-backed tests for the two append-only ledgers:
//   • balance = SUM(amount); per-currency SUM for the currency ledger
//   • idempotent referenceId via the unique partial index (AlreadyCharged ≠ Insufficient)
//   • tri-state SpendAsync atomicity
// plus the GauntletEntry upsert (double-join idempotency, league not re-evaluated).
public class GauntletLedgerRepositoryTests : IAsyncLifetime
{
    private PostgreSqlContainer _postgres = null!;

    public async Task InitializeAsync()
    {
        _postgres = new PostgreSqlBuilder()
            .WithDatabase("rota_gauntlet_test")
            .WithUsername("test")
            .WithPassword("test")
            .Build();
        await _postgres.StartAsync();

        await using var db = NewDbContext();
        await db.Database.MigrateAsync(); // includes AddGauntletSystem
    }

    public async Task DisposeAsync() => await _postgres.DisposeAsync();

    private RotaDbContext NewDbContext()
    {
        var options = new DbContextOptionsBuilder<RotaDbContext>()
            .UseNpgsql(_postgres.GetConnectionString())
            .Options;
        return new RotaDbContext(options);
    }

    private async Task<Guid> SeedPlayerAsync()
    {
        var suffix = Guid.NewGuid().ToString("N")[..8];
        var player = Player.Create($"g_{suffix}", $"{suffix}@test.dev", "hash");
        await using var db = NewDbContext();
        db.Players.Add(player);
        await db.SaveChangesAsync();
        return player.Id;
    }

    // ── Strike ledger: balance = SUM ────────────────────────────────────────────

    [Fact]
    public async Task StrikeBalance_IsSumOfAllRows()
    {
        var playerId = await SeedPlayerAsync();
        await using var db = NewDbContext();
        var repo = new StrikeRepository(db);

        await repo.CreateAsync(StrikeTransaction.Create(playerId, 10, StrikeTransactionType.RaidDefeat, "d:1"));
        await repo.CreateAsync(StrikeTransaction.Create(playerId, 25, StrikeTransactionType.GemPurchase, "b:1"));
        await repo.CreateAsync(StrikeTransaction.Create(playerId, -5, StrikeTransactionType.HitSpend, "h:1"));

        (await repo.GetBalanceAsync(playerId)).Should().Be(30L, "10 + 25 − 5");
    }

    [Fact]
    public async Task StrikeLedger_IdempotentReference_DuplicateInsertRejectedByIndex()
    {
        var playerId = await SeedPlayerAsync();
        await using var db = NewDbContext();
        var repo = new StrikeRepository(db);

        await repo.CreateAsync(StrikeTransaction.Create(playerId, 10, StrikeTransactionType.GemPurchase, "buy:dup"));

        // Second insert with the SAME (player, type, reference) must violate the unique partial index.
        await using var db2 = NewDbContext();
        var repo2 = new StrikeRepository(db2);
        var act = async () => await repo2.CreateAsync(
            StrikeTransaction.Create(playerId, 10, StrikeTransactionType.GemPurchase, "buy:dup"));

        await act.Should().ThrowAsync<DbUpdateException>();
        (await repo.GetBalanceAsync(playerId)).Should().Be(10L, "the duplicate must not have committed");
    }

    [Fact]
    public async Task StrikeLedger_NullReference_IsNotConstrained()
    {
        var playerId = await SeedPlayerAsync();
        await using var db = NewDbContext();
        var repo = new StrikeRepository(db);

        // Two null-reference rows of the same type are allowed (partial index filters reference IS NOT NULL).
        await repo.CreateAsync(StrikeTransaction.Create(playerId, 3, StrikeTransactionType.SpecialRaidDrop, null));
        await repo.CreateAsync(StrikeTransaction.Create(playerId, 4, StrikeTransactionType.SpecialRaidDrop, null));

        (await repo.GetBalanceAsync(playerId)).Should().Be(7L);
    }

    // ── Strike SpendAsync: tri-state ────────────────────────────────────────────

    [Fact]
    public async Task StrikeSpend_Charged_WhenSufficient_ThenAlreadyCharged_OnReplay()
    {
        var playerId = await SeedPlayerAsync();
        await using var db = NewDbContext();
        var repo = new StrikeRepository(db);
        await repo.CreateAsync(StrikeTransaction.Create(playerId, 20, StrikeTransactionType.RaidDefeat, "earn:1"));

        var first = await repo.SpendAsync(playerId, 5, "spend:hit:1");
        first.Should().Be(StrikeSpendOutcome.Charged);
        (await repo.GetBalanceAsync(playerId)).Should().Be(15L);

        // Replay with the same reference → AlreadyCharged, no second debit.
        var replay = await repo.SpendAsync(playerId, 5, "spend:hit:1");
        replay.Should().Be(StrikeSpendOutcome.AlreadyCharged);
        (await repo.GetBalanceAsync(playerId)).Should().Be(15L, "the replay must not debit again");
    }

    [Fact]
    public async Task StrikeSpend_Insufficient_WhenBalanceTooLow_NoRowWritten()
    {
        var playerId = await SeedPlayerAsync();
        await using var db = NewDbContext();
        var repo = new StrikeRepository(db);
        await repo.CreateAsync(StrikeTransaction.Create(playerId, 3, StrikeTransactionType.RaidDefeat, "earn:1"));

        var outcome = await repo.SpendAsync(playerId, 20, "spend:hit:big");

        outcome.Should().Be(StrikeSpendOutcome.Insufficient, "Insufficient is distinct from AlreadyCharged");
        (await repo.GetBalanceAsync(playerId)).Should().Be(3L);
    }

    // ── Currency ledger: per-currency SUM + idempotency ─────────────────────────

    [Fact]
    public async Task CurrencyBalance_IsSumPerCurrencyDiscriminator()
    {
        var playerId = await SeedPlayerAsync();
        await using var db = NewDbContext();
        var repo = new GauntletCurrencyRepository(db);

        await repo.CreateAsync(GauntletCurrencyTransaction.Create(
            playerId, GauntletCurrency.Token, 50, GauntletCurrencyTransactionType.RankReward, "settle:t"));
        await repo.CreateAsync(GauntletCurrencyTransaction.Create(
            playerId, GauntletCurrency.Token, 5, GauntletCurrencyTransactionType.RaidDefeatReward, "defeat:t"));
        await repo.CreateAsync(GauntletCurrencyTransaction.Create(
            playerId, GauntletCurrency.Pitchfork, 10, GauntletCurrencyTransactionType.RankReward, "settle:p"));

        (await repo.GetBalanceAsync(playerId, GauntletCurrency.Token)).Should().Be(55L);
        (await repo.GetBalanceAsync(playerId, GauntletCurrency.Pitchfork)).Should().Be(10L,
            "Pitchfork balance is isolated from Token by the currency discriminator");
    }

    [Fact]
    public async Task CurrencyLedger_SameReference_AllowedAcrossDifferentCurrencies()
    {
        // The idempotency index includes currency, so one settlement referenceId can credit BOTH
        // Token and Pitchfork without colliding.
        var playerId = await SeedPlayerAsync();
        await using var db = NewDbContext();
        var repo = new GauntletCurrencyRepository(db);

        await repo.CreateAsync(GauntletCurrencyTransaction.Create(
            playerId, GauntletCurrency.Token, 20, GauntletCurrencyTransactionType.RankReward, "settle:rank1"));
        await repo.CreateAsync(GauntletCurrencyTransaction.Create(
            playerId, GauntletCurrency.Pitchfork, 5, GauntletCurrencyTransactionType.RankReward, "settle:rank1"));

        (await repo.GetBalanceAsync(playerId, GauntletCurrency.Token)).Should().Be(20L);
        (await repo.GetBalanceAsync(playerId, GauntletCurrency.Pitchfork)).Should().Be(5L);
    }

    [Fact]
    public async Task CurrencySpend_TriState_ChargedThenAlreadyCharged()
    {
        var playerId = await SeedPlayerAsync();
        await using var db = NewDbContext();
        var repo = new GauntletCurrencyRepository(db);
        await repo.CreateAsync(GauntletCurrencyTransaction.Create(
            playerId, GauntletCurrency.Token, 100, GauntletCurrencyTransactionType.RankReward, "settle:t"));

        var first = await repo.SpendAsync(playerId, GauntletCurrency.Token, 30, "shop:item:1");
        first.Should().Be(GauntletCurrencySpendOutcome.Charged);
        (await repo.GetBalanceAsync(playerId, GauntletCurrency.Token)).Should().Be(70L);

        var replay = await repo.SpendAsync(playerId, GauntletCurrency.Token, 30, "shop:item:1");
        replay.Should().Be(GauntletCurrencySpendOutcome.AlreadyCharged);
        (await repo.GetBalanceAsync(playerId, GauntletCurrency.Token)).Should().Be(70L);
    }

    [Fact]
    public async Task CurrencySpend_Insufficient_WhenWrongCurrencyBalance()
    {
        // Player has Token but no Pitchfork — spending Pitchfork is Insufficient (currency isolation).
        var playerId = await SeedPlayerAsync();
        await using var db = NewDbContext();
        var repo = new GauntletCurrencyRepository(db);
        await repo.CreateAsync(GauntletCurrencyTransaction.Create(
            playerId, GauntletCurrency.Token, 100, GauntletCurrencyTransactionType.RankReward, "settle:t"));

        var outcome = await repo.SpendAsync(playerId, GauntletCurrency.Pitchfork, 5, "shop:pitchfork:1");

        outcome.Should().Be(GauntletCurrencySpendOutcome.Insufficient);
    }

    // ── GauntletEntry upsert: double-join idempotency ───────────────────────────

    [Fact]
    public async Task EntryUpsert_DoubleJoin_ReturnsSameRow_LeagueNotChanged()
    {
        var playerId = await SeedPlayerAsync();

        // Seed an event.
        Guid eventId;
        await using (var db = NewDbContext())
        {
            var ev = GauntletEvent.Create("Cycle",
                DateTimeOffset.UtcNow, DateTimeOffset.UtcNow.AddDays(7));
            ev.Activate();
            db.GauntletEvents.Add(ev);
            await db.SaveChangesAsync();
            eventId = ev.Id;
        }

        await using var db1 = NewDbContext();
        var repo = new GauntletEntryRepository(db1);

        var first = await repo.UpsertAsync(GauntletEntry.Create(eventId, playerId, GauntletLeague.Whelpling));

        // Second "join" supplies a DIFFERENT league (as if level changed) — must be ignored.
        await using var db2 = NewDbContext();
        var repo2 = new GauntletEntryRepository(db2);
        var second = await repo2.UpsertAsync(GauntletEntry.Create(eventId, playerId, GauntletLeague.Dragon));

        second.Id.Should().Be(first.Id, "the same (event, player) entry must be returned");
        second.League.Should().Be(GauntletLeague.Whelpling, "league is locked at first join");

        // Exactly one row exists.
        await using var readDb = NewDbContext();
        var count = await readDb.GauntletEntries.CountAsync(e => e.GauntletEventId == eventId && e.PlayerId == playerId);
        count.Should().Be(1);
    }
}
