namespace ROTA.Domain.Entities;

public class PlayerInventoryItem
{
    private PlayerInventoryItem() { }

    public static PlayerInventoryItem Create(Guid playerId, string itemDefinitionId, int quantity = 1)
        => new()
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            ItemDefinitionId = itemDefinitionId,
            Quantity = quantity,
            AcquiredAt = DateTimeOffset.UtcNow,
            IsUsed = false,
            CreatedAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public string ItemDefinitionId { get; private set; } = string.Empty;
    public int Quantity { get; private set; }
    public DateTimeOffset AcquiredAt { get; private set; }
    /// <summary>
    /// Unused: nothing reads it, and an empty stack is known by Quantity == 0. No longer written
    /// (0.5.0.1); the column is dropped in a later patch, separately, so this API can still be
    /// rolled back onto a database that has it.
    /// </summary>
    public bool IsUsed { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }

    public void AddQuantity(int amount)
    {
        Quantity += amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void ConsumeQuantity(int amount)
    {
        Quantity -= amount;
        if (Quantity < 0) Quantity = 0;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
