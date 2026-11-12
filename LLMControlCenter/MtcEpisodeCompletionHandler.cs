using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Threading;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;
using TWXProxy.Core;

namespace MTC;

/// <summary>
/// Ends a harness-managed MTC process: writes the episode result file, then optionally shuts MTC down with
/// an exit code that encodes the episode status (see <see cref="EpisodeExitCodes"/>).
/// </summary>
internal sealed class MtcEpisodeCompletionHandler(
    EpisodeOptions options,
    ITrajectoryStore store) : IEpisodeCompletionHandler
{
    public async Task OnEpisodeEndedAsync(TrajectoryEpisode episode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(episode);
        try
        {
            if (options.ResultPath is { } resultPath)
            {
                await new EpisodeResultFileWriter(resultPath, store)
                    .OnEpisodeEndedAsync(episode, CancellationToken.None)
                    .ConfigureAwait(false);
            }
        }
        catch (Exception exception)
        {
            GlobalModules.DebugLog($"[Episode] result file could not be written: {exception}\n");
        }

        GlobalModules.DebugLog(
            $"[Episode] {episode.EpisodeId} ended as {episode.Status}: {episode.EndReason}\n");
        if (!options.ExitOnEnd)
        {
            return;
        }

        int exitCode = EpisodeExitCodes.FromStatus(episode.Status);
        Dispatcher.UIThread.Post(() =>
        {
            Environment.ExitCode = exitCode;
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown(exitCode);
            }
        });
    }
}
