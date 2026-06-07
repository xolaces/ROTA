using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

// BETA (System 16 Slice 2) — per-event rank-magic ownership. FindAsync returns only ACTIVE
// (non-deleted) grants; revocation soft-deletes and returns the affected rows for honor write-back.
public sealed class PlayerEventMagicRepository : IPlayerEventMagicRepository
{
    private readonly RotaDbContext _db;

    public PlayerEventMagicRepository(RotaDbContext db) => _db = db;

    public Task<PlayerEventMagic?> FindAsync(
        Guid playerId, Guid gauntletEventId, string magicDefinitionId, CancellationToken ct = default)
        => _db.PlayerEventMagics
            .FirstOrDefaultAsync(
                m => m.PlayerId == playerId
                  && m.GauntletEventId == gauntletEventId
                  && m.MagicDefinitionId == magicDefinitionId
                  && !m.IsDeleted, ct);

    public async Task GrantAsync(
        Guid playerId, Guid gauntletEventId, string magicDefinitionId, CancellationToken ct = default)
    {
        // Include soft-deleted rows so a re-grant restores rather than violating the unique index.
        var existing = await _db.PlayerEventMagics
            .FirstOrDefaultAsync(
                m => m.PlayerId == playerId
                  && m.GauntletEventId == gauntletEventId
                  && m.MagicDefinitionId == magicDefinitionId, ct);

        if (existing is null)
        {
            _db.PlayerEventMagics.Add(PlayerEventMagic.Create(playerId, gauntletEventId, magicDefinitionId));
        }
        else if (existing.IsDeleted)
        {
            existing.Restore();
            _db.PlayerEventMagics.Update(existing);
        }
        // already active → no-op

        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PlayerEventMagic>> RevokeAllForEventAsync(
        Guid gauntletEventId, CancellationToken ct = default)
    {
        var active = await _db.PlayerEventMagics
            .Where(m => m.GauntletEventId == gauntletEventId && !m.IsDeleted)
            .ToListAsync(ct);

        foreach (var row in active)
            row.Revoke();

        if (active.Count > 0)
            await _db.SaveChangesAsync(ct);

        return active;
    }
}
