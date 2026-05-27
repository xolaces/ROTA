using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

// BETA — thin controller, zero business logic. PlayerId always from verified JWT.
[ApiController]
[Route("api/quests")]
[Authorize]
public sealed class QuestController : ControllerBase
{
    private readonly IQuestService _quests;

    public QuestController(IQuestService quests)
    {
        _quests = quests;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<QuestAvailabilityResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailable()
    {
        var result = await _quests.GetAvailableQuestsAsync(GetPlayerId());
        return Ok(result);
    }

    /// <summary>
    /// Attempts the specified quest at the given difficulty.
    /// Body: { "difficulty": "Normal" } — defaults to Normal if omitted.
    /// </summary>
    [HttpPost("{questId}/attempt")]
    [ProducesResponseType(typeof(QuestResultResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(QuestResultResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(QuestResultResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Attempt(
        [FromRoute] string questId,
        [FromBody] QuestAttemptRequest? request = null)
    {
        var difficultyStr = request?.Difficulty ?? "Normal";

        if (!Enum.TryParse<QuestDifficulty>(difficultyStr, ignoreCase: true, out var difficulty))
            return BadRequest(new { message = $"Invalid difficulty '{difficultyStr}'. Valid values: Normal, Hard, Legendary, Nightmare." });

        var result = await _quests.AttemptQuestAsync(GetPlayerId(), questId, difficulty);

        if (result.Success) return Ok(result);

        return result.FailureCode switch
        {
            QuestFailureCode.QuestNotFound      => NotFound(new { message = result.FailureReason }),
            QuestFailureCode.PlayerNotFound     => NotFound(new { message = result.FailureReason }),
            QuestFailureCode.DifficultyLocked   => StatusCode(StatusCodes.Status403Forbidden, result),
            _                                   => UnprocessableEntity(result),
        };
    }

    // SECURITY: PlayerId always from verified JWT sub claim — never from route or body.
    private Guid GetPlayerId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
