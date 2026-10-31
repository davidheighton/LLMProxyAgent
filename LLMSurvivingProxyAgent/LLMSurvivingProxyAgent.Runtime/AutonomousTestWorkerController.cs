using Microsoft.Extensions.Hosting;

namespace LLMSurvivingProxyAgent.Runtime;

public interface IAutonomousTestWorkerController
{
    bool IsRunning { get; }

    Task StartAsync(string modelName, CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}

public sealed class AutonomousTestWorkerController(
    Func<string, AutonomousTestWorker> createWorker) : IAutonomousTestWorkerController, IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private AutonomousTestWorker? _worker;
    private bool _disposed;

    public bool IsRunning => _worker?.ExecuteTask is { IsCompleted: false };

    public async Task StartAsync(
        string modelName,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);
        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (IsRunning)
            {
                return;
            }

            _worker?.Dispose();
            _worker = null;

            var worker = createWorker(modelName);
            try
            {
                await worker.StartAsync(cancellationToken).ConfigureAwait(false);
                _worker = worker;
                if (worker.ExecuteTask is { IsFaulted: true } executeTask)
                {
                    await executeTask.ConfigureAwait(false);
                }
            }
            catch
            {
                worker.Dispose();
                throw;
            }
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
            var worker = _worker;
            _worker = null;
            if (worker is null)
            {
                return;
            }

            try
            {
                await worker.StopAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                worker.Dispose();
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        await StopAsync().ConfigureAwait(false);
        _lifecycle.Dispose();
        _disposed = true;
    }
}
