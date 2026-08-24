namespace ROTA.Shared.DTOs;

// D-008 / D-013 — gem-priced instant refills. The premium tier of the escape valve: gold buys potions
// (items), gems buy an immediate full pool (a service, no inventory row).

/// <summary>One refillable pool, priced and hydrated with the caller's current state.</summary>
public class RefillOptionResponse
{
    /// <summary>ResourceType name — Energy / Stamina / Health.</summary>
    public string ResourceType { get; set; } = string.Empty;
    public int GemCost { get; set; }
    public int CurrentValue { get; set; }
    public int MaxValue { get; set; }

    /// <summary>False when the pool is already full — refilling would burn gems for nothing.</summary>
    public bool CanRefill { get; set; }
    public bool CanAfford { get; set; }
}

public class RefillOptionsResponse
{
    public List<RefillOptionResponse> Options { get; set; } = new();
    public long PlayerGems { get; set; }
}

public class RefillResourceResponse
{
    public bool Success { get; set; }
    public RefillFailureCode FailureCode { get; set; }
    public string? FailureReason { get; set; }

    public string ResourceType { get; set; } = string.Empty;
    public int GemsSpent { get; set; }
    public int AmountRestored { get; set; }
    public int NewValue { get; set; }
    public int MaxValue { get; set; }

    /// <summary>Balance after the spend, so the header updates without a refetch.</summary>
    public long NewGemBalance { get; set; }
}

public enum RefillFailureCode
{
    None              = 0,
    /// <summary>The resource name did not parse, or the player has no such pool.</summary>
    ResourceNotFound  = 1,
    /// <summary>Not priced for gem refill — that is how GuildStamina stays out of the premium path.</summary>
    NotRefillable     = 2,
    /// <summary>Already at maximum; refusing rather than charging for nothing.</summary>
    AlreadyFull       = 3,
    InsufficientGems  = 4,
}
