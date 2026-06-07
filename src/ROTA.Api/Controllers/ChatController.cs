using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

/// <summary>World + guild chat history. Live messages flow over the SignalR ChatHub; this serves backfill.</summary>
[ApiController]
[Route("api/chat")]
[Authorize]
public sealed class ChatController : ControllerBase
{
    private readonly IWorldChatStore _world;
    private readonly IGuildChatStore _guild;
    private readonly IPlayerRepository _players;

    public ChatController(IWorldChatStore world, IGuildChatStore guild, IPlayerRepository players)
    {
        _world = world;
        _guild = guild;
        _players = players;
    }

    /// <summary>Recent world-chat messages (up to 100, oldest→newest).</summary>
    [HttpGet("world/history")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatMessageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> WorldHistory([FromQuery] int count = 100)
        => Ok(await _world.GetRecentAsync(count));

    /// <summary>
    /// Recent guild-chat messages for the caller's own guild (up to 100, oldest→newest). Member-gated:
    /// a caller with no guild gets an empty list (mirrors the world-history shape — always 200 with a
    /// list, never an error for the not-in-a-guild case).
    /// </summary>
    // BETA
    [HttpGet("guild/history")]
    [ProducesResponseType(typeof(IReadOnlyList<ChatMessageDto>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ChatMessageDto>>> GuildHistory([FromQuery] int count = 100)
    {
        var player = await _players.FindByIdAsync(PlayerId());
        if (player?.GuildId is null) return Ok(Array.Empty<ChatMessageDto>());
        return Ok(await _guild.GetRecentAsync(player.GuildId.Value, count));
    }

    private Guid PlayerId() => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
