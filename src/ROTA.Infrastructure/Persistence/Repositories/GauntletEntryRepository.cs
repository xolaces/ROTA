using Microsoft.EntityFrameworkCore;
using Npgsql;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

// BETA (System 16 Slice 2) — per-event standings persistence.
public sealed class GauntletEntryRepository : IGauntletEntryRepository
{
    private readonly RotaDbContext _db;

    public GauntletEntryRepository(RotaDbContext db) => _db = db;

    public Task<GauntletEntry?> FindByEventAndPlayerAsync(
        Guid gauntletEventId, Guid playerId, CancellationToken ct = default)
        => _db.GauntletEntries
            .FirstOrDefaultAsync(
                e => e.GauntletEventId == gauntletEventId
                  && e.PlayerId == playerId
                  && !e.IsDeleted, ct);

    public async Task<IReadOnlyList<GauntletEntry>> GetForEventAsync(
        Guid gauntletEventId, CancellationToken ct = default)
        => await _db.GauntletEntries
            .Where(e => e.GauntletEventId == gauntletEventId && !e.IsDeleted)
            .ToListAsync(ct);

    public async Task<GauntletEntry> UpsertAsync(GauntletEntry entry, CancellationToken ct = default)
    {
        // Fast path: an entry already exists → return it unchanged (league never re-evaluated).
        var existing = await FindByEventAndPlayerAsync(entry.GauntletEventId, entry.PlayerId, ct);
        if (existing is not null)
            return existing;

        // Insert; the unique index on (gauntlet_event_id, player_id) makes a concurrent double-join
        // race-safe — the loser catches the unique violation and re-reads the winner's row.
        try
        {
            _db.GauntletEntries.Add(entry);
            await _db.SaveChangesAsync(ct);
            return entry;
        }
        catch (DbUpdateException ex)
            when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            _db.ChangeTracker.Clear();
            // Re-read the row the winning insert committed.
            return (await FindByEventAndPlayerAsync(entry.GauntletEventId, entry.PlayerId, ct))!;
        }
    }
}
