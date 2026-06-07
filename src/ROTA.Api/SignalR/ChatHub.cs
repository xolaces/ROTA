using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Api.SignalR;

/// <summary>
/// Real-time chat hub. World chat (T36) is broadcast to everyone and persisted to a 100-message Redis
/// ring buffer; raid chat (T35) is broadcast to a per-raid group and is ephemeral; guild chat (System 21
/// Slice 2) is broadcast to a per-guild group and persisted to a per-guild ring buffer, member-gated.
/// Muted players (T40) are rejected. PM delivery (T37) reuses this hub's connection via Clients.User from
/// SocialController.
/// </summary>
[Authorize]
public sealed class ChatHub : Hub
{
    private const int MaxBody = 500;

    private readonly IWorldChatStore _world;
    private readonly IGuildChatStore _guild;
    private readonly IPlayerRepository _players;

    public ChatHub(IWorldChatStore world, IGuildChatStore guild, IPlayerRepository players)
    {
        _world = world;
        _guild = guild;
        _players = players;
    }

    /// <summary>Broadcasts a world-chat message to all clients and stores it in the ring buffer.</summary>
    public async Task SendWorldMessage(string body)
    {
        body = Sanitize(body);
        if (body.Length == 0) return;
        if (await CannotChatAsync()) { await NotifyBlocked(); return; }

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
        if (await CannotChatAsync()) { await NotifyBlocked(); return; }

        var msg = BuildMessage("Raid", raidId, body);
        await Clients.Group(RaidGroup(raidId)).SendAsync("RaidMessage", msg);
    }

    // ---- guild chat (System 21 Slice 2) ----

    /// <summary>
    /// Joins the caller to their own guild's chat group. The caller is in ≤1 guild (Player.GuildId);
    /// not in a guild → no group is joined and the caller is told. Mirrors JoinRaid but the group is
    /// resolved server-side from the verified identity, never from a client-supplied id.
    /// </summary>
    public async Task JoinGuildChannel()
    {
        var guildId = await CallerGuildIdAsync();
        if (guildId is null) { await NotifyNotInGuild(); return; }
        await Groups.AddToGroupAsync(Context.ConnectionId, GuildGroup(guildId.Value));
    }

    /// <summary>Leaves the caller's guild chat group (symmetry with LeaveRaid).</summary>
    public async Task LeaveGuildChannel()
    {
        var guildId = await CallerGuildIdAsync();
        if (guildId is null) return;
        await Groups.RemoveFromGroupAsync(Context.ConnectionId, GuildGroup(guildId.Value));
    }

    /// <summary>
    /// Broadcasts a guild-chat message to the caller's guild group and stores it in that guild's ring
    /// buffer. Member-gated (Player.GuildId must be non-null) and mute-gated (a muted/banned player is
    /// rejected exactly as in world/raid chat).
    /// </summary>
    public async Task SendGuildMessage(string body)
    {
        body = Sanitize(body);
        if (body.Length == 0) return;

        // Resolve the caller once: drives both the mute-gate and the member-gate from the same row.
        var player = await _players.FindByIdAsync(SenderId());
        if (player is not null && (player.IsBanned || player.IsMuted)) { await NotifyBlocked(); return; }
        if (player?.GuildId is null) { await NotifyNotInGuild(); return; }

        var guildId = player.GuildId.Value;
        var msg = BuildMessage("Guild", null, body);
        await _guild.AppendAsync(guildId, msg);
        await Clients.Group(GuildGroup(guildId)).SendAsync("GuildMessage", msg);
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

    private async Task<bool> CannotChatAsync()
    {
        // A muted (T40) or banned player cannot send. Banned matters because their 15-min access token
        // outlives session revocation, so a live socket could otherwise keep chatting post-ban.
        var p = await _players.FindByIdAsync(SenderId());
        return p is not null && (p.IsBanned || p.IsMuted);
    }

    private Task NotifyBlocked() => Clients.Caller.SendAsync("Muted", "You cannot send messages right now (muted or banned).");

    /// <summary>Resolves the caller's current guild id from the verified identity; null if not in a guild.</summary>
    private async Task<Guid?> CallerGuildIdAsync()
    {
        var p = await _players.FindByIdAsync(SenderId());
        return p?.GuildId;
    }

    private Task NotifyNotInGuild() => Clients.Caller.SendAsync("GuildChatUnavailable", "You are not in a guild.");

    private static string Sanitize(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        body = body.Trim();
        return body.Length > MaxBody ? body[..MaxBody] : body;
    }

    private static string RaidGroup(string raidId) => $"raid:{raidId}";

    private static string GuildGroup(Guid guildId) => $"guild:{guildId}";
}
