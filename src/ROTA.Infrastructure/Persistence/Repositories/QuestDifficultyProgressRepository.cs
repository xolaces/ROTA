using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class QuestDifficultyProgressRepository : IQuestDifficultyProgressRepository
{
    private readonly RotaDbContext _db;

    public QuestDifficultyProgressRepository(RotaDbContext db)
    {
        _db = db;
    }

    public async Task<PlayerQuestDifficultyProgress?> GetAsync(
        Guid playerId, string questId, QuestDifficulty difficulty, CancellationToken ct = default)
        => await _db.PlayerQuestDifficultyProgress
            .FirstOrDefaultAsync(p => p.PlayerId == playerId
                                   && p.QuestId == questId
                                   && p.Difficulty == difficulty, ct);

    public async Task CreateAsync(PlayerQuestDifficultyProgress progress, CancellationToken ct = default)
    {
        _db.PlayerQuestDifficultyProgress.Add(progress);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(PlayerQuestDifficultyProgress progress, CancellationToken ct = default)
    {
        _db.PlayerQuestDifficultyProgress.Update(progress);
        await _db.SaveChangesAsync(ct);
    }
}
