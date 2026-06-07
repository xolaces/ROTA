using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

// BETA (System 16 Slice 2) — permanent trophy ownership. Mirrors IPlayerMagicRepository.
public interface IPlayerGauntletTrophyRepository
{
    /// <summary>All non-deleted trophies the player owns.</summary>
    Task<IReadOnlyList<PlayerGauntletTrophy>> GetForPlayerAsync(
        Guid playerId, CancellationToken ct = default);

    /// <summary>Idempotent grant: insert if absent, restore if soft-deleted, no-op if owned.</summary>
    Task UpsertAsync(Guid playerId, string gauntletTrophyId, CancellationToken ct = default);
}
