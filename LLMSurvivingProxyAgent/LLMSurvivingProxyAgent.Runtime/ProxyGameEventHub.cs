using System.Threading.Channels;
using System.Runtime.CompilerServices;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed record ProxySessionEvent(string AgentId, ProxyGameEvent Event);

public sealed class ProxyGameEventHub
{
    public const int Capacity = 128;

    private readonly Channel<ProxySessionEvent> _events;
    private int _queuedEventCount;
    private long _droppedEventCount;

    public ProxyGameEventHub()
    {
        _events = Channel.CreateBounded<ProxySessionEvent>(
            new BoundedChannelOptions(Capacity)
            {
                FullMode = BoundedChannelFullMode.DropOldest,
                SingleReader = true,
                SingleWriter = false
            },
            _ =>
            {
                Interlocked.Decrement(ref _queuedEventCount);
                Interlocked.Increment(ref _droppedEventCount);
            });
    }

    public int QueueDepth => Volatile.Read(ref _queuedEventCount);
    public long DroppedEventCount => Interlocked.Read(ref _droppedEventCount);

    public bool TryPublish(ProxySessionEvent sessionEvent)
    {
        ArgumentNullException.ThrowIfNull(sessionEvent);
        Interlocked.Increment(ref _queuedEventCount);
        if (_events.Writer.TryWrite(sessionEvent))
        {
            return true;
        }

        Interlocked.Decrement(ref _queuedEventCount);
        Interlocked.Increment(ref _droppedEventCount);
        return false;
    }

    public IAsyncEnumerable<ProxySessionEvent> ReadAllAsync(
        CancellationToken cancellationToken = default) =>
        ReadAllCoreAsync(cancellationToken);

    private async IAsyncEnumerable<ProxySessionEvent> ReadAllCoreAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var sessionEvent in _events.Reader.ReadAllAsync(cancellationToken))
        {
            Interlocked.Decrement(ref _queuedEventCount);
            yield return sessionEvent;
        }
    }
}
