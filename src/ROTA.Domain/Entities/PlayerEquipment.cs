namespace ROTA.Domain.Entities;

using ROTA.Domain.Enums;

public class PlayerEquipment
{
    private PlayerEquipment() { }

    public static PlayerEquipment Create(Guid playerId, EquipmentSlot slot, string gearDefinitionId)
        => new()
        {
            Id               = Guid.NewGuid(),
            PlayerId         = playerId,
            Slot             = slot,
            GearDefinitionId = gearDefinitionId,
            EquippedAt       = DateTimeOffset.UtcNow,
            CreatedAt        = DateTimeOffset.UtcNow,
            UpdatedAt        = DateTimeOffset.UtcNow,
            IsDeleted        = false,
        };

    public Guid            Id               { get; private set; }
    public Guid            PlayerId         { get; private set; }
    public EquipmentSlot   Slot             { get; private set; }
    public string          GearDefinitionId { get; private set; } = string.Empty;
    public DateTimeOffset  EquippedAt       { get; private set; }
    public DateTimeOffset  CreatedAt        { get; private set; }
    public DateTimeOffset  UpdatedAt        { get; private set; }
    public bool            IsDeleted        { get; private set; }

    // Swap or restore gear in this slot.
    public void Equip(string gearDefinitionId)
    {
        GearDefinitionId = gearDefinitionId;
        IsDeleted        = false;
        EquippedAt       = DateTimeOffset.UtcNow;
        UpdatedAt        = DateTimeOffset.UtcNow;
    }

    public void Unequip()
    {
        IsDeleted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
