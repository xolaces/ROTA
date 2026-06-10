using System.IdentityModel.Tokens.Jwt;
using FluentValidation;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Api.Controllers;

[ApiController]
[Route("api/items")]
[Authorize]
public sealed class ItemController : ControllerBase
{
    private readonly IItemService _items;
    private readonly IValidator<UseItemRequest> _useValidator;

    public ItemController(IItemService items, IValidator<UseItemRequest> useValidator)
    {
        _items = items;
        _useValidator = useValidator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<InventoryItemResponse>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetInventory()
    {
        var result = await _items.GetInventoryAsync(GetPlayerId());
        return Ok(result);
    }

    [HttpPost("{itemDefinitionId}/use")]
    [ProducesResponseType(typeof(UseItemResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(UseItemResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> UseItem(
        [FromRoute] string itemDefinitionId,
        [FromBody] UseItemRequest request)
    {
        var v = await _useValidator.ValidateAsync(request);
        if (!v.IsValid) return InvalidRequest(v);

        var result = await _items.UseItemAsync(GetPlayerId(), itemDefinitionId, request.Quantity);

        if (result.Success) return Ok(result);

        return result.FailureCode switch
        {
            UseItemFailureCode.ItemNotFound      => NotFound(new { message = result.FailureReason }),
            UseItemFailureCode.InsufficientItems => UnprocessableEntity(result),
            _                                    => UnprocessableEntity(result),
        };
    }

    private Guid GetPlayerId()
        => Guid.Parse(User.FindFirst(JwtRegisteredClaimNames.Sub)!.Value);

    private IActionResult InvalidRequest(FluentValidation.Results.ValidationResult v)
    {
        foreach (var e in v.Errors)
            ModelState.AddModelError(e.PropertyName, e.ErrorMessage);
        return ValidationProblem();
    }
}
