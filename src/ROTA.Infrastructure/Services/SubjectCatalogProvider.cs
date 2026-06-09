using System.Text.Json;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;

namespace ROTA.Infrastructure.Services;

/// <summary>
/// Eager singleton (T52): loads <c>content/subjects.json</c> at construction and throws
/// <see cref="InvalidOperationException"/> on any invalid content (empty lists, duplicate keys, blank
/// feedback category) so a misconfigured catalog fails at boot, not on first use. Mirrors
/// <c>MagicDefinitionProvider</c> / <c>MasteryDefinitionProvider</c>.
/// </summary>
public sealed class SubjectCatalogProvider : ISubjectCatalogProvider
{
    private readonly SubjectCatalog _catalog;
    private readonly IReadOnlyDictionary<string, string> _bugByKeyOrLabel;
    private readonly IReadOnlyDictionary<string, string> _reportByKeyOrLabel;

    public SubjectCatalogProvider(string contentRootPath)
    {
        var path = Path.Combine(contentRootPath, "content", "subjects.json");
        if (!File.Exists(path))
            throw new InvalidOperationException($"subjects.json not found at '{path}'.");

        var json = File.ReadAllText(path);
        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        SubjectCatalog catalog;
        try
        {
            catalog = JsonSerializer.Deserialize<SubjectCatalog>(json, options)
                ?? throw new InvalidOperationException("subjects.json deserialized to null.");
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException($"subjects.json is invalid: {ex.Message}", ex);
        }

        Validate(catalog);

        _catalog = catalog;
        _bugByKeyOrLabel = BuildLookup(catalog.BugSubjects);
        _reportByKeyOrLabel = BuildLookup(catalog.ReportSubjects);
    }

    public IReadOnlyList<SubjectCategory> BugSubjects => _catalog.BugSubjects;
    public IReadOnlyList<SubjectCategory> ReportSubjects => _catalog.ReportSubjects;
    public string FeedbackCategory => _catalog.FeedbackCategory;

    public bool IsValidBugSubject(string subjectKeyOrLabel)
        => NormalizeBugSubject(subjectKeyOrLabel) is not null;

    public bool IsValidReportSubject(string subjectKeyOrLabel)
        => NormalizeReportSubject(subjectKeyOrLabel) is not null;

    public string? NormalizeBugSubject(string subjectKeyOrLabel)
        => Lookup(_bugByKeyOrLabel, subjectKeyOrLabel);

    public string? NormalizeReportSubject(string subjectKeyOrLabel)
        => Lookup(_reportByKeyOrLabel, subjectKeyOrLabel);

    public SubjectCatalog GetCatalog() => _catalog;

    private static string? Lookup(IReadOnlyDictionary<string, string> map, string keyOrLabel)
    {
        if (string.IsNullOrWhiteSpace(keyOrLabel)) return null;
        return map.TryGetValue(keyOrLabel.Trim(), out var label) ? label : null;
    }

    // Maps both key and label (case-insensitive) → canonical label, so validation accepts either form.
    private static IReadOnlyDictionary<string, string> BuildLookup(IEnumerable<SubjectCategory> subjects)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in subjects)
        {
            map[s.Key] = s.Label;
            map[s.Label] = s.Label;
        }
        return map;
    }

    private static void Validate(SubjectCatalog catalog)
    {
        ValidateList(catalog.BugSubjects, "bugSubjects");
        ValidateList(catalog.ReportSubjects, "reportSubjects");

        if (string.IsNullOrWhiteSpace(catalog.FeedbackCategory))
            throw new InvalidOperationException("subjects.json: feedbackCategory must not be blank.");
    }

    private static void ValidateList(List<SubjectCategory> subjects, string listName)
    {
        if (subjects is null || subjects.Count == 0)
            throw new InvalidOperationException($"subjects.json: '{listName}' must not be empty.");

        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var s in subjects)
        {
            if (string.IsNullOrWhiteSpace(s.Key))
                throw new InvalidOperationException($"subjects.json: '{listName}' has an entry with a blank key.");
            if (string.IsNullOrWhiteSpace(s.Label))
                throw new InvalidOperationException($"subjects.json: '{listName}' entry '{s.Key}' has a blank label.");
            if (!seenKeys.Add(s.Key))
                throw new InvalidOperationException($"subjects.json: '{listName}' has duplicate key '{s.Key}'.");
        }
    }
}
