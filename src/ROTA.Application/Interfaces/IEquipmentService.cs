using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public interface IEquipmentService
{
    Task<EquipResult>   EquipAsync(Guid playerId, string slotName, string gearDefinitionId, CancellationToken ct = default);
    Task<UnequipResult> UnequipAsync(Guid playerId, string slotName, CancellationToken ct = default);
    Task<IReadOnlyList<EquippedItemResponse>> GetEquipmentAsync(Guid playerId, CancellationToken ct = default);
    Task<IReadOnlyList<OwnedGearResponse>>    GetOwnedGearAsync(Guid playerId, CancellationToken ct = default);

    // Called by RaidService on every hit. baseAtk/baseDef are from PlayerStats.
    Task<EffectiveCombatData> GetEffectiveCombatDataAsync(Guid playerId, long baseAtk, long baseDef, CancellationToken ct = default);

    /// <summary>
    /// Idempotent gear grant — upsert: adds to existing stack or creates a new row.
    /// Safe to call from loot reward distribution (duplicate = quantity increase, never error).
    /// </summary>
    Task GrantGearAsync(Guid playerId, string gearDefinitionId, int quantity, CancellationToken ct = default);
}

// Lives in this file alongside the interface.
public sealed record EffectiveCombatData(
    long          EffectiveAttack,
    long          EffectiveDefense,
    GearProcData? MountProc,           // null when no mount is equipped
    double        FlatDamagePercent);  // accumulated from conditional bonuses; 0.0 when none

public sealed record GearProcData(double ProcChance, double ProcPercent);
