using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public interface IPlayerRepository
{
    Task<Player?> FindByIdAsync(Guid id, CancellationToken ct = default);

    Task<Player?> FindByEmailAsync(string email, CancellationToken ct = default);

    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);

    Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default);

    Task<Player> CreateAsync(Player player, CancellationToken ct = default);

    Task<Player?> FindByIdWithResourcesAsync(Guid id, CancellationToken ct = default);

    Task<Player?> FindByIdWithStatsAsync(Guid id, CancellationToken ct = default);

    Task UpdateAsync(Player player, CancellationToken ct = default);

    Task UpdateStatsAsync(Domain.Entities.PlayerStats stats, CancellationToken ct = default);

    /// <summary>Looks up a player by username. Returns null if not found or soft-deleted.</summary>
    Task<Player?> FindByUsernameAsync(string username, CancellationToken ct = default);

    /// <summary>
    /// Counts non-deleted players who have the specified role flag set.
    /// Uses bitwise: <c>WHERE (roles &amp; @r) = @r AND NOT is_deleted</c>.
    /// </summary>
    Task<int> CountByRoleAsync(ROTA.Domain.Enums.PlayerRoles role, CancellationToken ct = default);

    /// <summary>
    /// Atomically demotes the target from Admin while preserving the "always keep ≥1 admin" invariant
    /// under concurrency. All admin-role mutations serialize on a fixed advisory lock, so two
    /// simultaneous demotions of DIFFERENT admins cannot both pass a last-admin check and zero out the
    /// admins. Returns false (no change) if the target is not an admin or is the last remaining admin.
    /// </summary>
    Task<bool> TryDemoteAdminAsync(Guid targetId, CancellationToken ct = default);

    /// <summary>
    /// T59 — applies <paramref name="mutate"/> to the player and saves under the xmin optimistic-
    /// concurrency token, retrying on conflict (reload fresh values, re-apply, re-save). Use this for
    /// every gameplay reward write to the players row (quest rewards, raid on-hit gold/XP, kill-loop
    /// XP/gold) so simultaneous quest+raid writes can never lose gold/XP via last-write-wins.
    /// The callback MUST be repeatable: touch only the passed player (no external side effects).
    /// Returns the callback's result from the attempt that committed.
    /// Throws <see cref="InvalidOperationException"/> if the player does not exist.
    /// </summary>
    Task<TResult> MutateWithRetryAsync<TResult>(Guid playerId, Func<Player, TResult> mutate, CancellationToken ct = default);

    /// <summary>
    /// Atomically debits <paramref name="amount"/> gold, re-checking affordability in the SAME statement
    /// (mirrors the gem ledger's SUM-guarded conditional debit). Returns the new balance, or
    /// <c>null</c> when the player could not afford it — in which case nothing was written.
    /// Use this for every gold SPEND: a read-then-write check can be raced by a concurrent reward and
    /// drive the balance negative. Ambient-transaction aware, so it enlists in a mutation-lock
    /// transaction and commits or rolls back with the grant it pays for.
    /// </summary>
    Task<long?> TrySpendGoldAsync(Guid playerId, long amount, CancellationToken ct = default);
}
