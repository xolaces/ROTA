using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Repositories;

// System 24 (D8) — a player's Gauntlet battalion loadout. One row per player (unique index on
// player_id). The loadout slot lists are JSON columns; (de)serialisation + validation live in the
// service, so this repo is a thin EF read/upsert.
public sealed class GauntletBattalionRepository : IGauntletBattalionRepository
{
    private readonly RotaDbContext _db;

    public GauntletBattalionRepository(RotaDbContext db) => _db = db;

    public Task<PlayerGauntletBattalion?> GetForPlayerAsync(Guid playerId, CancellationToken ct = default)
        => _db.PlayerGauntletBattalions
            .FirstOrDefaultAsync(b => b.PlayerId == playerId && !b.IsDeleted, ct);

    public async Task UpsertAsync(PlayerGauntletBattalion battalion, CancellationToken ct = default)
    {
        // A battalion returned by GetForPlayerAsync is already tracked (its SetLoadout mutation is
        // picked up by SaveChanges); a freshly Created one is not yet tracked, so Add it first.
        if (_db.Entry(battalion).State == EntityState.Detached)
            await _db.PlayerGauntletBattalions.AddAsync(battalion, ct);

        await _db.SaveChangesAsync(ct);
    }
}
