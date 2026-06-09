namespace ROTA.Shared.DTOs;

/// <summary>
/// The subject catalog served by GET /api/subjects (T52). Clients populate the Bug / Report subject
/// pickers from these lists; Feedback stays open-text but is filed under <see cref="FeedbackCategory"/>.
/// </summary>
public class SubjectCatalogResponse
{
    public IReadOnlyList<SubjectOption> BugSubjects { get; init; } = Array.Empty<SubjectOption>();
    public IReadOnlyList<SubjectOption> ReportSubjects { get; init; } = Array.Empty<SubjectOption>();
    public string FeedbackCategory { get; init; } = string.Empty;
}

/// <summary>One selectable subject: a stable <see cref="Key"/> and a human-readable <see cref="Label"/>.</summary>
public class SubjectOption
{
    public string Key { get; init; } = string.Empty;
    public string Label { get; init; } = string.Empty;
}
