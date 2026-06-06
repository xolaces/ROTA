namespace ROTA.Application.Models;

/// <summary>A concrete message handed to <c>IEmailService</c> for delivery.</summary>
public sealed class EmailMessage
{
    public required string To { get; init; }
    public required string Subject { get; init; }
    public required string Body { get; init; }
    public bool IsHtml { get; init; } = true;
}
