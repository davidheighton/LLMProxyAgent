using System.Text;

namespace LLMSurvivingProxyAgent.Runtime;

public static class EventLogFileAppender
{
    private static readonly SemaphoreSlim WriteLock = new(1, 1);
    private static readonly UTF8Encoding Utf8WithoutBom = new(encoderShouldEmitUTF8Identifier: false);

    public static async Task AppendLineAsync(
        string path,
        string line,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(line);
        string fullPath = Path.GetFullPath(path);

        await WriteLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            await File.AppendAllTextAsync(
                fullPath,
                line + Environment.NewLine,
                Utf8WithoutBom,
                cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            WriteLock.Release();
        }
    }

    public static void AppendLine(string path, string line)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        ArgumentNullException.ThrowIfNull(line);
        string fullPath = Path.GetFullPath(path);

        WriteLock.Wait();
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
            File.AppendAllText(
                fullPath,
                line + Environment.NewLine,
                Utf8WithoutBom);
        }
        finally
        {
            WriteLock.Release();
        }
    }
}
