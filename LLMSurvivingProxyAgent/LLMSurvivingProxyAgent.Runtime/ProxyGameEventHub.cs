using System.Threading.Channels;
using System.Runtime.CompilerServices;
using System.Collections.Concurrent;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed record ProxySessionEvent(string AgentId, ProxyGameEvent Event);

public sealed class ProxyGameEventHub
{
    public const int Capacity = 128;

    private readonly Channel<ProxySessionEvent> _events;
    private readonly ConcurrentDictionary<string, AgentEventQueue> _agentEvents =
        new(StringComparer.OrdinalIgnoreCase);
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
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionEvent.AgentId);

        var agentQueue = _agentEvents.GetOrAdd(sessionEvent.AgentId, static _ => new AgentEventQueue());
        var agentAccepted = agentQueue.TryPublish(sessionEvent);
        Interlocked.Increment(ref _queuedEventCount);
        var observerAccepted = _events.Writer.TryWrite(sessionEvent);
        if (observerAccepted)
        {
            return agentAccepted;
        }

        Interlocked.Decrement(ref _queuedEventCount);
        Interlocked.Increment(ref _droppedEventCount);
        return false;
    }

    public IAsyncEnumerable<ProxySessionEvent> ReadAllAsync(
        CancellationToken cancellationToken = default) =>
        ReadAllCoreAsync(cancellationToken);

    public IAsyncEnumerable<ProxySessionEvent> ReadAllAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        var agentQueue = _agentEvents.GetOrAdd(agentId, static _ => new AgentEventQueue());
        return agentQueue.ReadAllAsync(cancellationToken);
    }

    public int GetQueueDepth(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return _agentEvents.TryGetValue(agentId, out var queue) ? queue.QueueDepth : 0;
    }

    public long GetDroppedEventCount(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return _agentEvents.TryGetValue(agentId, out var queue) ? queue.DroppedEventCount : 0;
    }

    private async IAsyncEnumerable<ProxySessionEvent> ReadAllCoreAsync(
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await foreach (var sessionEvent in _events.Reader.ReadAllAsync(cancellationToken))
        {
            Interlocked.Decrement(ref _queuedEventCount);
            yield return sessionEvent;
        }
    }

    private sealed class AgentEventQueue
    {
        private readonly Channel<ProxySessionEvent> _events;
        private int _queuedEventCount;
        private long _droppedEventCount;

        public AgentEventQueue()
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
            CancellationToken cancellationToken) =>
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
}
