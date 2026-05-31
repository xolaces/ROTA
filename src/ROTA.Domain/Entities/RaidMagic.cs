namespace ROTA.Domain.Entities;

public class RaidMagic
{
    private RaidMagic() { }

    public static RaidMagic Create(Guid activeRaidId, string magicDefinitionId, Guid appliedByPlayerId)
        => new()
        {
            Id                = Guid.NewGuid(),
            ActiveRaidId      = activeRaidId,
            MagicDefinitionId = magicDefinitionId,
            AppliedByPlayerId = appliedByPlayerId,
            CreatedAt         = DateTimeOffset.UtcNow,
            UpdatedAt         = DateTimeOffset.UtcNow,
            IsDeleted         = false,
        };

    public Guid           Id                { get; private set; }
    public Guid           ActiveRaidId      { get; private set; }
    public string         MagicDefinitionId { get; private set; } = string.Empty;
    public Guid           AppliedByPlayerId { get; private set; }
    public DateTimeOffset CreatedAt         { get; private set; }
    public DateTimeOffset UpdatedAt         { get; private set; }
    public bool           IsDeleted         { get; private set; }

    public void SoftDelete()
    {
        IsDeleted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
