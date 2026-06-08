using System.Text.Json;
using System.Text.Json.Serialization;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Enums;

namespace ROTA.Infrastructure.Services;

/// <summary>
/// Eager singleton (System 22 Phase A): loads <c>content/masteries.json</c> at construction and
/// throws <see cref="InvalidOperationException"/> on any invalid content so a misconfigured roster
/// fails at boot, not on first use. Mirrors <c>MagicDefinitionProvider</c>.
/// </summary>
public sealed class MasteryDefinitionProvider : IMasteryDefinitionProvider
{
    private const int MaxLevel = 5;
    private static readonly MasteryAncient[] AllAncients = Enum.GetValues<MasteryAncient>();

    private readonly IReadOnlyDictionary<MasteryAncient, AncientDefinition> _ancients;

    public MasteryDefinitionProvider(string contentRootPath)
    {
        var path = Path.Combine(contentRootPath, "content", "masteries.json");
        if (!File.Exists(path))
            throw new InvalidOperationException($"masteries.json not found at '{path}'.");

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            Converters = { new JsonStringEnumConverter() },
        };

        List<AncientDefinition> list;
        try
        {
            list = JsonSerializer.Deserialize<List<AncientDefinition>>(json, options)
                ?? throw new InvalidOperationException("masteries.json deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"masteries.json is invalid: {ex.Message}", ex);
        }

        Validate(list);

        _ancients = list.ToDictionary(a => a.Ancient, a => a);
    }

    public IReadOnlyList<AncientDefinition> GetAll() => _ancients.Values.ToList();

    public AncientDefinition? Get(MasteryAncient ancient)
        => _ancients.TryGetValue(ancient, out var a) ? a : null;

    public double GlobalPercent(MasteryAncient ancient, int level)
    {
        if (level < 1 || level > MaxLevel)
            throw new ArgumentOutOfRangeException(
                nameof(level), $"Mastery level must be 1..{MaxLevel} (was {level}).");
        if (!_ancients.TryGetValue(ancient, out var def))
            throw new InvalidOperationException($"No definition for ancient '{ancient}'.");
        return def.GlobalPercentByLevel[level - 1];
    }

    public MasteryTierChallenge? GetTierChallenge(MasteryAncient ancient, int fromLevel)
        => _ancients.TryGetValue(ancient, out var a)
            ? a.TierChallenges.FirstOrDefault(t => t.FromLevel == fromLevel)
            : null;

    private static void Validate(List<AncientDefinition> list)
    {
        // Exactly the four Ancients, each present once.
        var seen = new HashSet<MasteryAncient>();
        foreach (var a in list)
        {
            if (!Enum.IsDefined(a.Ancient))
                throw new InvalidOperationException(
                    $"masteries.json: unknown ancient value '{(int)a.Ancient}'.");
            if (!seen.Add(a.Ancient))
                throw new InvalidOperationException(
                    $"masteries.json: duplicate ancient '{a.Ancient}'.");
        }
        foreach (var expected in AllAncients)
            if (!seen.Contains(expected))
                throw new InvalidOperationException(
                    $"masteries.json: missing ancient '{expected}'.");

        foreach (var a in list)
        {
            // Global magnitude table: exactly MaxLevel entries, non-negative, non-decreasing.
            if (a.GlobalPercentByLevel.Length != MaxLevel)
                throw new InvalidOperationException(
                    $"masteries.json: '{a.Ancient}' globalPercentByLevel must have {MaxLevel} entries " +
                    $"(has {a.GlobalPercentByLevel.Length}).");
            for (int i = 0; i < MaxLevel; i++)
            {
                if (a.GlobalPercentByLevel[i] < 0)
                    throw new InvalidOperationException(
                        $"masteries.json: '{a.Ancient}' globalPercentByLevel[{i}] is negative.");
                if (i > 0 && a.GlobalPercentByLevel[i] < a.GlobalPercentByLevel[i - 1])
                    throw new InvalidOperationException(
                        $"masteries.json: '{a.Ancient}' globalPercentByLevel is not non-decreasing at index {i}.");
            }

            // Tier challenges: exactly MaxLevel-1, fromLevel 1..MaxLevel-1 contiguous, non-empty checklists.
            if (a.TierChallenges.Count != MaxLevel - 1)
                throw new InvalidOperationException(
                    $"masteries.json: '{a.Ancient}' must have {MaxLevel - 1} tierChallenges " +
                    $"(has {a.TierChallenges.Count}).");
            for (int fromLevel = 1; fromLevel <= MaxLevel - 1; fromLevel++)
            {
                var tier = a.TierChallenges.FirstOrDefault(t => t.FromLevel == fromLevel)
                    ?? throw new InvalidOperationException(
                        $"masteries.json: '{a.Ancient}' missing tierChallenge fromLevel {fromLevel}.");
                if (tier.Checklist.Count == 0)
                    throw new InvalidOperationException(
                        $"masteries.json: '{a.Ancient}' tier {fromLevel} has an empty checklist.");
                foreach (var item in tier.Checklist)
                {
                    if (!Enum.IsDefined(item.ActivityType))
                        throw new InvalidOperationException(
                            $"masteries.json: '{a.Ancient}' tier {fromLevel} has unknown activityType " +
                            $"'{(int)item.ActivityType}'.");
                    if (item.Threshold <= 0)
                        throw new InvalidOperationException(
                            $"masteries.json: '{a.Ancient}' tier {fromLevel} activityType {item.ActivityType} " +
                            $"threshold must be > 0 (was {item.Threshold}).");
                }
            }

            // Breadth curve: the final tier (T4→5) must span ≥ 2 distinct activity types.
            var finalTier = a.TierChallenges.First(t => t.FromLevel == MaxLevel - 1);
            var distinctTypes = finalTier.Checklist.Select(c => c.ActivityType).Distinct().Count();
            if (distinctTypes < 2)
                throw new InvalidOperationException(
                    $"masteries.json: '{a.Ancient}' final tier (T{MaxLevel - 1}→{MaxLevel}) must span " +
                    $"≥2 distinct activity types (breadth curve).");
        }
    }
}
