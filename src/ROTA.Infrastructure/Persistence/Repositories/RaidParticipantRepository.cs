using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class RaidParticipantRepository : IRaidParticipantRepository
{
    private readonly RotaDbContext _db;

    public RaidParticipantRepository(RotaDbContext db)
    {
        _db = db;
    }

    public async Task<RaidParticipant?> FindByRaidAndPlayerAsync(
        Guid activeRaidId, Guid playerId, CancellationToken ct = default)
        => await _db.RaidParticipants
            .Where(p => p.ActiveRaidId == activeRaidId && p.PlayerId == playerId && !p.IsDeleted)
            .FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<RaidParticipant>> GetAllForRaidAsync(
        Guid activeRaidId, CancellationToken ct = default)
        => await _db.RaidParticipants
            .Where(p => p.ActiveRaidId == activeRaidId && !p.IsDeleted)
            .ToListAsync(ct);

    public async Task<IReadOnlyList<RaidParticipant>> GetCompletedForPlayerAsync(
        Guid playerId, int limit, CancellationToken ct = default)
        => await _db.RaidParticipants
            .Include(p => p.ActiveRaid)
            .Where(p => p.PlayerId == playerId
                     && !p.IsDeleted
                     && p.ActiveRaid != null
                     && p.ActiveRaid.IsDefeated
                     && p.RewardedAt != null)
            .OrderByDescending(p => p.RewardedAt)
            .Take(limit)
            .ToListAsync(ct);

    public async Task<RaidParticipant> CreateAsync(RaidParticipant participant, CancellationToken ct = default)
    {
        _db.RaidParticipants.Add(participant);
        await _db.SaveChangesAsync(ct);
        return participant;
    }

    public async Task UpdateAsync(RaidParticipant participant, CancellationToken ct = default)
    {
        _db.RaidParticipants.Update(participant);
        await _db.SaveChangesAsync(ct);
    }

    // T57 claim latch. Raw conditional UPDATE so the check-and-set is a single row-locked statement:
    // under concurrency the second caller blocks on the row lock, then matches zero rows. Runs on the
    // DbContext connection, so it participates in any ambient transaction (the loot-claim tx).
    public async Task<bool> TryClaimRewardsAsync(
        Guid participantId, DateTimeOffset claimedAt, CancellationToken ct = default)
    {
        var rows = await _db.Database.ExecuteSqlInterpolatedAsync($@"
            UPDATE raid_participants
               SET rewarded_at = {claimedAt}, updated_at = {claimedAt}
             WHERE id = {participantId}
               AND rewarded_at IS NULL
               AND is_deleted = false", ct);
        return rows == 1;
    }

    public async Task<IReadOnlyList<RaidParticipantRank>> GetTopByDamageAsync(
        Guid activeRaidId, int top, CancellationToken ct = default)
    {
        var query =
            from rp in _db.RaidParticipants
            where rp.ActiveRaidId == activeRaidId && !rp.IsDeleted
            join pl in _db.Players on rp.PlayerId equals pl.Id
            orderby rp.TotalDamageDealt descending, rp.UpdatedAt ascending
            select new RaidParticipantRank(rp.PlayerId, pl.DisplayName, rp.TotalDamageDealt, rp.HitCount);

        return await query.Take(top).ToListAsync(ct);
    }
}
