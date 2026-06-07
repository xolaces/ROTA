using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

// BETA (System 16 Slice 2) — persistence for per-event player standings.
public interface IGauntletEntryRepository
{
    Task<GauntletEntry?> FindByEventAndPlayerAsync(
        Guid gauntletEventId, Guid playerId, CancellationToken ct = default);

    /// <summary>All non-deleted entries for an event (used by the snapshot/leaderboard in Slice 3).</summary>
    Task<IReadOnlyList<GauntletEntry>> GetForEventAsync(
        Guid gauntletEventId, CancellationToken ct = default);

    /// <summary>
    /// Inserts the entry if (event, player) has none, else returns the existing row unchanged.
    /// The unique index on (gauntlet_event_id, player_id) makes this race-safe; a conflicting
    /// insert is swallowed and the persisted row is re-read (so league is never re-evaluated).
    /// </summary>
    Task<GauntletEntry> UpsertAsync(GauntletEntry entry, CancellationToken ct = default);
}
