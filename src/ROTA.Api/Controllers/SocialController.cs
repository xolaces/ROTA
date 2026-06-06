using System.IdentityModel.Tokens.Jwt;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using ROTA.Api.SignalR;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>
/// Friends, private messaging, blocking, and player reports (T37). PMs deliver in real time to the
/// recipient over the chat hub; reports route to a PlayerReport operator email (T39).
/// </summary>
[ApiController]
[Route("api/social")]
[Authorize]
public sealed class SocialController : ControllerBase
{
    private readonly ISocialService _social;
    private readonly IHubContext<ChatHub> _hub;
    private readonly IValidator<ReportPlayerRequest> _reportValidator;
    private readonly IValidator<SendMessageRequest> _messageValidator;

    public SocialController(
        ISocialService social,
        IHubContext<ChatHub> hub,
        IValidator<ReportPlayerRequest> reportValidator,
        IValidator<SendMessageRequest> messageValidator)
    {
        _social = social;
        _hub = hub;
        _reportValidator = reportValidator;
        _messageValidator = messageValidator;
    }

    // ---- friends ----

    [HttpGet("friends")]
    public async Task<ActionResult<IReadOnlyList<FriendDto>>> Friends()
        => Ok(await _social.ListFriendsAsync(PlayerId()));

    [HttpPost("friends/request")]
    public async Task<IActionResult> RequestFriend([FromBody] FriendTargetRequest req)
        => Respond(await _social.SendFriendRequestAsync(PlayerId(), req.Target));

    [HttpPost("friends/{friendshipId:guid}/accept")]
    public async Task<IActionResult> AcceptFriend(Guid friendshipId)
        => Respond(await _social.AcceptFriendRequestAsync(PlayerId(), friendshipId));

    [HttpPost("friends/remove")]
    public async Task<IActionResult> RemoveFriend([FromBody] FriendTargetRequest req)
        => Respond(await _social.RemoveFriendAsync(PlayerId(), req.Target));

    // ---- blocks ----

    [HttpGet("blocks")]
    public async Task<ActionResult<IReadOnlyList<BlockDto>>> Blocks()
        => Ok(await _social.ListBlocksAsync(PlayerId()));

    [HttpPost("blocks")]
    public async Task<IActionResult> Block([FromBody] BlockRequest req)
        => Respond(await _social.BlockAsync(PlayerId(), req.Target));

    [HttpPost("blocks/remove")]
    public async Task<IActionResult> Unblock([FromBody] BlockRequest req)
        => Respond(await _social.UnblockAsync(PlayerId(), req.Target));

    // ---- private messages ----

    [HttpGet("messages/{target}")]
    public async Task<ActionResult<IReadOnlyList<PrivateMessageDto>>> Conversation(string target, [FromQuery] int count = 50)
        => Ok(await _social.GetConversationAsync(PlayerId(), target, count));

    [HttpPost("messages")]
    [ProducesResponseType(typeof(PrivateMessageDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SendMessage([FromBody] SendMessageRequest req)
    {
        var v = await _messageValidator.ValidateAsync(req);
        if (!v.IsValid) return Invalid(v);

        var result = await _social.SendMessageAsync(PlayerId(), req.Target, req.Body);
        if (!result.Success)
        {
            if (result.FailureReason?.Contains("not found") == true)
                return NotFound(new { message = result.FailureReason });
            return BadRequest(new { message = result.FailureReason });
        }

        // Real-time delivery to the recipient (PM rides the chat hub via Clients.User → sub).
        await _hub.Clients.User(result.Message!.RecipientId.ToString())
            .SendAsync("PrivateMessage", result.Message);

        return Ok(result.Message);
    }

    // ---- report ----

    [HttpPost("report")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status429TooManyRequests)]
    public async Task<IActionResult> Report([FromBody] ReportPlayerRequest req)
    {
        var v = await _reportValidator.ValidateAsync(req);
        if (!v.IsValid) return Invalid(v);

        var result = await _social.ReportPlayerAsync(PlayerId(), req.Target, req.Reason, req.Description, Ip());
        if (result.Success) return Ok(new { message = "Report submitted." });
        if (result.FailureReason?.Contains("not found") == true)
            return NotFound(new { message = result.FailureReason });
        if (result.FailureReason?.Contains("Too many") == true)
            return StatusCode(StatusCodes.Status429TooManyRequests, new { message = result.FailureReason });
        return BadRequest(new { message = result.FailureReason });
    }

    // ---- helpers ----

    private IActionResult Respond(AdminActionResult r)
    {
        if (r.Success) return Ok(new { message = "OK" });
        if (r.FailureReason?.Contains("not found") == true)
            return NotFound(new { message = r.FailureReason });
        return BadRequest(new { message = r.FailureReason });
    }

    private IActionResult Invalid(FluentValidation.Results.ValidationResult v)
    {
        foreach (var e in v.Errors)
            ModelState.AddModelError(e.PropertyName, e.ErrorMessage);
        return ValidationProblem();
    }

    private string? Ip() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private Guid PlayerId() => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
