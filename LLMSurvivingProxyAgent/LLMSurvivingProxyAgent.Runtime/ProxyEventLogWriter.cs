using System.Text.Json;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed class ProxyEventLogWriter : IAsyncDisposable
{
    private readonly string _directory;

    public ProxyEventLogWriter(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
        Directory.CreateDirectory(_directory);
    }

    public Task WriteAsync(
        ProxyGameEvent gameEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(gameEvent);
        var path = Path.Combine(
            _directory,
            $"events-{gameEvent.Timestamp.UtcDateTime:yyyyMMdd}.jsonl");
        return EventLogFileAppender.AppendLineAsync(
            path,
            JsonSerializer.Serialize(gameEvent),
            cancellationToken);
    }

    public ValueTask DisposeAsync() => ValueTask.CompletedTask;
}
