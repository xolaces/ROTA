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
}

public class ResourceValueResponse
{
    public string Type { get; set; } = string.Empty;
    public int LiveValue { get; set; }
    public int MaxValue { get; set; }
    public int RegenPerMinute { get; set; }
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
