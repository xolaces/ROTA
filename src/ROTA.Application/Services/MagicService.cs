using ROTA.Application.Interfaces;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

// BETA
public sealed class MagicService : IMagicService
{
    private readonly IPlayerMagicRepository  _magicRepo;
    private readonly IMagicDefinitionProvider _defs;

    public MagicService(IPlayerMagicRepository magicRepo, IMagicDefinitionProvider defs)
    {
        _magicRepo = magicRepo;
        _defs      = defs;
    }

    public async Task<IReadOnlyList<OwnedMagicResponse>> GetOwnedMagicsAsync(
        Guid playerId, CancellationToken ct = default)
    {
        var rows = await _magicRepo.GetOwnedAsync(playerId, ct);
        var result = new List<OwnedMagicResponse>(rows.Count);
        foreach (var row in rows)
        {
            var def = _defs.GetById(row.MagicDefinitionId);
            if (def is null) continue;
            result.Add(new OwnedMagicResponse
            {
                MagicDefinitionId = def.Id,
                Name              = def.Name,
                Description       = def.Description,
                Rarity            = def.Rarity.ToString(),
                Category          = def.Category.ToString(),
                EffectType        = def.EffectType.ToString(),
                ProcChance        = def.ProcChance,
                ProcAmount        = def.ProcAmount,
                Stacks            = def.Stacks,
                IconPath          = def.IconPath,
                Acquisition       = def.Acquisition,
                AcquiredAt        = row.CreatedAt,
            });
        }
        return result;
    }
}
