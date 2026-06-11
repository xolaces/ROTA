using ROTA.Application.Interfaces;

namespace ROTA.Infrastructure.Services;

/// <summary>
/// Eager singleton (T68): loads <c>content/legal/terms.md</c> + <c>privacy.md</c> at construction
/// and throws on missing/blank files so a broken legal bundle fails at boot. Mirrors
/// <c>SubjectCatalogProvider</c>.
/// </summary>
public sealed class LegalTextProvider : ILegalTextProvider
{
    public string TermsMarkdown { get; }
    public string PrivacyMarkdown { get; }

    public LegalTextProvider(string contentRootPath)
    {
        TermsMarkdown = Load(contentRootPath, "terms.md");
        PrivacyMarkdown = Load(contentRootPath, "privacy.md");
    }

    private static string Load(string contentRootPath, string fileName)
    {
        var path = Path.Combine(contentRootPath, "content", "legal", fileName);
        if (!File.Exists(path))
            throw new InvalidOperationException($"Legal document not found at '{path}'.");

        var text = File.ReadAllText(path);
        if (string.IsNullOrWhiteSpace(text))
            throw new InvalidOperationException($"Legal document '{path}' is empty.");
        return text;
    }
}
