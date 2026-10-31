using System.Threading.Channels;
using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed class AgentRuntimeQueue(int capacity = 64) : IAgentRuntime
{
    private readonly Channel<ReactiveWorkItem> _reactive = Channel.CreateBounded<ReactiveWorkItem>(
        new BoundedChannelOptions(ValidateCapacity(capacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly Channel<StrategicWorkItem> _strategic = Channel.CreateBounded<StrategicWorkItem>(
        new BoundedChannelOptions(ValidateCapacity(capacity))
        {
            FullMode = BoundedChannelFullMode.Wait,
            SingleReader = true,
            SingleWriter = false
        });

    private readonly object _statusLock = new();
    private bool _isRunning;
    private DateTimeOffset? _lastReactiveCycleAt;
    private DateTimeOffset? _lastStrategicCycleAt;

    public async Task<ReactiveEventRecord> SubmitReactiveEventAsync(
        ReactiveEvent reactiveEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reactiveEvent);
        var workItem = new ReactiveWorkItem(reactiveEvent);
        await _reactive.Writer.WriteAsync(workItem, cancellationToken);
        return await workItem.Completion.Task.WaitAsync(cancellationToken);
    }

    public async Task<StrategicReviewRecord> SubmitStrategicReviewAsync(
        string agentId,
        GameSnapshot snapshot,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(snapshot);
        var workItem = new StrategicWorkItem(agentId, snapshot);
        await _strategic.Writer.WriteAsync(workItem, cancellationToken);
        return await workItem.Completion.Task.WaitAsync(cancellationToken);
    }

    public AgentRuntimeStatus GetStatus()
    {
        lock (_statusLock)
        {
            return new AgentRuntimeStatus(
                _isRunning,
                _reactive.Reader.Count,
                _strategic.Reader.Count,
                _lastReactiveCycleAt,
                _lastStrategicCycleAt);
        }
    }

    internal ChannelReader<ReactiveWorkItem> ReactiveReader => _reactive.Reader;
    internal ChannelReader<StrategicWorkItem> StrategicReader => _strategic.Reader;

    internal void SetRunning(bool isRunning)
    {
        lock (_statusLock)
        {
            _isRunning = isRunning;
        }
    }

    internal void RecordReactiveCycle() =>
        RecordCycle(ref _lastReactiveCycleAt);

    internal void RecordStrategicCycle() =>
        RecordCycle(ref _lastStrategicCycleAt);

    internal void FailPending(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);
        _reactive.Writer.TryComplete(exception);
        _strategic.Writer.TryComplete(exception);
        while (_reactive.Reader.TryRead(out var reactiveWorkItem))
        {
            reactiveWorkItem.Completion.TrySetException(exception);
        }
        while (_strategic.Reader.TryRead(out var strategicWorkItem))
        {
            strategicWorkItem.Completion.TrySetException(exception);
        }
    }

    private void RecordCycle(ref DateTimeOffset? timestamp)
    {
        lock (_statusLock)
        {
            timestamp = DateTimeOffset.UtcNow;
        }
    }

    private static int ValidateCapacity(int capacity)
    {
        if (capacity is < 1 or > 1_024)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), "Runtime queue capacity must be between 1 and 1,024.");
        }
        return capacity;
    }
}

internal sealed class ReactiveWorkItem(ReactiveEvent reactiveEvent)
{
    public ReactiveEvent Event { get; } = reactiveEvent;
    public TaskCompletionSource<ReactiveEventRecord> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}

internal sealed class StrategicWorkItem(string agentId, GameSnapshot snapshot)
{
    public string AgentId { get; } = agentId;
    public GameSnapshot Snapshot { get; } = snapshot;
    public TaskCompletionSource<StrategicReviewRecord> Completion { get; } =
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
