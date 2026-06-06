namespace ROTA.Application.Configuration;

/// <summary>
/// SMTP / operator-email settings. Non-secret fields live in appsettings; <see cref="Username"/> and
/// <see cref="Password"/> come from user-secrets / environment (never committed). Bound from the
/// "Email" configuration section.
/// </summary>
// BETA: provider is Gmail SMTP (concrete creds supplied by owner). IEmailService keeps this swappable;
// a SendGrid provider is the documented future path. Password is a temporary credential pending rotation.
public sealed class EmailConfig
{
    /// <summary>Master switch. When false, sends are skipped (rows still persist + surface in the dashboard).</summary>
    public bool Enabled { get; set; } = true;

    public string Host { get; set; } = "smtp.gmail.com";
    public int Port { get; set; } = 587;

    /// <summary>STARTTLS (Gmail on 587). Maps to SmtpClient.EnableSsl.</summary>
    public bool UseStartTls { get; set; } = true;

    /// <summary>SMTP username — from user-secrets (Email:Username).</summary>
    public string Username { get; set; } = string.Empty;

    /// <summary>SMTP password / app-password — from user-secrets (Email:Password). Temporary; rotate.</summary>
    public string Password { get; set; } = string.Empty;

    public string FromAddress { get; set; } = "rotadevteam@gmail.com";
    public string FromName { get; set; } = "ROTA Ops";

    /// <summary>Where every operator notification is delivered.</summary>
    public string OperatorRecipient { get; set; } = "rotadevteam@gmail.com";
}
