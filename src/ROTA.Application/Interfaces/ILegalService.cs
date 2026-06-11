using ROTA.Shared.DTOs;

namespace ROTA.Application.Interfaces;

public enum AcceptTermsStatus { Success, NotFound, StaleVersion }

public interface ILegalService
{
    /// <summary>The canonical terms-of-service document + current version.</summary>
    LegalDocumentResponse GetTerms();

    /// <summary>The canonical privacy-policy document + current version.</summary>
    LegalDocumentResponse GetPrivacy();

    /// <summary>
    /// Records the player's acceptance of <paramref name="version"/>. Only the CURRENT version is
    /// acceptable (anything else → StaleVersion); re-accepting an already-accepted version is an
    /// idempotent success.
    /// </summary>
    Task<AcceptTermsStatus> AcceptTermsAsync(Guid playerId, int version, string ipAddress, CancellationToken ct = default);
}
