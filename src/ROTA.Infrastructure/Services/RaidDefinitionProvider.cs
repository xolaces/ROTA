using System.Text.Json;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;

namespace ROTA.Infrastructure.Services;

//        of the process. Adding raids requires a redeploy.
public sealed class RaidDefinitionProvider : IRaidDefinitionProvider
{
    private readonly IReadOnlyDictionary<string, RaidDefinition> _raids;

    public RaidDefinitionProvider(string contentRootPath)
    {
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        var path = Path.Combine(contentRootPath, "content", "raids.json");
        var json = File.ReadAllText(path);
        var list = JsonSerializer.Deserialize<List<RaidDefinition>>(json, options)
            ?? throw new InvalidOperationException("raids.json deserialized to null.");

        var byId = list.ToDictionary(r => r.Id, r => r);

        // BETA (System 16 Slice 7) — the Gauntlet ladder stages (content/gauntlet_raids.json) are ALSO
        // registered here as plain RaidDefinitions so HitRaidAsync's
        // `_raidDefinitions.GetById(raid.RaidDefinitionId)` resolves a `gauntlet_stage_N` raid unchanged.
        // GauntletRaidDefinition's fields overlap RaidDefinition (id/name/tier/baseHp/staminaCostPerHit/
        // lootTableId/baseGold/Xp/GemReward/artKey); lootTableId is "" so DistributeKillRewardsAsync's
        // loot pass is benign (no items/stat points). The Gauntlet combat behaviour (strike spend,
        // off-cap auras, score update, per-defeat reward) is gated on ActiveRaid.GauntletEventId — NOT
        // on the definition — so mapping the stage to a RaidDefinition does not alter any combat branch.
        // The Gauntlet-only ladder validation stays in GauntletContentProvider over the dedicated type;
        // this only mirrors the resolved-definition shape for the combat lookup.
        var gauntletPath = Path.Combine(contentRootPath, "content", "gauntlet_raids.json");
        if (File.Exists(gauntletPath))
        {
            var gauntletJson = File.ReadAllText(gauntletPath);
            var gauntletList = JsonSerializer.Deserialize<List<GauntletRaidDefinition>>(gauntletJson, options)
                ?? throw new InvalidOperationException("gauntlet_raids.json deserialized to null.");

            foreach (var g in gauntletList)
            {
                if (byId.ContainsKey(g.Id))
                    throw new InvalidOperationException(
                        $"gauntlet_raids.json: stage id '{g.Id}' collides with a raids.json raid id; " +
                        "Gauntlet stage ids must be distinct from ordinary raids.");

                byId[g.Id] = ToRaidDefinition(g);
            }
        }

        _raids = byId;
    }

    public IReadOnlyList<RaidDefinition> GetAll()
        => _raids.Values.ToList();

    public RaidDefinition? GetById(string id)
        => _raids.TryGetValue(id, out var r) ? r : null;

    // Projects a Gauntlet ladder stage onto the shared RaidDefinition shape used by the combat lookup.
    // PersonalBaseHp is left 0 so SummonRaidAsync falls back to BaseHp — but the ladder service stamps
    // the stage's BaseHp directly at spawn (no difficulty multiplier), so this provider value is only
    // ever used for the kill-reward / name / tier reads inside HitRaidAsync.
    private static RaidDefinition ToRaidDefinition(GauntletRaidDefinition g)
        => new()
        {
            Id                   = g.Id,
            Name                 = g.Name,
            Tier                 = g.Tier,                 // "Event"
            BaseHp               = g.BaseHp,
            PersonalBaseHp       = 0,
            TimerHours           = g.TimerHours,
            StaminaCostPerHit    = g.StaminaCostPerHit,
            LootTableId          = g.LootTableId,          // "" → benign kill-reward loot pass
            BaseGoldReward       = g.BaseGoldReward,
            BaseExperienceReward = g.BaseExperienceReward,
            BaseGemReward        = g.BaseGemReward,
            HasOnHitDrops        = false,
            ArtKey               = g.ArtKey,
        };
}
