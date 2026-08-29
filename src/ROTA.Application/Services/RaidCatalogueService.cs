using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

/// <inheritdoc cref="IRaidCatalogueService"/>
public sealed class RaidCatalogueService : IRaidCatalogueService
{
    private readonly IRaidDefinitionProvider _raids;
    private readonly ILootTableProvider _lootTables;
    private readonly IItemDefinitionProvider _items;
    private readonly IMagicDefinitionProvider _magics;
    private readonly IUnitDefinitionProvider _units;
    private readonly ILegionDefinitionProvider _legions;
    private readonly IGearDefinitionProvider _gear;

    public RaidCatalogueService(
        IRaidDefinitionProvider raids,
        ILootTableProvider lootTables,
        IItemDefinitionProvider items,
        IMagicDefinitionProvider magics,
        IUnitDefinitionProvider units,
        ILegionDefinitionProvider legions,
        IGearDefinitionProvider gear)
    {
        _raids      = raids;
        _lootTables = lootTables;
        _items      = items;
        _magics     = magics;
        _units      = units;
        _legions    = legions;
        _gear       = gear;
    }

    public IReadOnlyList<RaidPreviewResponse> GetCatalogue()
        => _raids.GetAll().Select(ToPreview).ToList();

    public RaidPreviewResponse? GetPreview(string raidDefinitionId)
    {
        var def = _raids.GetById(raidDefinitionId);
        return def is null ? null : ToPreview(def);
    }

    public RaidLootPreviewResponse? GetLootPreview(string raidDefinitionId, string difficulty)
    {
        var def = _raids.GetById(raidDefinitionId);
        if (def is null) return null;

        var table = _lootTables.GetById(def.LootTableId);
        if (table?.Difficulties is null) return null;

        // Difficulty arrives from a query string. Matched case-insensitively so "nightmare" works,
        // but the RESPONSE echoes the content's own spelling rather than the caller's.
        var key = table.Difficulties.Keys.FirstOrDefault(
            k => string.Equals(k, difficulty, StringComparison.OrdinalIgnoreCase));
        if (key is null) return null;

        var tier = table.Difficulties[key];

        // Order by whichever key the table actually uses. A damage ladder sorted by a
        // contribution percent every rung leaves at zero would come back in file order, which is
        // only correct by luck.
        var rewards = tier.ThresholdRewards ?? new List<ThresholdReward>();
        bool damageLadder = rewards.Any(t => t.DamageThreshold > 0);
        var brackets = (damageLadder
                ? rewards.OrderBy(t => t.DamageThreshold)
                : rewards.OrderBy(t => t.ContributionPercent))
            .Select(ToBracket)
            .ToList();

        return new RaidLootPreviewResponse
        {
            RaidDefinitionId = def.Id,
            Difficulty       = key,
            Brackets         = brackets,
        };
    }

    // ── mapping ───────────────────────────────────────────────────────────────

    private RaidPreviewResponse ToPreview(RaidDefinition def)
    {
        var table = _lootTables.GetById(def.LootTableId);

        return new RaidPreviewResponse
        {
            RaidDefinitionId = def.Id,
            Name             = def.Name,
            Tier             = def.Tier,
            Grade            = def.Grade,
            ArtKey           = def.ArtKey,
            BaseHp           = def.BaseHp,
            // The definition treats 0 as "no separate personal size", and the summon screen is
            // asking about a personal raid, so resolve the fallback here rather than in every client.
            PersonalHp       = def.PersonalBaseHp > 0 ? def.PersonalBaseHp : def.BaseHp,
            TimerHours       = def.TimerHours,
            Difficulties     = table?.Difficulties?.Keys.ToList() ?? new List<string>(),
        };
    }

    private LootBracketResponse ToBracket(ThresholdReward t)
    {
        var drops = new List<LootDropResponse>();

        foreach (var d in t.ItemDrops)
        {
            var def = _items.GetById(d.ItemId);
            drops.Add(new LootDropResponse
            {
                Kind = "Item", DefinitionId = d.ItemId,
                Name = def?.Name ?? d.ItemId,
                Rarity = def?.Rarity.ToString() ?? string.Empty,
                Quantity = d.Quantity, Chance = d.Chance,
            });
        }

        foreach (var d in t.MagicDrops)
        {
            var def = _magics.GetById(d.MagicId);
            drops.Add(new LootDropResponse
            {
                Kind = "Magic", DefinitionId = d.MagicId,
                Name = def?.Name ?? d.MagicId,
                Rarity = def?.Rarity.ToString() ?? string.Empty,
                Quantity = 1, Chance = d.Chance,
            });
        }

        foreach (var d in t.UnitDrops)
        {
            var def = _units.GetById(d.UnitId);
            drops.Add(new LootDropResponse
            {
                Kind = "Unit", DefinitionId = d.UnitId,
                Name = def?.Name ?? d.UnitId,
                Rarity = def?.Rarity.ToString() ?? string.Empty,
                Quantity = 1, Chance = d.Chance,
            });
        }

        foreach (var d in t.LegionDrops)
        {
            var def = _legions.GetById(d.LegionId);
            drops.Add(new LootDropResponse
            {
                Kind = "Legion", DefinitionId = d.LegionId,
                Name = def?.Name ?? d.LegionId,
                Rarity = def?.Rarity.ToString() ?? string.Empty,
                Quantity = 1, Chance = d.Chance,
            });
        }

        foreach (var d in t.GearDrops)
        {
            var def = _gear.GetById(d.GearDefinitionId);
            drops.Add(new LootDropResponse
            {
                Kind = "Gear", DefinitionId = d.GearDefinitionId,
                Name = def?.Name ?? d.GearDefinitionId,
                Rarity = def?.Rarity.ToString() ?? string.Empty,
                Quantity = d.Quantity, Chance = d.Chance,
            });
        }

        return new LootBracketResponse
        {
            ContributionPercent = t.ContributionPercent,
            DamageThreshold     = t.DamageThreshold,
            StatPoints          = t.UnassignedStatPoints,
            AttackPoints        = t.AttackPoints,
            DefensePoints       = t.DefensePoints,
            DiscernmentPoints   = t.DiscernmentPoints,
            Drops               = drops,
        };
    }
}
