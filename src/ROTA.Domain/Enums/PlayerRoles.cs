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

    /// <summary>
    /// Developer flag (T43) — marks an internal developer account. Granted from the "Developer" config
    /// allowlist at startup (or via the CLI). Stored as a plain bit in the existing int Roles column, so
    /// no schema change is required. Devs are confined to the hidden Dev guild ("The Dev Coffee Shop"):
    /// they may only ever belong to that guild, and the guild is hidden from non-dev browse/detail.
    /// </summary>
    Developer = 1 << 3,
}
