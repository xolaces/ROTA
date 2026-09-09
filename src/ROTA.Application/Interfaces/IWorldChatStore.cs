using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

/// <summary>
/// World-chat history: a fixed-size Redis ring buffer of the most recent messages. Ephemeral —
/// not durable across a Redis flush. Raid chat is fully ephemeral and not stored here.
/// </summary>
public interface IWorldChatStore
{
    Task AppendAsync(ChatMessageDto message, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessageDto>> GetRecentAsync(int count, CancellationToken ct = default);
}
