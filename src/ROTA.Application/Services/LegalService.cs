using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Domain.Entities;
using ROTA.Shared.DTOs;

namespace ROTA.Application.Services;

public sealed class LegalService : ILegalService
{
    private readonly ILegalTextProvider _texts;
    private readonly IPlayerRepository _players;
    private readonly IAuditLogRepository _auditLog;
    private readonly LegalConfig _cfg;

    public LegalService(
        ILegalTextProvider texts,
        IPlayerRepository players,
        IAuditLogRepository auditLog,
        IOptions<LegalConfig> cfg)
    {
        _texts = texts;
        _players = players;
        _auditLog = auditLog;
        _cfg = cfg.Value;
    }

    public LegalDocumentResponse GetTerms() => new()
    {
        Document = "terms",
        Version = _cfg.CurrentTermsVersion,
        Markdown = _texts.TermsMarkdown,
    };

    public LegalDocumentResponse GetPrivacy() => new()
    {
        Document = "privacy",
        Version = _cfg.CurrentTermsVersion,
        Markdown = _texts.PrivacyMarkdown,
    };

    public async Task<AcceptTermsStatus> AcceptTermsAsync(
        Guid playerId, int version, string ipAddress, CancellationToken ct = default)
    {
        // Only the current version counts — accepting an old (or unknown future) version would
        // let a stale client silently bypass a re-acceptance round.
        if (version != _cfg.CurrentTermsVersion)
            return AcceptTermsStatus.StaleVersion;

        var player = await _players.FindByIdAsync(playerId, ct);
        if (player is null)
            return AcceptTermsStatus.NotFound;

        if (player.AcceptedTermsVersion >= version)
            return AcceptTermsStatus.Success; // idempotent re-accept

        player.AcceptTerms(version);
        await _players.UpdateAsync(player, ct);

        await _auditLog.AppendAsync(AuditLog.Create(
            playerId, "TermsAccepted", null,
            $"Accepted terms v{version}", ipAddress), ct);

        return AcceptTermsStatus.Success;
    }
}
