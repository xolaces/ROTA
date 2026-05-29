using ROTA.Domain.Enums;
namespace ROTA.Domain.Entities;

public class ActiveRaid
{
    // Required by EF Core
    private ActiveRaid() { }

    public static ActiveRaid Create(
        string raidDefinitionId,
        Guid summonedByPlayerId,
        long maxHp,
        DateTimeOffset expiresAt,
        RaidDifficulty difficulty = RaidDifficulty.Normal,
        RaidSize size = RaidSize.Large)
    {
        return new ActiveRaid
        {
            Id                 = Guid.NewGuid(),
            RaidDefinitionId   = raidDefinitionId,
            SummonedByPlayerId = summonedByPlayerId,
            CurrentHp          = maxHp,
            MaxHp              = maxHp,
            IsDefeated         = false,
            Difficulty         = difficulty,
            Size               = size,
            ParticipantCount   = 0,
            ExpiresAt          = expiresAt,
            CreatedAt          = DateTimeOffset.UtcNow,
            UpdatedAt          = DateTimeOffset.UtcNow,
            IsDeleted          = false,
        };
    }

    public Guid Id { get; private set; }
    public string RaidDefinitionId { get; private set; } = string.Empty;
    public Guid SummonedByPlayerId { get; private set; }
    public long CurrentHp { get; private set; }
    public long MaxHp { get; private set; }
    public bool IsDefeated { get; private set; }
    public RaidDifficulty Difficulty { get; private set; }
    public RaidSize Size { get; private set; }
    public DateTimeOffset ExpiresAt { get; private set; }

    // Denormalised for O(1) participant count on every hit response â€” incremented on first hit per player.
    public int ParticipantCount { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }
    public DateTimeOffset UpdatedAt { get; private set; }
    public bool IsDeleted { get; private set; }

    // Navigation property â€” populated by repository Include() calls only
    public Player? SummonedByPlayer { get; private set; }

    // Domain methods

    public void TakeDamage(long amount)
    {
        CurrentHp  = Math.Max(0, CurrentHp - amount);
        UpdatedAt  = DateTimeOffset.UtcNow;
    }

    public void MarkDefeated()
    {
        IsDefeated = true;
        UpdatedAt  = DateTimeOffset.UtcNow;
    }

    public void IncrementParticipantCount()
    {
        ParticipantCount++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}

