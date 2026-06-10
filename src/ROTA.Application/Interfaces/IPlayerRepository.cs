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
}
