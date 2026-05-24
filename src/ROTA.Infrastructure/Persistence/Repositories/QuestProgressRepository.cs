using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class QuestProgressRepository : IQuestProgressRepository
{
    private readonly RotaDbContext _db;

    public QuestProgressRepository(RotaDbContext db)
    {
        _db = db;
    }

    public async Task<IReadOnlyList<PlayerQuestProgress>> GetAllForPlayerAsync(
        Guid playerId, CancellationToken ct = default)
        => await _db.PlayerQuestProgress
            .Where(p => p.PlayerId == playerId)
            .ToListAsync(ct);

    public async Task<PlayerQuestProgress?> GetAsync(
        Guid playerId, string questId, CancellationToken ct = default)
        => await _db.PlayerQuestProgress
            .Where(p => p.PlayerId == playerId && p.QuestId == questId)
            .FirstOrDefaultAsync(ct);

    public async Task CreateAsync(PlayerQuestProgress progress, CancellationToken ct = default)
    {
        _db.PlayerQuestProgress.Add(progress);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(PlayerQuestProgress progress, CancellationToken ct = default)
    {
        _db.PlayerQuestProgress.Update(progress);
        await _db.SaveChangesAsync(ct);
    }
}
