using System.Diagnostics;
using System.Text.Json;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;

namespace LLMSurvivingProxyAgent.Harness;

public sealed record EpisodeLaunch(
    HarnessEpisode Episode,
    EpisodeOptions Options,
    IReadOnlyList<string> Arguments);

public sealed record EpisodeProcessOutcome(int? ExitCode, bool KilledAfterTimeout, string? Error);

public interface IEpisodeProcessLauncher
{
    Task<EpisodeProcessOutcome> RunAsync(EpisodeLaunch launch, TimeSpan timeout, CancellationToken cancellationToken);
}

public sealed class MtcProcessLauncher(string mtcExecutablePath) : IEpisodeProcessLauncher
{
    public async Task<EpisodeProcessOutcome> RunAsync(
        EpisodeLaunch launch,
        TimeSpan timeout,
        CancellationToken cancellationToken)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = mtcExecutablePath,
            WorkingDirectory = Path.GetDirectoryName(mtcExecutablePath)!,
            UseShellExecute = false
        };
        foreach (string argument in launch.Arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("MTC did not start.");
        using var timeoutSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutSource.CancelAfter(timeout);
        try
        {
            await process.WaitForExitAsync(timeoutSource.Token).ConfigureAwait(false);
            return new EpisodeProcessOutcome(process.ExitCode, false, null);
        }
        catch (OperationCanceledException)
        {
            process.Kill(entireProcessTree: true);
            await process.WaitForExitAsync(CancellationToken.None).ConfigureAwait(false);
            if (cancellationToken.IsCancellationRequested)
            {
                return new EpisodeProcessOutcome(null, false, "The harness was cancelled; MTC was stopped.");
            }
            return new EpisodeProcessOutcome(null, true, $"MTC did not exit within {timeout.TotalMinutes:N0} minutes.");
        }
    }
}

public sealed record SchedulerSettings(
    int Parallel,
    EpisodeGoal Goal,
    TimeSpan WallClockCap,
    TimeSpan ExitGracePeriod,
    TimeOnly GameDayReset,
    string ResultDirectory,
    bool CreateAccounts = true,
    string? PilotModel = null,
    string? MemoryId = null);

/// <summary>Stop conditions and pacing for an unattended rolling run.</summary>
public sealed record RollingLimits(
    int? MaxEpisodes = null,
    DateTimeOffset? Deadline = null,
    string? StopFilePath = null,
    int MaxConsecutiveErrors = 5)
{
    /// <summary>Wait before starting another new episode after one ends in an error (e.g. the server is down).</summary>
    public TimeSpan ErrorBackoff { get; init; } = TimeSpan.FromMinutes(5);

    /// <summary>How often stop conditions, backoff, and paused-episode resume times are re-checked.</summary>
    public TimeSpan PollInterval { get; init; } = TimeSpan.FromMinutes(1);
}

/// <summary>
/// Runs harness episodes with bounded parallelism. Each run is one MTC process for one episode segment.
/// Paused episodes (out of turns) are given a resume time at the next game-day reset.
/// </summary>
public sealed class HarnessScheduler(
    HarnessStore harnessStore,
    ITrajectoryStore trajectoryStore,
    IEpisodeProcessLauncher launcher,
    SchedulerSettings settings,
    TextWriter log,
    TimeProvider? timeProvider = null,
    Func<HarnessEpisode, Task>? episodeFinished = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;

    /// <summary>Runs every episode that is pending or due to resume; returns when none are left to start now.</summary>
    public async Task RunDueAsync(IReadOnlyList<HarnessEpisode> episodes, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(episodes);
        if (settings.Parallel < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(settings), "Parallel must be at least 1.");
        }

        var queue = new Queue<HarnessEpisode>(episodes.Where(IsDue).OrderBy(episode => episode.Ordinal));
        var running = new List<Task>();
        while (queue.Count > 0 || running.Count > 0)
        {
            while (queue.Count > 0 && running.Count < settings.Parallel && !cancellationToken.IsCancellationRequested)
            {
                running.Add(RunOneAsync(queue.Dequeue(), cancellationToken));
            }
            if (running.Count == 0)
            {
                break;
            }

            Task finished = await Task.WhenAny(running).ConfigureAwait(false);
            running.Remove(finished);
            await finished.ConfigureAwait(false);
        }
    }

    public bool IsDue(HarnessEpisode episode) =>
        episode.State == HarnessEpisodeState.Pending ||
        episode.State == HarnessEpisodeState.Paused &&
        (episode.ResumeAfter is null || episode.ResumeAfter <= _time.GetUtcNow());

    public DateTimeOffset NextGameDayReset(DateTimeOffset now)
    {
        DateTime localNow = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local).DateTime;
        DateTime candidate = localNow.Date + settings.GameDayReset.ToTimeSpan();
        if (candidate <= localNow)
        {
            candidate = candidate.AddDays(1);
        }

        // Resolve the offset for the target date so daylight-saving changes do not shift the reset time.
        return new DateTimeOffset(candidate, TimeZoneInfo.Local.GetUtcOffset(candidate));
    }

    private async Task<HarnessEpisode> RunOneAsync(HarnessEpisode episode, CancellationToken cancellationToken)
    {
        string resultPath = Path.Combine(settings.ResultDirectory, $"{episode.EpisodeId}.segment-{episode.Segments + 1}.json");
        var options = new EpisodeOptions(episode.EpisodeId, settings.Goal, settings.WallClockCap)
        {
            ResultPath = resultPath,
            ExitOnEnd = true,
            PilotModel = settings.PilotModel,
            MemoryId = settings.MemoryId
        };
        var arguments = new List<string>
        {
            "--instance-id", episode.AgentId,
            "--agent-id", episode.AgentId,
            "--game-profile", episode.ProfileFileName
        };
        if (settings.CreateAccounts)
        {
            arguments.Add("--create-account-if-needed");
        }
        arguments.AddRange(EpisodeCommandLine.ToArguments(options));

        DateTimeOffset startedAt = _time.GetUtcNow();
        HarnessEpisode running = episode with
        {
            State = HarnessEpisodeState.Running,
            ResumeAfter = null,
            UpdatedAt = startedAt
        };
        await harnessStore.UpsertEpisodeAsync(running, cancellationToken).ConfigureAwait(false);
        log.WriteLine($"[{startedAt:u}] start {episode.EpisodeId} ({episode.AgentId}) segment {episode.Segments + 1}");

        EpisodeProcessOutcome outcome;
        try
        {
            outcome = await launcher.RunAsync(
                new EpisodeLaunch(running, options, arguments),
                settings.WallClockCap + settings.ExitGracePeriod,
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            outcome = new EpisodeProcessOutcome(null, false, exception.Message);
        }

        EpisodeStatus? resultStatus = ReadResultStatus(resultPath);
        EpisodeStatus status = resultStatus
            ?? (outcome.ExitCode is { } code ? EpisodeExitCodes.ToStatus(code) : null)
            ?? (outcome.KilledAfterTimeout ? EpisodeStatus.TimedOut
                : outcome.ExitCode == 0 ? EpisodeStatus.Stopped
                : EpisodeStatus.Error);
        await ReconcileTrajectoryAsync(episode.EpisodeId, status, outcome, cancellationToken).ConfigureAwait(false);

        DateTimeOffset now = _time.GetUtcNow();
        HarnessEpisode finished = running with
        {
            State = ToHarnessState(status),
            Segments = episode.Segments + 1,
            ResumeAfter = status == EpisodeStatus.PausedOutOfTurns ? NextGameDayReset(now) : null,
            LastExitCode = outcome.ExitCode,
            LastError = outcome.Error,
            UpdatedAt = now
        };
        await harnessStore.UpsertEpisodeAsync(finished, CancellationToken.None).ConfigureAwait(false);
        log.WriteLine(
            $"[{now:u}] end   {episode.EpisodeId} -> {finished.State}" +
            (finished.ResumeAfter is { } resume ? $" (resume after {resume.ToLocalTime():g})" : string.Empty) +
            (outcome.Error is { } error ? $" — {error}" : string.Empty));
        if (finished.IsFinished && episodeFinished is not null)
        {
            try
            {
                await episodeFinished(finished).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException or Microsoft.Data.Sqlite.SqliteException)
            {
                log.WriteLine($"Cleanup of {finished.AgentId} failed: {exception.Message}");
            }
        }

        return finished;
    }

    /// <summary>
    /// Keeps <see cref="SchedulerSettings.Parallel"/> episodes running unattended: resumes this run's paused episodes
    /// when their game day has reset, otherwise starts a new episode from <paramref name="createEpisode"/>. Stops when a
    /// limit is reached, the stop file appears, the token is cancelled, or too many episodes end in a row with an error.
    /// Running episodes are always allowed to finish. Returns the number of new episodes started.
    /// </summary>
    public async Task<int> RunRollingAsync(
        Func<int, CancellationToken, Task<HarnessEpisode>> createEpisode,
        RollingLimits limits,
        Func<HarnessEpisode, Task>? afterEpisode,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(createEpisode);
        ArgumentNullException.ThrowIfNull(limits);
        var running = new Dictionary<Task<HarnessEpisode>, HarnessEpisode>();
        var paused = new List<HarnessEpisode>();
        int started = 0;
        int consecutiveErrors = 0;
        DateTimeOffset? backoffUntil = null;
        string? stopReason = null;
        while (true)
        {
            DateTimeOffset now = _time.GetUtcNow();
            stopReason ??= cancellationToken.IsCancellationRequested ? "cancelled"
                : limits.StopFilePath is { } stopFile && File.Exists(stopFile) ? $"stop file '{stopFile}' found"
                : limits.Deadline is { } deadline && now >= deadline ? "deadline reached"
                : consecutiveErrors >= limits.MaxConsecutiveErrors ? $"{consecutiveErrors} episodes in a row ended in an error"
                : null;
            bool creationExhausted = limits.MaxEpisodes is { } maximum && started >= maximum;
            bool inBackoff = backoffUntil is { } until && now < until;

            while (stopReason is null && running.Count < settings.Parallel)
            {
                HarnessEpisode? next = paused.FirstOrDefault(IsDue);
                if (next is not null)
                {
                    paused.Remove(next);
                }
                else if (!creationExhausted && !inBackoff)
                {
                    started++;
                    next = await createEpisode(started, cancellationToken).ConfigureAwait(false);
                    creationExhausted = limits.MaxEpisodes is { } cap && started >= cap;
                }
                else
                {
                    break;
                }

                running.Add(RunOneAsync(next, cancellationToken), next);
            }

            if (running.Count == 0)
            {
                if (stopReason is not null || creationExhausted && paused.Count == 0)
                {
                    break;
                }

                // Only paused episodes (waiting for a game-day reset) or an error backoff remain.
                try
                {
                    await Task.Delay(limits.PollInterval, _time, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                }
                continue;
            }

            Task delay = Task.Delay(limits.PollInterval, _time, CancellationToken.None);
            Task first = await Task.WhenAny(running.Keys.Cast<Task>().Append(delay)).ConfigureAwait(false);
            if (first == delay)
            {
                continue;
            }

            var finishedTask = (Task<HarnessEpisode>)first;
            running.Remove(finishedTask);
            HarnessEpisode finished = await finishedTask.ConfigureAwait(false);
            if (finished.State == HarnessEpisodeState.Error)
            {
                consecutiveErrors++;
                backoffUntil = _time.GetUtcNow() + limits.ErrorBackoff;
                log.WriteLine(
                    $"{finished.EpisodeId} ended in an error ({consecutiveErrors} in a row); new episodes wait until " +
                    $"{backoffUntil.Value.ToLocalTime():T}.");
            }
            else
            {
                consecutiveErrors = 0;
                backoffUntil = null;
            }
            if (finished.State == HarnessEpisodeState.Paused)
            {
                paused.Add(finished);
            }
            if (afterEpisode is not null)
            {
                await afterEpisode(finished).ConfigureAwait(false);
            }
        }

        log.WriteLine(
            $"Rolling run finished after starting {started} new episode(s)" +
            (stopReason is null ? "." : $": {stopReason}.") +
            (paused.Count > 0 ? $" {paused.Count} paused episode(s) can be continued with 'resume'." : string.Empty));
        return started;
    }

    /// <summary>
    /// When MTC was killed or crashed it could not close its episode; record the harness-observed outcome so the
    /// trajectory database never leaves a finished episode marked Running.
    /// </summary>
    private async Task ReconcileTrajectoryAsync(
        string episodeId,
        EpisodeStatus status,
        EpisodeProcessOutcome outcome,
        CancellationToken cancellationToken)
    {
        TrajectoryEpisode? episode = await trajectoryStore.GetEpisodeAsync(episodeId, CancellationToken.None)
            .ConfigureAwait(false);
        if (episode is null || episode.Status != EpisodeStatus.Running)
        {
            return;
        }

        await trajectoryStore.UpsertEpisodeAsync(
            episode with
            {
                Status = status,
                EndedAt = _time.GetUtcNow(),
                EndReason = outcome.Error ?? $"MTC exited with code {outcome.ExitCode?.ToString() ?? "unknown"}."
            },
            CancellationToken.None).ConfigureAwait(false);
    }

    private static EpisodeStatus? ReadResultStatus(string resultPath)
    {
        if (!File.Exists(resultPath))
        {
            return null;
        }

        try
        {
            EpisodeResult? result = JsonSerializer.Deserialize<EpisodeResult>(
                File.ReadAllText(resultPath),
                EpisodeResultFileWriter.JsonOptions);
            return result?.Episode.Status;
        }
        catch (JsonException)
        {
            return null;
        }
    }

    public static HarnessEpisodeState ToHarnessState(EpisodeStatus status) => status switch
    {
        EpisodeStatus.Completed => HarnessEpisodeState.Completed,
        EpisodeStatus.ShipLost => HarnessEpisodeState.ShipLost,
        EpisodeStatus.PausedOutOfTurns => HarnessEpisodeState.Paused,
        EpisodeStatus.TimedOut => HarnessEpisodeState.TimedOut,
        EpisodeStatus.Stopped => HarnessEpisodeState.Stopped,
        EpisodeStatus.Running => HarnessEpisodeState.Error,
        _ => HarnessEpisodeState.Error
    };
}
