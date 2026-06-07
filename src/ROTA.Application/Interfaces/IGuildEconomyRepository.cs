using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

/// <summary>
/// Persistence for the guild sigil economy (System 21 Slice 3a): the append-only per-player currency
/// ledger (<c>guild_currency_transactions</c>) and the per-guild sigil-pool ledger
/// (<c>guild_sigil_pool_transactions</c>). Balances are always SUM(amount); idempotency is enforced by
/// unique partial indexes on the reference_id. Multi-row writes commit in a single SaveChanges so a buy
/// (ticket debit + sigil credit) and a donation (personal debit + pool credit) are atomic.
/// </summary>
public interface IGuildEconomyRepository
{
    /// <summary>Per-player balance for a currency = SUM(amount) WHERE currency = X.</summary>
    Task<long> GetPlayerBalanceAsync(Guid playerId, GuildCurrency currency, CancellationToken ct = default);

    /// <summary>Guild sigil-pool balance = SUM(amount) for the guild.</summary>
    Task<long> GetPoolBalanceAsync(Guid guildId, CancellationToken ct = default);

    /// <summary>
    /// Count of the player's ledger rows for a (currency, type) whose reference_id starts with
    /// <paramref name="referencePrefix"/>. Used for the per-UTC-day buy/donate caps (the prefix encodes
    /// the day), so it is independent of any created_at clock skew.
    /// </summary>
    Task<int> CountByReferencePrefixAsync(
        Guid playerId, GuildCurrency currency, GuildCurrencyTransactionType type,
        string referencePrefix, CancellationToken ct = default);

    /// <summary>True when a row with this exact (player, currency, type, reference) already exists.</summary>
    Task<bool> ReferenceExistsAsync(
        Guid playerId, GuildCurrency currency, GuildCurrencyTransactionType type,
        string referenceId, CancellationToken ct = default);

    /// <summary>
    /// Append one or more per-player ledger rows atomically (single SaveChanges). Returns false when a
    /// unique-violation rolled the write back (a concurrent duplicate reference won the race).
    /// </summary>
    Task<bool> AddPlayerTransactionsAsync(
        IReadOnlyList<GuildCurrencyTransaction> transactions, CancellationToken ct = default);

    /// <summary>
    /// Atomically debit the player's sigils and credit the guild pool (single SaveChanges). Returns false
    /// when a unique-violation rolled the write back (duplicate reference race).
    /// </summary>
    Task<bool> AddDonationAsync(
        GuildCurrencyTransaction playerDebit, GuildSigilPoolTransaction poolCredit,
        CancellationToken ct = default);

    /// <summary>
    /// Atomically debit the guild sigil pool by <paramref name="amount"/> (Slice 3b — a raid summon),
    /// guarded by a balance check in the same statement so concurrent summons can never overspend.
    /// Raw parameterised SQL that participates in the ambient transaction when one is open (mirrors
    /// StrikeRepository.SpendAsync). Idempotent on (guild_id, RaidSummon, referenceId).
    /// </summary>
    Task<GuildPoolSpendOutcome> TrySpendPoolAsync(
        Guid guildId, int amount, string referenceId, CancellationToken ct = default);
}
