using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

/// <summary>
/// World-chat history (T36): a fixed-size Redis ring buffer of the most recent messages. Ephemeral —
/// not durable across a Redis flush. Raid chat (T35) is fully ephemeral and not stored here.
/// </summary>
public interface IWorldChatStore
{
    Task AppendAsync(ChatMessageDto message, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessageDto>> GetRecentAsync(int count, CancellationToken ct = default);
}
