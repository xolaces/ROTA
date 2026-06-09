namespace ROTA.Domain.Enums;

public enum ResourceType
{
    Energy = 1,
    Stamina = 2,
    GuildStamina = 3,
    // T56 — Health is a regenerating pool consumed in raids (flat per difficulty) and the Gauntlet
    // (Defense-scaled mob damage). Its MaxValue mirrors PlayerStats.BaseMaxHealth.
    Health = 4
    // Future resource types added here — PHASE-2 or beyond
}