namespace ROTA.Domain.Entities;

// BETA (System 16 Slice 2) — per-event consumable ownership of a rank magic (Wrath/Blessing).
// Granted at settlement scoped to the NEXT event; soft-deleted (Revoke) at event close. The
// former-owner "honor echo" (PlayerMagicHonor) is written when this is revoked (Slice 5).
// Unique on (player_id, gauntlet_event_id, magic_definition_id).
public class PlayerEventMagic
{
    // Required by EF Core
    private PlayerEventMagic() { }

    public static PlayerEventMagic Create(Guid playerId, Guid gauntletEventId, string magicDefinitionId)
        => new PlayerEventMagic
        {
            Id                = Guid.NewGuid(),
            PlayerId          = playerId,
            GauntletEventId   = gauntletEventId,
            MagicDefinitionId = magicDefinitionId,
            CreatedAt         = DateTimeOffset.UtcNow,
            UpdatedAt         = DateTimeOffset.UtcNow,
            IsDeleted         = false,
        };

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public Guid GauntletEventId { get; private set; }
    public string MagicDefinitionId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    /// <summary>Soft-deletes the per-event grant (the consumable expires at event close).</summary>
    public void Revoke()
    {
        IsDeleted = true;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    /// <summary>Un-deletes a previously revoked grant (idempotent re-grant).</summary>
    public void Restore()
    {
        IsDeleted = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
