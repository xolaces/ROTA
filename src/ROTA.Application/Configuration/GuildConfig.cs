namespace ROTA.Application.Configuration;

// BETA — guild configuration bound from appsettings "GuildConfig" via IOptions<GuildConfig>.
// All defaults are the LOCKED System 21 Slice 1 spec values (see docs/specs/active/system-21-guild-foundations.md §5).
public class GuildConfig
{
    /// <summary>Maximum members per guild. LOCKED at 50, fixed (decision §5.2). Scaling deferred to Slice 4.</summary>
    public int MemberCap { get; set; } = 50;

    /// <summary>
    /// Gold deducted to found a guild (decision §5.5). 25000 is a TUNABLE BALANCE VALUE — flagged for owner
    /// confirmation. The server guards the balance can never go negative on the spend.
    /// </summary>
    public long CreationGoldCost { get; set; } = 25000;

    /// <summary>Minimum player level required to create a guild (decision §5.5). LOCKED at 20.</summary>
    public int MinCreationLevel { get; set; } = 20;

    /// <summary>
    /// Days of leader inactivity (no membership activity) before succession may auto-promote the most-active
    /// officer (decision §5.6). LOCKED at ~14.
    /// </summary>
    public int LeaderInactivityDays { get; set; } = 14;

    /// <summary>Minimum guild tag length (decision §5.11). LOCKED at 2.</summary>
    public int TagMinLength { get; set; } = 2;

    /// <summary>Maximum guild tag length (decision §5.11). LOCKED at 5.</summary>
    public int TagMaxLength { get; set; } = 5;

    /// <summary>Maximum guild name length (matches the name-input validator). Standard-v1 default.</summary>
    public int NameMaxLength { get; set; } = 32;
}
