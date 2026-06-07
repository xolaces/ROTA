using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

// System 21 Slice 3a — guild sigil economy. Server-authoritative; every grant/spend is idempotent
// (referenceId) and audited. The caller's guild is resolved from their membership, never from input.
// Daily caps reset at UTC midnight: claim refs are guildclaim/guildticket:{playerId}:{date}; buy/donate
// refs are guildbuy/guilddonate:{playerId}:{date}:{slot}, and the per-day slot count is the cap counter.
public sealed class GuildEconomyService : IGuildEconomyService
{
    private readonly IGuildEconomyRepository _economy;
    private readonly IGuildMembershipRepository _memberships;
    private readonly IAuditLogRepository _audit;
    private readonly GuildConfig _config;

    public GuildEconomyService(
        IGuildEconomyRepository economy,
        IGuildMembershipRepository memberships,
        IAuditLogRepository audit,
        IOptions<GuildConfig> config)
    {
        _economy = economy;
        _memberships = memberships;
        _audit = audit;
        _config = config.Value;
    }

    private static string Today() => DateTimeOffset.UtcNow.ToString("yyyy-MM-dd");

    public async Task<GuildClaimResult> ClaimDailyAsync(Guid playerId, CancellationToken ct = default)
    {
        var membership = await _memberships.FindByPlayerAsync(playerId, ct);
        if (membership is null)
            return GuildClaimResult.Fail(GuildEconomyFailureCode.NotInGuild, "You are not a member of a guild.");

        var date = Today();
        var sigilRef = $"guildclaim:{playerId}:{date}";
        var ticketRef = $"guildticket:{playerId}:{date}";

        var sigilDone = await _economy.ReferenceExistsAsync(
            playerId, GuildCurrency.Sigil, GuildCurrencyTransactionType.DailyClaim, sigilRef, ct);
        var ticketDone = await _economy.ReferenceExistsAsync(
            playerId, GuildCurrency.ShopTicket, GuildCurrencyTransactionType.TicketGrant, ticketRef, ct);

        var rows = new List<GuildCurrencyTransaction>();
        var sigilsGranted = 0;
        var ticketsGranted = 0;
        if (!sigilDone && _config.DailySigilClaimAmount > 0)
        {
            rows.Add(GuildCurrencyTransaction.Create(
                playerId, GuildCurrency.Sigil, _config.DailySigilClaimAmount,
                GuildCurrencyTransactionType.DailyClaim, sigilRef));
            sigilsGranted = _config.DailySigilClaimAmount;
        }
        if (!ticketDone && _config.DailyTicketGrantAmount > 0)
        {
            rows.Add(GuildCurrencyTransaction.Create(
                playerId, GuildCurrency.ShopTicket, _config.DailyTicketGrantAmount,
                GuildCurrencyTransactionType.TicketGrant, ticketRef));
            ticketsGranted = _config.DailyTicketGrantAmount;
        }

        var alreadyClaimed = rows.Count == 0;
        if (!alreadyClaimed)
        {
            var persisted = await _economy.AddPlayerTransactionsAsync(rows, ct);
            if (!persisted)
            {
                // A concurrent claim won the race — nothing new from us.
                alreadyClaimed = true;
                sigilsGranted = 0;
                ticketsGranted = 0;
            }
            else
            {
                await Audit(playerId, "GuildSigilDailyClaim",
                    $"guild={membership.GuildId} sigils={sigilsGranted} tickets={ticketsGranted} date={date}", ct);
            }
        }

        var sigilBalance = await _economy.GetPlayerBalanceAsync(playerId, GuildCurrency.Sigil, ct);
        var ticketBalance = await _economy.GetPlayerBalanceAsync(playerId, GuildCurrency.ShopTicket, ct);
        return GuildClaimResult.Ok(alreadyClaimed, sigilsGranted, ticketsGranted, sigilBalance, ticketBalance);
    }

    public async Task<GuildBuyResult> BuySigilAsync(Guid playerId, CancellationToken ct = default)
    {
        var membership = await _memberships.FindByPlayerAsync(playerId, ct);
        if (membership is null)
            return GuildBuyResult.Fail(GuildEconomyFailureCode.NotInGuild, "You are not a member of a guild.");

        var date = Today();
        var prefix = $"guildbuy:{playerId}:{date}:";
        var boughtToday = await _economy.CountByReferencePrefixAsync(
            playerId, GuildCurrency.Sigil, GuildCurrencyTransactionType.ShopPurchase, prefix, ct);
        if (boughtToday >= _config.DailyBuyCap)
            return GuildBuyResult.Fail(GuildEconomyFailureCode.DailyCapReached,
                $"Daily sigil-buy cap ({_config.DailyBuyCap}) reached.");

        var price = _config.SigilShopTicketPrice;
        var ticketBalance = await _economy.GetPlayerBalanceAsync(playerId, GuildCurrency.ShopTicket, ct);
        if (ticketBalance < price)
            return GuildBuyResult.Fail(GuildEconomyFailureCode.InsufficientTickets,
                $"Need {price} shop ticket(s); have {ticketBalance}.");

        // The day's purchase index doubles as the cap counter and the idempotency slot.
        var reference = $"{prefix}{boughtToday}";
        var rows = new List<GuildCurrencyTransaction>
        {
            GuildCurrencyTransaction.Create(playerId, GuildCurrency.ShopTicket, -price,
                GuildCurrencyTransactionType.ShopPurchase, reference),
            GuildCurrencyTransaction.Create(playerId, GuildCurrency.Sigil, 1,
                GuildCurrencyTransactionType.ShopPurchase, reference),
        };
        var persisted = await _economy.AddPlayerTransactionsAsync(rows, ct);
        if (!persisted)
            return GuildBuyResult.Fail(GuildEconomyFailureCode.Conflict,
                "Purchase conflicted with a concurrent buy; retry.");

        await Audit(playerId, "GuildSigilBuy", $"guild={membership.GuildId} price={price} ref={reference}", ct);

        var newSigil = await _economy.GetPlayerBalanceAsync(playerId, GuildCurrency.Sigil, ct);
        var newTicket = await _economy.GetPlayerBalanceAsync(playerId, GuildCurrency.ShopTicket, ct);
        return GuildBuyResult.Ok(newSigil, newTicket, boughtToday + 1, _config.DailyBuyCap);
    }

    public async Task<GuildDonateResult> DonateSigilAsync(Guid playerId, CancellationToken ct = default)
    {
        var membership = await _memberships.FindByPlayerAsync(playerId, ct);
        if (membership is null)
            return GuildDonateResult.Fail(GuildEconomyFailureCode.NotInGuild, "You are not a member of a guild.");

        var guildId = membership.GuildId;
        var date = Today();
        var prefix = $"guilddonate:{playerId}:{date}:";
        var donatedToday = await _economy.CountByReferencePrefixAsync(
            playerId, GuildCurrency.Sigil, GuildCurrencyTransactionType.Donation, prefix, ct);
        if (donatedToday >= _config.DailyDonateCap)
            return GuildDonateResult.Fail(GuildEconomyFailureCode.DailyCapReached,
                $"Daily sigil-donate cap ({_config.DailyDonateCap}) reached.");

        var sigilBalance = await _economy.GetPlayerBalanceAsync(playerId, GuildCurrency.Sigil, ct);
        if (sigilBalance < 1)
            return GuildDonateResult.Fail(GuildEconomyFailureCode.InsufficientSigils,
                "You have no sigils to donate.");

        var reference = $"{prefix}{donatedToday}";
        var playerDebit = GuildCurrencyTransaction.Create(playerId, GuildCurrency.Sigil, -1,
            GuildCurrencyTransactionType.Donation, reference);
        var poolCredit = GuildSigilPoolTransaction.Create(guildId, 1,
            GuildSigilPoolTransactionType.Donation, reference);
        var persisted = await _economy.AddDonationAsync(playerDebit, poolCredit, ct);
        if (!persisted)
            return GuildDonateResult.Fail(GuildEconomyFailureCode.Conflict,
                "Donation conflicted with a concurrent donation; retry.");

        await Audit(playerId, "GuildSigilDonate", $"guild={guildId} amount=1 ref={reference}", ct);

        var newSigil = await _economy.GetPlayerBalanceAsync(playerId, GuildCurrency.Sigil, ct);
        var newPool = await _economy.GetPoolBalanceAsync(guildId, ct);
        return GuildDonateResult.Ok(newSigil, newPool, donatedToday + 1, _config.DailyDonateCap);
    }

    public async Task<GuildSigilBalances> GetBalancesAsync(Guid playerId, CancellationToken ct = default)
    {
        var membership = await _memberships.FindByPlayerAsync(playerId, ct);
        var date = Today();

        var sigilBalance = await _economy.GetPlayerBalanceAsync(playerId, GuildCurrency.Sigil, ct);
        var ticketBalance = await _economy.GetPlayerBalanceAsync(playerId, GuildCurrency.ShopTicket, ct);

        var claimedToday = await _economy.ReferenceExistsAsync(
            playerId, GuildCurrency.Sigil, GuildCurrencyTransactionType.DailyClaim,
            $"guildclaim:{playerId}:{date}", ct);
        var boughtToday = await _economy.CountByReferencePrefixAsync(
            playerId, GuildCurrency.Sigil, GuildCurrencyTransactionType.ShopPurchase,
            $"guildbuy:{playerId}:{date}:", ct);
        var donatedToday = await _economy.CountByReferencePrefixAsync(
            playerId, GuildCurrency.Sigil, GuildCurrencyTransactionType.Donation,
            $"guilddonate:{playerId}:{date}:", ct);

        var poolBalance = membership is null ? 0L : await _economy.GetPoolBalanceAsync(membership.GuildId, ct);

        return new GuildSigilBalances
        {
            InGuild = membership is not null,
            SigilBalance = sigilBalance,
            TicketBalance = ticketBalance,
            PoolBalance = poolBalance,
            ClaimedToday = claimedToday,
            BoughtToday = boughtToday,
            DonatedToday = donatedToday,
            DailyBuyCap = _config.DailyBuyCap,
            DailyDonateCap = _config.DailyDonateCap,
        };
    }

    private Task Audit(Guid? actorId, string action, string summary, CancellationToken ct)
        => _audit.AppendAsync(AuditLog.Create(actorId, action, null, summary, null), ct);
}
