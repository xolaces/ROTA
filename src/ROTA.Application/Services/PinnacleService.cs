using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Entities;
using ROTA.Domain.Enums;

namespace ROTA.Application.Services;

/// <summary>
/// Records pinnacle first-claims (T33). The first player to reach a pinnacle level triggers an audit
/// entry and a PinnacleFirstClaim operator email; subsequent players at the same level are no-ops.
/// </summary>
public sealed class PinnacleService : IPinnacleService
{
    private readonly IPinnacleClaimRepository _claims;
    private readonly IEmailNotificationService _emails;
    private readonly IAuditLogRepository _audit;

    public PinnacleService(
        IPinnacleClaimRepository claims,
        IEmailNotificationService emails,
        IAuditLogRepository audit)
    {
        _claims = claims;
        _emails = emails;
        _audit = audit;
    }

    public async Task<bool> RecordFirstClaimAsync(Guid playerId, int pinnacleLevel, CancellationToken ct = default)
    {
        var claimed = await _claims.TryClaimAsync(pinnacleLevel, playerId, ct);
        if (!claimed) return false;

        await _audit.AppendAsync(AuditLog.Create(
            playerId,
            "PinnacleFirstClaim",
            inputHash: null,
            resultSummary: $"player={playerId} first to reach pinnacle level {pinnacleLevel}",
            ipAddress: null), ct);

        await _emails.QueueAsync(new EmailPayload
        {
            Type = EmailType.PinnacleFirstClaim,
            Subject = $"Pinnacle {pinnacleLevel} first-claimed",
            Summary = $"Player {playerId} is the first to reach pinnacle level {pinnacleLevel}.",
            TriggeringPlayerId = playerId,
            TriggeringSystem = "T33",
            Detail = new Dictionary<string, object?>
            {
                ["pinnacleLevel"] = pinnacleLevel,
                ["playerId"] = playerId.ToString(),
            },
        }, ipAddress: null, ct);

        return true;
    }
}
