namespace ROTA.Shared.DTOs;

public class EquippedItemResponse
{
    public string          Slot             { get; set; } = string.Empty;
    public string          GearDefinitionId { get; set; } = string.Empty;
    public string          Name             { get; set; } = string.Empty;
    public string          Description      { get; set; } = string.Empty;
    public string          Rarity           { get; set; } = string.Empty;
    public int             BonusAttack      { get; set; }
    public int             BonusDefense     { get; set; }
    public double?         ProcChance       { get; set; }
    public double?         ProcPercent      { get; set; }
    public string          IconPath         { get; set; } = string.Empty;
    public DateTimeOffset  EquippedAt       { get; set; }
}

public class EquipRequest
{
    public string GearDefinitionId { get; set; } = string.Empty;
}

public class EquipResult
{
    public bool                  Success       { get; set; }
    public string?               FailureReason { get; set; }
    public EquippedItemResponse? Item          { get; set; }
}

public class UnequipResult
{
    public bool    Success       { get; set; }
    public string? FailureReason { get; set; }
}

// One owned gear stack, hydrated with its definition.
// Available = Owned − Equipped (reserve-on-equip; ownership is permanent).
public class OwnedGearResponse
{
    public string  GearDefinitionId  { get; set; } = string.Empty;
    public string  Name              { get; set; } = string.Empty;
    public string  Description       { get; set; } = string.Empty;
    public string  Rarity            { get; set; } = string.Empty;
    public string  Slot              { get; set; } = string.Empty;
    public int     BonusAttack       { get; set; }
    public int     BonusDefense      { get; set; }
    public double? ProcChance        { get; set; }
    public double? ProcPercent       { get; set; }
    public string  IconPath          { get; set; } = string.Empty;
    public int     OwnedQuantity     { get; set; }
    public int     EquippedQuantity  { get; set; }
    public int     AvailableQuantity { get; set; }
}
