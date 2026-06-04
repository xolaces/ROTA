namespace ROTA.Domain.Entities;

public class PlayerGear
{
    private PlayerGear() { }

    public static PlayerGear Create(Guid playerId, string gearDefinitionId, int quantity = 1)
        => new()
        {
            Id               = Guid.NewGuid(),
            PlayerId         = playerId,
            GearDefinitionId = gearDefinitionId,
            Quantity         = quantity,
            CreatedAt        = DateTimeOffset.UtcNow,
            UpdatedAt        = DateTimeOffset.UtcNow,
            IsDeleted        = false,
        };

    public Guid           Id               { get; private set; }
    public Guid           PlayerId         { get; private set; }
    public string         GearDefinitionId { get; private set; } = string.Empty;
    public int            Quantity         { get; private set; }
    public DateTimeOffset CreatedAt        { get; private set; }
    public DateTimeOffset UpdatedAt        { get; private set; }
    public bool           IsDeleted        { get; private set; }

    // Acquisition stacks onto the existing row. Gear is never consumed by
    // equip/unequip — ownership is permanent — so there is no Consume here.
    public void AddQuantity(int amount)
    {
        Quantity += amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
