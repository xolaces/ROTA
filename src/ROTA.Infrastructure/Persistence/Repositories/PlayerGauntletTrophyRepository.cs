using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

// BETA (System 16 Slice 2) — permanent trophy ownership. Mirrors PlayerMagicRepository.
public sealed class PlayerGauntletTrophyRepository : IPlayerGauntletTrophyRepository
{
    private readonly RotaDbContext _db;

    public PlayerGauntletTrophyRepository(RotaDbContext db) => _db = db;

    public async Task<IReadOnlyList<PlayerGauntletTrophy>> GetForPlayerAsync(
        Guid playerId, CancellationToken ct = default)
        => await _db.PlayerGauntletTrophies
            .Where(t => t.PlayerId == playerId && !t.IsDeleted)
            .ToListAsync(ct);

    public async Task UpsertAsync(Guid playerId, string gauntletTrophyId, CancellationToken ct = default)
    {
        var existing = await _db.PlayerGauntletTrophies
            .FirstOrDefaultAsync(t => t.PlayerId == playerId && t.GauntletTrophyId == gauntletTrophyId, ct);

        if (existing is null)
        {
            _db.PlayerGauntletTrophies.Add(PlayerGauntletTrophy.Create(playerId, gauntletTrophyId));
        }
        else if (existing.IsDeleted)
        {
            existing.Restore();
            _db.PlayerGauntletTrophies.Update(existing);
        }
        // already owned and not deleted → no-op

        await _db.SaveChangesAsync(ct);
    }
}
