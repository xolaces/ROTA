using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class PlayerRepository : IPlayerRepository
{
    private readonly RotaDbContext _db;

    public PlayerRepository(RotaDbContext db)
    {
        _db = db;
    }

    public async Task<Player?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.Players
            .Where(p => p.Id == id && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

    public async Task<Player?> FindByEmailAsync(string email, CancellationToken ct = default)
        => await _db.Players
            .Where(p => p.Email == email.ToLowerInvariant() && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

    public async Task<bool> EmailExistsAsync(string email, CancellationToken ct = default)
        => await _db.Players
            .AnyAsync(p => p.Email == email.ToLowerInvariant() && !p.IsDeleted, ct);

    public async Task<bool> UsernameExistsAsync(string username, CancellationToken ct = default)
        => await _db.Players
            .AnyAsync(p => p.Username == username && !p.IsDeleted, ct);

    public async Task<Player> CreateAsync(Player player, CancellationToken ct = default)
    {
        _db.Players.Add(player);
        await _db.SaveChangesAsync(ct);
        return player;
    }

    public async Task<Player?> FindByIdWithResourcesAsync(Guid id, CancellationToken ct = default)
        => await _db.Players
            .Include(p => p.Resources)
            .Where(p => p.Id == id && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

    public async Task<Player?> FindByIdWithStatsAsync(Guid id, CancellationToken ct = default)
        => await _db.Players
            .Include(p => p.Stats)
            .Where(p => p.Id == id && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

    public async Task UpdateAsync(Player player, CancellationToken ct = default)
    {
        _db.Players.Update(player);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateStatsAsync(Domain.Entities.PlayerStats stats, CancellationToken ct = default)
    {
        _db.PlayerStats.Update(stats);
        await _db.SaveChangesAsync(ct);
    }
}
