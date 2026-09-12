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

    /// <summary>
    /// Registration: every gear definition marked Starter is granted once and put on, so a new
    /// player is dressed before their first screen. Nothing to do when content marks none.
    /// </summary>
    Task GrantStarterKitAsync(Guid playerId, CancellationToken ct = default);
}

// Lives in this file alongside the interface.
//
// GearProcs is every worn piece that carries a proc, the mount included, each rolled on its own
// per hit (owner, 2026-09-12: the relics carry procs, and until then a proc on anything but a
// mount was dead data — the Cinder Cuff's and the Stoned Horns' never fired). MountProc stays as
// the mount's own, with the conditional ProcChanceFlat/ProcAmountFlat folded into it, and is
// what a caller that passes no GearProcs still rolls.
public sealed record EffectiveCombatData(
    long          EffectiveAttack,
    long          EffectiveDefense,
    GearProcData? MountProc,           // null when no mount is equipped
    double        FlatDamagePercent,   // accumulated from conditional bonuses; 0.0 when none
    IReadOnlyList<GearProcData>? GearProcs = null)
{
    /// <summary>Every proc a hit rolls: GearProcs when given, else the mount's alone.</summary>
    public IEnumerable<GearProcData> ProcsToRoll
        => GearProcs ?? (MountProc is null ? Array.Empty<GearProcData>() : new[] { MountProc });
}

public sealed record GearProcData(double ProcChance, double ProcPercent);
