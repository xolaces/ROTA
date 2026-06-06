using Microsoft.AspNetCore.SignalR;

namespace ROTA.Api.SignalR;

/// <summary>
/// Keys SignalR's per-user identity on the JWT "sub" claim. Required because the app keeps
/// MapInboundClaims off (sub is not remapped to NameIdentifier), and PM delivery (T37) uses
/// <c>Clients.User(playerId)</c>.
/// </summary>
public sealed class SubUserIdProvider : IUserIdProvider
{
    public string? GetUserId(HubConnectionContext connection)
        => connection.User?.FindFirst("sub")?.Value;
}
