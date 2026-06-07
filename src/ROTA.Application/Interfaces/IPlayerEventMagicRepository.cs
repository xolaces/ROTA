using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

// BETA (System 16 Slice 2) — per-event rank-magic ownership. Granted at settlement (Slice 5),
// revoked at event close. FindAsync filters IsDeleted=false (only active grants count as "owned").
public interface IPlayerEventMagicRepository
{
    /// <summary>The active (non-deleted) grant for (player, event, magic), or null.</summary>
    Task<PlayerEventMagic?> FindAsync(
        Guid playerId, Guid gauntletEventId, string magicDefinitionId, CancellationToken ct = default);

    /// <summary>Idempotent grant: insert if absent, restore if soft-deleted, no-op if active.</summary>
    Task GrantAsync(
        Guid playerId, Guid gauntletEventId, string magicDefinitionId, CancellationToken ct = default);

    /// <summary>
    /// Soft-deletes all active grants for an event (the per-event consumables expire at close).
    /// Returns the rows that were revoked (so Slice 5 can write the honor echo per holder).
    /// </summary>
    Task<IReadOnlyList<PlayerEventMagic>> RevokeAllForEventAsync(
        Guid gauntletEventId, CancellationToken ct = default);
}
