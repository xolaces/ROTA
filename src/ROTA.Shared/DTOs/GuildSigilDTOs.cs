namespace ROTA.Shared.DTOs;

// ── Guild sigil economy (System 21 Slice 3a) DTOs ─────────────────────────────

/// <summary>Failure reason for a guild sigil-economy operation. Controllers map to HTTP codes.</summary>
public enum GuildEconomyFailureCode
{
    None = 0,
    NotInGuild = 1,          // 409 — caller is not a member of any guild
    DailyCapReached = 2,     // 409 — daily buy/donate allowance exhausted
    InsufficientTickets = 3, // 409 — not enough shop tickets to buy a sigil
    InsufficientSigils = 4,  // 409 — not enough personal sigils to donate
    Conflict = 5,            // 409 — duplicate/race lost (idempotency backstop)
}

/// <summary>Result of the once-daily guild claim (free sigil + shop-ticket allowance).</summary>
public class GuildClaimResult
{
    public bool Success { get; init; }
    public GuildEconomyFailureCode FailureCode { get; init; }
    public string? FailureReason { get; init; }

    /// <summary>True when nothing new was granted because the player already claimed today.</summary>
    public bool AlreadyClaimed { get; init; }

    public int SigilsGranted { get; init; }
    public int TicketsGranted { get; init; }

    /// <summary>Personal balances after the claim.</summary>
    public long SigilBalance { get; init; }
    public long TicketBalance { get; init; }

    public static GuildClaimResult Ok(bool alreadyClaimed, int sigils, int tickets, long sigilBalance, long ticketBalance)
        => new()
        {
            Success = true,
            AlreadyClaimed = alreadyClaimed,
            SigilsGranted = sigils,
            TicketsGranted = tickets,
            SigilBalance = sigilBalance,
            TicketBalance = ticketBalance,
        };

    public static GuildClaimResult Fail(GuildEconomyFailureCode code, string reason)
        => new() { Success = false, FailureCode = code, FailureReason = reason };
}

/// <summary>Result of buying one guild sigil with shop tickets.</summary>
public class GuildBuyResult
{
    public bool Success { get; init; }
    public GuildEconomyFailureCode FailureCode { get; init; }
    public string? FailureReason { get; init; }

    public long SigilBalance { get; init; }
    public long TicketBalance { get; init; }
    public int BoughtToday { get; init; }
    public int DailyBuyCap { get; init; }

    public static GuildBuyResult Ok(long sigilBalance, long ticketBalance, int boughtToday, int cap)
        => new()
        {
            Success = true,
            SigilBalance = sigilBalance,
            TicketBalance = ticketBalance,
            BoughtToday = boughtToday,
            DailyBuyCap = cap,
        };

    public static GuildBuyResult Fail(GuildEconomyFailureCode code, string reason)
        => new() { Success = false, FailureCode = code, FailureReason = reason };
}

/// <summary>Result of donating one personal sigil to the guild pool.</summary>
public class GuildDonateResult
{
    public bool Success { get; init; }
    public GuildEconomyFailureCode FailureCode { get; init; }
    public string? FailureReason { get; init; }

    public long SigilBalance { get; init; }
    public long PoolBalance { get; init; }
    public int DonatedToday { get; init; }
    public int DailyDonateCap { get; init; }

    public static GuildDonateResult Ok(long sigilBalance, long poolBalance, int donatedToday, int cap)
        => new()
        {
            Success = true,
            SigilBalance = sigilBalance,
            PoolBalance = poolBalance,
            DonatedToday = donatedToday,
            DailyDonateCap = cap,
        };

    public static GuildDonateResult Fail(GuildEconomyFailureCode code, string reason)
        => new() { Success = false, FailureCode = code, FailureReason = reason };
}

/// <summary>Read-model of the caller's guild sigil-economy state.</summary>
public class GuildSigilBalances
{
    /// <summary>True when the caller belongs to a guild (pool + actions require this).</summary>
    public bool InGuild { get; init; }

    public long SigilBalance { get; init; }
    public long TicketBalance { get; init; }

    /// <summary>The caller's guild pool balance (0 when not in a guild).</summary>
    public long PoolBalance { get; init; }

    public bool ClaimedToday { get; init; }
    public int BoughtToday { get; init; }
    public int DonatedToday { get; init; }

    public int DailyBuyCap { get; init; }
    public int DailyDonateCap { get; init; }
}
