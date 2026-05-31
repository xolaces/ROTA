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

    /// <summary>Idempotent magic grant. Safe to call multiple times — duplicate = no-op.</summary>
    Task GrantMagicAsync(Guid playerId, string magicDefinitionId, CancellationToken ct = default);

    /// <summary>Spend gems to purchase a magic. Duplicate purchase charges again but grant is idempotent.</summary>
    Task<BuyMagicResult> BuyMagicAsync(Guid playerId, string magicDefinitionId, CancellationToken ct = default);
}
