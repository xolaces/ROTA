namespace ROTA.Domain.Entities;

// BETA
/// <summary>
/// Records which gear item occupies the commander slot of the player's active legion.
/// One row per player (soft-delete in place to keep history). In combat only the
/// gear's proc fires — BonusAttack / BonusDefense are deliberately ignored.
/// </summary>
public sealed class PlayerCommanderGear
{
    private PlayerCommanderGear() { }

    public Guid            Id               { get; private set; }
    public Guid            PlayerId         { get; private set; }
    public string          GearDefinitionId { get; private set; } = string.Empty;
    public DateTimeOffset  CreatedAt        { get; private set; }
    public DateTimeOffset  UpdatedAt        { get; private set; }
    public bool            IsDeleted        { get; private set; }

    public static PlayerCommanderGear Create(Guid playerId, string gearDefinitionId)
        => new()
        {
            Id               = Guid.NewGuid(),
            PlayerId         = playerId,
            GearDefinitionId = gearDefinitionId,
            CreatedAt        = DateTimeOffset.UtcNow,
            UpdatedAt        = DateTimeOffset.UtcNow,
            IsDeleted        = false,
        };

    /// <summary>Equip (or re-equip) the gear in this commander slot.</summary>
    public void Equip(string gearDefinitionId)
    {
        GearDefinitionId = gearDefinitionId;
        IsDeleted        = false;
        UpdatedAt        = DateTimeOffset.UtcNow;
    }

    /// <summary>Remove the commander gear (soft-delete).</summary>
    public void Unequip()
    {
        IsDeleted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
