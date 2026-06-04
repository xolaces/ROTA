namespace ROTA.Domain.Entities;

public class RaidParticipant
{
    // Required by EF Core
    private RaidParticipant() { }

    public static RaidParticipant Create(Guid activeRaidId, Guid playerId)
    {
        return new RaidParticipant
        {
            Id              = Guid.NewGuid(),
            ActiveRaidId    = activeRaidId,
            PlayerId        = playerId,
            TotalDamageDealt = 0,
            HitCount        = 0,
            CreatedAt       = DateTimeOffset.UtcNow,
            UpdatedAt       = DateTimeOffset.UtcNow,
            IsDeleted       = false,
        };
    }

    public Guid Id { get; private set; }
    public Guid ActiveRaidId { get; private set; }
    public Guid PlayerId { get; private set; }
    public long TotalDamageDealt { get; private set; }
    public int HitCount { get; private set; }
    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    // Navigation property — populated by repository Include() calls only.
    public ActiveRaid? ActiveRaid { get; private set; }

    // Reward summary — written once at kill, never mutated afterward.
    // BETA: items stored as a JSON blob (application-serialized List<ItemGrantDTO>).
    //       Phase-2 option: normalize to a child table for queryability.
    public string ContributionTier { get; private set; } = string.Empty;
    public long GoldEarned { get; private set; }
    public int XpEarned { get; private set; }
    public int GemsEarned { get; private set; }
    public int StatPointsEarned { get; private set; }
    public string ItemsEarnedJson { get; private set; } = string.Empty;
    public DateTimeOffset? RewardedAt { get; private set; }

    public void RecordHit(long damage)
    {
        TotalDamageDealt += damage;
        HitCount++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    public void RecordRewards(
        string tier,
        long gold,
        int xp,
        int gems,
        int statPoints,
        string itemsJson,
        DateTimeOffset rewardedAt)
    {
        ContributionTier  = tier;
        GoldEarned        = gold;
        XpEarned          = xp;
        GemsEarned        = gems;
        StatPointsEarned  = statPoints;
        ItemsEarnedJson   = itemsJson;
        RewardedAt        = rewardedAt;
        UpdatedAt         = DateTimeOffset.UtcNow;
    }
}
