using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

/// <summary>
/// Friends, private messaging, blocking, and player reports (T37). PMs are friends-only and rejected if
/// either side has blocked the other. Reports are rate-limited and routed to a PlayerReport email (T39).
/// </summary>
public sealed class SocialService : ISocialService
{
    private const int ReportPerPlayerPerHour = 5;
    private const int ReportPerIpPerHour = 15;
    private const int MaxConversation = 100;

    private readonly IPlayerRepository _players;
    private readonly IFriendshipRepository _friends;
    private readonly IBlockRepository _blocks;
    private readonly IPrivateMessageRepository _messages;
    private readonly IAuditLogRepository _audit;
    private readonly IEmailNotificationService _emails;
    private readonly ISubmissionRateLimiter _rateLimiter;
    private readonly ISubjectCatalogProvider _subjects;

    public SocialService(
        IPlayerRepository players,
        IFriendshipRepository friends,
        IBlockRepository blocks,
        IPrivateMessageRepository messages,
        IAuditLogRepository audit,
        IEmailNotificationService emails,
        ISubmissionRateLimiter rateLimiter,
        ISubjectCatalogProvider subjects)
    {
        _players = players;
        _friends = friends;
        _blocks = blocks;
        _messages = messages;
        _audit = audit;
        _emails = emails;
        _rateLimiter = rateLimiter;
        _subjects = subjects;
    }

    // -------------------------------------------------------------------- friends

    public async Task<AdminActionResult> SendFriendRequestAsync(Guid requesterId, string targetUsernameOrId, CancellationToken ct = default)
    {
        var target = await ResolveAsync(targetUsernameOrId, ct);
        if (target is null) return AdminActionResult.Fail($"Player '{targetUsernameOrId}' not found.");
        if (target.Id == requesterId) return AdminActionResult.Fail("You cannot friend yourself.");
        if (await _blocks.EitherBlockedAsync(requesterId, target.Id, ct))
            return AdminActionResult.Fail("Cannot send a request — a block is in place.");

        var existing = await _friends.FindBetweenAsync(requesterId, target.Id, ct);
        if (existing is not null)
        {
            if (existing.Status == FriendshipStatus.Accepted)
                return AdminActionResult.Fail("Already friends.");
            // A pending request addressed to ME means the other player already asked — mutual intent,
            // so accept it rather than create a second (reverse-direction) row.
            if (existing.AddresseeId == requesterId)
            {
                existing.Accept();
                await _friends.UpdateAsync(existing, ct);
                await Audit(requesterId, "FriendRequestAccepted", $"friendship={existing.Id} (mutual)", ct);
                return AdminActionResult.Ok();
            }
            return AdminActionResult.Fail("A friend request is already pending.");
        }

        if (!await _friends.AddAsync(Friendship.Create(requesterId, target.Id), ct))
            return AdminActionResult.Fail("A friend request already exists.");
        await Audit(requesterId, "FriendRequestSent", $"requester={requesterId} target={target.Id}", ct);
        return AdminActionResult.Ok();
    }

    public async Task<AdminActionResult> AcceptFriendRequestAsync(Guid playerId, Guid friendshipId, CancellationToken ct = default)
    {
        var f = await _friends.GetByIdAsync(friendshipId, ct);
        if (f is null) return AdminActionResult.Fail("Friend request not found.");
        if (f.AddresseeId != playerId) return AdminActionResult.Fail("Only the addressee can accept this request.");
        if (f.Status == FriendshipStatus.Accepted) return AdminActionResult.Ok();

        f.Accept();
        await _friends.UpdateAsync(f, ct);
        await Audit(playerId, "FriendRequestAccepted", $"friendship={friendshipId}", ct);
        return AdminActionResult.Ok();
    }

    public async Task<AdminActionResult> RemoveFriendAsync(Guid playerId, string targetUsernameOrId, CancellationToken ct = default)
    {
        var target = await ResolveAsync(targetUsernameOrId, ct);
        if (target is null) return AdminActionResult.Fail($"Player '{targetUsernameOrId}' not found.");

        var f = await _friends.FindBetweenAsync(playerId, target.Id, ct);
        if (f is null) return AdminActionResult.Fail("Not friends.");

        f.Remove();
        await _friends.UpdateAsync(f, ct);
        await Audit(playerId, "FriendRemoved", $"player={playerId} target={target.Id}", ct);
        return AdminActionResult.Ok();
    }

    public async Task<IReadOnlyList<FriendDto>> ListFriendsAsync(Guid playerId, CancellationToken ct = default)
    {
        var rows = await _friends.ListForPlayerAsync(playerId, null, ct);
        var list = new List<FriendDto>(rows.Count);
        foreach (var f in rows)
        {
            var otherId = f.OtherSide(playerId);
            var other = await _players.FindByIdAsync(otherId, ct);
            if (other is null) continue;
            list.Add(new FriendDto
            {
                FriendshipId = f.Id,
                PlayerId = otherId,
                Username = other.Username,
                DisplayName = other.DisplayName,
                Status = f.Status.ToString(),
                IncomingRequest = f.Status == FriendshipStatus.Pending && f.AddresseeId == playerId,
            });
        }
        return list;
    }

    // -------------------------------------------------------------------- blocks

    public async Task<AdminActionResult> BlockAsync(Guid blockerId, string targetUsernameOrId, CancellationToken ct = default)
    {
        var target = await ResolveAsync(targetUsernameOrId, ct);
        if (target is null) return AdminActionResult.Fail($"Player '{targetUsernameOrId}' not found.");
        if (target.Id == blockerId) return AdminActionResult.Fail("You cannot block yourself.");
        if (await _blocks.ExistsAsync(blockerId, target.Id, ct)) return AdminActionResult.Ok(); // idempotent

        await _blocks.AddAsync(PlayerBlock.Create(blockerId, target.Id), ct);
        await Audit(blockerId, "PlayerBlocked", $"blocker={blockerId} blocked={target.Id}", ct);
        return AdminActionResult.Ok();
    }

    public async Task<AdminActionResult> UnblockAsync(Guid blockerId, string targetUsernameOrId, CancellationToken ct = default)
    {
        var target = await ResolveAsync(targetUsernameOrId, ct);
        if (target is null) return AdminActionResult.Fail($"Player '{targetUsernameOrId}' not found.");

        await _blocks.RemoveAsync(blockerId, target.Id, ct);
        await Audit(blockerId, "PlayerUnblocked", $"blocker={blockerId} blocked={target.Id}", ct);
        return AdminActionResult.Ok();
    }

    public async Task<IReadOnlyList<BlockDto>> ListBlocksAsync(Guid blockerId, CancellationToken ct = default)
    {
        var rows = await _blocks.ListForPlayerAsync(blockerId, ct);
        return rows.Select(b => new BlockDto { BlockedId = b.BlockedId, CreatedAt = b.CreatedAt }).ToList();
    }

    // -------------------------------------------------------------------- private messages

    public async Task<SendMessageResult> SendMessageAsync(Guid senderId, string targetUsernameOrId, string body, CancellationToken ct = default)
    {
        var target = await ResolveAsync(targetUsernameOrId, ct);
        if (target is null) return SendMessageResult.Fail("Recipient not found.");
        if (target.Id == senderId) return SendMessageResult.Fail("You cannot message yourself.");

        // Banned/muted players cannot send PMs (mirrors the chat hub's gate — mute covers all messaging).
        var sender = await _players.FindByIdAsync(senderId, ct);
        if (sender is { IsBanned: true }) return SendMessageResult.Fail("You are banned and cannot send messages.");
        if (sender is { IsMuted: true }) return SendMessageResult.Fail("You are muted and cannot send messages.");

        if (await _blocks.EitherBlockedAsync(senderId, target.Id, ct))
            return SendMessageResult.Fail("Cannot message — a block is in place.");

        var friendship = await _friends.FindBetweenAsync(senderId, target.Id, ct);
        if (friendship is null || friendship.Status != FriendshipStatus.Accepted)
            return SendMessageResult.Fail("You can only message accepted friends.");

        var msg = PrivateMessage.Create(senderId, target.Id, body);
        await _messages.AddAsync(msg, ct);
        await Audit(senderId, "PrivateMessageSent", $"sender={senderId} recipient={target.Id} id={msg.Id}", ct);
        return SendMessageResult.Ok(ToDto(msg));
    }

    public async Task<IReadOnlyList<PrivateMessageDto>> GetConversationAsync(Guid playerId, string targetUsernameOrId, int take, CancellationToken ct = default)
    {
        var target = await ResolveAsync(targetUsernameOrId, ct);
        if (target is null) return Array.Empty<PrivateMessageDto>();
        // A block hides the conversation history from both sides.
        if (await _blocks.EitherBlockedAsync(playerId, target.Id, ct))
            return Array.Empty<PrivateMessageDto>();

        take = Math.Clamp(take, 1, MaxConversation);
        var rows = await _messages.GetConversationAsync(playerId, target.Id, take, ct);
        return rows.Select(ToDto).ToList();
    }

    // -------------------------------------------------------------------- report

    public async Task<AdminActionResult> ReportPlayerAsync(
        Guid reporterId, string targetUsernameOrId, string reason, string? description, string? ipAddress,
        CancellationToken ct = default)
    {
        if (!await _rateLimiter.TryConsumeAsync("report", reporterId, ipAddress, ReportPerPlayerPerHour, ReportPerIpPerHour, ct))
            return AdminActionResult.Fail("Too many reports in the last hour. Please try again later.");

        var target = await ResolveAsync(targetUsernameOrId, ct);
        if (target is null) return AdminActionResult.Fail($"Player '{targetUsernameOrId}' not found.");
        if (target.Id == reporterId) return AdminActionResult.Fail("You cannot report yourself.");

        // T52 — normalize the validated reason (key or label) to its canonical label; stash the key.
        var reasonLabel = _subjects.NormalizeReportSubject(reason) ?? reason;
        var reasonKey = _subjects.ReportSubjects.FirstOrDefault(s => s.Label == reasonLabel)?.Key;

        await _emails.QueueAsync(new EmailPayload
        {
            Type = EmailType.PlayerReport,
            Subject = $"Report: {target.Username} — {reasonLabel}",
            Priority = EmailPriority.High,
            Summary = $"{reporterId} reported {target.Username} — {reasonLabel}",
            TriggeringPlayerId = reporterId,
            TriggeringSystem = "T37",
            Detail = new Dictionary<string, object?>
            {
                ["reporterId"] = reporterId.ToString(),
                ["reportedId"] = target.Id.ToString(),
                ["reportedUsername"] = target.Username,
                ["subjectKey"] = reasonKey,
                ["reason"] = reasonLabel,
                ["description"] = description,
            },
        }, ipAddress, ct);

        await Audit(reporterId, "PlayerReported", $"reporter={reporterId} reported={target.Id} reason={reasonLabel}", ct);
        return AdminActionResult.Ok();
    }

    // -------------------------------------------------------------------- helpers

    private async Task<Player?> ResolveAsync(string usernameOrId, CancellationToken ct)
        => Guid.TryParse(usernameOrId, out var id)
            ? await _players.FindByIdAsync(id, ct)
            : await _players.FindByUsernameAsync(usernameOrId, ct);

    private Task Audit(Guid actorId, string action, string summary, CancellationToken ct)
        => _audit.AppendAsync(AuditLog.Create(actorId, action, null, summary, null), ct);

    private static PrivateMessageDto ToDto(PrivateMessage m) => new()
    {
        Id = m.Id,
        SenderId = m.SenderId,
        RecipientId = m.RecipientId,
        Body = m.Body,
        SentAt = m.SentAt,
        IsRead = m.IsRead,
    };
}
