using System.Text.Json;
using System.Text.Json.Serialization;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Enums;

namespace ROTA.Infrastructure.Services;

/// <summary>
/// Eager singleton (TICKET 46): loads <c>content/achievements.json</c> at construction and throws
/// <see cref="InvalidOperationException"/> on any invalid content so a misconfigured roster fails at
/// boot, not on first use. Mirrors <c>MasteryDefinitionProvider</c>.
/// </summary>
public sealed class AchievementDefinitionProvider : IAchievementDefinitionProvider
{
    private readonly IReadOnlyDictionary<string, AchievementDefinition> _byId;
    private readonly List<AchievementDefinition> _ordered;

    public AchievementDefinitionProvider(string contentRootPath)
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

        Validate(list);

        _ordered = list;
        _byId = list.ToDictionary(a => a.Id, a => a);
    }

    public IReadOnlyList<AchievementDefinition> GetAll() => _ordered;

    public AchievementDefinition? GetById(string id)
        => _byId.TryGetValue(id, out var a) ? a : null;

    public IReadOnlyList<AchievementDefinition> GetByMetric(AchievementMetric metric)
        => _ordered.Where(a => a.Metric == metric).ToList();

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
