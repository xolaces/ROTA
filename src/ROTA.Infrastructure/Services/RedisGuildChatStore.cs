using System.Text.Json;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;
using StackExchange.Redis;

namespace ROTA.Infrastructure.Services;

/// <summary>
/// 100-message-per-guild chat ring buffer in Redis (System 21 Slice 2): LPUSH the newest then LTRIM to the
/// cap, under a per-guild key (<c>chat:guild:{guildId}</c>). Reads return the buffer oldest→newest. No DB
/// table — retention is intentionally ephemeral. Mirrors <see cref="RedisWorldChatStore"/>; the only
/// difference is the per-guild key, which isolates each guild's buffer.
/// </summary>
// BETA
public sealed class RedisGuildChatStore : IGuildChatStore
{
    private const int MaxMessages = 100;
    private static readonly JsonSerializerOptions Json = new();

    private readonly IDatabase _redis;

    public RedisGuildChatStore(IConnectionMultiplexer mux) => _redis = mux.GetDatabase();

    public async Task AppendAsync(Guid guildId, ChatMessageDto message, CancellationToken ct = default)
    {
        var key = Key(guildId);
        var json = JsonSerializer.Serialize(message, Json);
        await _redis.ListLeftPushAsync(key, json);
        await _redis.ListTrimAsync(key, 0, MaxMessages - 1);
    }

    public async Task<IReadOnlyList<ChatMessageDto>> GetRecentAsync(Guid guildId, int count, CancellationToken ct = default)
    {
        if (count < 1 || count > MaxMessages) count = MaxMessages;

        var values = await _redis.ListRangeAsync(Key(guildId), 0, count - 1);
        var list = new List<ChatMessageDto>(values.Length);
        foreach (var v in values)
        {
            if (v.IsNullOrEmpty) continue;
            var msg = JsonSerializer.Deserialize<ChatMessageDto>((string)v!, Json);
            if (msg is not null) list.Add(msg);
        }

        // LPUSH stores newest-first; return chronological (oldest-first) for display.
        list.Reverse();
        return list;
    }

    private static string Key(Guid guildId) => $"chat:guild:{guildId}";
}
