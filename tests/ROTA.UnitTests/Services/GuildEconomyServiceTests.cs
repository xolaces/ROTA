using FluentAssertions;
using Microsoft.Extensions.Options;
using Moq;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Services;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.UnitTests.Services;

/// <summary>
/// System 21 Slice 3a — GuildEconomyService unit tests. An in-memory fake economy repository mirrors the
/// real ledger semantics (SUM balances, reference-prefix day counts, unique-reference idempotency, atomic
/// multi-row writes) so the service's daily-claim idempotency, buy/donate caps, ticket spend, and
/// personal→pool donation are exercised without a database. The DB-level unique partial indexes are the
/// integration concern; here we prove the service orchestration + the repo contract it relies on.
/// </summary>
public class GuildEconomyServiceTests
{
    private static readonly Guid GuildId = Guid.NewGuid();

    // ── In-memory fake of the economy repository (faithful to the real idempotency/atomicity) ──
    private sealed class FakeEconomyRepo : IGuildEconomyRepository
    {
        public readonly List<GuildCurrencyTransaction> Player = new();
        public readonly List<GuildSigilPoolTransaction> Pool = new();

        public Task<long> GetPlayerBalanceAsync(Guid playerId, GuildCurrency currency, CancellationToken ct = default)
            => Task.FromResult(Player.Where(t => t.PlayerId == playerId && t.Currency == currency).Sum(t => (long)t.Amount));

        public Task<long> GetPoolBalanceAsync(Guid guildId, CancellationToken ct = default)
            => Task.FromResult(Pool.Where(t => t.GuildId == guildId).Sum(t => (long)t.Amount));

        public Task<int> CountByReferencePrefixAsync(
            Guid playerId, GuildCurrency currency, GuildCurrencyTransactionType type,
            string referencePrefix, CancellationToken ct = default)
            => Task.FromResult(Player.Count(t => t.PlayerId == playerId && t.Currency == currency
                && t.TransactionType == type && t.ReferenceId != null && t.ReferenceId.StartsWith(referencePrefix)));

        public Task<bool> ReferenceExistsAsync(
            Guid playerId, GuildCurrency currency, GuildCurrencyTransactionType type,
            string referenceId, CancellationToken ct = default)
            => Task.FromResult(Player.Any(t => t.PlayerId == playerId && t.Currency == currency
                && t.TransactionType == type && t.ReferenceId == referenceId));

        private bool PlayerRefExists(GuildCurrencyTransaction t)
            => t.ReferenceId != null && Player.Any(x => x.PlayerId == t.PlayerId && x.Currency == t.Currency
                && x.TransactionType == t.TransactionType && x.ReferenceId == t.ReferenceId);

        private bool PoolRefExists(GuildSigilPoolTransaction t)
            => t.ReferenceId != null && Pool.Any(x => x.GuildId == t.GuildId
                && x.TransactionType == t.TransactionType && x.ReferenceId == t.ReferenceId);

        public Task<bool> AddPlayerTransactionsAsync(IReadOnlyList<GuildCurrencyTransaction> transactions, CancellationToken ct = default)
        {
            if (transactions.Count == 0) return Task.FromResult(true);
            if (transactions.Any(PlayerRefExists)) return Task.FromResult(false); // simulate unique violation
            Player.AddRange(transactions);
            return Task.FromResult(true);
        }

        public Task<bool> AddDonationAsync(GuildCurrencyTransaction playerDebit, GuildSigilPoolTransaction poolCredit, CancellationToken ct = default)
        {
            if (PlayerRefExists(playerDebit) || PoolRefExists(poolCredit)) return Task.FromResult(false);
            Player.Add(playerDebit);
            Pool.Add(poolCredit);
            return Task.FromResult(true);
        }
    }

    private sealed class FakeMembershipRepo : IGuildMembershipRepository
    {
        public readonly List<GuildMembership> Rows = new();
        public Task<GuildMembership?> FindByPlayerAsync(Guid playerId, CancellationToken ct = default)
            => Task.FromResult(Rows.FirstOrDefault(m => m.PlayerId == playerId && !m.IsDeleted));
        public Task<GuildMembership?> FindByGuildAndPlayerAsync(Guid guildId, Guid playerId, CancellationToken ct = default)
            => Task.FromResult(Rows.FirstOrDefault(m => m.GuildId == guildId && m.PlayerId == playerId && !m.IsDeleted));
        public Task<IReadOnlyList<GuildMembership>> GetForGuildAsync(Guid guildId, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<GuildMembership>>(Rows.Where(m => m.GuildId == guildId && !m.IsDeleted).ToList());
        public Task<int> CountActiveAsync(Guid guildId, CancellationToken ct = default)
            => Task.FromResult(Rows.Count(m => m.GuildId == guildId && !m.IsDeleted));
        public Task<GuildMembership> CreateAsync(GuildMembership membership, CancellationToken ct = default) { Rows.Add(membership); return Task.FromResult(membership); }
        public Task UpdateAsync(GuildMembership membership, CancellationToken ct = default) => Task.CompletedTask;
    }

    private sealed class Bundle
    {
        public readonly FakeEconomyRepo Economy = new();
        public readonly FakeMembershipRepo Memberships = new();
        public readonly Mock<IAuditLogRepository> Audit = new();
        public readonly GuildConfig Config = new();
        public readonly Guid PlayerId = Guid.NewGuid();

        public Bundle(bool inGuild = true)
        {
            Audit.Setup(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>())).Returns(Task.CompletedTask);
            if (inGuild)
                Memberships.Rows.Add(GuildMembership.Create(GuildId, PlayerId, GuildRank.Member));
        }

        public GuildEconomyService Build()
            => new(Economy, Memberships, Audit.Object, Options.Create(Config));
    }

    // ── Daily claim ───────────────────────────────────────────────────────────

    [Fact]
    public async Task ClaimDaily_FirstCall_GrantsSigilAndTickets()
    {
        var b = new Bundle();
        var r = await b.Build().ClaimDailyAsync(b.PlayerId);

        r.Success.Should().BeTrue();
        r.AlreadyClaimed.Should().BeFalse();
        r.SigilsGranted.Should().Be(b.Config.DailySigilClaimAmount);   // 1
        r.TicketsGranted.Should().Be(b.Config.DailyTicketGrantAmount); // 3
        r.SigilBalance.Should().Be(1);
        r.TicketBalance.Should().Be(3);
    }

    [Fact]
    public async Task ClaimDaily_SecondCallSameDay_IsIdempotent_NoNewGrant()
    {
        var b = new Bundle();
        var svc = b.Build();
        await svc.ClaimDailyAsync(b.PlayerId);
        var r = await svc.ClaimDailyAsync(b.PlayerId);

        r.Success.Should().BeTrue();
        r.AlreadyClaimed.Should().BeTrue();
        r.SigilsGranted.Should().Be(0);
        r.TicketsGranted.Should().Be(0);
        // Balances unchanged after the second claim.
        r.SigilBalance.Should().Be(1);
        r.TicketBalance.Should().Be(3);
        // Exactly two ledger rows total (one sigil claim + one ticket grant), never four.
        b.Economy.Player.Should().HaveCount(2);
    }

    [Fact]
    public async Task ClaimDaily_NotInGuild_Fails()
    {
        var b = new Bundle(inGuild: false);
        var r = await b.Build().ClaimDailyAsync(b.PlayerId);

        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildEconomyFailureCode.NotInGuild);
        b.Economy.Player.Should().BeEmpty();
    }

    // ── Buy ─────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task BuySigil_SpendsTickets_GrantsSigil()
    {
        var b = new Bundle();
        var svc = b.Build();
        await svc.ClaimDailyAsync(b.PlayerId); // 1 sigil, 3 tickets

        var r = await svc.BuySigilAsync(b.PlayerId);

        r.Success.Should().BeTrue();
        r.BoughtToday.Should().Be(1);
        r.TicketBalance.Should().Be(3 - b.Config.SigilShopTicketPrice); // 2
        r.SigilBalance.Should().Be(2); // 1 claimed + 1 bought
    }

    [Fact]
    public async Task BuySigil_RespectsDailyCap()
    {
        var b = new Bundle();
        // Plenty of tickets so the cap (not the balance) is the binding constraint.
        b.Config.DailyTicketGrantAmount = 100;
        var svc = b.Build();
        await svc.ClaimDailyAsync(b.PlayerId);

        for (var i = 0; i < b.Config.DailyBuyCap; i++)
            (await svc.BuySigilAsync(b.PlayerId)).Success.Should().BeTrue();

        var over = await svc.BuySigilAsync(b.PlayerId);
        over.Success.Should().BeFalse();
        over.FailureCode.Should().Be(GuildEconomyFailureCode.DailyCapReached);
    }

    [Fact]
    public async Task BuySigil_InsufficientTickets_Fails()
    {
        var b = new Bundle(); // no claim → 0 tickets
        var r = await b.Build().BuySigilAsync(b.PlayerId);

        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildEconomyFailureCode.InsufficientTickets);
    }

    // ── Donate ──────────────────────────────────────────────────────────────────

    [Fact]
    public async Task DonateSigil_MovesPersonalToPool()
    {
        var b = new Bundle();
        var svc = b.Build();
        await svc.ClaimDailyAsync(b.PlayerId); // 1 sigil

        var r = await svc.DonateSigilAsync(b.PlayerId);

        r.Success.Should().BeTrue();
        r.DonatedToday.Should().Be(1);
        r.SigilBalance.Should().Be(0);                       // personal sigil spent
        r.PoolBalance.Should().Be(1);                        // moved into the guild pool
        (await b.Economy.GetPoolBalanceAsync(GuildId)).Should().Be(1);
    }

    [Fact]
    public async Task DonateSigil_RespectsDailyCap()
    {
        var b = new Bundle();
        b.Config.DailyTicketGrantAmount = 100; // fund buys so we have > cap sigils to donate
        var svc = b.Build();
        await svc.ClaimDailyAsync(b.PlayerId);
        for (var i = 0; i < b.Config.DailyBuyCap; i++) await svc.BuySigilAsync(b.PlayerId); // +3 sigils (4 total)

        for (var i = 0; i < b.Config.DailyDonateCap; i++)
            (await svc.DonateSigilAsync(b.PlayerId)).Success.Should().BeTrue();

        var over = await svc.DonateSigilAsync(b.PlayerId);
        over.Success.Should().BeFalse();
        over.FailureCode.Should().Be(GuildEconomyFailureCode.DailyCapReached);
        // Pool holds exactly the capped number of donations.
        (await b.Economy.GetPoolBalanceAsync(GuildId)).Should().Be(b.Config.DailyDonateCap);
    }

    [Fact]
    public async Task DonateSigil_InsufficientSigils_Fails()
    {
        var b = new Bundle(); // no sigils
        var r = await b.Build().DonateSigilAsync(b.PlayerId);

        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildEconomyFailureCode.InsufficientSigils);
        (await b.Economy.GetPoolBalanceAsync(GuildId)).Should().Be(0);
    }

    [Fact]
    public async Task DonateSigil_NotInGuild_Fails()
    {
        var b = new Bundle(inGuild: false);
        var r = await b.Build().DonateSigilAsync(b.PlayerId);

        r.Success.Should().BeFalse();
        r.FailureCode.Should().Be(GuildEconomyFailureCode.NotInGuild);
    }

    // ── Balances + audit ─────────────────────────────────────────────────────────

    [Fact]
    public async Task GetBalances_ReflectsClaimBuyDonateState()
    {
        var b = new Bundle();
        var svc = b.Build();
        await svc.ClaimDailyAsync(b.PlayerId);  // +1 sigil, +3 tickets
        await svc.BuySigilAsync(b.PlayerId);    // -1 ticket, +1 sigil → 2 sigil / 2 ticket
        await svc.DonateSigilAsync(b.PlayerId); // -1 sigil, pool +1 → 1 sigil / pool 1

        var bal = await svc.GetBalancesAsync(b.PlayerId);

        bal.InGuild.Should().BeTrue();
        bal.SigilBalance.Should().Be(1);
        bal.TicketBalance.Should().Be(2);
        bal.PoolBalance.Should().Be(1);
        bal.ClaimedToday.Should().BeTrue();
        bal.BoughtToday.Should().Be(1);
        bal.DonatedToday.Should().Be(1);
        bal.DailyBuyCap.Should().Be(b.Config.DailyBuyCap);
        bal.DailyDonateCap.Should().Be(b.Config.DailyDonateCap);
    }

    [Fact]
    public async Task StateChanges_WriteAudit()
    {
        var b = new Bundle();
        var svc = b.Build();
        await svc.ClaimDailyAsync(b.PlayerId);
        await svc.BuySigilAsync(b.PlayerId);
        await svc.DonateSigilAsync(b.PlayerId);

        // Claim + buy + donate each append exactly one audit row.
        b.Audit.Verify(a => a.AppendAsync(It.IsAny<AuditLog>(), It.IsAny<CancellationToken>()), Times.Exactly(3));
    }
}
