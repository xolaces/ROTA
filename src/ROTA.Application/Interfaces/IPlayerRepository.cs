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
}