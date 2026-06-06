using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Interfaces;

public interface IFriendshipRepository
{
    /// <summary>Finds the friendship between two players regardless of who requested (or null).</summary>
    Task<Friendship?> FindBetweenAsync(Guid a, Guid b, CancellationToken ct = default);
    Task<Friendship?> GetByIdAsync(Guid id, CancellationToken ct = default);
    Task AddAsync(Friendship friendship, CancellationToken ct = default);
    Task UpdateAsync(Friendship friendship, CancellationToken ct = default);
    Task<IReadOnlyList<Friendship>> ListForPlayerAsync(Guid playerId, FriendshipStatus? status, CancellationToken ct = default);
}

public interface IBlockRepository
{
    Task<bool> ExistsAsync(Guid blockerId, Guid blockedId, CancellationToken ct = default);
    /// <summary>True if either player has blocked the other.</summary>
    Task<bool> EitherBlockedAsync(Guid a, Guid b, CancellationToken ct = default);
    Task AddAsync(PlayerBlock block, CancellationToken ct = default);
    Task RemoveAsync(Guid blockerId, Guid blockedId, CancellationToken ct = default);
    Task<IReadOnlyList<PlayerBlock>> ListForPlayerAsync(Guid blockerId, CancellationToken ct = default);
}

public interface IPrivateMessageRepository
{
    Task AddAsync(PrivateMessage message, CancellationToken ct = default);
    Task<IReadOnlyList<PrivateMessage>> GetConversationAsync(Guid playerA, Guid playerB, int take, CancellationToken ct = default);
}
