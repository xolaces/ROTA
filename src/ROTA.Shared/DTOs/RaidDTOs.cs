namespace ROTA.Shared.DTOs;

public class ActiveRaidResponse
{
    public Guid ActiveRaidId { get; set; }
    public string RaidDefinitionId { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public long CurrentHp { get; set; }
    public long MaxHp { get; set; }
    public double HpPercent { get; set; }
    public bool IsDefeated { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public long TimerRemainingSeconds { get; set; }
    public string SummonedByUsername { get; set; } = string.Empty;
    public int ParticipantCount { get; set; }
    public long YourTotalDamage { get; set; }
    public int YourHitCount { get; set; }
    public string Tier { get; set; } = string.Empty;
    public string Difficulty { get; set; } = string.Empty;
    public string DifficultyColor { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string YourCurrentTier { get; set; } = string.Empty;
}

public class RaidHitResponse
{
    public bool Success { get; set; }
    public long DamageDealt { get; set; }
    public long CurrentHp { get; set; }
    public long MaxHp { get; set; }
    public double HpPercent { get; set; }
    public bool IsDefeated { get; set; }
    public long YourTotalDamage { get; set; }
    public int YourHitCount { get; set; }
    // ParticipantCount is on ActiveRaidResponse (list screen) — not exposed per-hit.
    public int NewStaminaValue { get; set; }
    public int NewStaminaMax { get; set; }
    public RaidRewards? Rewards { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public string Difficulty { get; set; } = string.Empty;
    public string DifficultyColor { get; set; } = string.Empty;
    public List<ItemGrantDTO>? OnHitDrops { get; set; }
    public string YourCurrentTier { get; set; } = string.Empty;
}

public class RaidRewards
{
    public long GoldGranted { get; set; }
    public int ExperienceGranted { get; set; }
    public int GemsGranted { get; set; }
    public long NewPlayerGold { get; set; }
    public long NewPlayerExperience { get; set; }
    public int? NewPlayerLevel { get; set; }
    public string ContributionTier { get; set; } = string.Empty;
    public decimal TierMultiplier { get; set; }
    public int UnassignedStatPointsGranted { get; set; }
    public int AttackPointsGranted { get; set; }
    public int DefensePointsGranted { get; set; }
    public int DiscernmentPointsGranted { get; set; }
    public List<ItemGrantDTO> ItemsGranted { get; set; } = new();

    // XP progression detail
    public int XpToNextLevel { get; set; }
    public long CurrentLevelXp { get; set; }
    public int LevelsGained { get; set; }
}

public class SummonRaidResponse
{
    public Guid ActiveRaidId { get; set; }
    public string Name { get; set; } = string.Empty;
    public long MaxHp { get; set; }
    public DateTimeOffset ExpiresAt { get; set; }
    public long TimerRemainingSeconds { get; set; }
    public string Difficulty { get; set; } = string.Empty;
    public string DifficultyColor { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
}

public class SummonRaidRequest
{
    public string Difficulty { get; set; } = "Normal";
}

public class RaidHitRequest
{
    public int HitSize { get; set; }
    public string IdempotencyKey { get; set; } = string.Empty;
}

// --- Service result wrappers ---

public class SummonRaidResult
{
    public bool Success { get; set; }
    public SummonRaidFailureCode FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public SummonRaidResponse? Response { get; set; }
}

public class RaidHitResult
{
    public bool Success { get; set; }
    public RaidHitFailureCode FailureCode { get; set; }
    public string? FailureReason { get; set; }
    public RaidHitResponse? Response { get; set; }
}

public enum SummonRaidFailureCode
{
    None                = 0,
    DefinitionNotFound  = 1,
    PlayerNotFound      = 2,
}

public enum RaidHitFailureCode
{
    None                = 0,
    RaidNotFound        = 1,
    RaidExpired         = 2,
    RaidAlreadyDefeated = 3,
    InvalidHitSize      = 4,
    InsufficientStamina = 5,
    AccessDenied        = 6,  // Personal raid — only the summoner may strike
    RaidFull            = 7,  // Participant cap reached for this raid size
}
