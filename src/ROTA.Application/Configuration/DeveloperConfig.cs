namespace ROTA.Application.Configuration;

/// <summary>
/// Developer allowlist (T43), bound from the appsettings "Developer" section via
/// <c>IOptions&lt;DeveloperConfig&gt;</c>. Accounts listed here are granted the
/// <see cref="ROTA.Domain.Enums.PlayerRoles.Developer"/> flag at startup and auto-joined to the hidden
/// Dev guild ("The Dev Coffee Shop"). EMPTY by default — the owner adds developer identifiers here;
/// no account is ever hardcoded.
/// </summary>
public class DeveloperConfig
{
    /// <summary>Usernames (exact, as stored) to flag as developers. Empty by default.</summary>
    public string[] Usernames { get; set; } = [];

    /// <summary>Player GUIDs (string form) to flag as developers. Empty by default.</summary>
    public string[] PlayerIds { get; set; } = [];
}
