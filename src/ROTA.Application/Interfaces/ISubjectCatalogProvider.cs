using ROTA.Application.Models;

namespace ROTA.Application.Interfaces;

/// <summary>
/// Single source of truth for the server-validated subject catalog (T52). Loaded once at startup from
/// <c>content/subjects.json</c> (eager singleton, startup-validated). Validators reject off-list
/// subjects; the public catalog endpoint serves the lists to clients.
/// </summary>
public interface ISubjectCatalogProvider
{
    /// <summary>Allowed bug-report subjects (key + label).</summary>
    IReadOnlyList<SubjectCategory> BugSubjects { get; }

    /// <summary>Allowed player-report subjects (key + label).</summary>
    IReadOnlyList<SubjectCategory> ReportSubjects { get; }

    /// <summary>The fixed category label every Feedback submission is filed under.</summary>
    string FeedbackCategory { get; }

    /// <summary>True if <paramref name="subjectKeyOrLabel"/> matches a bug subject by key or label (case-insensitive).</summary>
    bool IsValidBugSubject(string subjectKeyOrLabel);

    /// <summary>True if <paramref name="subjectKeyOrLabel"/> matches a report subject by key or label (case-insensitive).</summary>
    bool IsValidReportSubject(string subjectKeyOrLabel);

    /// <summary>Resolves a bug subject (key or label) to its canonical label; null if not on-list.</summary>
    string? NormalizeBugSubject(string subjectKeyOrLabel);

    /// <summary>Resolves a report subject (key or label) to its canonical label; null if not on-list.</summary>
    string? NormalizeReportSubject(string subjectKeyOrLabel);

    /// <summary>The full catalog snapshot (for the public GET /api/subjects endpoint).</summary>
    SubjectCatalog GetCatalog();
}
