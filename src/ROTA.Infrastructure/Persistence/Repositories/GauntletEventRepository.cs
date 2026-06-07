using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

// BETA (System 16 Slice 2) — Gauntlet event persistence.
public sealed class GauntletEventRepository : IGauntletEventRepository
{
    private readonly RotaDbContext _db;

    public GauntletEventRepository(RotaDbContext db) => _db = db;

    public Task<GauntletEvent?> GetActiveAsync(CancellationToken ct = default)
        => _db.GauntletEvents
            .FirstOrDefaultAsync(
                e => e.State == GauntletEventState.Active && !e.IsDeleted, ct);

    // System 16 Slice 7 — most recently settled event (by SettledAt desc). Settled events always have
    // a non-null SettledAt (stamped by MarkSettled), so ordering is well-defined.
    public Task<GauntletEvent?> GetMostRecentSettledAsync(CancellationToken ct = default)
        => _db.GauntletEvents
            .Where(e => e.State == GauntletEventState.Settled && !e.IsDeleted)
            .OrderByDescending(e => e.SettledAt)
            .FirstOrDefaultAsync(ct);

    public Task<GauntletEvent?> FindByIdAsync(Guid id, CancellationToken ct = default)
        => _db.GauntletEvents
            .FirstOrDefaultAsync(e => e.Id == id && !e.IsDeleted, ct);

    public async Task<GauntletEvent> CreateAsync(
        GauntletEvent gauntletEvent, CancellationToken ct = default)
    {
        _db.GauntletEvents.Add(gauntletEvent);
        await _db.SaveChangesAsync(ct);
        return gauntletEvent;
    }

    public async Task UpdateAsync(GauntletEvent gauntletEvent, CancellationToken ct = default)
    {
        _db.GauntletEvents.Update(gauntletEvent);
        await _db.SaveChangesAsync(ct);
    }
}
