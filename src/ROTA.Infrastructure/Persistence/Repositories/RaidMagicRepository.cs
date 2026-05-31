using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class RaidMagicRepository : IRaidMagicRepository
{
    private readonly RotaDbContext _db;

    public RaidMagicRepository(RotaDbContext db) => _db = db;

    public async Task<IReadOnlyList<RaidMagic>> GetForRaidAsync(
        Guid activeRaidId, CancellationToken ct = default)
        => await _db.RaidMagics
            .Where(m => m.ActiveRaidId == activeRaidId && !m.IsDeleted)
            .ToListAsync(ct);

    public Task<int> CountForRaidAsync(
        Guid activeRaidId, CancellationToken ct = default)
        => _db.RaidMagics
            .CountAsync(m => m.ActiveRaidId == activeRaidId && !m.IsDeleted, ct);

    public Task<RaidMagic?> FindAsync(
        Guid activeRaidId, string magicDefinitionId, CancellationToken ct = default)
        => _db.RaidMagics
            .FirstOrDefaultAsync(
                m => m.ActiveRaidId == activeRaidId
                  && m.MagicDefinitionId == magicDefinitionId
                  && !m.IsDeleted, ct);

    public Task<RaidMagic?> FindByPlayerAsync(
        Guid activeRaidId, Guid playerId, CancellationToken ct = default)
        => _db.RaidMagics
            .FirstOrDefaultAsync(
                m => m.ActiveRaidId == activeRaidId
                  && m.AppliedByPlayerId == playerId
                  && !m.IsDeleted, ct);

    public async Task<RaidMagic> CreateAsync(RaidMagic raidMagic, CancellationToken ct = default)
    {
        _db.RaidMagics.Add(raidMagic);
        await _db.SaveChangesAsync(ct);
        return raidMagic;
    }

    public async Task SoftDeleteAsync(RaidMagic raidMagic, CancellationToken ct = default)
    {
        raidMagic.SoftDelete();
        _db.RaidMagics.Update(raidMagic);
        await _db.SaveChangesAsync(ct);
    }
}
