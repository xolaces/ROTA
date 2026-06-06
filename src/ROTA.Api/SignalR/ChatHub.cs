using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Api.SignalR;

/// <summary>
/// Real-time chat hub. World chat (T36) is broadcast to everyone and persisted to a 100-message Redis
/// ring buffer; raid chat (T35) is broadcast to a per-raid group and is ephemeral. Muted players (T40)
/// are rejected. PM delivery (T37) reuses this hub's connection via Clients.User from SocialController.
/// </summary>
[Authorize]
public sealed class ChatHub : Hub
{
    private const int MaxBody = 500;

    private readonly IWorldChatStore _world;
    private readonly IPlayerRepository _players;

    public ChatHub(IWorldChatStore world, IPlayerRepository players)
    {
        _world = world;
        _players = players;
    }

    /// <summary>Broadcasts a world-chat message to all clients and stores it in the ring buffer.</summary>
    public async Task SendWorldMessage(string body)
    {
        body = Sanitize(body);
        if (body.Length == 0) return;
        if (await IsMutedAsync()) { await NotifyMuted(); return; }

        var msg = BuildMessage("World", null, body);
        await _world.AppendAsync(msg);
        await Clients.All.SendAsync("WorldMessage", msg);
    }

    /// <summary>Joins the caller to a raid's chat group (T35).</summary>
    public Task JoinRaid(string raidId) => Groups.AddToGroupAsync(Context.ConnectionId, RaidGroup(raidId));

    /// <summary>Leaves a raid's chat group.</summary>
    public Task LeaveRaid(string raidId) => Groups.RemoveFromGroupAsync(Context.ConnectionId, RaidGroup(raidId));

    /// <summary>Broadcasts an ephemeral raid-chat message to the raid group only.</summary>
    public async Task SendRaidMessage(string raidId, string body)
    {
        body = Sanitize(body);
        if (body.Length == 0 || string.IsNullOrWhiteSpace(raidId)) return;
        if (await IsMutedAsync()) { await NotifyMuted(); return; }

        var msg = BuildMessage("Raid", raidId, body);
        await Clients.Group(RaidGroup(raidId)).SendAsync("RaidMessage", msg);
    }

    // ---- helpers ----

    private ChatMessageDto BuildMessage(string scope, string? raidId, string body) => new()
    {
        Id = Guid.NewGuid(),
        Scope = scope,
        RaidId = raidId,
        SenderId = SenderId(),
        SenderName = Context.User?.FindFirst("display_name")?.Value
                     ?? Context.User?.Identity?.Name
                     ?? "Player",
        SenderRole = Context.User?.IsInRole(nameof(PlayerRoles.Admin)) == true ? "Admin"
                   : Context.User?.IsInRole(nameof(PlayerRoles.Moderator)) == true ? "Moderator"
                   : "Player",
        Body = body,
        SentAt = DateTimeOffset.UtcNow,
    };

    private Guid SenderId() => Guid.Parse(Context.User!.FindFirst("sub")!.Value);

    private async Task<bool> IsMutedAsync()
    {
        var p = await _players.FindByIdAsync(SenderId());
        return p is not null && p.IsMuted;
    }

    private Task NotifyMuted() => Clients.Caller.SendAsync("Muted", "You are muted and cannot send messages.");

    private static string Sanitize(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        body = body.Trim();
        return body.Length > MaxBody ? body[..MaxBody] : body;
    }

    private static string RaidGroup(string raidId) => $"raid:{raidId}";
}
