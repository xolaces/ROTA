namespace ROTA.Domain.Entities;

public class PlayerLegion
{
    private PlayerLegion() { }

    public static PlayerLegion Create(Guid playerId, string legionDefinitionId)
        => new()
        {
            Id                 = Guid.NewGuid(),
            PlayerId           = playerId,
            LegionDefinitionId = legionDefinitionId,
            IsActive           = false,
            CreatedAt          = DateTimeOffset.UtcNow,
            UpdatedAt          = DateTimeOffset.UtcNow,
            IsDeleted          = false,
        };

    public Guid           Id                 { get; private set; }
    public Guid           PlayerId           { get; private set; }
    public string         LegionDefinitionId { get; private set; } = string.Empty;
    // Exactly one IsActive=true per player — enforced by service layer.
    public bool           IsActive           { get; private set; }
    public DateTimeOffset CreatedAt          { get; private set; }
    public DateTimeOffset UpdatedAt          { get; private set; }
    public bool           IsDeleted          { get; private set; }

    public void SetActive(bool active)
    {
        IsActive  = active;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void Restore()
    {
        IsDeleted = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    // System 26 (D-018) — crafting consumes a legion to produce its tier-II definition. Soft delete so
    // the row (and Restore's re-acquire path) still work exactly as they did before.
    public void SoftDelete()
    {
        IsDeleted = true;
        IsActive  = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
