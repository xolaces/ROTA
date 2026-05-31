using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IEquipmentService
{
    Task<EquipResult>   EquipAsync(Guid playerId, string slotName, string gearDefinitionId, CancellationToken ct = default);
    Task<UnequipResult> UnequipAsync(Guid playerId, string slotName, CancellationToken ct = default);
    Task<IReadOnlyList<EquippedItemResponse>> GetEquipmentAsync(Guid playerId, CancellationToken ct = default);

    // Called by RaidService on every hit. baseAtk/baseDef are from PlayerStats.
    Task<EffectiveCombatData> GetEffectiveCombatDataAsync(Guid playerId, int baseAtk, int baseDef, CancellationToken ct = default);
}

// Lives in this file alongside the interface.
public sealed record EffectiveCombatData(
    int           EffectiveAttack,
    int           EffectiveDefense,
    GearProcData? MountProc,           // null when no mount is equipped
    double        FlatDamagePercent);  // accumulated from conditional bonuses; 0.0 when none

public sealed record GearProcData(double ProcChance, double ProcPercent);
