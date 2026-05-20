namespace ROTA.Domain.Entities;

/// <summary>
/// Append-only record of every state-changing action on the server.
/// The DB role has no UPDATE or DELETE permission on this table.
/// Every entry is written regardless of whether the action succeeded or failed.
/// </summary>
public class AuditLog
{
    public long Id { get; private set; }

    /// <summary>
    /// Null for unauthenticated requests (e.g. failed login attempts).
    /// </summary>
    public Guid? PlayerId { get; private set; }

    /// <summary>
    /// Action identifier — e.g. "QuestAttempt", "EnergyRefill", "LoginFailed".
    /// </summary>
    public string Action { get; private set; } = string.Empty;

    /// <summary>
    /// SHA256 hash of the incoming request payload.
    /// Stored for tamper detection — never the raw payload.
    /// </summary>
    public string? InputHash { get; private set; }

    /// <summary>
    /// Human-readable summary of the outcome — e.g. "Quest 42 completed. XP +150."
    /// </summary>
    public string? ResultSummary { get; private set; }

    public string? IpAddress { get; private set; }
    public string? SessionId { get; private set; }

    /// <summary>
    /// Flagged automatically when anomalous frequency or suspicious pattern detected.
    /// </summary>
    public bool Flagged { get; private set; } = false;
    public string? FlagReason { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; } = DateTimeOffset.UtcNow;
}