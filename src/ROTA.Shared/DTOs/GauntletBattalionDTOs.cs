namespace ROTA.Shared.DTOs;

// System 24 (D8) — Gauntlet battalion DTOs. Slot units are surfaced as OwnedUnitResponse (the same
// shape the battalion editor lists), so the client reuses its existing unit card.

/// <summary>The caller's battalion: resolved general/troop units + computed power + slot caps.</summary>
public class GauntletBattalionResponse
{
    public List<OwnedUnitResponse> Generals { get; set; } = new();
    public List<OwnedUnitResponse> Troops   { get; set; } = new();

    /// <summary>(playerATK + Σ battalionATK)×4 + (playerDEF + Σ battalionDEF)×1 — the strike basis.</summary>
    public long Power { get; set; }

    public int GeneralSlots { get; set; }
    public int TroopSlots   { get; set; }
}

/// <summary>Assign request: the ordered unit-definition-id lists for each band.</summary>
public class SetGauntletBattalionRequest
{
    public List<string> Generals { get; set; } = new();
    public List<string> Troops   { get; set; } = new();
}

/// <summary>Outcome of a battalion assignment (validation happens server-side).</summary>
public enum BattalionUpdateStatus
{
    Success       = 0,
    UnitNotOwned  = 1,
    WrongUnitType = 2,
    DuplicateUnit = 3,
}

/// <summary>Service result: a status + the resulting battalion on success.</summary>
public sealed record SetBattalionResult(BattalionUpdateStatus Status, GauntletBattalionResponse? Battalion = null);
