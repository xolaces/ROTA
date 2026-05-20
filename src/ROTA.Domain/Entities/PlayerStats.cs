namespace ROTA.Domain.Entities;

/// <summary>
/// Base stats for a player. Stored as raw base values only.
/// Effective stats (base + item bonuses + dragon bonuses + legion bonuses)
/// are computed server-side on demand and never persisted — prevents desync exploits.
/// </summary>
public class PlayerStats
{
    public Guid Id { get; private set; } = Guid.NewGuid();

    public Guid PlayerId { get; private set; }
    public Player Player { get; private set; } = null!;

    public int BaseAttack { get; private set; } = 10;
    public int BaseDefense { get; private set; } = 10;
    public int BaseMaxHealth { get; private set; } = 100;
    public int CurrentHealth { get; private set; } = 100;

    public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;
}