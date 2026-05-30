using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

[ApiController]
[Route("api/equipment")]
[Authorize]
public sealed class EquipmentController : ControllerBase
{
    private readonly IEquipmentService _equipment;

    public EquipmentController(IEquipmentService equipment) => _equipment = equipment;

    private Guid PlayerId => Guid.Parse(User.FindFirst("sub")!.Value);

    // GET /api/equipment
    [HttpGet]
    public async Task<IActionResult> GetEquipment(CancellationToken ct)
    {
        var items = await _equipment.GetEquipmentAsync(PlayerId, ct);
        return Ok(items);
    }

    // PUT /api/equipment/{slot}
    [HttpPut("{slot}")]
    public async Task<IActionResult> Equip(string slot, [FromBody] EquipRequest request, CancellationToken ct)
    {
        if (string.IsNullOrWhiteSpace(request.GearDefinitionId))
            return BadRequest(new { error = "gearDefinitionId is required." });

        var result = await _equipment.EquipAsync(PlayerId, slot, request.GearDefinitionId, ct);
        if (!result.Success)
            return result.FailureReason!.Contains("not found")
                ? NotFound(new { error = result.FailureReason })
                : UnprocessableEntity(new { error = result.FailureReason });

        return Ok(result.Item);
    }

    // DELETE /api/equipment/{slot}
    [HttpDelete("{slot}")]
    public async Task<IActionResult> Unequip(string slot, CancellationToken ct)
    {
        var result = await _equipment.UnequipAsync(PlayerId, slot, ct);
        if (!result.Success)
            return NotFound(new { error = result.FailureReason });

        return NoContent();
    }
}
