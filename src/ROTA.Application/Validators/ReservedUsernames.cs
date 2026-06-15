namespace ROTA.Application.Validators;

/// <summary>
/// Staff / system handles that public registration and self-service rename may NOT use.
/// These names are reserved so player-facing identity can't impersonate dev, moderator,
/// or system accounts. Staff accounts are created only via the admin CLI / direct seeding,
/// which bypass these input validators — so "only the owner, directly" stays true.
///
/// NOTE (world-chat, future): mod display names render orange with a trailing '*'
/// (e.g. "BlueZBear*"); dev display names render red in the "DEV_xxxx" form. Those are
/// presentation-layer markers applied server-side from <c>PlayerRoles</c> — they are NOT
/// part of the stored username, and '*' is already excluded by the username charset rule.
/// </summary>
public static class ReservedUsernames
{
    // Exact matches (case-insensitive) reserved for staff / system / the owner.
    private static readonly HashSet<string> ReservedExact = new(StringComparer.OrdinalIgnoreCase)
    {
        "admin", "administrator", "mod", "moderator", "staff",
        "dev", "developer", "gm", "gamemaster", "owner",
        "official", "support", "system", "rota", "ancient",
    };

    // Prefixes (case-insensitive) reserved for staff / system handles.
    // "dev_" is the locked form for developer accounts (DEV_xxxx).
    private static readonly string[] ReservedPrefixes =
    {
        "dev_", "mod_", "admin_", "staff_", "gm_",
        "rota_", "official_", "system_", "support_",
    };

    /// <summary>True when <paramref name="username"/> is reserved and must be rejected for public use.</summary>
    public static bool IsReserved(string? username)
    {
        if (string.IsNullOrWhiteSpace(username)) return false;

        var u = username.Trim();
        if (ReservedExact.Contains(u)) return true;

        foreach (var prefix in ReservedPrefixes)
        {
            if (u.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return true;
        }

        return false;
    }
}
