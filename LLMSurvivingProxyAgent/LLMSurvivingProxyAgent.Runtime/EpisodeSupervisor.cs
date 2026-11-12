using System.Text.Json;
using System.Text.Json.Serialization;
using LLMSurvivingProxyAgent.Core;
using Microsoft.Extensions.Logging;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed record EpisodeEndDecision(EpisodeStatus Status, string Reason);

/// <summary>
/// Evaluates milestones after each recorded decision and decides when a harness-managed episode ends:
/// goal completed, ship lost, too few turns left to act (pause for the next game day), or the
/// per-segment wall-clock cap.
/// </summary>
public sealed class EpisodeSupervisor(
    EpisodeOptions options,
    ITrajectoryRecorder recorder,
    IShipCapacityProvider? capacityProvider,
    ILogger<EpisodeSupervisor> logger,
    TimeProvider? timeProvider = null)
{
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    // Keyed by episode so a worker restarted in the same MTC process (after a manual takeover) keeps the original
    // segment clock instead of granting a fresh cap.
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTimeOffset> SegmentStarts =
        new(StringComparer.OrdinalIgnoreCase);
    private DateTimeOffset? _segmentStartedAt;

    public EpisodeOptions Options => options;

    public void MarkSegmentStarted() =>
        _segmentStartedAt = SegmentStarts.GetOrAdd(options.EpisodeId, _ => _time.GetUtcNow());

    public async Task<EpisodeEndDecision?> EvaluateAsync(
        string agentId,
        GameSituationReport situation,
        int? starDockSector,
        AutonomousRefurbQuote? refurbQuote,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(situation);
        DateTimeOffset now = _time.GetUtcNow();
        _segmentStartedAt ??= now;

        if (situation.Snapshot is { } snapshot && GameSnapshotValidation.IsValid(snapshot))
        {
            if (GameplayRewardCalculator.IsEscapePod(snapshot))
            {
                return new EpisodeEndDecision(EpisodeStatus.ShipLost, "Synchronized status shows an Escape Pod.");
            }

            ShipCapacity? capacity = null;
            try
            {
                capacity = capacityProvider?.GetCapacity(snapshot.Ship?.ShipType);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                logger.LogWarning(exception, "The ship catalog could not be read for agent {AgentId}.", agentId);
            }

            IReadOnlyList<EpisodeMilestoneObservation> milestones = EpisodeGoalEvaluator.Evaluate(
                new EpisodeGoalContext(
                    snapshot,
                    starDockSector,
                    capacity,
                    refurbQuote,
                    recorder.GetLastClosedStep(agentId)),
                recorder.GetReachedMilestones(agentId),
                now);
            await recorder.RecordMilestonesAsync(agentId, milestones, cancellationToken).ConfigureAwait(false);

            if (options.Goal == EpisodeGoal.FullyUpgradedShip &&
                recorder.GetReachedMilestones(agentId).Contains(EpisodeMilestone.FullyUpgraded))
            {
                return new EpisodeEndDecision(EpisodeStatus.Completed, "The ship is fully upgraded.");
            }
            if (snapshot.Turns is { } turns && turns < options.MinimumTurns)
            {
                return new EpisodeEndDecision(
                    EpisodeStatus.PausedOutOfTurns,
                    $"Only {turns} turns remain; resume on the next game day.");
            }
        }

        return now - _segmentStartedAt.Value >= options.WallClockCap
            ? new EpisodeEndDecision(
                EpisodeStatus.TimedOut,
                $"The {options.WallClockCap.TotalMinutes:N0}-minute wall-clock cap was reached.")
            : null;
    }
}

/// <summary>Writes the final episode summary and its milestones to a JSON file for the harness.</summary>
public sealed class EpisodeResultFileWriter(string resultPath, ITrajectoryStore store) : IEpisodeCompletionHandler
{
    public static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task OnEpisodeEndedAsync(TrajectoryEpisode episode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(episode);
        IReadOnlyList<TrajectoryMilestone> milestones =
            await store.GetMilestonesAsync(episode.EpisodeId, cancellationToken).ConfigureAwait(false);
        var result = new EpisodeResult(episode, milestones);
        string fullPath = Path.GetFullPath(resultPath);
        Directory.CreateDirectory(Path.GetDirectoryName(fullPath)!);
        string temporaryPath = $"{fullPath}.{Guid.NewGuid():N}.tmp";
        await File.WriteAllTextAsync(
            temporaryPath,
            JsonSerializer.Serialize(result, JsonOptions),
            cancellationToken).ConfigureAwait(false);
        File.Move(temporaryPath, fullPath, overwrite: true);
    }
}

public sealed record EpisodeResult(TrajectoryEpisode Episode, IReadOnlyList<TrajectoryMilestone> Milestones);

public static class EpisodeExitCodes
{
    public const int Completed = 10;
    public const int ShipLost = 11;
    public const int PausedOutOfTurns = 12;
    public const int TimedOut = 13;
    public const int Stopped = 14;
    public const int Error = 15;

    public static int FromStatus(EpisodeStatus status) => status switch
    {
        EpisodeStatus.Completed => Completed,
        EpisodeStatus.ShipLost => ShipLost,
        EpisodeStatus.PausedOutOfTurns => PausedOutOfTurns,
        EpisodeStatus.TimedOut => TimedOut,
        EpisodeStatus.Stopped => Stopped,
        _ => Error
    };

    public static EpisodeStatus? ToStatus(int exitCode) => exitCode switch
    {
        Completed => EpisodeStatus.Completed,
        ShipLost => EpisodeStatus.ShipLost,
        PausedOutOfTurns => EpisodeStatus.PausedOutOfTurns,
        TimedOut => EpisodeStatus.TimedOut,
        Stopped => EpisodeStatus.Stopped,
        Error => EpisodeStatus.Error,
        _ => null
    };
}
