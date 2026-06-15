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

    // The JWT "name" claim carries Player.Username (AuthService emits JwtRegisteredClaimNames.Name =
    // "name"). MapInboundClaims is off, so the claim type is verbatim "name", not ClaimTypes.Name —
    // which is why Identity.Name (keyed on the long URI) was unreliable here.
    private const string UsernameClaim = "name";

    private readonly IWorldChatStore _world;
    private readonly IGuildChatStore _guild;
    private readonly IPlayerRepository _players;
    private readonly IRaidParticipantRepository _raidParticipants;

    public ChatHub(IWorldChatStore world, IGuildChatStore guild, IPlayerRepository players,
                   IRaidParticipantRepository raidParticipants)
    {
        _world = world;
        _guild = guild;
        _players = players;
        _raidParticipants = raidParticipants;
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

    /// <summary>
    /// Joins the caller to a raid's chat group (T35). AUDIT FIX — participant-gated: the raidId is
    /// client-supplied, so without the gate any authenticated player who learned a raid GUID could
    /// subscribe to (and read) another party's raid chat. Mirrors the guild-chat member gate below.
    /// The group key is canonicalized from the parsed GUID so case-variant strings can't fork groups.
    /// </summary>
    public async Task JoinRaid(string raidId)
    {
        if (!Guid.TryParse(raidId, out var raidGuid)) return;
        if (await IsCallerBannedAsync()) { await NotifyBlocked(); return; }
        if (!await IsRaidParticipantAsync(raidGuid)) { await NotifyNotInRaid(); return; }
        await Groups.AddToGroupAsync(Context.ConnectionId, RaidGroup(raidGuid));
    }

    /// <summary>Leaves a raid's chat group.</summary>
    public Task LeaveRaid(string raidId)
        => Guid.TryParse(raidId, out var raidGuid)
            ? Groups.RemoveFromGroupAsync(Context.ConnectionId, RaidGroup(raidGuid))
            : Task.CompletedTask;

    /// <summary>
    /// Broadcasts an ephemeral raid-chat message to the raid group only. AUDIT FIX — participant-
    /// gated like JoinRaid: without it any player could inject messages into any raid's chat.
    /// </summary>
    public async Task SendRaidMessage(string raidId, string body)
    {
        body = Sanitize(body);
        if (body.Length == 0 || !Guid.TryParse(raidId, out var raidGuid)) return;
        if (await CannotChatAsync()) { await NotifyBlocked(); return; }
        if (!await IsRaidParticipantAsync(raidGuid)) { await NotifyNotInRaid(); return; }

        var msg = BuildMessage("Raid", raidId, body);
        await Clients.Group(RaidGroup(raidGuid)).SendAsync("RaidMessage", msg);
    }

    // ---- guild chat (System 21 Slice 2) ----

    /// <summary>
    /// Joins the caller to their own guild's chat group. The caller is in ≤1 guild (Player.GuildId);
    /// not in a guild → no group is joined and the caller is told. Mirrors JoinRaid but the group is
    /// resolved server-side from the verified identity, never from a client-supplied id.
    /// </summary>
    public async Task JoinGuildChannel()
    {
        if (await IsCallerBannedAsync()) { await NotifyBlocked(); return; }
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
                     ?? Context.User?.FindFirst(UsernameClaim)?.Value
                     ?? "Player",
        // Stable unique handle for moderation/social targeting (the "name" claim = Player.Username).
        // Verified identity only — never a client-supplied value.
        SenderUsername = Context.User?.FindFirst(UsernameClaim)?.Value,
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

    // SECURITY (exploit audit 2026-06-14, finding K): ban-gate the chat JOINs too — a banned player's
    // 15-min access token outlives session revocation, so a live socket could otherwise re-join and READ
    // raid/guild chat. Ban only (NOT mute): a muted player may still read, just not send.
    private async Task<bool> IsCallerBannedAsync()
    {
        var p = await _players.FindByIdAsync(SenderId());
        return p is not null && p.IsBanned;
    }

    private Task NotifyBlocked() => Clients.Caller.SendAsync("Muted", "You cannot send messages right now (muted or banned).");

    /// <summary>Resolves the caller's current guild id from the verified identity; null if not in a guild.</summary>
    private async Task<Guid?> CallerGuildIdAsync()
    {
        var p = await _players.FindByIdAsync(SenderId());
        return p?.GuildId;
    }

    private Task NotifyNotInGuild() => Clients.Caller.SendAsync("GuildChatUnavailable", "You are not in a guild.");

    /// <summary>The caller may use a raid's chat only if they hold a participant row in it.</summary>
    private async Task<bool> IsRaidParticipantAsync(Guid raidId)
        => await _raidParticipants.FindByRaidAndPlayerAsync(raidId, SenderId()) is not null;

    private Task NotifyNotInRaid() => Clients.Caller.SendAsync("RaidChatUnavailable", "You are not a participant in this raid.");

    private static string Sanitize(string body)
    {
        if (string.IsNullOrWhiteSpace(body)) return string.Empty;
        body = body.Trim();
        return body.Length > MaxBody ? body[..MaxBody] : body;
    }

    private static string RaidGroup(Guid raidId) => $"raid:{raidId:D}";

    private static string GuildGroup(Guid guildId) => $"guild:{guildId}";
}
