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
}
