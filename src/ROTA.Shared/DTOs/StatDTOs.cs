namespace ROTA.Shared.DTOs;

public class AllocateStatRequest
{
    public string StatType { get; set; } = string.Empty;
    public int Amount { get; set; } = 1;
}

/// <summary>
/// Several stats in one request, applied all together or not at all. The profile's allocation
/// screen sends its whole pending set this way, so a refusal can never leave half of it spent.
/// </summary>
public class AllocateStatsRequest
{
    public List<AllocateStatRequest> Allocations { get; set; } = new();
}

public class AllocateStatResponse
{
    public bool Success { get; set; }
    public string? FailureReason { get; set; }

    // Fields populated only on success
    // int32-overflow-audit Unit 2 — stat/investment/max amounts widened to long (uncapped growth).
    public string StatType { get; set; } = string.Empty;
    public long AmountAllocated { get; set; }
    public long NewSkillPointsRemaining { get; set; }
    public long NewEnergyInvestment { get; set; }
    public long NewStaminaInvestment { get; set; }
    public long NewDiscernmentInvestment { get; set; }
    public long NewMaxEnergy { get; set; }
    public long NewMaxStamina { get; set; }
    public long NewMaxGuildStamina { get; set; }
    public decimal CurrentLsi { get; set; }

    // atk-def-chip-stale-on-alloc — gear-inclusive effective ATK/DEF returned on a successful allocate so
    // the client can patch its stat chips inline (no dependency on a follow-up profile round-trip).
    public long EffectiveAttack { get; set; }
    public long EffectiveDefense { get; set; }
}

public class PlayerStatsResponse
{
    // int32-overflow-audit Unit 2 — stat/investment/effective fields widened to long (uncapped growth);
    // MaxEnergy/MaxStamina follow PlayerStats.ComputeMax* (now long). MaxGuildStamina = Player.Level (int).
    public long SkillPoints { get; set; }
    public long EnergyInvestment { get; set; }
    public long StaminaInvestment { get; set; }
    public long DiscernmentInvestment { get; set; }
    public decimal CurrentLsi { get; set; }
    /// <summary>The LSI ceiling the server enforces: (Energy + Stamina x StaminaLsiWeight) / Level may not
    /// exceed it. Sent so the client never hardcodes it (it once showed 9.0 against a real 7.45).</summary>
    public decimal LsiCap { get; set; }
    /// <summary>How much one Stamina point counts toward LSI, against Energy's one.</summary>
    public decimal StaminaLsiWeight { get; set; }
    /// <summary>Skill points charged per point, for the stats that do not cost 1 (Stamina costs 2).</summary>
    public Dictionary<string, int> SkillPointCosts { get; set; } = new();
    public long MaxEnergy { get; set; }
    public long MaxStamina { get; set; }
    public int MaxGuildStamina { get; set; }
    public long BaseAttack { get; set; }
    public long BaseDefense { get; set; }
    public long BaseMaxHealth { get; set; }
    public long CurrentHealth { get; set; }
    public long EffectiveAttack  { get; set; }
    public long EffectiveDefense { get; set; }
}

/// <summary>Crit chance and multiplier computed from a player's Discernment investment.</summary>
public readonly record struct CritProfile(double Chance, double Multiplier);
