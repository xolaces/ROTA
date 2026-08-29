using System.Text.Json;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;

namespace ROTA.Infrastructure.Services;

// Raid definitions are loaded once from JSON at construction and held for the life of the process.
// Adding raids requires a redeploy.
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

        Validate(list);

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

        // System 21 Slice 3b — guild raids (content/guild_raids.json) are registered as plain
        // RaidDefinitions (Tier="Guild"). The guild combat behaviour (GuildStamina spend, member-only
        // access, contribution accrual) is gated on ActiveRaid.GuildId — NOT the definition — so the
        // combat lookup is unchanged. lootTableId "" → benign kill-reward loot pass (gold/XP/gem tier
        // rewards are still granted); item loot tables for guild raids are a content follow-up.
        var guildPath = Path.Combine(contentRootPath, "content", "guild_raids.json");
        if (File.Exists(guildPath))
        {
            var guildJson = File.ReadAllText(guildPath);
            var guildList = JsonSerializer.Deserialize<List<RaidDefinition>>(guildJson, options)
                ?? throw new InvalidOperationException("guild_raids.json deserialized to null.");

            foreach (var g in guildList)
            {
                if (byId.ContainsKey(g.Id))
                    throw new InvalidOperationException(
                        $"guild_raids.json: raid id '{g.Id}' collides with an existing raid id; " +
                        "guild raid ids must be distinct from ordinary and Gauntlet raids.");

                byId[g.Id] = g;
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

    private static readonly HashSet<string> KnownGrades =
        new(StringComparer.OrdinalIgnoreCase) { "Common", "Deadly", "Elite", "Mythic" };

    /// <summary>
    /// Fails the boot on content a designer got wrong, rather than surfacing it as a raid nobody can
    /// kill. Health is hand-typed in raids.json now (owner 2026-08-29: "we just input the health"),
    /// which makes adding a raid trivial and makes a typo silent — so this is where the typo stops.
    /// </summary>
    private static void Validate(List<RaidDefinition> raids)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);

        foreach (var r in raids)
        {
            if (string.IsNullOrWhiteSpace(r.Id))
                throw new InvalidOperationException("raids.json: a raid has no id.");
            if (!seen.Add(r.Id))
                throw new InvalidOperationException($"raids.json: duplicate raid id '{r.Id}'.");
            if (string.IsNullOrWhiteSpace(r.Name))
                throw new InvalidOperationException($"raids.json: raid '{r.Id}' has no name.");

            if (!KnownGrades.Contains(r.Grade))
                throw new InvalidOperationException(
                    $"raids.json: raid '{r.Id}' has grade '{r.Grade}'; expected one of "
                    + string.Join(", ", KnownGrades) + ".");

            // A World raid is TIMER-ONLY (owner 2026-08-29): no collective health, rewards come from
            // a damage ladder. Zero health is therefore meaningful there and a mistake anywhere else,
            // which is exactly the distinction a hand-typed number cannot make for itself.
            bool timerOnly = string.Equals(r.Tier, "World", StringComparison.OrdinalIgnoreCase);

            if (!timerOnly && r.BaseHp <= 0)
                throw new InvalidOperationException(
                    $"raids.json: raid '{r.Id}' has baseHp {r.BaseHp}. Only World raids may have no "
                    + "health; everything else needs a positive number.");

            if (timerOnly && r.BaseHp != 0)
                throw new InvalidOperationException(
                    $"raids.json: World raid '{r.Id}' has baseHp {r.BaseHp}. World raids are decided "
                    + "by a timer and a damage ladder, so their health must be 0.");

            if (r.PersonalBaseHp < 0)
                throw new InvalidOperationException(
                    $"raids.json: raid '{r.Id}' has a negative personalBaseHp.");

            if (r.TimerHours <= 0)
                throw new InvalidOperationException(
                    $"raids.json: raid '{r.Id}' has timerHours {r.TimerHours}; a raid that never "
                    + "expires can never be settled.");

            if (r.StaminaCostPerHit <= 0)
                throw new InvalidOperationException(
                    $"raids.json: raid '{r.Id}' has staminaCostPerHit {r.StaminaCostPerHit}.");
        }
    }
}
