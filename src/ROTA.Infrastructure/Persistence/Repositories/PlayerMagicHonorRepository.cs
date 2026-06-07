using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

// BETA (System 16 Slice 2) — permanent "honor echo" records. Granted (idempotently) at settlement.
public sealed class PlayerMagicHonorRepository : IPlayerMagicHonorRepository
{
    private readonly RotaDbContext _db;

    public PlayerMagicHonorRepository(RotaDbContext db) => _db = db;

    public Task<bool> HasHonorAsync(
        Guid playerId, string magicDefinitionId, CancellationToken ct = default)
        => _db.PlayerMagicHonors.AnyAsync(
            h => h.PlayerId == playerId
              && h.MagicDefinitionId == magicDefinitionId
              && !h.IsDeleted, ct);

    public async Task GrantAsync(
        Guid playerId, string magicDefinitionId, CancellationToken ct = default)
    {
        var exists = await _db.PlayerMagicHonors.AnyAsync(
            h => h.PlayerId == playerId && h.MagicDefinitionId == magicDefinitionId, ct);
        if (exists)
            return; // idempotent — honor is permanent and recorded once

        _db.PlayerMagicHonors.Add(PlayerMagicHonor.Create(playerId, magicDefinitionId));
        await _db.SaveChangesAsync(ct);
    }
}
