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

    // Reward summary. T57 — the AMOUNTS are computed + stored on the killing hit, but RewardedAt now
    // marks the per-participant CLAIM: null = computed-but-unclaimed (pending), set = claimed via Loot.
    // BETA: items stored as a JSON blob (application-serialized List<ItemGrantDTO>).
    public string ContributionTier { get; private set; } = string.Empty;
    public long GoldEarned { get; private set; }
    // int32-overflow-audit Unit 2 (owner-locked, no caps): earned reward amounts widened to bigint
    // alongside the gem ledger + reward DTOs, so late-game tier×difficulty payouts never lap int32.
    public long XpEarned { get; private set; }
    public long GemsEarned { get; private set; }
    public long StatPointsEarned { get; private set; }
    public string ItemsEarnedJson { get; private set; } = string.Empty;
    // T57 — deferred magic/unit/legion/gear drops (JSON list of PendingDrop), granted at Loot.
    public string PendingDropsJson { get; private set; } = string.Empty;
    public DateTimeOffset? RewardedAt { get; private set; }

    // T57 — true once this participant has claimed (looted) their deferred rewards.
    public bool RewardsClaimed => RewardedAt is not null;

    public void RecordHit(long damage)
    {
        TotalDamageDealt += damage;
        HitCount++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }

    // T57 — store the computed reward amounts on the killing hit WITHOUT marking them claimed (RewardedAt
    // stays null). The player claims them later via Loot (per-participant). Gold/gems/stat-points/items
    // are NOT granted here — only the XP/level-ups (granted separately at kill) are immediate.
    public void RecordPendingRewards(
        string tier,
        long gold,
        long xp,
        long gems,
        long statPoints,
        string itemsJson,
        string pendingDropsJson = "")
    {
        ContributionTier = tier;
        GoldEarned       = gold;
        XpEarned         = xp;
        GemsEarned       = gems;
        StatPointsEarned = statPoints;
        ItemsEarnedJson  = itemsJson;
        PendingDropsJson = pendingDropsJson ?? string.Empty;
        // RewardedAt intentionally left null — claimed on Loot.
        UpdatedAt        = DateTimeOffset.UtcNow;
    }

    // T57 — mark this participant's deferred rewards as claimed (granted at Loot).
    public void MarkRewardsClaimed(DateTimeOffset claimedAt)
    {
        RewardedAt = claimedAt;
        UpdatedAt  = DateTimeOffset.UtcNow;
    }

    // Legacy grant-on-kill recorder (pre-T57). Retained for back-compat; the live path uses
    // RecordPendingRewards + MarkRewardsClaimed.
    public void RecordRewards(
        string tier,
        long gold,
        long xp,
        long gems,
        long statPoints,
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
