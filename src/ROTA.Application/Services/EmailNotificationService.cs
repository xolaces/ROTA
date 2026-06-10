using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;
using ROTA.Domain.Entities;

namespace ROTA.Application.Services;

/// <summary>
/// Orchestrates operator notifications: build payload → persist <c>outbound_emails</c> row (source of
/// truth) + <c>audit_log</c> → enqueue SMTP send. The send itself runs on the background queue and can
/// fail without affecting the caller (mirrors AuditLogMiddleware's swallow-and-log discipline).
/// </summary>
public sealed class EmailNotificationService : IEmailNotificationService
{
    private static readonly JsonSerializerOptions CompactJson = new() { WriteIndented = false };

    private readonly IOutboundEmailRepository _emails;
    private readonly IEmailSendQueue _queue;
    private readonly IEmailService _email;
    private readonly IAuditLogRepository _audit;
    private readonly EmailConfig _cfg;
    private readonly ILogger<EmailNotificationService> _log;

    public EmailNotificationService(
        IOutboundEmailRepository emails,
        IEmailSendQueue queue,
        IEmailService email,
        IAuditLogRepository audit,
        IOptions<EmailConfig> cfg,
        ILogger<EmailNotificationService> log)
    {
        _emails = emails;
        _queue = queue;
        _email = email;
        _audit = audit;
        _cfg = cfg.Value;
        _log = log;
    }

    public async Task<Guid> QueueAsync(EmailPayload payload, string? ipAddress = null, CancellationToken ct = default)
    {
        var subject = $"[ROTA][{payload.Type}] {payload.Subject}";
        var detailJson = payload.Detail is null ? null : JsonSerializer.Serialize(payload.Detail, CompactJson);
        var metadataJson = payload.Metadata is null ? null : JsonSerializer.Serialize(payload.Metadata, CompactJson);

        var email = OutboundEmail.Create(
            payload.Type,
            subject,
            _cfg.OperatorRecipient,
            payload.Summary,
            payload.TriggeringPlayerId,
            payload.TriggeringSystem,
            detailJson,
            metadataJson,
            payload.Priority);

        // [1] Persist first — the row survives even if the send never succeeds.
        await _emails.AddAsync(email, ct);

        // [2] Audit every state change (CLAUDE.md).
        await _audit.AppendAsync(AuditLog.Create(
            payload.TriggeringPlayerId,
            "EmailQueued",
            inputHash: null,
            resultSummary: $"type={payload.Type} id={email.Id} system={payload.TriggeringSystem ?? "-"}",
            ipAddress), ct);

        // [3] Send second — off the request path. Caller is never blocked.
        _queue.Enqueue(email.Id);
        return email.Id;
    }

    public async Task<bool> ProcessSendAsync(Guid emailId, CancellationToken ct = default)
    {
        var email = await _emails.GetByIdAsync(emailId, ct);
        if (email is null)
        {
            _log.LogWarning("Outbound email {Id} not found for send", emailId);
            return true; // nothing to retry
        }
        if (email.SendStatus == Domain.Enums.EmailSendStatus.Sent)
            return true; // already delivered (startup sweep + retry can overlap a live enqueue)

        try
        {
            await _email.SendAsync(new EmailMessage
            {
                To = email.Recipient,
                Subject = email.Subject,
                Body = BuildBody(email),
                IsHtml = true,
            }, ct);
            email.MarkSent();
        }
        catch (Exception ex)
        {
            // A delivery failure must never break anything — it is recorded and surfaced in the dashboard.
            email.MarkFailed(ex.Message);
            _log.LogError(ex, "Outbound email {Id} ({Type}) send failed (attempt {Attempts}/{Max})",
                email.Id, email.Type, email.SendAttempts, _cfg.MaxSendAttempts);
        }

        await _emails.UpdateAsync(email, ct);

        // T71 — retryable while attempts remain; exhausted rows stay Failed for dashboard triage.
        return email.SendStatus == Domain.Enums.EmailSendStatus.Sent
            || email.SendAttempts >= _cfg.MaxSendAttempts;
    }

    private static string BuildBody(OutboundEmail email)
    {
        var detail = string.IsNullOrWhiteSpace(email.DetailJson) ? "{}" : Prettify(email.DetailJson!);
        var summary = System.Net.WebUtility.HtmlEncode(email.Summary);
        var detailHtml = System.Net.WebUtility.HtmlEncode(detail);
        // Single-quoted HTML attributes avoid escaping inside the interpolated string.
        return
            "<div style='font-family:system-ui,Segoe UI,Arial,sans-serif'>" +
            $"<h2 style='margin:0 0 4px'>[{email.Type}]</h2>" +
            $"<p style='margin:0 0 12px;color:#444'>{summary}</p>" +
            "<table style='border-collapse:collapse;font-size:13px'>" +
            $"<tr><td style='padding:2px 8px;color:#888'>Triggering player</td><td>{email.TriggeringPlayerId?.ToString() ?? "—"}</td></tr>" +
            $"<tr><td style='padding:2px 8px;color:#888'>System</td><td>{email.TriggeringSystem ?? "—"}</td></tr>" +
            $"<tr><td style='padding:2px 8px;color:#888'>Created</td><td>{email.CreatedAt:u}</td></tr>" +
            "</table>" +
            $"<pre style='background:#0d1117;color:#c9d1d9;padding:12px;border-radius:8px;overflow:auto;font-size:12px'>{detailHtml}</pre>" +
            "</div>";
    }

    private static string Prettify(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = true });
        }
        catch
        {
            return json;
        }
    }
}
