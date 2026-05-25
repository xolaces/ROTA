using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

// BETA — no soft-delete filter on FindByIdAsync so expired/defeated raids can still be loaded for validation.
public sealed class ActiveRaidRepository : IActiveRaidRepository
{
    private readonly RotaDbContext _db;

    public ActiveRaidRepository(RotaDbContext db)
    {
        _db = db;
    }

    /// <inheritdoc />
    public async Task<ActiveRaid?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => await _db.ActiveRaids
            .Where(r => r.Id == id && !r.IsDeleted)
            .FirstOrDefaultAsync(ct);

    /// <inheritdoc />
    public async Task<IReadOnlyList<ActiveRaid>> GetAllActiveAsync(CancellationToken ct = default)
        => await _db.ActiveRaids
            .Include(r => r.SummonedByPlayer)
            .Where(r => !r.IsDefeated && !r.IsDeleted && r.ExpiresAt > DateTimeOffset.UtcNow)
            .ToListAsync(ct);

    /// <inheritdoc />
    public async Task<ActiveRaid> CreateAsync(ActiveRaid raid, CancellationToken ct = default)
    {
        _db.ActiveRaids.Add(raid);
        await _db.SaveChangesAsync(ct);
        return raid;
    }

    /// <inheritdoc />
    public async Task UpdateAsync(ActiveRaid raid, CancellationToken ct = default)
    {
        _db.ActiveRaids.Update(raid);
        await _db.SaveChangesAsync(ct);
    }
}
