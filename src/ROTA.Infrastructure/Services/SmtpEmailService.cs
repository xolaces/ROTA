using System.Net;
using System.Net.Mail;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ROTA.Application.Configuration;
using ROTA.Application.Interfaces;
using ROTA.Application.Models;

namespace ROTA.Infrastructure.Services;

/// <summary>
/// SMTP email transport (Gmail by default). Uses in-framework <see cref="SmtpClient"/> — no extra package.
/// Throws on a misconfigured/failed send; the caller treats that as non-fatal and records it on the row.
/// </summary>
// BETA: Gmail SMTP with an app-password-style credential supplied by the owner (temporary, pending
// rotation). IEmailService keeps the provider swappable — a SendGrid implementation is the future path.
public sealed class SmtpEmailService : IEmailService
{
    private readonly EmailConfig _cfg;
    private readonly ILogger<SmtpEmailService> _log;

    public SmtpEmailService(IOptions<EmailConfig> cfg, ILogger<SmtpEmailService> log)
    {
        _cfg = cfg.Value;
        _log = log;
    }

    public async Task SendAsync(EmailMessage message, CancellationToken ct = default)
    {
        if (!_cfg.Enabled)
        {
            _log.LogInformation("Email disabled — skipping send to {To} ({Subject})", message.To, message.Subject);
            return;
        }

        if (string.IsNullOrWhiteSpace(_cfg.Username) || string.IsNullOrWhiteSpace(_cfg.Password))
            throw new InvalidOperationException(
                "SMTP credentials are not configured (set Email:Username and Email:Password via user-secrets).");

        using var client = new SmtpClient(_cfg.Host, _cfg.Port)
        {
            EnableSsl = _cfg.UseStartTls,
            DeliveryMethod = SmtpDeliveryMethod.Network,
            Credentials = new NetworkCredential(_cfg.Username, _cfg.Password),
        };

        using var mail = new MailMessage
        {
            From = new MailAddress(_cfg.FromAddress, _cfg.FromName),
            Subject = message.Subject,
            Body = message.Body,
            IsBodyHtml = message.IsHtml,
        };
        mail.To.Add(message.To);

        await client.SendMailAsync(mail, ct);
    }
}
