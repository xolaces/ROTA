namespace ROTA.Shared.DTOs;

public class PlayerProfileResponse
{
    public Guid Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public int Level { get; set; }
    public long Experience { get; set; }
    public long Gold { get; set; }
    public Guid? GuildId { get; set; }
    public string? GuildRank { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }
    public IReadOnlyList<ResourceValueResponse> Resources { get; set; } = [];
    public int EffectiveAttack  { get; set; }
    public int EffectiveDefense { get; set; }
}

public class ResourceValueResponse
{
    public string Type { get; set; } = string.Empty;
    public int LiveValue { get; set; }
    public int MaxValue { get; set; }
    /// <summary>Legacy stored field — vestigial, do not use for regen timers.</summary>
    public int RegenPerMinute { get; set; }
    /// <summary>
    /// Class-based regen rate: minutes required to regenerate one point.
    /// E.g. Conscript = 5.0 (one Energy/Stamina point every 5 minutes).
    /// Use this for client-side refill countdown timers.
    /// </summary>
    public double RegenMinutesPerPoint { get; set; }
    /// <summary>
    /// Seconds until the next point regenerates. 0 when the resource is already full.
    /// Derived from RegenMinutesPerPoint and the fractional elapsed time at snapshot.
    /// </summary>
    public int SecondsToNextPoint { get; set; }
}

public class UpdateUsernameRequest
{
    public string Username { get; set; } = string.Empty;
}

public class UpdateUsernameResponse
{
    public string Username { get; set; } = string.Empty;
    public DateTimeOffset UpdatedAt { get; set; }
}
