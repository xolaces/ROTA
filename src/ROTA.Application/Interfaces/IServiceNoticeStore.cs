namespace ROTA.Application.Interfaces;

/// <summary>
/// What the owner is currently telling players: whether the game is closed for maintenance (and the
/// line to show while it is), and the message of the day.
/// </summary>
public sealed record ServiceNotice(bool Maintenance, string? MaintenanceMessage, string? Motd)
{
    public static readonly ServiceNotice None = new(false, null, null);
}

/// <summary>
/// Owner-set notices, held outside the API process so they survive its restart: maintenance is
/// switched on BEFORE a deploy and must still be on when the new container comes up.
/// Set from the admin CLI (<c>maintenance on|off</c>, <c>motd set|clear</c>).
/// </summary>
public interface IServiceNoticeStore
{
    /// <summary>The current notices. Never throws: if the store cannot be read, the game is open.</summary>
    Task<ServiceNotice> GetAsync(CancellationToken ct = default);
    Task SetMaintenanceAsync(bool on, string? message, CancellationToken ct = default);
    Task SetMotdAsync(string? text, CancellationToken ct = default);
}
