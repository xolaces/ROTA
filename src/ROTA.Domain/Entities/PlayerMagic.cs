namespace ROTA.Domain.Entities;

public class PlayerMagic
{
    private PlayerMagic() { }

    public static PlayerMagic Create(Guid playerId, string magicDefinitionId)
        => new()
        {
            Id                = Guid.NewGuid(),
            PlayerId          = playerId,
            MagicDefinitionId = magicDefinitionId,
            Quantity          = 1,
            CreatedAt         = DateTimeOffset.UtcNow,
            UpdatedAt         = DateTimeOffset.UtcNow,
            IsDeleted         = false,
        };

    public Guid           Id                { get; private set; }
    public Guid           PlayerId          { get; private set; }
    public string         MagicDefinitionId { get; private set; } = string.Empty;
    public int            Quantity          { get; private set; }  // reserved for future consumable model
    public DateTimeOffset CreatedAt         { get; private set; }
    public DateTimeOffset UpdatedAt         { get; private set; }
    public bool           IsDeleted         { get; private set; }

    public void Restore()
    {
        IsDeleted = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
