// T68 — terms/privacy acceptance shapes.
namespace ROTA.Shared.DTOs;

public class LegalDocumentResponse
{
    /// <summary>"terms" or "privacy".</summary>
    public string Document { get; set; } = string.Empty;

    /// <summary>The current terms version this document belongs to.</summary>
    public int Version { get; set; }

    /// <summary>The document body, markdown.</summary>
    public string Markdown { get; set; } = string.Empty;
}

public class AcceptTermsRequest
{
    /// <summary>Must equal the server's current terms version.</summary>
    public int Version { get; set; }
}
