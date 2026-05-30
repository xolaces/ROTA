namespace ROTA.Domain.Enums;

/// <summary>
/// Bitwise role flags stored as a single int column on the Player entity.
/// Roles are additive — a player may hold multiple roles simultaneously.
/// </summary>
[Flags]
public enum PlayerRoles
{
    /// <summary>No roles assigned.</summary>
    None = 0,

    /// <summary>Standard authenticated player. Assigned at registration; never removed.</summary>
    Player = 1 << 0,

    /// <summary>Moderator role — community management capabilities.</summary>
    Moderator = 1 << 1,

    /// <summary>Administrator role — full server access.</summary>
    Admin = 1 << 2,
}
