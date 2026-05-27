using ROTA.Domain.Enums;

namespace ROTA.Domain.Entities;

/// <summary>
/// Generic player resource pool. One row per resource type per player.
/// Adding a new resource type requires only a new ResourceType enum value ï¿½
/// no schema changes, no new entities, no new service methods.
///
/// SECURITY: CurrentValue is a server-side checkpoint only.
/// Always call ResourceService.ComputeCurrent() to get the real value ï¿½
/// regen is calculated from LastRegenAt, never from client-reported values.
/// </summary>
public class PlayerResource
{
    /// <summary>Factory used during player creation to seed default resource pools.</summary>
    public static PlayerResource Create(
        Guid playerId,
        ResourceType type,
        int maxValue,
        int regenPerMinute)
        => new PlayerResource
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            ResourceType = type,
            CurrentValue = maxValue,
            MaxValue = maxValue,
            RegenPerMinute = regenPerMinute,
            LastRegenAt = DateTimeOffset.UtcNow,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    public Guid Id { get; private set; } = Guid.NewGuid();

	public Guid PlayerId { get; private set; }
	public Player Player { get; private set; } = null!;

	/// <summary>
	/// Identifies which resource pool this row represents (Energy, Stamina, etc.)
	/// </summary>
	public ResourceType ResourceType { get; private set; }

	/// <summary>
	/// Last known value ï¿½ checkpoint only. Not the live value.
	/// </summary>
	public int CurrentValue { get; private set; } = 0;

	public int MaxValue { get; private set; } = 0;

	/// <summary>
	/// How many units regenerate per minute. 0 = no regen (e.g. GuildStamina).
	/// </summary>
	public int RegenPerMinute { get; private set; } = 0;

	/// <summary>
	/// Timestamp of last regen checkpoint. Server derives current value from this.
	/// Never written by the client.
	/// </summary>
	public DateTimeOffset LastRegenAt { get; private set; } = DateTimeOffset.UtcNow;

	public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

	/// <summary>
	/// Saves a new regen checkpoint. Called by EnergyService after computing the live value.
	/// This is the only way to update CurrentValue â€” client-reported values are never accepted.
	/// </summary>
	public void SaveCheckpoint(int value, DateTimeOffset now)
	{
		CurrentValue = value;
		LastRegenAt = now;
		UpdatedAt = now;
	}

    public void SetMaxValue(int newMax, DateTimeOffset now)
    {
        if (CurrentValue > newMax)
            CurrentValue = newMax;
        MaxValue = newMax;
        UpdatedAt = now;
    }
}
