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

    // Acquisition stacks onto the existing row. Equipping never consumes gear —
    // ownership is permanent — so only crafting (System 26, D-018) takes any away.
    public void AddQuantity(int amount)
    {
        Quantity += amount;
        // Re-acquiring gear that was consumed down to nothing must bring the row back: the grant path
        // upserts through GetAsync, which returns soft-deleted rows, so without this the player would
        // hold a positive quantity on a row GetOwnedAsync still filters out.
        IsDeleted = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>
    /// Consumes <paramref name="amount"/> copies. Throws rather than clamping: the caller has already
    /// re-checked the balance under the player mutation lock, so a short stack here means a bug that
    /// must not be papered over by silently crafting from gear the player does not have.
    /// </summary>
    public void ConsumeQuantity(int amount)
    {
        if (amount <= 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Gear consumption must be positive.");
        if (amount > Quantity)
            throw new InvalidOperationException(
                $"Cannot consume {amount}x {GearDefinitionId}: only {Quantity} held.");

        Quantity -= amount;
        if (Quantity == 0) IsDeleted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
