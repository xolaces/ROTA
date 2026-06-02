namespace ROTA.Shared.DTOs;

// ----------------------------------------------------------------
// Slice 2 — ownership list responses
// ----------------------------------------------------------------

public class OwnedUnitResponse
{
    public string UnitDefinitionId { get; set; } = string.Empty;
    public string Name             { get; set; } = string.Empty;
    public string Description      { get; set; } = string.Empty;
    public string UnitType         { get; set; } = string.Empty;
    public string Rarity           { get; set; } = string.Empty;
    public int    BaseAttack       { get; set; }
    public int    BaseDefense      { get; set; }
    public string Race             { get; set; } = string.Empty;
    public string Role             { get; set; } = string.Empty;
    public string Attribute        { get; set; } = string.Empty;
    public bool   HasAbility       { get; set; }
    public double LegionBonus      { get; set; }
    public string IconPath         { get; set; } = string.Empty;
}

public class OwnedLegionResponse
{
    public string LegionDefinitionId { get; set; } = string.Empty;
    public string Name               { get; set; } = string.Empty;
    public string Description        { get; set; } = string.Empty;
    public string Rarity             { get; set; } = string.Empty;
    public double PowerBonus         { get; set; }
    public int    GeneralSlotCount   { get; set; }
    public int    TroopSlotCount     { get; set; }
    public bool   IsActive           { get; set; }
    public string IconPath           { get; set; } = string.Empty;
}

// ----------------------------------------------------------------
// Slice 3 — assembly endpoints + power computation
// ----------------------------------------------------------------

public class SetActiveLegionResult
{
    public bool   Success       { get; set; }
    public string? FailureReason { get; set; }
}

public enum AssignSlotFailureCode
{
    LegionNotOwned,
    SlotOutOfRange,
    UnitNotOwned,
    WrongUnitType,
    ConstraintMismatch,
    UnitAlreadyAssigned,
}

public class AssignSlotResult
{
    public bool                 Success       { get; set; }
    public AssignSlotFailureCode FailureCode  { get; set; }
    public string?              FailureReason { get; set; }
}

public class ClearSlotResult
{
    public bool   Success       { get; set; }
    public string? FailureReason { get; set; }
}

public class LegionPowerResult
{
    public double RawPower           { get; set; }
    public double LegionBonusFraction { get; set; }
    public double UnitSum            { get; set; }
}

public class SlotAssignmentResponse
{
    public string Family           { get; set; } = string.Empty;
    public int    SlotIndex        { get; set; }
    public string ConstraintType   { get; set; } = string.Empty;
    public string? ConstraintValue { get; set; }
    public string? UnitDefinitionId { get; set; }
    public string? UnitName         { get; set; }
}

public class LegionDetailResponse
{
    public string LegionDefinitionId { get; set; } = string.Empty;
    public string Name               { get; set; } = string.Empty;
    public bool   IsActive           { get; set; }
    public double PowerBonus         { get; set; }
    public List<SlotAssignmentResponse> Slots { get; set; } = new();
    public LegionPowerResult ComputedPower    { get; set; } = new();
}

// ----------------------------------------------------------------
// Slice 5 — Commander slot
// ----------------------------------------------------------------

public enum CommanderEquipFailureCode
{
    GearDefinitionNotFound,
}

public class CommanderEquipResult
{
    public bool                      Success       { get; set; }
    public CommanderEquipFailureCode FailureCode   { get; set; }
    public string?                   FailureReason { get; set; }
    public string?                   GearDefinitionId { get; set; }
    public string?                   GearName      { get; set; }
}

public class CommanderUnequipResult
{
    public bool   Success       { get; set; }
    public string? FailureReason { get; set; }
}

public class CommanderGearResponse
{
    public string GearDefinitionId { get; set; } = string.Empty;
    public string GearName         { get; set; } = string.Empty;
    public string GearDescription  { get; set; } = string.Empty;
    public string Rarity           { get; set; } = string.Empty;
    public double? ProcChance      { get; set; }
    public double? ProcPercent     { get; set; }
    public string Note             { get; set; } = "Stat bonuses are ignored in combat; only the proc applies.";
}

// ----------------------------------------------------------------
// Slice 6 — Economy / acquisition
// ----------------------------------------------------------------

public enum BuyFailureCode
{
    DefinitionNotFound,
    NotForSale,
    InsufficientGems,
    AlreadyOwned,
}

public class BuyUnitRequest
{
    public string UnitDefinitionId { get; set; } = string.Empty;
}

public class BuyLegionRequest
{
    public string LegionDefinitionId { get; set; } = string.Empty;
}

public class BuyUnitResult
{
    public bool           Success       { get; set; }
    public BuyFailureCode FailureCode   { get; set; }
    public string?        FailureReason { get; set; }
    public string?        UnitDefinitionId { get; set; }
    public string?        UnitName      { get; set; }
    public int            GemsSpent     { get; set; }
}

public class BuyLegionResult
{
    public bool           Success       { get; set; }
    public BuyFailureCode FailureCode   { get; set; }
    public string?        FailureReason { get; set; }
    public string?        LegionDefinitionId { get; set; }
    public string?        LegionName    { get; set; }
    public int            GemsSpent     { get; set; }
}
