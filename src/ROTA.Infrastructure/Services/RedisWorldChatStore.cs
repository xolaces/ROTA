using System.Text.Json;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;
using StackExchange.Redis;

namespace ROTA.Infrastructure.Services;

/// <summary>
/// 100-message world-chat ring buffer in Redis (T36): LPUSH the newest then LTRIM to the cap. Reads return
/// the buffer oldest→newest. No DB table — retention is intentionally ephemeral.
/// </summary>
public sealed class RedisWorldChatStore : IWorldChatStore
{
    private const string Key = "chat:world:ring";
    private const int MaxMessages = 100;
    private static readonly JsonSerializerOptions Json = new();

    private readonly IDatabase _redis;

    public RedisWorldChatStore(IConnectionMultiplexer mux) => _redis = mux.GetDatabase();

    public async Task AppendAsync(ChatMessageDto message, CancellationToken ct = default)
    {
        var json = JsonSerializer.Serialize(message, Json);
        await _redis.ListLeftPushAsync(Key, json);
        await _redis.ListTrimAsync(Key, 0, MaxMessages - 1);
    }

    public async Task<IReadOnlyList<ChatMessageDto>> GetRecentAsync(int count, CancellationToken ct = default)
    {
        if (count < 1 || count > MaxMessages) count = MaxMessages;

        var values = await _redis.ListRangeAsync(Key, 0, count - 1);
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
}
