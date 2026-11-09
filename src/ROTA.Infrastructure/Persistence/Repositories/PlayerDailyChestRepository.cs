using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;

namespace ROTA.Infrastructure.Persistence.Repositories;

// System 29 — both writes are single SQL statements whose WHERE / ON CONFLICT clause is the race guard.
// ExecuteSqlInterpolatedAsync enlists in the DbContext's current transaction, so inside
// IPlayerMutationLock.RunAsync the pick and the item grant commit or roll back together.
public sealed class PlayerDailyChestRepository : IPlayerDailyChestRepository
{
    private readonly RotaDbContext _db;
    public PlayerDailyChestRepository(RotaDbContext db) => _db = db;

    public Task<PlayerDailyChest?> FindAsync(Guid playerId, DateOnly chestDay, CancellationToken ct = default)
        => _db.PlayerDailyChests.AsNoTracking()
            .FirstOrDefaultAsync(c => c.PlayerId == playerId && c.ChestDay == chestDay && !c.IsDeleted, ct);

    public async Task<bool> InsertIfAbsentAsync(PlayerDailyChest chest, CancellationToken ct = default)
    {
        var rows = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO player_daily_chests
                (id, player_id, chest_day, contents, picked_index, picked_at, created_at, updated_at, is_deleted)
            VALUES
                ({chest.Id}, {chest.PlayerId}, {chest.ChestDay}, CAST({chest.ContentsJson} AS jsonb),
                 NULL, NULL, NOW(), NOW(), false)
            ON CONFLICT (player_id, chest_day) DO NOTHING
            """, ct);
        return rows == 1;
    }

    public async Task<bool> TryPickAsync(Guid chestId, Guid playerId, short index, CancellationToken ct = default)
    {
        var rows = await _db.Database.ExecuteSqlInterpolatedAsync($"""
            UPDATE player_daily_chests
               SET picked_index = {index},
                   picked_at    = NOW(),
                   updated_at   = NOW()
             WHERE id           = {chestId}
               AND player_id    = {playerId}
               AND picked_index IS NULL
               AND is_deleted   = false
            """, ct);
        return rows == 1;
    }
}
