using ROTA.Domain.Entities;

namespace ROTA.Application.Interfaces;

public record RaidParticipantRank(Guid PlayerId, string DisplayName, long TotalDamageDealt, int HitCount);

public interface IRaidParticipantRepository
{
    Task<RaidParticipant?> FindByRaidAndPlayerAsync(Guid activeRaidId, Guid playerId, CancellationToken ct = default);
    Task<IReadOnlyList<RaidParticipant>> GetAllForRaidAsync(Guid activeRaidId, CancellationToken ct = default);
    /// <summary>
    /// Player's looted/rewarded raid history, newest first, capped at <paramref name="limit"/>.
    /// <paramref name="since"/> (default ~30 days ago) filters out stale history so the Completed tab
    /// shows recent activity only; pass <see cref="DateTimeOffset.MinValue"/> for all-time.
    /// </summary>
    Task<IReadOnlyList<RaidParticipant>> GetCompletedForPlayerAsync(Guid playerId, int limit, DateTimeOffset? since = null, CancellationToken ct = default);
    Task<RaidParticipant> CreateAsync(RaidParticipant participant, CancellationToken ct = default);
    Task UpdateAsync(RaidParticipant participant, CancellationToken ct = default);

    /// <summary>
    /// T57 claim latch — atomically marks the participant's deferred rewards as claimed via a
    /// conditional UPDATE (rewarded_at IS NULL). Returns true only for the single caller that wins
    /// the latch; every concurrent or repeated claim returns false. Participates in an ambient
    /// transaction so the latch commits/rolls back together with the reward grants.
    /// </summary>
    Task<bool> TryClaimRewardsAsync(Guid participantId, DateTimeOffset claimedAt, CancellationToken ct = default);

    /// <summary>
    /// Top participants of a raid by total damage, descending; ties broken by earliest UpdatedAt.
    /// Joins players to resolve DisplayName. Excludes soft-deleted participants. Caps at `top` rows.
    /// </summary>
    Task<IReadOnlyList<RaidParticipantRank>> GetTopByDamageAsync(Guid activeRaidId, int top, CancellationToken ct = default);
}
