using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

/// <summary>
/// The dedicated Gauntlet battalion (System 24 D8): read/assign a 6-general + 20-troop loadout
/// from owned units, and compute battalion power — the Gauntlet strike-damage basis
/// <c>(playerATK + Σ battalionATK) × 4 + (playerDEF + Σ battalionDEF) × 1</c>.
/// </summary>
public interface IGauntletBattalionService
{
    /// <summary>The caller's battalion (resolved units + computed power); empty if none assigned yet.</summary>
    Task<GauntletBattalionResponse> GetBattalionAsync(Guid playerId, CancellationToken ct = default);

    /// <summary>Replace the loadout. Validates ownership, UnitType band, and no-duplicates.</summary>
    Task<SetBattalionResult> SetBattalionAsync(
        Guid playerId, IReadOnlyList<string> generals, IReadOnlyList<string> troops, CancellationToken ct = default);

    /// <summary>
    /// Battalion power for the combat fork — the caller passes the player's already-computed
    /// EFFECTIVE attack/defense (gear-included) so the hit path avoids a second equipment read.
    /// </summary>
    Task<long> ComputePowerAsync(
        Guid playerId, int playerEffectiveAttack, int playerEffectiveDefense, CancellationToken ct = default);
}
