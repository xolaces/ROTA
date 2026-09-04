namespace ROTA.Domain.Entities;

public class PlayerStats
{
    private PlayerStats() { }

    public static PlayerStats Create(Guid playerId)
    {
        return new PlayerStats
        {
            Id = Guid.NewGuid(),
            PlayerId = playerId,
            BaseAttack = 10,
            BaseDefense = 10,
            BaseMaxHealth = 100,
            CurrentHealth = 100,
            EnergyInvestment = 0,
            StaminaInvestment = 0,
            DiscernmentInvestment = 0,
            SkillPoints = 0,
            UpdatedAt = DateTimeOffset.UtcNow,
        };
    }

    public Guid Id { get; private set; }
    public Guid PlayerId { get; private set; }
    public Player Player { get; private set; } = null!;

    // int32-overflow-audit Unit 2 (owner-locked, no caps): uncapped stat/investment growth over a
    // no-reset capped-scaling lifetime can lap int32 (e.g. ~250k SP by L25000, uncapped ATK growth).
    // bigint columns. BaseMaxHealth/CurrentHealth stay int (bounded by the health pool, not economy).
    public long BaseAttack { get; private set; }
    public long BaseDefense { get; private set; }
    public int BaseMaxHealth { get; private set; }
    public int CurrentHealth { get; private set; }

    // Investable resource pools (from SkillPoints)
    public long EnergyInvestment { get; private set; }
    public long StaminaInvestment { get; private set; }
    // P2: effects deferred
    public long DiscernmentInvestment { get; private set; }
    public long SkillPoints { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    // Computed resource caps. OWNER DECISION (audit fix, 2026-06-09): bases rebased from 10/10 to
    // match the seeded starting pools (Energy 25 / Stamina 5, the playtested DotD-heritage feel).
    // The old 10+investment formula CONTRADICTED the seeds: a fresh player's first Energy allocation
    // clamped 25→11, destroying 14 max + 14 current energy. Allocation now always strictly grows pools.
    public const int BaseMaxEnergy  = 25;
    public const int BaseMaxStamina = 5;

    public long ComputeMaxEnergy() => BaseMaxEnergy + EnergyInvestment;
    public long ComputeMaxStamina() => BaseMaxStamina + StaminaInvestment;

    // LSI = (EnergyInvestment + StaminaInvestment x 2) / Level -- cap server-enforced in StatService.LsiCap (currently 7.45)
    public double ComputeLSI(int level) =>
        level > 0 ? (EnergyInvestment + StaminaInvestment * 2.0) / level : 0;

    public void AddSkillPoints(long amount)
    {
        SkillPoints += amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    // Restore CurrentHealth to BaseMaxHealth. Used on level-up (full resource refill).
    // Health does not deplete in current combat (PHASE-2) — forward-compatible no-op today.
    public void RestoreFullHealth()
    {
        CurrentHealth = BaseMaxHealth;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    // skillPointCost is the TOTAL skill points to debit, which is not always the number of stat
    // points gained -- stamina costs 2 SP per point. Passing it explicitly keeps the price in one
    // place (LevelingConfig.SkillPointCost) instead of letting the entity assume 1:1.
    public void AllocateToEnergy(int amount, int skillPointCost)
    {
        EnergyInvestment += amount;
        SkillPoints -= skillPointCost;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AllocateToStamina(int amount, int skillPointCost)
    {
        StaminaInvestment += amount;
        SkillPoints -= skillPointCost;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AllocateToDiscernment(int amount, int skillPointCost)
    {
        DiscernmentInvestment += amount;
        SkillPoints -= skillPointCost;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AllocateToAttack(int amount, int skillPointCost)
    {
        BaseAttack += amount;
        SkillPoints -= skillPointCost;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AllocateToDefense(int amount, int skillPointCost)
    {
        BaseDefense += amount;
        SkillPoints -= skillPointCost;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AllocateToHealth(int amount, int skillPointCost)
    {
        BaseMaxHealth += amount;
        SkillPoints -= skillPointCost;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
