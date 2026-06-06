using System.IdentityModel.Tokens.Jwt;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>
/// In-game bug/ticket submission (T38). Any authenticated player can file a Bug or Feedback item; it is
/// validated, rate-limited (anti-spam), and routed to the operator as a BugReport/GeneralTicket email (T39).
/// </summary>
[ApiController]
[Route("api/feedback")]
[Authorize]
public sealed class FeedbackController : ControllerBase
{
    private const int PerPlayerPerHour = 5;
    private const int PerIpPerHour = 15;

    private readonly IEmailNotificationService _emails;
    private readonly ISubmissionRateLimiter _rateLimiter;
    private readonly IValidator<FeedbackRequest> _validator;

    public FeedbackController(
        IEmailNotificationService emails,
        ISubmissionRateLimiter rateLimiter,
        IValidator<FeedbackRequest> validator)
    {
        _emails = emails;
        _rateLimiter = rateLimiter;
        _validator = validator;
    }

    [HttpPost]
    [ProducesResponseType(typeof(FeedbackSubmittedResponse), StatusCodes.Status202Accepted)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Submit([FromBody] FeedbackRequest request)
    {
        var v = await _validator.ValidateAsync(request);
        if (!v.IsValid)
        {
            foreach (var e in v.Errors)
                ModelState.AddModelError(e.PropertyName, e.ErrorMessage);
            return ValidationProblem();
        }

        var playerId = GetPlayerId();
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString();

        if (!await _rateLimiter.TryConsumeAsync("feedback", playerId, ip, PerPlayerPerHour, PerIpPerHour))
            return StatusCode(StatusCodes.Status429TooManyRequests,
                new { message = "Too many submissions in the last hour. Please try again later." });

        var isBug = string.Equals(request.Category, "Bug", StringComparison.OrdinalIgnoreCase);
        var detail = new Dictionary<string, object?>
        {
            ["category"] = request.Category,
            ["description"] = request.Description,
            ["screen"] = request.Screen,
            ["snapshot"] = request.Snapshot,
        };

        var id = await _emails.QueueAsync(new EmailPayload
        {
            Type = isBug ? EmailType.BugReport : EmailType.GeneralTicket,
            Subject = request.Subject,
            Summary = $"{(isBug ? "Bug" : "Feedback")} from {playerId}: {request.Subject}",
            TriggeringPlayerId = playerId,
            TriggeringSystem = "T38",
            Detail = detail,
        }, ip);

        return Accepted(new FeedbackSubmittedResponse { Id = id });
    }

    private Guid GetPlayerId() => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
