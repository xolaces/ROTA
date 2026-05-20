namespace ROTA.Domain.Enums;

/// <summary>
/// Defines all player resource pool types.
/// Adding a new resource type requires only a new enum value —
/// no schema changes, no new entities.
/// </summary>
public enum ResourceType
{
    Energy = 1,
    Stamina = 2,
    GuildStamina = 3
    // Future resource types added here — PHASE-2 or beyond
}