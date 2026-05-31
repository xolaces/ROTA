using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IMagicService
{
    Task<IReadOnlyList<OwnedMagicResponse>> GetOwnedMagicsAsync(Guid playerId, CancellationToken ct = default);

    Task<MagicApplyResult> ApplyMagicAsync(
        Guid playerId, Guid raidId, string magicDefinitionId, bool isAdmin,
        CancellationToken ct = default);

    Task<MagicApplyResult> RemoveMagicAsync(
        Guid playerId, Guid raidId, string magicDefinitionId, bool isAdmin,
        CancellationToken ct = default);
}
