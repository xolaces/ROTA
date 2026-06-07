using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

/// <summary>
/// The guild sigil economy (System 21 Slice 3a): a once-daily free claim (sigil + shop-ticket
/// allowance), buying sigils with tickets (capped per day), and donating personal sigils into the
/// guild pool (capped per day) that guild raids draw from (Slice 3b). All caps reset at UTC midnight via
/// <c>daily:{yyyy-MM-dd}</c>-style reference ids; every grant/spend is idempotent and audited. The
/// caller's guild is always resolved server-side from their membership — never from client input.
/// </summary>
public interface IGuildEconomyService
{
    /// <summary>
    /// Once-per-UTC-day claim: grants the free sigil and the shop-ticket allowance. Idempotent — a
    /// second call the same day returns <see cref="GuildClaimResult.AlreadyClaimed"/> with nothing new.
    /// </summary>
    Task<GuildClaimResult> ClaimDailyAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>Buy one guild sigil, spending shop tickets. Capped at the daily buy allowance.</summary>
    Task<GuildBuyResult> BuySigilAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>Donate one personal sigil into the caller's guild pool. Capped at the daily donate allowance.</summary>
    Task<GuildDonateResult> DonateSigilAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>The caller's current sigil/ticket balances, guild pool balance, and today's usage vs caps.</summary>
    Task<GuildSigilBalances> GetBalancesAsync(Guid playerId, CancellationToken ct = default);
}
