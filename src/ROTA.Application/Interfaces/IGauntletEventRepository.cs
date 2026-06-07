using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

// BETA (System 16 Slice 2) — persistence for Gauntlet events. The admin service enforces ≤1
// Active; GetActiveAsync is the read used both to surface the current event and to guard opens.
public interface IGauntletEventRepository
{
    /// <summary>The single Active (non-deleted) event, or null if none is open.</summary>
    Task<GauntletEvent?> GetActiveAsync(CancellationToken ct = default);

    Task<GauntletEvent?> FindByIdAsync(Guid id, CancellationToken ct = default);

    Task<GauntletEvent> CreateAsync(GauntletEvent gauntletEvent, CancellationToken ct = default);

    Task UpdateAsync(GauntletEvent gauntletEvent, CancellationToken ct = default);
}
