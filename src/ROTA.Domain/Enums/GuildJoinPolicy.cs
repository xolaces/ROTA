namespace ROTA.Domain.Enums;

/// <summary>
/// Per-guild join model (System 21 Slice 1, decision §5.3). The leader picks one:
/// <list type="bullet">
/// <item><see cref="Open"/> — anyone may join instantly (auto-accept up to the member cap).</item>
/// <item><see cref="Application"/> — a player applies; an officer+ accepts or rejects.</item>
/// <item><see cref="InviteOnly"/> — players cannot apply; an officer+ must invite them.</item>
/// </list>
/// </summary>
public enum GuildJoinPolicy
{
    Open = 0,
    Application = 1,
    InviteOnly = 2,
}
