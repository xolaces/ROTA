using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>World-chat history (T36). Live messages flow over the SignalR ChatHub; this serves backfill.</summary>
[ApiController]
[Route("api/chat")]
[Authorize]
public sealed class ChatController : ControllerBase
{
    private readonly IWorldChatStore _world;

    public ChatController(IWorldChatStore world) => _world = world;

    /// <summary>Recent world-chat messages (up to 100, oldest→newest).</summary>
    [HttpGet("world/history")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatMessageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> WorldHistory([FromQuery] int count = 100)
        => Ok(await _world.GetRecentAsync(count));
}
