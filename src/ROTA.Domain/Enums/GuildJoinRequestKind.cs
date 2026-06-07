namespace ROTA.Domain.Enums;

/// <summary>
/// Direction of a pending guild join request (System 21 Slice 1).
/// <list type="bullet">
/// <item><see cref="Application"/> — player → guild (an officer+ accepts).</item>
/// <item><see cref="Invite"/> — officer+ → player (the invited player accepts).</item>
/// </list>
/// </summary>
public enum GuildJoinRequestKind
{
    Application = 0,
    Invite = 1,
}
