using System.Threading.Channels;
using ROTA.Application.Interfaces;

namespace ROTA.Infrastructure.Services;

/// <summary>Unbounded in-process channel backing <see cref="IEmailSendQueue"/>. Singleton.</summary>
public sealed class EmailSendQueue : IEmailSendQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(
        new UnboundedChannelOptions { SingleReader = true, SingleWriter = false });

    public void Enqueue(Guid emailId) => _channel.Writer.TryWrite(emailId);

    public ValueTask<Guid> DequeueAsync(CancellationToken ct) => _channel.Reader.ReadAsync(ct);
}
