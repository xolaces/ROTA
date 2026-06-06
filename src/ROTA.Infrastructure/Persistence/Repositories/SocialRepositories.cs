using Microsoft.EntityFrameworkCore;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Infrastructure.Persistence;

namespace ROTA.Infrastructure.Persistence.Repositories;

public sealed class FriendshipRepository : IFriendshipRepository
{
    private readonly RotaDbContext _db;
    public FriendshipRepository(RotaDbContext db) => _db = db;

    public Task<Friendship?> FindBetweenAsync(Guid a, Guid b, CancellationToken ct = default)
        => _db.Friendships.FirstOrDefaultAsync(f => !f.IsDeleted &&
            ((f.RequesterId == a && f.AddresseeId == b) || (f.RequesterId == b && f.AddresseeId == a)), ct);

    public Task<Friendship?> GetByIdAsync(Guid id, CancellationToken ct = default)
        => _db.Friendships.FirstOrDefaultAsync(f => f.Id == id && !f.IsDeleted, ct);

    public async Task AddAsync(Friendship friendship, CancellationToken ct = default)
    {
        _db.Friendships.Add(friendship);
        await _db.SaveChangesAsync(ct);
    }

    public async Task UpdateAsync(Friendship friendship, CancellationToken ct = default)
    {
        _db.Friendships.Update(friendship);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<Friendship>> ListForPlayerAsync(Guid playerId, FriendshipStatus? status, CancellationToken ct = default)
    {
        var q = _db.Friendships.AsNoTracking()
            .Where(f => !f.IsDeleted && (f.RequesterId == playerId || f.AddresseeId == playerId));
        if (status.HasValue) q = q.Where(f => f.Status == status.Value);
        return await q.OrderByDescending(f => f.UpdatedAt).ToListAsync(ct);
    }
}

public sealed class BlockRepository : IBlockRepository
{
    private readonly RotaDbContext _db;
    public BlockRepository(RotaDbContext db) => _db = db;

    public Task<bool> ExistsAsync(Guid blockerId, Guid blockedId, CancellationToken ct = default)
        => _db.PlayerBlocks.AnyAsync(b => !b.IsDeleted && b.BlockerId == blockerId && b.BlockedId == blockedId, ct);

    public Task<bool> EitherBlockedAsync(Guid a, Guid b, CancellationToken ct = default)
        => _db.PlayerBlocks.AnyAsync(x => !x.IsDeleted &&
            ((x.BlockerId == a && x.BlockedId == b) || (x.BlockerId == b && x.BlockedId == a)), ct);

    public async Task AddAsync(PlayerBlock block, CancellationToken ct = default)
    {
        _db.PlayerBlocks.Add(block);
        await _db.SaveChangesAsync(ct);
    }

    public async Task RemoveAsync(Guid blockerId, Guid blockedId, CancellationToken ct = default)
    {
        var row = await _db.PlayerBlocks
            .FirstOrDefaultAsync(b => !b.IsDeleted && b.BlockerId == blockerId && b.BlockedId == blockedId, ct);
        if (row is null) return;
        _db.PlayerBlocks.Remove(row);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PlayerBlock>> ListForPlayerAsync(Guid blockerId, CancellationToken ct = default)
        => await _db.PlayerBlocks.AsNoTracking()
            .Where(b => !b.IsDeleted && b.BlockerId == blockerId)
            .ToListAsync(ct);
}

public sealed class PrivateMessageRepository : IPrivateMessageRepository
{
    private readonly RotaDbContext _db;
    public PrivateMessageRepository(RotaDbContext db) => _db = db;

    public async Task AddAsync(PrivateMessage message, CancellationToken ct = default)
    {
        _db.PrivateMessages.Add(message);
        await _db.SaveChangesAsync(ct);
    }

    public async Task<IReadOnlyList<PrivateMessage>> GetConversationAsync(Guid playerA, Guid playerB, int take, CancellationToken ct = default)
        => await _db.PrivateMessages.AsNoTracking()
            .Where(m => !m.IsDeleted &&
                ((m.SenderId == playerA && m.RecipientId == playerB) ||
                 (m.SenderId == playerB && m.RecipientId == playerA)))
            .OrderByDescending(m => m.SentAt)
            .Take(take)
            .ToListAsync(ct);
}
