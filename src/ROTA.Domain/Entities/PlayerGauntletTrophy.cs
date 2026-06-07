namespace ROTA.Domain.Entities;

// BETA (System 16 Slice 2) — permanent ownership of a Gauntlet trophy. Mirrors PlayerMagic.
// Highest-only stacking is applied in combat (Slice 4); this row only records ownership.
// Unique on (player_id, gauntlet_trophy_id).
public class PlayerGauntletTrophy
{
    // Required by EF Core
    private PlayerGauntletTrophy() { }

    public static PlayerGauntletTrophy Create(Guid playerId, string gauntletTrophyId)
        => new PlayerGauntletTrophy
        {
            Id               = Guid.NewGuid(),
            PlayerId         = playerId,
            GauntletTrophyId = gauntletTrophyId,
            CreatedAt        = DateTimeOffset.UtcNow,
            UpdatedAt        = DateTimeOffset.UtcNow,
            IsDeleted        = false,
        };

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public string GauntletTrophyId { get; private set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    /// <summary>Un-deletes a previously soft-deleted ownership row (idempotent re-grant).</summary>
    public void Restore()
    {
        IsDeleted = false;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
