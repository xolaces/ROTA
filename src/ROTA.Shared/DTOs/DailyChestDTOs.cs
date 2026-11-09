namespace ROTA.Shared.DTOs;

// System 29 — the daily chest. Six closed chests; pick one, and all six open.

/// <summary>Today's chest. <see cref="Chests"/> stays null until the pick, so the contents never reach the client early.</summary>
public class DailyChestResponse
{
    /// <summary>The UTC day this chest belongs to, yyyy-MM-dd.</summary>
    public string Day { get; set; } = string.Empty;

    /// <summary>Next UTC midnight, when an unpicked chest expires and a new one is rolled.</summary>
    public DateTimeOffset ResetsAt { get; set; }

    public bool Picked { get; set; }
    public int? PickedIndex { get; set; }

    /// <summary>All six, in chest order. Null before the pick.</summary>
    public List<DailyChestSlotResponse>? Chests { get; set; }
}

public class DailyChestSlotResponse
{
    public string ItemDefinitionId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }

    /// <summary>ItemRarity name (Grey → Orange); the client colors the chest glow from it.</summary>
    public string Rarity { get; set; } = string.Empty;
}

public class PickDailyChestRequest
{
    public int Index { get; set; }
}

public enum DailyChestFailureCode
{
    None          = 0,
    InvalidIndex  = 1,
    AlreadyPicked = 2,
}

public class PickDailyChestResponse
{
    public bool Success { get; set; }
    public DailyChestFailureCode FailureCode { get; set; }
    public string? FailureReason { get; set; }

    /// <summary>The full reveal after a successful pick; today's state after a failed one.</summary>
    public DailyChestResponse? Chest { get; set; }

    /// <summary>What the player received. Null on failure.</summary>
    public DailyChestSlotResponse? Granted { get; set; }
}
