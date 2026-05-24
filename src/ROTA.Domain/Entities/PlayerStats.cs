namespace ROTA.Domain.Entities;

/// <summary>
/// Base stats for a player. Stored as raw base values only.
/// Effective stats (base + item bonuses + dragon bonuses + legion bonuses)
/// are computed server-side on demand and never persisted — prevents desync exploits.
/// </summary>
public class PlayerStats
{
    // Required by EF Core
    private PlayerStats() { }

    public static PlayerStats Create(Guid playerId)
    {
        return new PlayerStats
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            BaseAttack = 10,
            BaseDefense = 10,
            BaseMaxHealth = 100,
            CurrentHealth = 100,
            UpdatedAt = DateTimeOffset.UtcNow
        };
    }

    public Guid Id { get; private set; }

    public Guid PlayerId { get; private set; }
    public Player Player { get; private set; } = null!;

    public int BaseAttack { get; private set; }
    public int BaseDefense { get; private set; }
    public int BaseMaxHealth { get; private set; }
    public int CurrentHealth { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }
}