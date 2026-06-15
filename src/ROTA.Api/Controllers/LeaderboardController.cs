using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Domain.Enums;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

// BETA — System 17 Global Leaderboards read endpoints.
// All endpoints require authentication; PlayerId is sourced from the JWT sub claim.
// The controller is thin: validation and board/period logic lives in ILeaderboardService.
[ApiController]
[Route("api/leaderboards")]
[Authorize]
public sealed class LeaderboardController : ControllerBase
{
    private readonly ILeaderboardService _leaderboards;

    public LeaderboardController(ILeaderboardService leaderboards)
    {
        _leaderboards = leaderboards;
    }

    /// <summary>
    /// Returns the discovery list of all available boards, their supported periods,
    /// and the current period_key for each.
    /// </summary>
    [HttpGet]
    [ProducesResponseType(typeof(List<LeaderboardSummary>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetBoards()
    {
        var summaries = await _leaderboards.GetBoardsAsync();
        return Ok(summaries);
    }

    /// <summary>
    /// Returns a ranked page for a specific board.
    /// </summary>
    /// <param name="board">Board name (StatAttack, StatDefense, StatDiscernment, EnergySpent, DamageDealt, LargestHit).</param>
    /// <param name="period">Period granularity (Live, Daily, Weekly, Monthly).</param>
    /// <param name="periodKey">Optional period key; omit for the current period.</param>
    /// <param name="page">1-based page number (default: 1).</param>
    [HttpGet("{board}")]
    [ProducesResponseType(typeof(LeaderboardPageResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> GetBoard(
        string board,
        [FromQuery] string? period,
        [FromQuery] string? periodKey,
        [FromQuery] int page = 1)
    {
        if (!Enum.TryParse<LeaderboardBoard>(board, ignoreCase: true, out var boardEnum))
            return BadRequest(new { message = $"Unknown board '{board}'." });

        if (period != null && !Enum.TryParse<LeaderboardPeriod>(period, ignoreCase: true, out var periodEnum))
            return BadRequest(new { message = $"Unknown period '{period}'." });

        LeaderboardPeriod resolvedPeriod;
        if (period == null)
        {
            resolvedPeriod = DefaultPeriodForBoard(boardEnum);
        }
        else
        {
            Enum.TryParse<LeaderboardPeriod>(period, ignoreCase: true, out resolvedPeriod);
        }

        var result = await _leaderboards.GetPageAsync(
            boardEnum, resolvedPeriod, periodKey, page, GetPlayerId());

        if (!result.Success)
            return BadRequest(new { message = result.ErrorMessage });

        return Ok(result.Page);
    }

    private Guid GetPlayerId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    private static LeaderboardPeriod DefaultPeriodForBoard(LeaderboardBoard board)
        => board switch
        {
            LeaderboardBoard.StatAttack      => LeaderboardPeriod.Live,
            LeaderboardBoard.StatDefense     => LeaderboardPeriod.Live,
            LeaderboardBoard.StatDiscernment => LeaderboardPeriod.Live,
            LeaderboardBoard.LargestHit      => LeaderboardPeriod.Daily,
            LeaderboardBoard.EnergySpent     => LeaderboardPeriod.Weekly,
            LeaderboardBoard.DamageDealt     => LeaderboardPeriod.Weekly,
            _                                => LeaderboardPeriod.Weekly,
        };
}
