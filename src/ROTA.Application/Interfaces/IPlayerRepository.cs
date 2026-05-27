using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

/// <summary>
/// Data-access contract for the Player aggregate.
/// Implemented in ROTA.Infrastructure. Application layer never touches EF Core directly.
/// </summary>
public interface IPlayerRepository
{
    /// <summary>Returns a player by primary key, or null if not found.</summary>
    Task<Player?> FindByIdAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns a player by email (case-insensitive), or null if not found.</summary>
    Task<Player?> FindByEmailAsync(string email, CancellationToken ct = default);

    /// <summary>Returns true if the email is already registered.</summary>
    Task<bool> EmailExistsAsync(string email, CancellationToken ct = default);

    /// <summary>Returns true if the username is already taken.</summary>
    Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default);

    /// <summary>Persists a new player and its seeded child records (Stats, Resources).</summary>
    Task<Player> CreateAsync(Player player, CancellationToken ct = default);

    /// <summary>Returns a player with their Resources collection eagerly loaded, or null if not found/deleted.</summary>
    Task<Player?> FindByIdWithResourcesAsync(Guid id, CancellationToken ct = default);

    /// <summary>Returns a player with their Stats eagerly loaded, or null if not found/deleted.</summary>
    Task<Player?> FindByIdWithStatsAsync(Guid id, CancellationToken ct = default);

    /// <summary>Persists changes to an existing tracked player entity.</summary>
    Task UpdateAsync(Player player, CancellationToken ct = default);

    /// <summary>Persists changes to a tracked PlayerStats entity.</summary>
    Task UpdateStatsAsync(Domain.Entities.PlayerStats stats, CancellationToken ct = default);
}
