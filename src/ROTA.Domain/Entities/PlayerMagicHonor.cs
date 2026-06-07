namespace ROTA.Domain.Entities;

// BETA (System 16 Slice 2) — permanent "honor echo" record written when a player's
// PlayerEventMagic expires (Slice 5). Read by combat (Slice 4) to apply the ×1.10 former-owner
// proc multiplier indefinitely after the per-event consumable is gone. Unique on
// (player_id, magic_definition_id).
public class PlayerMagicHonor
{
    // Required by EF Core
    private PlayerMagicHonor() { }

    public static PlayerMagicHonor Create(Guid playerId, string magicDefinitionId)
        => new PlayerMagicHonor
        {
            Id                = Guid.NewGuid(),
            PlayerId          = playerId,
            MagicDefinitionId = magicDefinitionId,
            CreatedAt         = DateTimeOffset.UtcNow,
            UpdatedAt         = DateTimeOffset.UtcNow,
            IsDeleted         = false,
        };

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public string MagicDefinitionId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }
}
