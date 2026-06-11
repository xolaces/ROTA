namespace ROTA.Application.Interfaces;

/// <summary>
/// T68 — serves the canonical terms-of-service / privacy-policy markdown shipped with the server
/// (content/legal/). Eager singleton: missing or blank documents fail at boot, not on first read.
/// </summary>
public interface ILegalTextProvider
{
    /// <summary>Markdown of the terms of service.</summary>
    string TermsMarkdown { get; }

    /// <summary>Markdown of the privacy policy.</summary>
    string PrivacyMarkdown { get; }
}
