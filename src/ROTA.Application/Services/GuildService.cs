using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Validators;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

/// <summary>
/// Guild identity, membership, join flow, roles, and lifecycle (System 21 Slice 1). BETA.
/// Server-authoritative; every state change writes audit_log. Player.GuildId/GuildRank are kept in
/// sync with the GuildMembership row (the membership row is the source of truth; the Player columns
/// are denormalized mirrors for O(1) reads). No guild chat (S2) or guild raids (S3).
///
/// Permission model: ranks compare by integer value (Leader=3 &gt; Officer=2 &gt; Member=1).
/// - Kick/Promote/Demote require actor.Rank &gt; target.Rank (you can only act on someone strictly
///   below you), AND promote/demote additionally require the RESULTING rank &lt; actor.Rank. In the
///   3-tier ladder this means only the Leader changes ranks (an Officer has nothing below Member to
///   raise into Officer without it being ≥ the Officer's own rank) — so officers can't create officers.
/// - Leader-only: disband, rename/tag/description, transfer leadership.
/// - Officer+: invite, accept/reject applications, set MOTD.
/// </summary>
public sealed class GuildService : IGuildService
{
    private readonly IGuildRepository _guilds;
    private readonly IGuildMembershipRepository _memberships;
    private readonly IGuildJoinRequestRepository _requests;
    private readonly IPlayerRepository _players;
    private readonly IAuditLogRepository _audit;
    private readonly GuildConfig _config;

    public GuildService(
        IGuildRepository guilds,
        IGuildMembershipRepository memberships,
        IGuildJoinRequestRepository requests,
        IPlayerRepository players,
        IAuditLogRepository audit,
        IOptions<GuildConfig> config)
    {
        _guilds = guilds;
        _memberships = memberships;
        _requests = requests;
        _players = players;
        _audit = audit;
        _config = config.Value;
    }

    // ════════════════════════════════════════════════════════════ Lifecycle

    public async Task<CreateGuildResult> CreateGuildAsync(
        Guid playerId, string name, string tag, string description, GuildJoinPolicy joinPolicy,
        CancellationToken ct = default)
    {
        var player = await _players.FindByIdAsync(playerId, ct);
        if (player is null)
            return CreateGuildResult.Fail(GuildFailureCode.NotFound, "Player not found.");

        // One guild per player.
        if (player.GuildId is not null || await _memberships.FindByPlayerAsync(playerId, ct) is not null)
            return CreateGuildResult.Fail(GuildFailureCode.AlreadyInGuild, "You are already in a guild.");

        if (player.Level < _config.MinCreationLevel)
            return CreateGuildResult.Fail(GuildFailureCode.InsufficientLevel,
                $"You must be at least level {_config.MinCreationLevel} to create a guild.");

        if (player.Gold < _config.CreationGoldCost)
            return CreateGuildResult.Fail(GuildFailureCode.InsufficientGold,
                $"Creating a guild costs {_config.CreationGoldCost} gold.");

        // Name/tag validation (length + reserved + uniqueness). Validator covers length/charset/reserved
        // at the edge; re-check uniqueness + reserved here as the authoritative server gate.
        var nameCheck = ValidateName(name);
        if (nameCheck is not null) return CreateGuildResult.Fail(nameCheck.Value.code, nameCheck.Value.reason);
        var tagCheck = ValidateTag(tag);
        if (tagCheck is not null) return CreateGuildResult.Fail(tagCheck.Value.code, tagCheck.Value.reason);

        if (await _guilds.NameExistsAsync(name, null, ct))
            return CreateGuildResult.Fail(GuildFailureCode.NameTaken, "That guild name is already taken.");
        if (await _guilds.TagExistsAsync(tag, null, ct))
            return CreateGuildResult.Fail(GuildFailureCode.TagTaken, "That guild tag is already taken.");

        // Deduct gold (guard ≥ 0 — checked above, AddGold with a negative amount).
        player.AddGold(-_config.CreationGoldCost);

        var guild = Guild.Create(playerId, name.Trim(), tag.Trim(), description ?? string.Empty, joinPolicy, _config.MemberCap);
        await _guilds.CreateAsync(guild, ct);

        await _memberships.CreateAsync(GuildMembership.Create(guild.Id, playerId, GuildRank.Leader), ct);

        player.JoinGuild(guild.Id, GuildRank.Leader);
        await _players.UpdateAsync(player, ct);

        await Audit(playerId, "GuildCreated",
            $"guild={guild.Id} name={guild.Name} tag={guild.Tag} cost={_config.CreationGoldCost}", ct);

        return CreateGuildResult.Ok(guild.Id);
    }

    public async Task<GuildActionResult> DisbandGuildAsync(Guid playerId, Guid guildId, CancellationToken ct = default)
    {
        var guild = await _guilds.FindByIdAsync(guildId, ct);
        if (guild is null) return NotFound("Guild not found.");
        if (guild.LeaderId != playerId)
            return Denied("Only the guild leader can disband the guild.");

        // Release every member, then soft-delete the guild.
        var members = await _memberships.GetForGuildAsync(guildId, ct);
        foreach (var m in members)
        {
            m.SoftDelete();
            await _memberships.UpdateAsync(m, ct);

            var p = await _players.FindByIdAsync(m.PlayerId, ct);
            if (p is not null)
            {
                p.LeaveGuild();
                await _players.UpdateAsync(p, ct);
            }
        }

        guild.Disband();
        await _guilds.UpdateAsync(guild, ct);

        await Audit(playerId, "GuildDisbanded", $"guild={guildId} releasedMembers={members.Count}", ct);
        return GuildActionResult.Ok();
    }

    public async Task<GuildActionResult> UpdateGuildAsync(
        Guid actorId, Guid guildId, string? name, string? tag, string? description, string? motd,
        GuildJoinPolicy? joinPolicy, CancellationToken ct = default)
    {
        var guild = await _guilds.FindByIdAsync(guildId, ct);
        if (guild is null) return NotFound("Guild not found.");

        var actor = await _memberships.FindByGuildAndPlayerAsync(guildId, actorId, ct);
        if (actor is null) return Conflict(GuildFailureCode.NotInGuild, "You are not a member of this guild.");

        bool changingLeaderFields = name is not null || tag is not null || description is not null || joinPolicy is not null;
        bool isLeader = actor.Rank == GuildRank.Leader;
        bool isOfficerPlus = actor.Rank >= GuildRank.Officer;

        // Leader-only: name / tag / description / join policy. Officer+: MOTD.
        if (changingLeaderFields && !isLeader)
            return Denied("Only the guild leader can change the name, tag, description, or join policy.");
        if (motd is not null && !isOfficerPlus)
            return Denied("Only officers and the leader can set the MOTD.");

        if (name is not null)
        {
            var check = ValidateName(name);
            if (check is not null) return GuildActionResult.Fail(check.Value.code, check.Value.reason);
            if (await _guilds.NameExistsAsync(name, guildId, ct))
                return Conflict(GuildFailureCode.NameTaken, "That guild name is already taken.");
            guild.Rename(name.Trim());
        }

        if (tag is not null)
        {
            var check = ValidateTag(tag);
            if (check is not null) return GuildActionResult.Fail(check.Value.code, check.Value.reason);
            if (await _guilds.TagExistsAsync(tag, guildId, ct))
                return Conflict(GuildFailureCode.TagTaken, "That guild tag is already taken.");
            guild.SetTag(tag.Trim());
        }

        if (description is not null) guild.SetDescription(description);
        if (motd is not null) guild.SetMotd(motd);
        if (joinPolicy is not null) guild.SetJoinPolicy(joinPolicy.Value);

        await _guilds.UpdateAsync(guild, ct);
        await Audit(actorId, "GuildUpdated",
            $"guild={guildId} name={(name is not null)} tag={(tag is not null)} desc={(description is not null)} motd={(motd is not null)} policy={(joinPolicy?.ToString() ?? "-")}", ct);
        return GuildActionResult.Ok();
    }

    // ════════════════════════════════════════════════════════════ Join flow

    public async Task<ApplyGuildResult> ApplyAsync(Guid playerId, Guid guildId, CancellationToken ct = default)
    {
        var player = await _players.FindByIdAsync(playerId, ct);
        if (player is null) return ApplyGuildResult.Fail(GuildFailureCode.NotFound, "Player not found.");
        if (player.GuildId is not null || await _memberships.FindByPlayerAsync(playerId, ct) is not null)
            return ApplyGuildResult.Fail(GuildFailureCode.AlreadyInGuild, "You are already in a guild.");

        var guild = await _guilds.FindByIdAsync(guildId, ct);
        if (guild is null) return ApplyGuildResult.Fail(GuildFailureCode.NotFound, "Guild not found.");

        switch (guild.JoinPolicy)
        {
            case GuildJoinPolicy.Open:
            {
                if (await _memberships.CountActiveAsync(guildId, ct) >= guild.MemberCap)
                    return ApplyGuildResult.Fail(GuildFailureCode.MemberCapReached, "That guild is full.");
                await JoinAsMemberAsync(guild, player, ct);
                await Audit(playerId, "GuildJoined", $"guild={guildId} policy=Open", ct);
                return ApplyGuildResult.JoinedNow();
            }

            case GuildJoinPolicy.Application:
            {
                // Idempotent: a duplicate pending application returns the existing one.
                var existing = await _requests.FindPendingAsync(guildId, playerId, GuildJoinRequestKind.Application, ct);
                if (existing is not null) return ApplyGuildResult.Pending(existing.Id);

                var req = await _requests.CreateAsync(
                    GuildJoinRequest.Create(guildId, playerId, GuildJoinRequestKind.Application), ct);
                await Audit(playerId, "GuildApplicationCreated", $"guild={guildId} request={req.Id}", ct);
                return ApplyGuildResult.Pending(req.Id);
            }

            case GuildJoinPolicy.InviteOnly:
            default:
                return ApplyGuildResult.Fail(GuildFailureCode.PolicyForbidsApply,
                    "This guild is invite-only; you cannot apply.");
        }
    }

    public async Task<GuildActionResult> AcceptApplicationAsync(Guid actorId, Guid guildId, Guid requestId, CancellationToken ct = default)
    {
        var guild = await _guilds.FindByIdAsync(guildId, ct);
        if (guild is null) return NotFound("Guild not found.");

        var perm = await RequireOfficerPlusAsync(actorId, guildId, ct);
        if (perm is not null) return perm;

        var req = await _requests.FindByIdAsync(requestId, ct);
        if (req is null || req.GuildId != guildId || req.Kind != GuildJoinRequestKind.Application)
            return NotFound("Application not found.");
        if (req.Status != GuildJoinRequestStatus.Pending)
            return Conflict(GuildFailureCode.Conflict, "That application is no longer pending.");

        var target = await _players.FindByIdAsync(req.PlayerId, ct);
        if (target is null) return NotFound("Applicant not found.");
        if (target.GuildId is not null || await _memberships.FindByPlayerAsync(req.PlayerId, ct) is not null)
        {
            // Applicant joined elsewhere in the meantime — mark the stale request rejected.
            req.Reject();
            await _requests.UpdateAsync(req, ct);
            return Conflict(GuildFailureCode.AlreadyInGuild, "The applicant has already joined a guild.");
        }
        if (await _memberships.CountActiveAsync(guildId, ct) >= guild.MemberCap)
            return Conflict(GuildFailureCode.MemberCapReached, "The guild is full.");

        await JoinAsMemberAsync(guild, target, ct);
        req.Accept();
        await _requests.UpdateAsync(req, ct);

        await Audit(actorId, "GuildApplicationAccepted", $"guild={guildId} request={requestId} player={target.Id}", ct);
        return GuildActionResult.Ok();
    }

    public async Task<GuildActionResult> RejectApplicationAsync(Guid actorId, Guid guildId, Guid requestId, CancellationToken ct = default)
    {
        var guild = await _guilds.FindByIdAsync(guildId, ct);
        if (guild is null) return NotFound("Guild not found.");

        var perm = await RequireOfficerPlusAsync(actorId, guildId, ct);
        if (perm is not null) return perm;

        var req = await _requests.FindByIdAsync(requestId, ct);
        if (req is null || req.GuildId != guildId || req.Kind != GuildJoinRequestKind.Application)
            return NotFound("Application not found.");
        if (req.Status != GuildJoinRequestStatus.Pending)
            return Conflict(GuildFailureCode.Conflict, "That application is no longer pending.");

        req.Reject();
        await _requests.UpdateAsync(req, ct);
        await Audit(actorId, "GuildApplicationRejected", $"guild={guildId} request={requestId} player={req.PlayerId}", ct);
        return GuildActionResult.Ok();
    }

    public async Task<GuildActionResult> InviteAsync(Guid actorId, Guid guildId, string targetUsernameOrId, CancellationToken ct = default)
    {
        var guild = await _guilds.FindByIdAsync(guildId, ct);
        if (guild is null) return NotFound("Guild not found.");

        var perm = await RequireOfficerPlusAsync(actorId, guildId, ct);
        if (perm is not null) return perm;

        var target = await ResolveAsync(targetUsernameOrId, ct);
        if (target is null) return NotFound($"Player '{targetUsernameOrId}' not found.");
        if (target.GuildId is not null || await _memberships.FindByPlayerAsync(target.Id, ct) is not null)
            return Conflict(GuildFailureCode.AlreadyInGuild, "That player is already in a guild.");

        // Idempotent: a duplicate pending invite returns OK without a second row.
        var existing = await _requests.FindPendingAsync(guildId, target.Id, GuildJoinRequestKind.Invite, ct);
        if (existing is not null) return GuildActionResult.Ok();

        await _requests.CreateAsync(GuildJoinRequest.Create(guildId, target.Id, GuildJoinRequestKind.Invite), ct);
        await Audit(actorId, "GuildInviteSent", $"guild={guildId} target={target.Id}", ct);
        return GuildActionResult.Ok();
    }

    public async Task<GuildActionResult> AcceptInviteAsync(Guid playerId, Guid requestId, CancellationToken ct = default)
    {
        var req = await _requests.FindByIdAsync(requestId, ct);
        if (req is null || req.Kind != GuildJoinRequestKind.Invite)
            return NotFound("Invite not found.");
        if (req.PlayerId != playerId)
            return Denied("This invite is addressed to another player.");
        if (req.Status != GuildJoinRequestStatus.Pending)
            return Conflict(GuildFailureCode.Conflict, "That invite is no longer pending.");

        var player = await _players.FindByIdAsync(playerId, ct);
        if (player is null) return NotFound("Player not found.");
        if (player.GuildId is not null || await _memberships.FindByPlayerAsync(playerId, ct) is not null)
            return Conflict(GuildFailureCode.AlreadyInGuild, "You are already in a guild.");

        var guild = await _guilds.FindByIdAsync(req.GuildId, ct);
        if (guild is null) return NotFound("Guild not found.");
        if (await _memberships.CountActiveAsync(guild.Id, ct) >= guild.MemberCap)
            return Conflict(GuildFailureCode.MemberCapReached, "The guild is full.");

        await JoinAsMemberAsync(guild, player, ct);
        req.Accept();
        await _requests.UpdateAsync(req, ct);

        await Audit(playerId, "GuildInviteAccepted", $"guild={guild.Id} request={requestId}", ct);
        return GuildActionResult.Ok();
    }

    // ════════════════════════════════════════════════════════════ Membership

    public async Task<GuildActionResult> LeaveAsync(Guid playerId, Guid guildId, CancellationToken ct = default)
    {
        var membership = await _memberships.FindByGuildAndPlayerAsync(guildId, playerId, ct);
        if (membership is null) return Conflict(GuildFailureCode.NotInGuild, "You are not a member of this guild.");
        if (membership.Rank == GuildRank.Leader)
            return Conflict(GuildFailureCode.LeaderCannotLeave,
                "The leader cannot leave — transfer leadership or disband the guild first.");

        await RemoveMemberAsync(guildId, playerId, membership, ct);
        await Audit(playerId, "GuildLeft", $"guild={guildId}", ct);
        return GuildActionResult.Ok();
    }

    public async Task<GuildActionResult> KickAsync(Guid actorId, Guid guildId, Guid targetPlayerId, CancellationToken ct = default)
    {
        var guild = await _guilds.FindByIdAsync(guildId, ct);
        if (guild is null) return NotFound("Guild not found.");

        var actor = await _memberships.FindByGuildAndPlayerAsync(guildId, actorId, ct);
        if (actor is null) return Conflict(GuildFailureCode.NotInGuild, "You are not a member of this guild.");

        var target = await _memberships.FindByGuildAndPlayerAsync(guildId, targetPlayerId, ct);
        if (target is null) return NotFound("That player is not a member of this guild.");

        if (actorId == targetPlayerId)
            return Denied("Use leave, not kick, to remove yourself.");
        // actor.Rank must be strictly greater than target.Rank (officer kicks members; leader kicks
        // officers + members; nobody kicks an equal/higher rank; the leader can't be kicked).
        if ((int)actor.Rank <= (int)target.Rank)
            return Denied("You can only kick members ranked below you.");

        await RemoveMemberAsync(guildId, targetPlayerId, target, ct);
        await Audit(actorId, "GuildMemberKicked", $"guild={guildId} target={targetPlayerId} targetRank={target.Rank}", ct);
        return GuildActionResult.Ok();
    }

    public Task<GuildActionResult> PromoteAsync(Guid actorId, Guid guildId, Guid targetPlayerId, CancellationToken ct = default)
        => ChangeRankAsync(actorId, guildId, targetPlayerId, promote: true, ct);

    public Task<GuildActionResult> DemoteAsync(Guid actorId, Guid guildId, Guid targetPlayerId, CancellationToken ct = default)
        => ChangeRankAsync(actorId, guildId, targetPlayerId, promote: false, ct);

    private async Task<GuildActionResult> ChangeRankAsync(
        Guid actorId, Guid guildId, Guid targetPlayerId, bool promote, CancellationToken ct)
    {
        var guild = await _guilds.FindByIdAsync(guildId, ct);
        if (guild is null) return NotFound("Guild not found.");

        var actor = await _memberships.FindByGuildAndPlayerAsync(guildId, actorId, ct);
        if (actor is null) return Conflict(GuildFailureCode.NotInGuild, "You are not a member of this guild.");

        var target = await _memberships.FindByGuildAndPlayerAsync(guildId, targetPlayerId, ct);
        if (target is null) return NotFound("That player is not a member of this guild.");

        if (actorId == targetPlayerId)
            return Denied("You cannot change your own rank.");

        // Compute the resulting rank (clamped to the Member..Officer band — Leader is only via transfer).
        var newRank = promote
            ? (GuildRank)Math.Min((int)target.Rank + 1, (int)GuildRank.Officer)
            : (GuildRank)Math.Max((int)target.Rank - 1, (int)GuildRank.Member);

        if (newRank == target.Rank)
            return Conflict(GuildFailureCode.Conflict, promote
                ? "That member is already at the highest rank you can promote to."
                : "That member is already at the lowest rank.");

        // Permission: actor must outrank the target AND outrank the RESULTING rank. In 3-tier this
        // means only the Leader can promote Member→Officer / demote Officer→Member; an Officer fails
        // the resulting-rank check (Officer is not < Officer), so officers can't create officers.
        if ((int)actor.Rank <= (int)target.Rank || (int)newRank >= (int)actor.Rank)
            return Denied("You do not have permission to change this member's rank.");

        target.SetRank(newRank);
        await _memberships.UpdateAsync(target, ct);

        var targetPlayer = await _players.FindByIdAsync(targetPlayerId, ct);
        if (targetPlayer is not null)
        {
            targetPlayer.SetGuildRank(newRank);
            await _players.UpdateAsync(targetPlayer, ct);
        }

        await Audit(actorId, promote ? "GuildMemberPromoted" : "GuildMemberDemoted",
            $"guild={guildId} target={targetPlayerId} newRank={newRank}", ct);
        return GuildActionResult.Ok();
    }

    public async Task<GuildActionResult> TransferLeadershipAsync(Guid leaderId, Guid guildId, Guid targetPlayerId, CancellationToken ct = default)
    {
        var guild = await _guilds.FindByIdAsync(guildId, ct);
        if (guild is null) return NotFound("Guild not found.");
        if (guild.LeaderId != leaderId)
            return Denied("Only the guild leader can transfer leadership.");
        if (leaderId == targetPlayerId)
            return Conflict(GuildFailureCode.Conflict, "You are already the leader.");

        var leaderMembership = await _memberships.FindByGuildAndPlayerAsync(guildId, leaderId, ct);
        var targetMembership = await _memberships.FindByGuildAndPlayerAsync(guildId, targetPlayerId, ct);
        if (targetMembership is null) return NotFound("That player is not a member of this guild.");
        if (leaderMembership is null) return Conflict(GuildFailureCode.NotInGuild, "Leader membership missing.");

        // Target → Leader, old leader → Officer.
        targetMembership.SetRank(GuildRank.Leader);
        await _memberships.UpdateAsync(targetMembership, ct);
        leaderMembership.SetRank(GuildRank.Officer);
        await _memberships.UpdateAsync(leaderMembership, ct);

        guild.SetLeader(targetPlayerId);
        await _guilds.UpdateAsync(guild, ct);

        await SyncRankAsync(targetPlayerId, GuildRank.Leader, ct);
        await SyncRankAsync(leaderId, GuildRank.Officer, ct);

        await Audit(leaderId, "GuildLeadershipTransferred", $"guild={guildId} from={leaderId} to={targetPlayerId}", ct);
        return GuildActionResult.Ok();
    }

    // ════════════════════════════════════════════════════════════ Succession

    public async Task<GuildActionResult> RunInactivitySuccessionAsync(Guid guildId, CancellationToken ct = default)
    {
        var guild = await _guilds.FindByIdAsync(guildId, ct);
        if (guild is null) return NotFound("Guild not found.");

        var members = await _memberships.GetForGuildAsync(guildId, ct);
        var leader = members.FirstOrDefault(m => m.Rank == GuildRank.Leader);
        if (leader is null)
            return Conflict(GuildFailureCode.NotInGuild, "Guild has no active leader membership.");

        var cutoff = DateTimeOffset.UtcNow.AddDays(-_config.LeaderInactivityDays);
        if (leader.LastActiveAt > cutoff)
            return Conflict(GuildFailureCode.Conflict, "The leader is not inactive.");

        // Most-active officer: highest LastActiveAt, tiebreak highest ContributionTotal.
        var successor = members
            .Where(m => m.Rank == GuildRank.Officer)
            .OrderByDescending(m => m.LastActiveAt)
            .ThenByDescending(m => m.ContributionTotal)
            .FirstOrDefault();

        if (successor is null)
            return Conflict(GuildFailureCode.NotInGuild, "No officer is available to succeed the leader.");

        // Re-fetch as tracked entities to mutate (GetForGuildAsync returns AsNoTracking copies).
        var leaderTracked = await _memberships.FindByGuildAndPlayerAsync(guildId, leader.PlayerId, ct);
        var successorTracked = await _memberships.FindByGuildAndPlayerAsync(guildId, successor.PlayerId, ct);
        if (leaderTracked is null || successorTracked is null)
            return Conflict(GuildFailureCode.Conflict, "Membership state changed during succession.");

        successorTracked.SetRank(GuildRank.Leader);
        await _memberships.UpdateAsync(successorTracked, ct);
        leaderTracked.SetRank(GuildRank.Officer);
        await _memberships.UpdateAsync(leaderTracked, ct);

        guild.SetLeader(successor.PlayerId);
        await _guilds.UpdateAsync(guild, ct);

        await SyncRankAsync(successor.PlayerId, GuildRank.Leader, ct);
        await SyncRankAsync(leader.PlayerId, GuildRank.Officer, ct);

        await Audit(null, "GuildInactivitySuccession",
            $"guild={guildId} oldLeader={leader.PlayerId} newLeader={successor.PlayerId} inactiveSince={leader.LastActiveAt:O}", ct);
        return GuildActionResult.Ok();
    }

    // ════════════════════════════════════════════════════════════ Reads

    public async Task<GuildDetailResponse?> GetGuildAsync(Guid guildId, Guid callerId, CancellationToken ct = default)
    {
        var guild = await _guilds.FindByIdAsync(guildId, ct);
        if (guild is null) return null;

        var roster = await _guilds.GetRosterAsync(guildId, ct);
        var callerMembership = await _memberships.FindByGuildAndPlayerAsync(guildId, callerId, ct);
        var callerRank = callerMembership?.Rank;

        // Officer+ sees the pending application/invite queue.
        IReadOnlyList<GuildJoinRequestDto> pending = Array.Empty<GuildJoinRequestDto>();
        if (callerRank is not null && callerRank.Value >= GuildRank.Officer)
        {
            var reqs = await _requests.GetPendingForGuildAsync(guildId, ct);
            pending = reqs.Select(r => new GuildJoinRequestDto
            {
                Id = r.Id,
                GuildId = r.GuildId,
                PlayerId = r.PlayerId,
                Kind = r.Kind.ToString(),
                Status = r.Status.ToString(),
                CreatedAt = r.CreatedAt,
            }).ToList();
        }

        return new GuildDetailResponse
        {
            Id = guild.Id,
            Name = guild.Name,
            Tag = guild.Tag,
            Description = guild.Description,
            Motd = guild.Motd,
            CrestId = guild.CrestId,
            JoinPolicy = guild.JoinPolicy.ToString(),
            Level = guild.Level,
            Xp = guild.Xp,
            MemberCount = guild.MemberCount,
            MemberCap = guild.MemberCap,
            LeaderId = guild.LeaderId,
            CallerRank = callerRank?.ToString(),
            Members = roster.Select(r => new GuildMemberDto
            {
                PlayerId = r.PlayerId,
                Username = r.Username,
                DisplayName = string.IsNullOrWhiteSpace(r.DisplayName) ? r.Username : r.DisplayName,
                Level = r.Level,
                Rank = r.Rank.ToString(),
                ContributionTotal = r.ContributionTotal,
                JoinedAt = r.JoinedAt,
                LastActiveAt = r.LastActiveAt,
            }).ToList(),
            PendingRequests = pending,
        };
    }

    public async Task<IReadOnlyList<GuildSummaryDto>> BrowseGuildsAsync(string? query, int page, CancellationToken ct = default)
    {
        const int pageSize = 25;
        if (page < 1) page = 1;
        var rows = await _guilds.BrowseAsync(query, page, pageSize, ct);
        return rows.Select(e => new GuildSummaryDto
        {
            Id = e.Guild.Id,
            Name = e.Guild.Name,
            Tag = e.Guild.Tag,
            Description = e.Guild.Description,
            JoinPolicy = e.Guild.JoinPolicy.ToString(),
            Level = e.Guild.Level,
            MemberCount = e.Guild.MemberCount,
            MemberCap = e.Guild.MemberCap,
            LeaderDisplayName = e.LeaderDisplayName,
        }).ToList();
    }

    // ════════════════════════════════════════════════════════════ Helpers

    /// <summary>Adds a player as a Member: membership row + Player sync + guild count increment.</summary>
    private async Task JoinAsMemberAsync(Guild guild, Player player, CancellationToken ct)
    {
        await _memberships.CreateAsync(GuildMembership.Create(guild.Id, player.Id, GuildRank.Member), ct);
        player.JoinGuild(guild.Id, GuildRank.Member);
        await _players.UpdateAsync(player, ct);
        guild.IncrementMemberCount();
        await _guilds.UpdateAsync(guild, ct);
    }

    /// <summary>Soft-deletes a membership, clears the Player's guild fields, decrements the count.</summary>
    private async Task RemoveMemberAsync(Guid guildId, Guid playerId, GuildMembership membership, CancellationToken ct)
    {
        membership.SoftDelete();
        await _memberships.UpdateAsync(membership, ct);

        var player = await _players.FindByIdAsync(playerId, ct);
        if (player is not null)
        {
            player.LeaveGuild();
            await _players.UpdateAsync(player, ct);
        }

        var guild = await _guilds.FindByIdAsync(guildId, ct);
        if (guild is not null)
        {
            guild.DecrementMemberCount();
            await _guilds.UpdateAsync(guild, ct);
        }
    }

    private async Task SyncRankAsync(Guid playerId, GuildRank rank, CancellationToken ct)
    {
        var player = await _players.FindByIdAsync(playerId, ct);
        if (player is null) return;
        player.SetGuildRank(rank);
        await _players.UpdateAsync(player, ct);
    }

    /// <summary>Returns a permission failure if the actor is not at least an Officer in the guild; else null.</summary>
    private async Task<GuildActionResult?> RequireOfficerPlusAsync(Guid actorId, Guid guildId, CancellationToken ct)
    {
        var actor = await _memberships.FindByGuildAndPlayerAsync(guildId, actorId, ct);
        if (actor is null) return Conflict(GuildFailureCode.NotInGuild, "You are not a member of this guild.");
        if (actor.Rank < GuildRank.Officer) return Denied("This action requires officer rank or higher.");
        return null;
    }

    private async Task<Player?> ResolveAsync(string usernameOrId, CancellationToken ct)
        => Guid.TryParse(usernameOrId, out var id)
            ? await _players.FindByIdAsync(id, ct)
            : await _players.FindByUsernameAsync(usernameOrId, ct);

    private Task Audit(Guid? actorId, string action, string summary, CancellationToken ct)
        => _audit.AppendAsync(AuditLog.Create(actorId, action, null, summary, null), ct);

    // ── Validation (length + reserved). Uniqueness is checked against the repo separately. ──

    private (GuildFailureCode code, string reason)? ValidateName(string name)
    {
        var n = (name ?? string.Empty).Trim();
        if (n.Length == 0) return (GuildFailureCode.Validation, "Guild name is required.");
        if (n.Length > _config.NameMaxLength)
            return (GuildFailureCode.Validation, $"Guild name must be at most {_config.NameMaxLength} characters.");
        if (ReservedUsernames.IsReserved(n)) return (GuildFailureCode.Validation, "That guild name is reserved.");
        return null;
    }

    private (GuildFailureCode code, string reason)? ValidateTag(string tag)
    {
        var t = (tag ?? string.Empty).Trim();
        if (t.Length < _config.TagMinLength || t.Length > _config.TagMaxLength)
            return (GuildFailureCode.Validation,
                $"Guild tag must be {_config.TagMinLength}–{_config.TagMaxLength} characters.");
        if (ReservedUsernames.IsReserved(t)) return (GuildFailureCode.Validation, "That guild tag is reserved.");
        return null;
    }

    // Result sugar.
    private static GuildActionResult NotFound(string reason) => GuildActionResult.Fail(GuildFailureCode.NotFound, reason);
    private static GuildActionResult Denied(string reason) => GuildActionResult.Fail(GuildFailureCode.PermissionDenied, reason);
    private static GuildActionResult Conflict(GuildFailureCode code, string reason) => GuildActionResult.Fail(code, reason);
}
