namespace ROTA.Shared.DTOs;

public class AllocateStatRequest
{
    public string StatType { get; set; } = string.Empty;
    public int Amount { get; set; } = 1;
}

public class AllocateStatResponse
{
    public bool Success { get; set; }
    public string? FailureReason { get; set; }

    // Fields populated only on success
    public string StatType { get; set; } = string.Empty;
    public int AmountAllocated { get; set; }
    public int NewSkillPointsRemaining { get; set; }
    public int NewEnergyInvestment { get; set; }
    public int NewStaminaInvestment { get; set; }
    public int NewDiscernmentInvestment { get; set; }
    public int NewMaxEnergy { get; set; }
    public int NewMaxStamina { get; set; }
    public int NewMaxGuildStamina { get; set; }
    public decimal CurrentLsi { get; set; }
}

public class PlayerStatsResponse
{
    public int SkillPoints { get; set; }
    public int EnergyInvestment { get; set; }
    public int StaminaInvestment { get; set; }
    public int DiscernmentInvestment { get; set; }
    public decimal CurrentLsi { get; set; }
    public int MaxEnergy { get; set; }
    public int MaxStamina { get; set; }
    public int MaxGuildStamina { get; set; }
    public int BaseAttack { get; set; }
    public int BaseDefense { get; set; }
    public int BaseMaxHealth { get; set; }
    public int CurrentHealth { get; set; }
    public int EffectiveAttack  { get; set; }
    public int EffectiveDefense { get; set; }
}

/// <summary>Crit chance and multiplier computed from a player's Discernment investment.</summary>
public readonly record struct CritProfile(double Chance, double Multiplier);
