namespace ROTA.Domain.Entities;

public class AuditLog
{
    public static AuditLog Create(
        Guid? playerId,
        string action,
        string? inputHash,
        string? resultSummary,
        string? ipAddress) => new AuditLog
        {
            PlayerId = playerId,
            Action = action,
            InputHash = inputHash,
            ResultSummary = resultSummary,
            IpAddress = ipAddress,
            CreatedAt = DateTimeOffset.UtcNow,
        };

    public long Id { get; private set; }

    public Guid? PlayerId { get; private set; }

    public string Action { get; private set; } = string.Empty;

    public string? InputHash { get; private set; }

    public string? ResultSummary { get; private set; }

    public string? IpAddress { get; private set; }
    public string? SessionId { get; private set; }

    public bool Flagged { get; private set; } = false;
    public string? FlagReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}