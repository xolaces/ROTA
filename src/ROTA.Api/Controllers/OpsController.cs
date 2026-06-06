using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>
/// Admin-only ops surface for the operator dashboard: read + triage the <c>outbound_emails</c> log.
/// No automated action is ever taken — the operator approves/dismisses each entry manually.
/// </summary>
[ApiController]
[Route("api/admin/emails")]
[Authorize(Policy = "AdminOnly")]
public sealed class OpsController : ControllerBase
{
    private readonly IOutboundEmailRepository _emails;
    private readonly IAuditLogRepository _audit;
    private readonly IPinnacleClaimRepository _pinnacleClaims;

    public OpsController(
        IOutboundEmailRepository emails,
        IAuditLogRepository audit,
        IPinnacleClaimRepository pinnacleClaims)
    {
        _emails = emails;
        _audit = audit;
        _pinnacleClaims = pinnacleClaims;
    }

    /// <summary>Paged, filterable list of operator emails (newest first).</summary>
    [HttpGet]
    [ProducesResponseType(typeof(OutboundEmailListResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OutboundEmailListResponse>> List(
        [FromQuery] string? type,
        [FromQuery] string? reviewStatus,
        [FromQuery] string? search,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50)
    {
        EmailType? typeFilter = Enum.TryParse<EmailType>(type, ignoreCase: true, out var t) ? t : null;
        EmailReviewStatus? reviewFilter = Enum.TryParse<EmailReviewStatus>(reviewStatus, ignoreCase: true, out var r) ? r : null;

        var result = await _emails.ListAsync(typeFilter, reviewFilter, search, page, pageSize);

        return Ok(new OutboundEmailListResponse
        {
            Items = result.Items.Select(Map).ToList(),
            Total = result.Total,
            Page = page < 1 ? 1 : page,
            PageSize = pageSize,
        });
    }

    /// <summary>Aggregate counts for the dashboard overview.</summary>
    [HttpGet("stats")]
    [ProducesResponseType(typeof(OpsEmailStatsResponse), StatusCodes.Status200OK)]
    public async Task<ActionResult<OpsEmailStatsResponse>> Stats()
    {
        var s = await _emails.GetStatsAsync();
        return Ok(new OpsEmailStatsResponse
        {
            Total = s.Total,
            Pending = s.Pending,
            Approved = s.Approved,
            Dismissed = s.Dismissed,
            Sent = s.Sent,
            Failed = s.Failed,
            ByType = s.ByType,
        });
    }

    /// <summary>Pinnacle first-claim leaderboard (T33) — who reached each pinnacle level first.</summary>
    [HttpGet("/api/admin/pinnacle-claims")]
    [ProducesResponseType(typeof(IReadOnlyList<PinnacleClaimResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<PinnacleClaimResponse>>> PinnacleClaims()
    {
        var claims = await _pinnacleClaims.ListAsync();
        return Ok(claims.Select(c => new PinnacleClaimResponse
        {
            PinnacleLevel = c.PinnacleLevel,
            PlayerId = c.PlayerId,
            ClaimedAt = c.ClaimedAt,
        }).ToList());
    }

    /// <summary>Full detail for a single email.</summary>
    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OutboundEmailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OutboundEmailResponse>> Get(Guid id)
    {
        var email = await _emails.GetByIdAsync(id);
        return email is null ? NotFound() : Ok(Map(email));
    }

    /// <summary>Operator triage: mark approved/acknowledged.</summary>
    [HttpPost("{id:guid}/approve")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Approve(Guid id) => Triage(id, approve: true);

    /// <summary>Operator triage: dismiss (no action needed).</summary>
    [HttpPost("{id:guid}/dismiss")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public Task<IActionResult> Dismiss(Guid id) => Triage(id, approve: false);

    private async Task<IActionResult> Triage(Guid id, bool approve)
    {
        var email = await _emails.GetByIdAsync(id);
        if (email is null) return NotFound();

        var actorId = GetActorId();
        if (approve) email.Approve(actorId);
        else email.Dismiss(actorId);
        await _emails.UpdateAsync(email);

        await _audit.AppendAsync(AuditLog.Create(
            actorId,
            approve ? "EmailApproved" : "EmailDismissed",
            inputHash: null,
            resultSummary: $"email={id} type={email.Type}",
            ipAddress: HttpContext.Connection.RemoteIpAddress?.ToString()));

        return NoContent();
    }

    private static OutboundEmailResponse Map(OutboundEmail e) => new()
    {
        Id = e.Id,
        Type = e.Type.ToString(),
        Subject = e.Subject,
        Recipient = e.Recipient,
        TriggeringPlayerId = e.TriggeringPlayerId,
        TriggeringSystem = e.TriggeringSystem,
        Summary = e.Summary,
        Detail = e.DetailJson,
        Metadata = e.MetadataJson,
        SendStatus = e.SendStatus.ToString(),
        SendAttempts = e.SendAttempts,
        LastSendError = e.LastSendError,
        ReviewStatus = e.ReviewStatus.ToString(),
        ReviewedBy = e.ReviewedBy,
        ReviewedAt = e.ReviewedAt,
        CreatedAt = e.CreatedAt,
    };

    private Guid GetActorId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
