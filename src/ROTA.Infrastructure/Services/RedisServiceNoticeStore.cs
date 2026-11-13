using ROTA.Application.Interfaces;
using StackExchange.Redis;

namespace ROTA.Infrastructure.Services;

/// <summary>
/// Notices in Redis, read through a few seconds of in-process cache: the maintenance gate asks on
/// every request, and a switch that takes five seconds to be seen is fine for a deploy.
/// A Redis failure reads as "no notices" so an outage of the notice store never closes the game.
/// </summary>
public sealed class RedisServiceNoticeStore : IServiceNoticeStore
{
    private const string MaintenanceKey = "notice:maintenance";
    private const string MotdKey        = "notice:motd";
    private const string DefaultMaintenanceMessage = "The game is down for an update. Back soon.";
    private static readonly TimeSpan CacheFor = TimeSpan.FromSeconds(5);
    public const int MaxLength = 280;

    private readonly IConnectionMultiplexer _mux;
    private ServiceNotice _cached = ServiceNotice.None;
    private long _cachedAtTicks;   // 0 = never read

    public RedisServiceNoticeStore(IConnectionMultiplexer mux) => _mux = mux;

    public async Task<ServiceNotice> GetAsync(CancellationToken ct = default)
    {
        long at = Interlocked.Read(ref _cachedAtTicks);
        if (at != 0 && DateTime.UtcNow.Ticks - at < CacheFor.Ticks)
            return _cached;

        try
        {
            var db = _mux.GetDatabase();
            var values = await db.StringGetAsync(new RedisKey[] { MaintenanceKey, MotdKey });
            string? maintenance = values[0].HasValue ? values[0].ToString() : null;
            string? motd        = values[1].HasValue ? values[1].ToString() : null;
            _cached = new ServiceNotice(maintenance is not null, maintenance, string.IsNullOrWhiteSpace(motd) ? null : motd);
        }
        catch (RedisException)        { _cached = ServiceNotice.None; }
        catch (RedisTimeoutException) { _cached = ServiceNotice.None; }

        Interlocked.Exchange(ref _cachedAtTicks, DateTime.UtcNow.Ticks);
        return _cached;
    }

    public async Task SetMaintenanceAsync(bool on, string? message, CancellationToken ct = default)
    {
        var db = _mux.GetDatabase();
        if (on) await db.StringSetAsync(MaintenanceKey, Clean(message) ?? DefaultMaintenanceMessage);
        else    await db.KeyDeleteAsync(MaintenanceKey);
        Interlocked.Exchange(ref _cachedAtTicks, 0);
    }

    public async Task SetMotdAsync(string? text, CancellationToken ct = default)
    {
        var db = _mux.GetDatabase();
        var clean = Clean(text);
        if (clean is null) await db.KeyDeleteAsync(MotdKey);
        else               await db.StringSetAsync(MotdKey, clean);
        Interlocked.Exchange(ref _cachedAtTicks, 0);
    }

    private static string? Clean(string? text)
    {
        if (string.IsNullOrWhiteSpace(text)) return null;
        text = text.Trim();
        return text.Length > MaxLength ? text[..MaxLength] : text;
    }
}
