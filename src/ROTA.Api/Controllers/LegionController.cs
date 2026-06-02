using System.IdentityModel.Tokens.Jwt;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

[Authorize]
[ApiController]
public class LegionController : ControllerBase
{
    private readonly ILegionService _legions;

    public LegionController(ILegionService legions) => _legions = legions;

    // ----------------------------------------------------------------
    // Slice 2 — ownership
    // ----------------------------------------------------------------

    [HttpGet("api/units")]
    [ProducesResponseType(typeof(IReadOnlyList<OwnedUnitResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetUnits()
        => Ok(await _legions.GetOwnedUnitsAsync(GetPlayerId()));

    [HttpGet("api/legions")]
    [ProducesResponseType(typeof(IReadOnlyList<OwnedLegionResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetLegions()
        => Ok(await _legions.GetOwnedLegionsAsync(GetPlayerId()));

    // ----------------------------------------------------------------
    // Slice 3 — assembly (endpoints wired in Slice 3 commit)
    // ----------------------------------------------------------------

    [HttpPut("api/legions/{legionDefinitionId}/active")]
    [ProducesResponseType(typeof(SetActiveLegionResult), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetActive(string legionDefinitionId)
    {
        var result = await _legions.SetActiveLegionAsync(GetPlayerId(), legionDefinitionId);
        if (!result.Success) return NotFound(result);
        return Ok(result);
    }

    [HttpPut("api/legions/{legionDefinitionId}/slots/{family}/{slotIndex}")]
    [ProducesResponseType(typeof(AssignSlotResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(AssignSlotResult), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(AssignSlotResult), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(AssignSlotResult), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(AssignSlotResult), StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> AssignSlot(
        string legionDefinitionId, string family, int slotIndex,
        [FromBody] AssignSlotRequest request)
    {
        var result = await _legions.AssignSlotAsync(
            GetPlayerId(), legionDefinitionId, family, slotIndex, request.UnitDefinitionId);
        if (result.Success) return Ok(result);
        return result.FailureCode switch
        {
            AssignSlotFailureCode.LegionNotOwned    => NotFound(result),
            AssignSlotFailureCode.UnitNotOwned      => NotFound(result),
            AssignSlotFailureCode.SlotOutOfRange    => BadRequest(result),
            AssignSlotFailureCode.WrongUnitType     => UnprocessableEntity(result),
            AssignSlotFailureCode.ConstraintMismatch => UnprocessableEntity(result),
            AssignSlotFailureCode.UnitAlreadyAssigned => Conflict(result),
            _ => BadRequest(result),
        };
    }

    [HttpDelete("api/legions/{legionDefinitionId}/slots/{family}/{slotIndex}")]
    [ProducesResponseType(typeof(ClearSlotResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> ClearSlot(string legionDefinitionId, string family, int slotIndex)
    {
        var result = await _legions.ClearSlotAsync(GetPlayerId(), legionDefinitionId, family, slotIndex);
        return Ok(result);
    }

    [HttpGet("api/legions/{legionDefinitionId}")]
    [ProducesResponseType(typeof(LegionDetailResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetDetail(string legionDefinitionId)
    {
        var result = await _legions.GetLegionDetailAsync(GetPlayerId(), legionDefinitionId);
        if (result is null) return NotFound();
        return Ok(result);
    }

    // ----------------------------------------------------------------
    // Slice 5 — Commander slot
    // ----------------------------------------------------------------

    [HttpPut("api/legions/commander")]
    [ProducesResponseType(typeof(CommanderEquipResult), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(CommanderEquipResult), StatusCodes.Status404NotFound)]
    public async Task<IActionResult> EquipCommander([FromBody] EquipCommanderRequest request)
    {
        var result = await _legions.EquipCommanderAsync(GetPlayerId(), request.GearDefinitionId);
        if (!result.Success) return NotFound(result);
        return Ok(result);
    }

    [HttpDelete("api/legions/commander")]
    [ProducesResponseType(typeof(CommanderUnequipResult), StatusCodes.Status200OK)]
    public async Task<IActionResult> UnequipCommander()
        => Ok(await _legions.UnequipCommanderAsync(GetPlayerId()));

    [HttpGet("api/legions/commander")]
    [ProducesResponseType(typeof(CommanderGearResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> GetCommander()
    {
        var result = await _legions.GetCommanderAsync(GetPlayerId());
        if (result is null) return NotFound();
        return Ok(result);
    }

    private Guid GetPlayerId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);
}

public class AssignSlotRequest
{
    public string UnitDefinitionId { get; set; } = string.Empty;
}

public class EquipCommanderRequest
{
    public string GearDefinitionId { get; set; } = string.Empty;
}
