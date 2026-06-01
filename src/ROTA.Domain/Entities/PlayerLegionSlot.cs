using ROTA.Domain.Enums;

namespace ROTA.Domain.Entities;

public class PlayerLegionSlot
{
    private PlayerLegionSlot() { }

    public static PlayerLegionSlot Create(
        Guid playerId, string legionDefinitionId, LegionSlotFamily slotFamily, int slotIndex, string unitDefinitionId)
        => new()
        {
            Id                 = Guid.NewGuid(),
            PlayerId           = playerId,
            LegionDefinitionId = legionDefinitionId,
            SlotFamily         = slotFamily,
            SlotIndex          = slotIndex,
            UnitDefinitionId   = unitDefinitionId,
            CreatedAt          = DateTimeOffset.UtcNow,
            UpdatedAt          = DateTimeOffset.UtcNow,
            IsDeleted          = false,
        };

    public Guid             Id                 { get; private set; }
    public Guid             PlayerId           { get; private set; }
    public string           LegionDefinitionId { get; private set; } = string.Empty;
    public LegionSlotFamily SlotFamily         { get; private set; }
    public int              SlotIndex          { get; private set; }
    public string           UnitDefinitionId   { get; private set; } = string.Empty;
    public DateTimeOffset   CreatedAt          { get; private set; }
    public DateTimeOffset   UpdatedAt          { get; private set; }
    public bool             IsDeleted          { get; private set; }

    public void Reassign(string newUnitDefinitionId)
    {
        UnitDefinitionId = newUnitDefinitionId;
        UpdatedAt        = DateTimeOffset.UtcNow;
    }

    public void SoftDelete()
    {
        IsDeleted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
