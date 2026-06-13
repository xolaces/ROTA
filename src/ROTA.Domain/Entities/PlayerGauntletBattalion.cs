namespace ROTA.Domain.Entities;

// System 24 (D8) — a player's dedicated Gauntlet battalion: a loadout of up to 6 generals + 20
// troops assembled from ANY owned units (no race restriction). The slot lists are stored as two
// JSON unit-definition-id arrays; the service validates ownership + UnitType + slot caps and
// serialises them here. Battalion power (the Gauntlet strike basis) is computed in the service
// from these units' base stats + the player's effective stats — never stored.
public class PlayerGauntletBattalion
{
    // Required by EF Core
    private PlayerGauntletBattalion() { }

    /// <summary>Creates an empty battalion for a player.</summary>
    public static PlayerGauntletBattalion Create(Guid playerId)
        => new PlayerGauntletBattalion
        {
            Id           = Guid.NewGuid(),
            PlayerId     = playerId,
            GeneralsJson = "[]",
            TroopsJson   = "[]",
            CreatedAt    = DateTimeOffset.UtcNow,
            UpdatedAt    = DateTimeOffset.UtcNow,
            IsDeleted    = false,
        };

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }

    /// <summary>JSON array of general unit-definition ids (≤ 6). Validated + serialised by the service.</summary>
    public string GeneralsJson { get; private set; } = "[]";

    /// <summary>JSON array of troop unit-definition ids (≤ 20). Validated + serialised by the service.</summary>
    public string TroopsJson { get; private set; } = "[]";

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    /// <summary>Replaces the loadout with pre-validated, pre-serialised JSON id arrays.</summary>
    public void SetLoadout(string generalsJson, string troopsJson)
    {
        GeneralsJson = generalsJson;
        TroopsJson   = troopsJson;
        UpdatedAt    = DateTimeOffset.UtcNow;
    }
}
