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

    public void RecordHit(long damage)
    {
        TotalDamageDealt += damage;
        HitCount++;
        UpdatedAt = DateTimeOffset.UtcNow;
    }
}
