namespace ROTA.Domain.Enums;

/// <summary>
/// Who an active raid is indexed for in the public/guild/friends list. This is the INDEXING tier and
/// is DISTINCT from whether a raid is "active" (alive + hittable by id). A raid stays joinable by its
/// GUID (the invite token) regardless of Visibility — Visibility only governs which list it appears in.
/// </summary>
public enum RaidVisibility
{
    /// <summary>Not indexed anywhere. The summoner's own list still surfaces it; others must have the id.</summary>
    Private     = 0,

    /// <summary>Indexed in the public list — every player sees it.</summary>
    Public      = 1,

    /// <summary>Indexed only for members of the summoner's guild.</summary>
    GuildOnly   = 2,

    /// <summary>Indexed only for the summoner's accepted friends.</summary>
    FriendsOnly = 3,
}
