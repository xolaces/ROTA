using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class PlayerMagicRepository : IPlayerMagicRepository
{
    private readonly RotaDbContext _db;

    public PlayerMagicRepository(RotaDbContext db) => _db = db;

    public async Task<IReadOnlyList<PlayerMagic>> GetOwnedAsync(
        Guid playerId, CancellationToken ct = default)
        => await _db.PlayerMagics
            .Where(m => m.PlayerId == playerId && !m.IsDeleted)
            .ToListAsync(ct);

    public Task<PlayerMagic?> FindAsync(
        Guid playerId, string magicDefinitionId, CancellationToken ct = default)
        => _db.PlayerMagics
            .FirstOrDefaultAsync(
                m => m.PlayerId == playerId && m.MagicDefinitionId == magicDefinitionId, ct);

    public async Task UpsertAsync(
        Guid playerId, string magicDefinitionId, CancellationToken ct = default)
    {
        var existing = await FindAsync(playerId, magicDefinitionId, ct);
        if (existing is null)
        {
            _db.PlayerMagics.Add(PlayerMagic.Create(playerId, magicDefinitionId));
        }
        else if (existing.IsDeleted)
        {
            existing.Restore();
            _db.PlayerMagics.Update(existing);
        }
        // already owned and not deleted → no-op

        await _db.SaveChangesAsync(ct);
    }
}
