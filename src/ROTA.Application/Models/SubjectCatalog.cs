namespace ROTA.Application.Models;

/// <summary>
/// The server-authoritative subject catalog (T52), loaded once from <c>content/subjects.json</c> by
/// <c>ISubjectCatalogProvider</c>. Bug and Player-report subjects are fixed, validated lists; player
/// feedback stays open-text but is always indexed under <see cref="FeedbackCategory"/>.
/// </summary>
public sealed class SubjectCatalog
{
    /// <summary>Allowed subjects for in-game bug reports.</summary>
    public List<SubjectCategory> BugSubjects { get; set; } = new();

    /// <summary>Allowed reasons for a player report.</summary>
    public List<SubjectCategory> ReportSubjects { get; set; } = new();

    /// <summary>The fixed label every Feedback submission is filed under (e.g. "Player Feedback").</summary>
    public string FeedbackCategory { get; set; } = string.Empty;
}

/// <summary>One selectable subject: a stable <see cref="Key"/> plus a human-readable <see cref="Label"/>.</summary>
public sealed class SubjectCategory
{
    /// <summary>Stable machine key (snake_case), e.g. "exploit_abuse".</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Human-readable label stored on the email, e.g. "Exploit Abuse".</summary>
    public string Label { get; set; } = string.Empty;
}
