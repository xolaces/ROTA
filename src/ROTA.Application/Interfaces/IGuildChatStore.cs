using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

/// <summary>
/// Guild-chat history (System 21 Slice 2): a per-guild, fixed-size Redis ring buffer of the most recent
/// messages. Mirrors <see cref="IWorldChatStore"/> exactly but keyed per guild, so guild A's buffer never
/// leaks into guild B's. Ephemeral — not durable across a Redis flush.
/// </summary>
// BETA
public interface IGuildChatStore
{
    Task AppendAsync(Guid guildId, ChatMessageDto message, CancellationToken ct = default);
    Task<IReadOnlyList<ChatMessageDto>> GetRecentAsync(Guid guildId, int count, CancellationToken ct = default);
}
