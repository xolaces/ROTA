namespace ROTA.Application.Interfaces;

// BETA (System 16 Slice 2) — permanent "honor echo" records. Read by combat for the
// ×1.10 former-owner proc multiplier; written at settlement when a PlayerEventMagic expires.
public interface IPlayerMagicHonorRepository
{
    Task<bool> HasHonorAsync(Guid playerId, string magicDefinitionId, CancellationToken ct = default);

    /// <summary>Idempotent grant: insert if absent, no-op if a record already exists.</summary>
    Task GrantAsync(Guid playerId, string magicDefinitionId, CancellationToken ct = default);
}
