// T75 — admin/dev tooling shapes. Every action is AdminOnly + audited; the dev tools screen in
// the client is the only intended consumer.
namespace ROTA.Shared.DTOs;

public class DevGrantRequest
{
    /// <summary>Target player; null/empty = the calling admin.</summary>
    public Guid? TargetPlayerId { get; set; }
    public long Gold { get; set; }
    public int Gems { get; set; }
    public int SkillPoints { get; set; }
    public long Xp { get; set; }
}

public class DevGrantItemRequest
{
    public Guid? TargetPlayerId { get; set; }
    public string ItemDefinitionId { get; set; } = string.Empty;
    public int Quantity { get; set; } = 1;
}

public class DevRefillRequest
{
    public Guid? TargetPlayerId { get; set; }
}

public class DevActionResponse
{
    public bool Success { get; set; }
    public string Summary { get; set; } = string.Empty;
    /// <summary>Set when an XP grant caused level-ups.</summary>
    public int? NewLevel { get; set; }
}
