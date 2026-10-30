using System.Threading.Channels;

namespace Klavyra.Api.Infrastructure;

public sealed class TranscriptionQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new() { SingleReader = true });

    public void Enqueue(Guid projectId) => _channel.Writer.TryWrite(projectId);

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);
}
