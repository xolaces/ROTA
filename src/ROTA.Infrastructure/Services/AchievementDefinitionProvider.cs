using System.Text.Json;
using System.Text.Json.Serialization;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Enums;

namespace ROTA.Infrastructure.Services;

/// <summary>
/// Eager singleton (TICKET 46): loads <c>content/achievements.json</c> at construction and throws
/// <see cref="InvalidOperationException"/> on any invalid content so a misconfigured roster fails at
/// boot, not on first use. Mirrors <c>MasteryDefinitionProvider</c>.
///
/// System 25: when a quest provider + <see cref="AchievementConfig.ZoneRerunLadder"/> are supplied, the
/// per-zone rerun ladders are SYNTHESIZED here — one 6-tier rarity chain per distinct (chapter, zone) in
/// the quest roster — rather than hand-authored. Adding chapters/zones to quests.json grows the roster
/// automatically; the synthesized rows are validated by the same rules as authored ones.
/// </summary>
public sealed class AchievementDefinitionProvider : IAchievementDefinitionProvider
{
    private readonly IReadOnlyDictionary<string, AchievementDefinition> _byId;
    private readonly List<AchievementDefinition> _ordered;
    private readonly IReadOnlyDictionary<(int Chapter, int ZoneIndex), List<AchievementDefinition>> _zoneReruns;
    private readonly IReadOnlyDictionary<string, List<AchievementDefinition>> _raidClears;

    public AchievementDefinitionProvider(
        string contentRootPath,
        IQuestDefinitionProvider? quests = null,
        AchievementConfig? config = null,
        IRaidDefinitionProvider? raids = null)
    {
        var path = Path.Combine(contentRootPath, "content", "achievements.json");
        if (!File.Exists(path))
            throw new InvalidOperationException($"achievements.json not found at '{path}'.");

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        List<AchievementDefinition> list;
        try
        {
            list = JsonSerializer.Deserialize<List<AchievementDefinition>>(json, options)
                ?? throw new InvalidOperationException("achievements.json deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"achievements.json is invalid: {ex.Message}", ex);
        }

        // System 25 — append the per-zone rerun ladders (skipped when there's no quest provider/ladder,
        // e.g. bare unit fixtures that construct the provider with just a path).
        list.AddRange(SynthesizeZoneRerunLadders(quests, config));
        list.AddRange(SynthesizeRaidClearLadders(raids, config));

        Validate(list);

        _ordered = list;
        _byId = list.ToDictionary(a => a.Id, a => a);
        _zoneReruns = list
            .Where(a => a.Metric == AchievementMetric.ZoneReruns && a.Chapter is not null && a.ZoneIndex is not null)
            .GroupBy(a => (a.Chapter!.Value, a.ZoneIndex!.Value))
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Threshold).ToList());
        _raidClears = list
            .Where(a => a.Metric == AchievementMetric.RaidClears && !string.IsNullOrWhiteSpace(a.RaidDefinitionId))
            .GroupBy(a => a.RaidDefinitionId!)
            .ToDictionary(g => g.Key, g => g.OrderBy(a => a.Threshold).ToList(), StringComparer.Ordinal);
    }

    public IReadOnlyList<AchievementDefinition> GetAll() => _ordered;

    public AchievementDefinition? GetById(string id)
        => _byId.TryGetValue(id, out var a) ? a : null;

    public IReadOnlyList<AchievementDefinition> GetByMetric(AchievementMetric metric)
        => _ordered.Where(a => a.Metric == metric).ToList();

    public IReadOnlyList<AchievementDefinition> GetZoneRerunTiers(int chapter, int zoneIndex)
        => _zoneReruns.TryGetValue((chapter, zoneIndex), out var tiers)
            ? tiers
            : Array.Empty<AchievementDefinition>();

    public IReadOnlyList<AchievementDefinition> GetRaidClearTiers(string raidDefinitionId)
        => _raidClears.TryGetValue(raidDefinitionId, out var tiers)
            ? tiers
            : Array.Empty<AchievementDefinition>();

    // Expand the rarity ladder across every distinct (chapter, zone) in the quest roster. Deterministic
    // ids + a synthesized NextId chain per zone, so the result is stable across restarts and validated
    // exactly like authored rows (strictly-increasing thresholds, same metric, no cycles).
    //
    // Ids are keyed on THRESHOLD, not rarity. ItemRarity stops at Orange permanently, so a ladder with
    // more than six rungs necessarily repeats a rarity — keying on rarity would emit duplicate ids and
    // fail the boot validator. Thresholds are strictly increasing, so they are unique by construction.
    private static IEnumerable<AchievementDefinition> SynthesizeZoneRerunLadders(
        IQuestDefinitionProvider? quests, AchievementConfig? config)
    {
        var ladder = config?.ZoneRerunLadder;
        if (quests is null || ladder is null || ladder.Count == 0)
            yield break;

        var tiers = ladder.OrderBy(t => t.Threshold).ToList();

        var zones = quests.GetAll()
            .GroupBy(q => (q.Chapter, q.ZoneIndex))
            .Select(g => (g.Key.Chapter, g.Key.ZoneIndex, ZoneName: g.First().ZoneName))
            .OrderBy(z => z.Chapter).ThenBy(z => z.ZoneIndex);

        foreach (var zone in zones)
        {
            for (int i = 0; i < tiers.Count; i++)
            {
                var tier = tiers[i];
                var rarity = tier.Rarity.ToString().ToLowerInvariant();
                string ZoneId(int n) => $"ach_zonererun_c{zone.Chapter}z{zone.ZoneIndex}_t{tiers[n].Threshold}";
                yield return new AchievementDefinition
                {
                    Id          = ZoneId(i),
                    Category    = AchievementCategory.ZoneMastery,
                    Metric      = AchievementMetric.ZoneReruns,
                    Name        = $"{zone.ZoneName} — {tier.Rarity} Mastery",
                    Description = $"Re-run {zone.ZoneName} {tier.Threshold} times.",
                    Points      = tier.Points,
                    Threshold   = tier.Threshold,
                    NextId      = i < tiers.Count - 1 ? ZoneId(i + 1) : null,
                    Chapter     = zone.Chapter,
                    ZoneIndex   = zone.ZoneIndex,
                    IconKey     = $"rarity_{rarity}",
                };
            }
        }
    }

    // The same expansion, per RAID definition. Owner 2026-09-03: RaidCompletions stays the global
    // lifetime tally; this adds a per-raid ladder so each raid carries its own chase to 5,000 clears.
    private static IEnumerable<AchievementDefinition> SynthesizeRaidClearLadders(
        IRaidDefinitionProvider? raids, AchievementConfig? config)
    {
        var ladder = config?.RaidClearLadder;
        if (raids is null || ladder is null || ladder.Count == 0)
            yield break;

        var tiers = ladder.OrderBy(t => t.Threshold).ToList();

        foreach (var raid in raids.GetAll().OrderBy(r => r.Id, StringComparer.Ordinal))
        {
            for (int i = 0; i < tiers.Count; i++)
            {
                var tier = tiers[i];
                var rarity = tier.Rarity.ToString().ToLowerInvariant();
                string RaidId(int n) => $"ach_raidclear_{raid.Id}_t{tiers[n].Threshold}";
                yield return new AchievementDefinition
                {
                    Id               = RaidId(i),
                    Category         = AchievementCategory.RaidMastery,
                    Metric           = AchievementMetric.RaidClears,
                    Name             = $"{raid.Name} — {tier.Rarity} Mastery",
                    Description      = $"Defeat {raid.Name} {tier.Threshold} times.",
                    Points           = tier.Points,
                    Threshold        = tier.Threshold,
                    NextId           = i < tiers.Count - 1 ? RaidId(i + 1) : null,
                    RaidDefinitionId = raid.Id,
                    IconKey          = $"rarity_{rarity}",
                };
            }
        }
    }

    private static void Validate(List<AchievementDefinition> list)
    {
        if (list.Count == 0)
            throw new InvalidOperationException("achievements.json: roster is empty.");

        var byId = new Dictionary<string, AchievementDefinition>();
        foreach (var a in list)
        {
            if (string.IsNullOrWhiteSpace(a.Id))
                throw new InvalidOperationException("achievements.json: an achievement has a blank id.");
            if (!byId.TryAdd(a.Id, a))
                throw new InvalidOperationException($"achievements.json: duplicate id '{a.Id}'.");

            if (!Enum.IsDefined(a.Category))
                throw new InvalidOperationException(
                    $"achievements.json: '{a.Id}' has unknown category '{(int)a.Category}'.");
            if (!Enum.IsDefined(a.Metric))
                throw new InvalidOperationException(
                    $"achievements.json: '{a.Id}' has unknown metric '{(int)a.Metric}'.");

            if (a.Points <= 0)
                throw new InvalidOperationException(
                    $"achievements.json: '{a.Id}' points must be > 0 (was {a.Points}).");
            if (a.Threshold <= 0)
                throw new InvalidOperationException(
                    $"achievements.json: '{a.Id}' threshold must be > 0 (was {a.Threshold}).");

            // Collector achievements MUST name the item key whose distinct count they watch.
            if (a.Category == AchievementCategory.Collector && string.IsNullOrWhiteSpace(a.CollectorKey))
                throw new InvalidOperationException(
                    $"achievements.json: Collector achievement '{a.Id}' is missing collectorKey.");

            // System 25 — ZoneReruns achievements MUST be scoped so RecordZoneRerunAsync can route them.
            if (a.Metric == AchievementMetric.ZoneReruns && (a.Chapter is null || a.ZoneIndex is null))
                throw new InvalidOperationException(
                    $"achievements.json: ZoneReruns achievement '{a.Id}' must set chapter + zoneIndex.");

            // Owner 2026-09-03 — RaidClears achievements MUST be scoped so RecordRaidClearAsync can route
            // them. An unscoped one would be invisible to the scoped path and never advance at all.
            if (a.Metric == AchievementMetric.RaidClears && string.IsNullOrWhiteSpace(a.RaidDefinitionId))
                throw new InvalidOperationException(
                    $"achievements.json: RaidClears achievement '{a.Id}' must set raidDefinitionId.");
        }

        // NextId chains: resolve, same metric, strictly increasing threshold, no cycles.
        foreach (var a in list)
        {
            if (a.NextId is null) continue;

            // Walk the chain from this node; detect dangling refs, metric mismatch, non-increasing
            // thresholds, and cycles (a node revisited within its own walk).
            var visited = new HashSet<string> { a.Id };
            var current = a;
            while (current.NextId is not null)
            {
                if (!byId.TryGetValue(current.NextId, out var next))
                    throw new InvalidOperationException(
                        $"achievements.json: '{current.Id}' nextId '{current.NextId}' does not resolve.");
                if (!visited.Add(next.Id))
                    throw new InvalidOperationException(
                        $"achievements.json: nextId chain starting at '{a.Id}' is cyclic (revisits '{next.Id}').");
                if (next.Metric != current.Metric)
                    throw new InvalidOperationException(
                        $"achievements.json: '{current.Id}' nextId '{next.Id}' has a different metric " +
                        $"({next.Metric} vs {current.Metric}).");
                if (next.Threshold <= current.Threshold)
                    throw new InvalidOperationException(
                        $"achievements.json: '{current.Id}' nextId '{next.Id}' threshold must be strictly " +
                        $"higher (was {next.Threshold}, need > {current.Threshold}).");
                current = next;
            }
        }
    }
}
