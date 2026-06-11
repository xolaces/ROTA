using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

/// <summary>
/// T75 — dev/admin grant actions backing the client Dev Tools screen. AdminOnly at the controller;
/// every action audits with the ACTING admin id + the affected target. Test/dev affordance — these
/// bypass the normal earn loops by design, which is exactly why each one writes audit_log.
/// </summary>
public interface IDevService
{
    /// <summary>Grants gold / gems / unassigned skill points / XP (XP fires real level-ups).</summary>
    Task<DevActionResponse> GrantAsync(Guid actorId, DevGrantRequest request, string ipAddress, CancellationToken ct = default);

    /// <summary>Grants quantity of a content item (validated against items.json).</summary>
    Task<DevActionResponse> GrantItemAsync(Guid actorId, DevGrantItemRequest request, string ipAddress, CancellationToken ct = default);

    /// <summary>Refills every resource pool (energy/stamina/guild-stamina/health) to max.</summary>
    Task<DevActionResponse> RefillAsync(Guid actorId, DevRefillRequest request, string ipAddress, CancellationToken ct = default);
}
