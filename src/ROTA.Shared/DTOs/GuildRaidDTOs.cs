namespace ROTA.Shared.DTOs;

// ── Guild raids (System 21 Slice 3b) DTOs ─────────────────────────────────────

/// <summary>Failure reason for an officer-gated guild-raid summon. Controllers map to HTTP codes.</summary>
public enum GuildRaidFailureCode
{
    None = 0,
    NotInGuild = 1,          // 409 — caller is not a member of a guild
    PermissionDenied = 2,    // 403 — caller is not an officer or the leader
    DefinitionNotFound = 3,  // 404 — no guild boss with that definition id
    InsufficientPool = 4,    // 409 — the guild sigil pool is empty
    Conflict = 5,            // 409 — idempotency/race backstop
}

/// <summary>Result of summoning a guild raid — carries the raid payload + the pool balance after the debit.</summary>
public class SummonGuildRaidResult
{
    public bool Success { get; init; }
    public GuildRaidFailureCode FailureCode { get; init; }
    public string? FailureReason { get; init; }

    /// <summary>The summoned raid (reuses the standard summon payload). Null on failure.</summary>
    public SummonRaidResponse? Response { get; init; }

    /// <summary>The guild sigil pool balance after this summon's 1-sigil debit.</summary>
    public long PoolBalanceAfter { get; init; }

    public static SummonGuildRaidResult Ok(SummonRaidResponse response, long poolBalanceAfter)
        => new() { Success = true, Response = response, PoolBalanceAfter = poolBalanceAfter };

    public static SummonGuildRaidResult Fail(GuildRaidFailureCode code, string reason)
        => new() { Success = false, FailureCode = code, FailureReason = reason };
}

/// <summary>Body for POST /api/guilds/raids/summon.</summary>
public class SummonGuildRaidRequest
{
    /// <summary>The guild boss definition id (from content/guild_raids.json).</summary>
    public string RaidDefinitionId { get; set; } = string.Empty;

    /// <summary>Normal / Hard / Legendary / Nightmare. Defaults to Normal.</summary>
    public string Difficulty { get; set; } = "Normal";
}
