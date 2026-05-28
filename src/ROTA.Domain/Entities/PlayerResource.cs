using ROTA.Domain.Enums;

namespace ROTA.Domain.Entities;

public class PlayerResource
{
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

	public ResourceType ResourceType { get; private set; }

	public int CurrentValue { get; private set; } = 0;

	public int MaxValue { get; private set; } = 0;

	public int RegenPerMinute { get; private set; } = 0;

	public DateTimeOffset LastRegenAt { get; private set; } = DateTimeOffset.UtcNow;

	public DateTimeOffset UpdatedAt { get; private set; } = DateTimeOffset.UtcNow;

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
