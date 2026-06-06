using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

/// <summary>Friends, private messaging, blocking, and player reports (T37).</summary>
public interface ISocialService
{
    Task<AdminActionResult> SendFriendRequestAsync(Guid requesterId, string targetUsernameOrId, CancellationToken ct = default);
    Task<AdminActionResult> AcceptFriendRequestAsync(Guid playerId, Guid friendshipId, CancellationToken ct = default);
    Task<AdminActionResult> RemoveFriendAsync(Guid playerId, string targetUsernameOrId, CancellationToken ct = default);
    Task<IReadOnlyList<FriendDto>> ListFriendsAsync(Guid playerId, CancellationToken ct = default);

    Task<AdminActionResult> BlockAsync(Guid blockerId, string targetUsernameOrId, CancellationToken ct = default);
    Task<AdminActionResult> UnblockAsync(Guid blockerId, string targetUsernameOrId, CancellationToken ct = default);
    Task<IReadOnlyList<BlockDto>> ListBlocksAsync(Guid blockerId, CancellationToken ct = default);

    /// <summary>Sends a PM (friends only; rejected if either side blocked the other). Persists + returns the message.</summary>
    Task<SendMessageResult> SendMessageAsync(Guid senderId, string targetUsernameOrId, string body, CancellationToken ct = default);
    Task<IReadOnlyList<PrivateMessageDto>> GetConversationAsync(Guid playerId, string targetUsernameOrId, int take, CancellationToken ct = default);

    /// <summary>Files a player report (rate-limited) → PlayerReport operator email (T39).</summary>
    Task<AdminActionResult> ReportPlayerAsync(
        Guid reporterId, string targetUsernameOrId, string reason, string? description, string? ipAddress,
        CancellationToken ct = default);
}
