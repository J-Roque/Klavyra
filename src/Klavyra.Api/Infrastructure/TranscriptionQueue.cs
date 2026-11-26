using System.Collections.Concurrent;
using System.Threading.Channels;

namespace Klavyra.Api.Infrastructure;

public sealed class TranscriptionQueue
{
    private readonly Channel<Guid> _channel = Channel.CreateUnbounded<Guid>(new() { SingleReader = true });
    private readonly ConcurrentDictionary<Guid, bool> _cancelRequested = new();

    public void Enqueue(Guid projectId)
    {
        _cancelRequested.TryRemove(projectId, out _); // a retry starts clean
        _channel.Writer.TryWrite(projectId);
    }

    public IAsyncEnumerable<Guid> ReadAllAsync(CancellationToken ct) => _channel.Reader.ReadAllAsync(ct);

    /// <summary>The user asked to stop this project; the worker loop honours it even if it had just started.</summary>
    public void RequestCancel(Guid projectId) => _cancelRequested[projectId] = true;

    public bool IsCancelRequested(Guid projectId) => _cancelRequested.ContainsKey(projectId);

    public void ClearCancel(Guid projectId) => _cancelRequested.TryRemove(projectId, out _);
}
