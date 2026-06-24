using System.Text.Json;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

/// <summary>
/// System 24 (D8) — the dedicated Gauntlet battalion. Reads/assigns a 6-general + 20-troop loadout
/// from the player's owned units and computes battalion power, the Gauntlet strike-damage basis:
/// <c>(playerATK + Σ battalionATK) × 4 + (playerDEF + Σ battalionDEF) × 1</c> (base stats inherent;
/// Discernment crit stays passive and is never folded into power). A unit owns at most one copy, so
/// no unit may occupy two slots.
/// </summary>
public sealed class GauntletBattalionService : IGauntletBattalionService
{
    public const int GeneralSlots = 6;
    public const int TroopSlots   = 20;

    private readonly IGauntletBattalionRepository _battalions;
    private readonly ILegionService _legion;
    private readonly IPlayerRepository _players;
    private readonly IEquipmentService _equipment;

    public GauntletBattalionService(
        IGauntletBattalionRepository battalions,
        ILegionService legion,
        IPlayerRepository players,
        IEquipmentService equipment)
    {
        _battalions = battalions;
        _legion     = legion;
        _players    = players;
        _equipment  = equipment;
    }

    public async Task<GauntletBattalionResponse> GetBattalionAsync(Guid playerId, CancellationToken ct = default)
    {
        var (effAtk, effDef) = await GetEffectiveStatsAsync(playerId, ct);
        var owned     = await OwnedByIdAsync(playerId, ct);
        var battalion = await _battalions.GetForPlayerAsync(playerId, ct);
        return BuildResponse(battalion, owned, effAtk, effDef);
    }

    public async Task<long> ComputePowerAsync(
        Guid playerId, long playerEffectiveAttack, long playerEffectiveDefense, CancellationToken ct = default)
    {
        var owned     = await OwnedByIdAsync(playerId, ct);
        var battalion = await _battalions.GetForPlayerAsync(playerId, ct);
        var (atkSum, defSum, _, _) = Resolve(battalion, owned);
        return Power(playerEffectiveAttack + atkSum, playerEffectiveDefense + defSum);
    }

    public async Task<SetBattalionResult> SetBattalionAsync(
        Guid playerId, IReadOnlyList<string> generals, IReadOnlyList<string> troops, CancellationToken ct = default)
    {
        // Drop blanks (the validator already enforces the slot caps → 400 before we get here).
        var gens = (generals ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();
        var trps = (troops   ?? Array.Empty<string>()).Where(s => !string.IsNullOrWhiteSpace(s)).ToList();

        // A unit owns at most one copy, so the same id may not fill two slots (anti power-inflation).
        var all = gens.Concat(trps).ToList();
        if (all.Count != all.Distinct().Count())
            return new SetBattalionResult(BattalionUpdateStatus.DuplicateUnit);

        var owned = await OwnedByIdAsync(playerId, ct);

        foreach (var id in gens)
        {
            if (!owned.TryGetValue(id, out var u)) return new SetBattalionResult(BattalionUpdateStatus.UnitNotOwned);
            if (!string.Equals(u.UnitType, "General", StringComparison.OrdinalIgnoreCase))
                return new SetBattalionResult(BattalionUpdateStatus.WrongUnitType);
        }
        foreach (var id in trps)
        {
            if (!owned.TryGetValue(id, out var u)) return new SetBattalionResult(BattalionUpdateStatus.UnitNotOwned);
            if (!string.Equals(u.UnitType, "Troop", StringComparison.OrdinalIgnoreCase))
                return new SetBattalionResult(BattalionUpdateStatus.WrongUnitType);
        }

        var battalion = await _battalions.GetForPlayerAsync(playerId, ct) ?? PlayerGauntletBattalion.Create(playerId);
        battalion.SetLoadout(JsonSerializer.Serialize(gens), JsonSerializer.Serialize(trps));
        await _battalions.UpsertAsync(battalion, ct);

        var (effAtk, effDef) = await GetEffectiveStatsAsync(playerId, ct);
        return new SetBattalionResult(BattalionUpdateStatus.Success, BuildResponse(battalion, owned, effAtk, effDef));
    }

    // ── helpers ───────────────────────────────────────────────────────────────

    private async Task<(long effAtk, long effDef)> GetEffectiveStatsAsync(Guid playerId, CancellationToken ct)
    {
        var player   = await _players.FindByIdWithStatsAsync(playerId, ct);
        long baseAtk = player?.Stats?.BaseAttack  ?? 0;
        long baseDef = player?.Stats?.BaseDefense ?? 0;
        var combat   = await _equipment.GetEffectiveCombatDataAsync(playerId, baseAtk, baseDef, ct);
        return (combat.EffectiveAttack, combat.EffectiveDefense);
    }

    private async Task<Dictionary<string, OwnedUnitResponse>> OwnedByIdAsync(Guid playerId, CancellationToken ct)
    {
        var owned = await _legion.GetOwnedUnitsAsync(playerId, ct);
        var dict  = new Dictionary<string, OwnedUnitResponse>(owned.Count);
        foreach (var u in owned) dict[u.UnitDefinitionId] = u;
        return dict;
    }

    // Resolve the stored ids to owned units (silently dropping any no-longer-owned), summing stats.
    private static (long atkSum, long defSum, List<OwnedUnitResponse> generals, List<OwnedUnitResponse> troops)
        Resolve(PlayerGauntletBattalion? battalion, IReadOnlyDictionary<string, OwnedUnitResponse> owned)
    {
        var generals = new List<OwnedUnitResponse>();
        var troops   = new List<OwnedUnitResponse>();
        long atkSum = 0, defSum = 0;
        if (battalion is null) return (0, 0, generals, troops);

        foreach (var id in Decode(battalion.GeneralsJson))
            if (owned.TryGetValue(id, out var u)) { generals.Add(u); atkSum += u.BaseAttack; defSum += u.BaseDefense; }
        foreach (var id in Decode(battalion.TroopsJson))
            if (owned.TryGetValue(id, out var u)) { troops.Add(u); atkSum += u.BaseAttack; defSum += u.BaseDefense; }

        return (atkSum, defSum, generals, troops);
    }

    private GauntletBattalionResponse BuildResponse(
        PlayerGauntletBattalion? battalion, IReadOnlyDictionary<string, OwnedUnitResponse> owned, long effAtk, long effDef)
    {
        var (atkSum, defSum, generals, troops) = Resolve(battalion, owned);
        return new GauntletBattalionResponse
        {
            Generals     = generals,
            Troops       = troops,
            Power        = Power(effAtk + atkSum, effDef + defSum),
            GeneralSlots = GeneralSlots,
            TroopSlots   = TroopSlots,
        };
    }

    // The locked D8 weighting (base stats inherent).
    private static long Power(long totalAtk, long totalDef) => totalAtk * 4 + totalDef;

    private static IReadOnlyList<string> Decode(string json)
        => string.IsNullOrWhiteSpace(json)
            ? Array.Empty<string>()
            : JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
}
