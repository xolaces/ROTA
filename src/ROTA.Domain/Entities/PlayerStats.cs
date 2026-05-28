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

    public int BaseAttack { get; private set; }
    public int BaseDefense { get; private set; }
    public int BaseMaxHealth { get; private set; }
    public int CurrentHealth { get; private set; }

    // Investable resource pools (from SkillPoints)
    public int EnergyInvestment { get; private set; }
    public int StaminaInvestment { get; private set; }
    // P2: effects deferred
    public int DiscernmentInvestment { get; private set; }
    public int SkillPoints { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    // Computed resource caps
    public int ComputeMaxEnergy() => 10 + EnergyInvestment;
    public int ComputeMaxStamina() => 10 + StaminaInvestment;

    // LSI = (EnergyInvestment + StaminaInvestment x 2) / Level -- cap is 9.0 (server-enforced)
    public double ComputeLSI(int level) =>
        level > 0 ? (EnergyInvestment + StaminaInvestment * 2.0) / level : 0;

    public void AddSkillPoints(int amount)
    {
        SkillPoints += amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AllocateToEnergy(int amount)
    {
        EnergyInvestment += amount;
        SkillPoints -= amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AllocateToStamina(int amount)
    {
        StaminaInvestment += amount;
        SkillPoints -= amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AllocateToDiscernment(int amount)
    {
        DiscernmentInvestment += amount;
        SkillPoints -= amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AllocateToAttack(int amount)
    {
        BaseAttack += amount;
        SkillPoints -= amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AllocateToDefense(int amount)
    {
        BaseDefense += amount;
        SkillPoints -= amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void AllocateToHealth(int amount)
    {
        BaseMaxHealth += amount;
        SkillPoints -= amount;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
