using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

// BETA - Full implementation. Covers all IPlayerRepository contract methods.
/// <summary>
/// EF Core implementation of IPlayerRepository.
/// All queries exclude soft-deleted players (is_deleted = true).
/// </summary>
public sealed class PlayerRepository : IPlayerRepository
{
    private readonly RotaDbContext _db;

    public PlayerRepository(RotaDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<Player?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Players
            .Where(p => p.Id == id && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<Player?> FindByEmailAsync(string email, CancellationToken ct = default)
        => await _db.Players
            .Where(p => p.Email == email.ToLowerInvariant() && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
        => await _db.Players
            .AnyAsync(p => p.Email == email.ToLowerInvariant() && !p.IsDeleted, ct);

    /// <inheritdoc />
    public async Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default)
        => await _db.Players
            .AnyAsync(p => p.Username == username && !p.IsDeleted, ct);

    /// <inheritdoc />
    public async Task<Player> CreateAsync(Player player, CancellationToken ct = default)
    {
        _db.Players.Add(player);
        await _db.SaveChangesAsync(ct);
        return player;
    }
}