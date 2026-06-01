namespace ROTA.Domain.Entities;

public class PlayerUnit
{
    private PlayerUnit() { }

    public static PlayerUnit Create(Guid playerId, string unitDefinitionId)
        => new()
        {
            Id               = Guid.NewGuid(),
            PlayerId         = playerId,
            UnitDefinitionId = unitDefinitionId,
            Quantity         = 1,
            CreatedAt        = DateTimeOffset.UtcNow,
            UpdatedAt        = DateTimeOffset.UtcNow,
            IsDeleted        = false,
        };

    public Guid           Id               { get; private set; }
    public Guid           PlayerId         { get; private set; }
    public string         UnitDefinitionId { get; private set; } = string.Empty;
    public int            Quantity         { get; private set; }  // = 1; reserved for multi-copy future
    public DateTimeOffset CreatedAt        { get; private set; }
    public DateTimeOffset UpdatedAt        { get; private set; }
    public bool           IsDeleted        { get; private set; }

    public void Restore()
    {
        IsDeleted = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
