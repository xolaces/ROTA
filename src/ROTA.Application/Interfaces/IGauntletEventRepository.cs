using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

// BETA (System 16 Slice 2) — persistence for Gauntlet events. The admin service enforces ≤1
// Active; GetActiveAsync is the read used both to surface the current event and to guard opens.
public interface IGauntletEventRepository
{
    /// <summary>The single Active (non-deleted) event, or null if none is open.</summary>
    Task<GauntletEvent?> GetActiveAsync(CancellationToken ct = default);

    /// <summary>
    /// The most recently settled (non-deleted) event by <c>SettledAt</c> descending, or null if none
    /// has ever settled. Used at open (System 16 Slice 7) to hand the prior event's rank winners their
    /// per-event consumable for the NEW event.
    /// </summary>
    Task<GauntletEvent?> GetMostRecentSettledAsync(CancellationToken ct = default);

    /// <summary>T76 — most recently settled event of the given KIND (seasonal-crown scope), or null.</summary>
    Task<GauntletEvent?> GetMostRecentSettledAsync(Domain.Enums.GauntletEventKind kind, CancellationToken ct = default);

    /// <summary>T76 — count of all (non-deleted) events of the given kind; drives RunNumber.</summary>
    Task<int> CountByKindAsync(Domain.Enums.GauntletEventKind kind, CancellationToken ct = default);

    Task<GauntletEvent?> FindByIdAsync(Guid id, CancellationToken ct = default);

    Task<GauntletEvent> CreateAsync(GauntletEvent gauntletEvent, CancellationToken ct = default);

    Task UpdateAsync(GauntletEvent gauntletEvent, CancellationToken ct = default);
}
