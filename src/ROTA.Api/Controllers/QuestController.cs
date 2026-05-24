using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
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

    /// <summary>Returns all quests the authenticated player has unlocked.</summary>
    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<QuestAvailabilityResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailable()
    {
        var result = await _quests.GetAvailableQuestsAsync(GetPlayerId());
        return Ok(result);
    }

    /// <summary>
    /// Attempts the specified quest: spends energy and grants rewards on success.
    /// Returns 200 on success or 422 when the attempt fails (insufficient energy, prerequisite, etc.).
    /// Quest not found returns 404.
    /// </summary>
    [HttpPost("{questId}/attempt")]
    [ProducesResponseType(typeof(QuestResultResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(QuestResultResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Attempt([FromRoute] string questId)
    {
        var result = await _quests.AttemptQuestAsync(GetPlayerId(), questId);

        if (result.Success) return Ok(result);

        return result.FailureCode switch
        {
            QuestFailureCode.QuestNotFound  => NotFound(new { message = result.FailureReason }),
            QuestFailureCode.PlayerNotFound => NotFound(new { message = result.FailureReason }),
            _                               => UnprocessableEntity(result),
        };
    }

    // SECURITY: PlayerId always from verified JWT sub claim — never from route or body.
    private Guid GetPlayerId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}
