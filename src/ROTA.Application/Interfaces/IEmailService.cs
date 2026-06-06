using ROTA.Application.Models;

namespace ROTA.Application.Interfaces;

/// <summary>
/// Low-level email transport. Implementations are provider-specific (SMTP/Gmail today; SendGrid is the
/// documented future swap). Throwing is acceptable — the caller persists first and treats a send failure
/// as non-fatal, recording it on the outbound row.
/// </summary>
public interface IEmailService
{
    Task SendAsync(EmailMessage message, CancellationToken ct = default);
}
