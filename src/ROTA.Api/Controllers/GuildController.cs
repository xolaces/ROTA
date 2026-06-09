using System.IdentityModel.Tokens.Jwt;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>
/// Guild identity, membership, join flow, roles, and lifecycle (System 21 Slice 1). Thin: PlayerId
/// comes from the JWT sub claim; all logic lives in <see cref="IGuildService"/>. Failure codes map to
/// 400 (validation) / 403 (permission) / 404 (not found) / 409 (conflict).
/// </summary>
[ApiController]
[Route("api/guilds")]
[Authorize]
public sealed class GuildController : ControllerBase
{
    private readonly IGuildService _guilds;
    private readonly IValidator<CreateGuildRequest> _createValidator;
    private readonly IValidator<UpdateGuildRequest> _updateValidator;
    private readonly IValidator<GuildInviteRequest> _inviteValidator;

    public GuildController(
        IGuildService guilds,
        IValidator<CreateGuildRequest> createValidator,
        IValidator<UpdateGuildRequest> updateValidator,
        IValidator<GuildInviteRequest> inviteValidator)
    {
        _guilds = guilds;
        _createValidator = createValidator;
        _updateValidator = updateValidator;
        _inviteValidator = inviteValidator;
    }

    // ── Browse + detail ──────────────────────────────────────────────────────

    [HttpGet]
    public async Task<ActionResult<IReadOnlyList<GuildSummaryDto>>> Browse(
        [FromQuery] string? query, [FromQuery] int page = 1)
        => Ok(await _guilds.BrowseGuildsAsync(query, page));

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(GuildDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GuildDetailResponse>> Detail(Guid id)
    {
        var detail = await _guilds.GetGuildAsync(id, PlayerId());
        return detail is null ? NotFound(new { message = "Guild not found." }) : Ok(detail);
    }

    // ── Lifecycle ──────────────────────────────────────────────────────────

    [HttpPost]
    [ProducesResponseType(typeof(object), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Create([FromBody] CreateGuildRequest req)
    {
        var v = await _createValidator.ValidateAsync(req);
        if (!v.IsValid) return Invalid(v);

        var policy = ParsePolicy(req.JoinPolicy) ?? GuildJoinPolicy.Application;
        var result = await _guilds.CreateGuildAsync(PlayerId(), req.Name, req.Tag, req.Description, policy);
        if (!result.Success) return MapCreate(result);

        return CreatedAtAction(nameof(Detail), new { id = result.GuildId }, new { guildId = result.GuildId });
    }

    [HttpPut("{id:guid}")]
    public async Task<IActionResult> Update(Guid id, [FromBody] UpdateGuildRequest req)
    {
        var v = await _updateValidator.ValidateAsync(req);
        if (!v.IsValid) return Invalid(v);

        var policy = req.JoinPolicy is null ? (GuildJoinPolicy?)null : ParsePolicy(req.JoinPolicy);
        var result = await _guilds.UpdateGuildAsync(PlayerId(), id, req.Name, req.Tag, req.Description, req.Motd, policy);
        return Map(result);
    }

    [HttpPost("{id:guid}/disband")]
    public async Task<IActionResult> Disband(Guid id)
        => Map(await _guilds.DisbandGuildAsync(PlayerId(), id));

    // ── Join flow ──────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/apply")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Apply(Guid id)
    {
        var result = await _guilds.ApplyAsync(PlayerId(), id);
        if (!result.Success) return MapApply(result);
        return Ok(new { joined = result.Joined, requestId = result.RequestId });
    }

    [HttpPost("{id:guid}/requests/{reqId:guid}/accept")]
    public async Task<IActionResult> AcceptApplication(Guid id, Guid reqId)
        => Map(await _guilds.AcceptApplicationAsync(PlayerId(), id, reqId));

    [HttpPost("{id:guid}/requests/{reqId:guid}/reject")]
    public async Task<IActionResult> RejectApplication(Guid id, Guid reqId)
        => Map(await _guilds.RejectApplicationAsync(PlayerId(), id, reqId));

    [HttpPost("{id:guid}/invite")]
    public async Task<IActionResult> Invite(Guid id, [FromBody] GuildInviteRequest req)
    {
        var v = await _inviteValidator.ValidateAsync(req);
        if (!v.IsValid) return Invalid(v);
        return Map(await _guilds.InviteAsync(PlayerId(), id, req.Target));
    }

    [HttpPost("invites/{reqId:guid}/accept")]
    public async Task<IActionResult> AcceptInvite(Guid reqId)
        => Map(await _guilds.AcceptInviteAsync(PlayerId(), reqId));

    // ── Membership ─────────────────────────────────────────────────────────

    [HttpPost("{id:guid}/leave")]
    public async Task<IActionResult> Leave(Guid id)
        => Map(await _guilds.LeaveAsync(PlayerId(), id));

    [HttpPost("{id:guid}/members/{playerId:guid}/kick")]
    public async Task<IActionResult> Kick(Guid id, Guid playerId)
        => Map(await _guilds.KickAsync(PlayerId(), id, playerId));

    [HttpPost("{id:guid}/members/{playerId:guid}/promote")]
    public async Task<IActionResult> Promote(Guid id, Guid playerId)
        => Map(await _guilds.PromoteAsync(PlayerId(), id, playerId));

    [HttpPost("{id:guid}/members/{playerId:guid}/demote")]
    public async Task<IActionResult> Demote(Guid id, Guid playerId)
        => Map(await _guilds.DemoteAsync(PlayerId(), id, playerId));

    [HttpPost("{id:guid}/transfer")]
    public async Task<IActionResult> Transfer(Guid id, [FromBody] TransferLeadershipRequest req)
        => Map(await _guilds.TransferLeadershipAsync(PlayerId(), id, req.TargetPlayerId));

    // ── Helpers ──────────────────────────────────────────────────────────────

    private static GuildJoinPolicy? ParsePolicy(string? value)
        => Enum.TryParse<GuildJoinPolicy>(value, ignoreCase: true, out var p) ? p : null;

    private IActionResult Map(GuildActionResult r)
        => r.Success ? Ok(new { message = "OK" }) : MapFailure(r.FailureCode, r.FailureReason);

    private IActionResult MapCreate(CreateGuildResult r)
        => MapFailure(r.FailureCode, r.FailureReason);

    private IActionResult MapApply(ApplyGuildResult r)
        => MapFailure(r.FailureCode, r.FailureReason);

    private IActionResult MapFailure(GuildFailureCode code, string? reason)
    {
        var body = new { message = reason };
        return code switch
        {
            GuildFailureCode.NotFound => NotFound(body),
            GuildFailureCode.Validation => BadRequest(body),
            GuildFailureCode.PermissionDenied => StatusCode(StatusCodes.Status403Forbidden, body),
            GuildFailureCode.DevGuildRestricted => StatusCode(StatusCodes.Status403Forbidden, body), // T43
            _ => Conflict(body), // all remaining states are 409 conflicts (taken / full / already-in / policy / etc.)
        };
    }

    private IActionResult Invalid(FluentValidation.Results.ValidationResult v)
    {
        foreach (var e in v.Errors)
            ModelState.AddModelError(e.PropertyName, e.ErrorMessage);
        return ValidationProblem();
    }

    private Guid PlayerId() => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
