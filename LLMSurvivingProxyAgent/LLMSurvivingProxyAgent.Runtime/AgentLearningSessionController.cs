using LLMSurvivingProxyAgent.Core;
using Microsoft.Extensions.Logging;

namespace LLMSurvivingProxyAgent.Runtime;

public interface IAgentLearningSessionController
{
    bool IsRunning { get; }

    Task StartAsync(string agentId, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed class AgentLearningSessionController(
    GameSituationObservationService observationService,
    ILogger<AgentLearningSessionController> logger,
    TimeSpan observationInterval) : IAgentLearningSessionController, IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private CancellationTokenSource? _stopSource;
    private Task? _observationTask;
    private bool _disposed;
    private int _disposeRequested;

    public bool IsRunning => _observationTask is { IsCompleted: false };

    public async Task StartAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        if (observationInterval <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(observationInterval),
                "The learning observation interval must be greater than zero.");
        }

        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(
                _disposed || Volatile.Read(ref _disposeRequested) != 0,
                this);
            if (IsRunning)
            {
                return;
            }

            _stopSource?.Dispose();
            var stopSource = new CancellationTokenSource();
            _stopSource = stopSource;
            _observationTask = ObserveAsync(
                agentId,
                $"learn-{Guid.NewGuid():N}",
                stopSource.Token);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposeRequested, 1) != 0)
        {
            return;
        }

        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            _disposed = true;
            await StopCoreAsync().ConfigureAwait(false);
        }
        finally
        {
            _lifecycle.Release();
            _lifecycle.Dispose();
        }
    }

    private async Task StopCoreAsync()
    {
        CancellationTokenSource? stopSource = _stopSource;
        Task? observationTask = _observationTask;
        _stopSource = null;
        _observationTask = null;
        if (stopSource is null)
        {
            return;
        }

        stopSource.Cancel();
        try
        {
            if (observationTask is not null)
            {
                await observationTask.ConfigureAwait(false);
            }
        }
        finally
        {
            stopSource.Dispose();
        }
    }

    private async Task ObserveAsync(
        string agentId,
        string sessionId,
        CancellationToken cancellationToken)
    {
        using var timer = new PeriodicTimer(observationInterval);
        try
        {
            do
            {
                try
                {
                    GameSituationObservationResult result =
                        await observationService.CaptureAndRecordChangedAsync(
                            agentId,
                            sessionId,
                            cancellationToken).ConfigureAwait(false);
                    if (result.Recorded)
                    {
                        logger.LogInformation(
                            "Learn mode recorded a trusted game observation for agent {AgentId}.",
                            agentId);
                    }
                    else
                    {
                        logger.LogDebug(
                            "Learn mode did not record an observation for agent {AgentId}: {Reason}",
                            agentId,
                            result.RejectionReason);
                    }
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return;
                }
                catch (Exception exception)
                {
                    logger.LogError(
                        exception,
                        "Learn mode failed to record a game observation for agent {AgentId}.",
                        agentId);
                }
            }
            while (await timer.WaitForNextTickAsync(cancellationToken).ConfigureAwait(false));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
