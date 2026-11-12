using System.Collections.Concurrent;
using LLMSurvivingProxyAgent.Core;
using Microsoft.Extensions.Logging;

namespace LLMSurvivingProxyAgent.Runtime;

/// <summary>
/// Records (state, action, reward, next state) transitions for each autonomous decision. A step is
/// closed, and its reward computed, when the next decision is recorded or the episode ends, using the
/// newest valid synchronized snapshot observed by then. Persistence failures never interrupt gameplay.
/// </summary>
public sealed class TrajectoryRecorder(
    ITrajectoryStore store,
    ILogger<TrajectoryRecorder> logger,
    GameplayRewardWeights? weights = null,
    TimeProvider? timeProvider = null) : ITrajectoryRecorder
{
    private readonly GameplayRewardWeights _weights = weights ?? GameplayRewardWeights.Default;
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly ConcurrentDictionary<string, EpisodeState> _episodes = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, GameSnapshot> _latestSnapshots =
        new(StringComparer.OrdinalIgnoreCase);
    private bool _initialized;

    public void ObserveSnapshot(string agentId, GameSnapshot? snapshot)
    {
        if (string.IsNullOrWhiteSpace(agentId) || !GameSnapshotValidation.IsValid(snapshot))
        {
            return;
        }

        _latestSnapshots.AddOrUpdate(
            agentId,
            snapshot!,
            (_, existing) => snapshot!.ObservedAt > existing.ObservedAt ? snapshot! : existing);
    }

    public TrajectoryStep? GetLastClosedStep(string agentId) =>
        _episodes.TryGetValue(agentId, out EpisodeState? state) ? state.LastClosedStep : null;

    public TrajectoryEpisode? GetActiveEpisode(string agentId) =>
        _episodes.TryGetValue(agentId, out EpisodeState? state) ? state.Episode : null;

    public IReadOnlySet<EpisodeMilestone> GetReachedMilestones(string agentId) =>
        _episodes.TryGetValue(agentId, out EpisodeState? state)
            ? new HashSet<EpisodeMilestone>(state.Milestones)
            : new HashSet<EpisodeMilestone>();

    public async Task<TrajectoryEpisode?> BeginEpisodeAsync(
        string agentId,
        string model,
        string episodeId,
        EpisodeGoal goal,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(episodeId);

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            if (_episodes.TryGetValue(agentId, out EpisodeState? current) &&
                string.Equals(current.Episode.EpisodeId, episodeId, StringComparison.OrdinalIgnoreCase))
            {
                // The worker was restarted in the same process (the user paused it to take over, then resumed):
                // continue the open episode rather than ending it.
                return current.Episode;
            }
            if (_episodes.TryRemove(agentId, out EpisodeState? previous))
            {
                await FinishAsync(previous, EpisodeStatus.Stopped, "Superseded by a new episode", cancellationToken)
                    .ConfigureAwait(false);
            }

            TrajectoryEpisode? existing = await store.GetEpisodeAsync(episodeId, cancellationToken).ConfigureAwait(false);
            EpisodeState state;
            if (existing is null)
            {
                state = new EpisodeState(new TrajectoryEpisode(
                    episodeId,
                    agentId,
                    NormalizeModel(model),
                    _time.GetUtcNow())
                {
                    Goal = goal
                });
            }
            else
            {
                if (!string.Equals(existing.AgentId, agentId, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Episode '{episodeId}' belongs to agent '{existing.AgentId}', not '{agentId}'.");
                }
                if (existing.IsTerminal)
                {
                    throw new InvalidOperationException(
                        $"Episode '{episodeId}' already ended with status {existing.Status} and cannot resume.");
                }

                state = new EpisodeState(existing with
                {
                    Status = EpisodeStatus.Running,
                    Segment = existing.Segment + 1,
                    EndedAt = null,
                    EndReason = null
                })
                {
                    HasBaseline = existing.StartNetWorth is not null || existing.StartTurns is not null
                };
                state.Milestones.UnionWith(
                    (await store.GetMilestonesAsync(episodeId, cancellationToken).ConfigureAwait(false))
                    .Select(milestone => milestone.Milestone));
            }

            _episodes[agentId] = state;
            await store.UpsertEpisodeAsync(state.Episode, cancellationToken).ConfigureAwait(false);
            return state.Episode;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (InvalidOperationException)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Trajectory episode {EpisodeId} could not begin for agent {AgentId}.", episodeId, agentId);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordDecisionAsync(
        string agentId,
        string model,
        GameSituationReport situation,
        AutonomousTestActionDecision decision,
        TrajectoryDecisionSource source,
        string? objective,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(situation);
        ArgumentNullException.ThrowIfNull(decision);

        ObserveSnapshot(agentId, situation.Snapshot);
        GameSnapshot? before = GameSnapshotValidation.IsValid(situation.Snapshot) ? situation.Snapshot : null;
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await EnsureInitializedAsync(cancellationToken).ConfigureAwait(false);
            if (!_episodes.TryGetValue(agentId, out EpisodeState? state))
            {
                state = new EpisodeState(new TrajectoryEpisode(
                    Guid.NewGuid().ToString("N"),
                    agentId,
                    NormalizeModel(model),
                    _time.GetUtcNow()));
                _episodes[agentId] = state;
            }
            if (!state.HasBaseline && before is not null)
            {
                ApplyBaseline(state, before);
            }

            await CloseOpenStepAsync(state, cancellationToken).ConfigureAwait(false);

            var step = new TrajectoryStep(
                Guid.NewGuid().ToString("N"),
                state.Episode.EpisodeId,
                agentId,
                state.Episode.StepCount,
                _time.GetUtcNow(),
                source,
                decision.Action.ToString(),
                decision.TargetSector,
                decision.Rationale,
                decision.InteractionId,
                objective,
                before)
            {
                Segment = state.Episode.Segment
            };
            state.OpenStep = step;
            state.Episode = state.Episode with
            {
                StepCount = state.Episode.StepCount + 1,
                PilotDecisionCount = state.Episode.PilotDecisionCount +
                    (source == TrajectoryDecisionSource.Pilot ? 1 : 0),
                PolicyDecisionCount = state.Episode.PolicyDecisionCount +
                    (source == TrajectoryDecisionSource.Pilot ? 0 : 1)
            };
            await store.UpsertEpisodeAsync(state.Episode, cancellationToken).ConfigureAwait(false);
            await store.UpsertStepAsync(step, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Trajectory step recording failed for agent {AgentId}.", agentId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task RecordMilestonesAsync(
        string agentId,
        IReadOnlyList<EpisodeMilestoneObservation> milestones,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(milestones);
        if (milestones.Count == 0)
        {
            return;
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (!_episodes.TryGetValue(agentId, out EpisodeState? state))
            {
                return;
            }

            foreach (EpisodeMilestoneObservation observation in milestones)
            {
                if (!state.Milestones.Add(observation.Milestone))
                {
                    continue;
                }

                double bonus = _weights.GetMilestoneBonus(observation.Milestone);
                await store.UpsertMilestoneAsync(
                    new TrajectoryMilestone(
                        state.Episode.EpisodeId,
                        observation.Milestone,
                        _time.GetUtcNow(),
                        Math.Max(0, state.Episode.StepCount - 1),
                        state.Episode.Segment,
                        state.Episode.TurnsUsed,
                        bonus,
                        observation.Evidence),
                    cancellationToken).ConfigureAwait(false);
                state.Episode = state.Episode with
                {
                    MilestoneCount = state.Milestones.Count,
                    TotalReward = Math.Round(state.Episode.TotalReward + bonus, 4)
                };
                logger.LogInformation(
                    "Episode {EpisodeId} for agent {AgentId} reached milestone {Milestone}: {Evidence}",
                    state.Episode.EpisodeId,
                    agentId,
                    observation.Milestone,
                    observation.Evidence);
            }
            await store.UpsertEpisodeAsync(state.Episode, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Milestone recording failed for agent {AgentId}.", agentId);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<TrajectoryEpisode?> EndEpisodeAsync(
        string agentId,
        EpisodeStatus status,
        string reason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reason);
        if (status == EpisodeStatus.Running)
        {
            throw new ArgumentException("An ended episode cannot have Running status.", nameof(status));
        }

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            return _episodes.TryRemove(agentId, out EpisodeState? state)
                ? await FinishAsync(state, status, reason, cancellationToken).ConfigureAwait(false)
                : null;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            logger.LogWarning(exception, "Trajectory episode completion failed for agent {AgentId}.", agentId);
            return null;
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<TrajectoryEpisode> FinishAsync(
        EpisodeState state,
        EpisodeStatus status,
        string reason,
        CancellationToken cancellationToken)
    {
        await CloseOpenStepAsync(state, cancellationToken).ConfigureAwait(false);
        state.Episode = state.Episode with
        {
            Status = status,
            EndedAt = _time.GetUtcNow(),
            EndReason = reason
        };
        await store.UpsertEpisodeAsync(state.Episode, cancellationToken).ConfigureAwait(false);
        logger.LogInformation(
            "Trajectory episode {EpisodeId} for agent {AgentId} ended as {Status} ({Reason}): {Steps} steps, reward {Reward}, net worth change {NetWorthDelta}.",
            state.Episode.EpisodeId,
            state.Episode.AgentId,
            status,
            reason,
            state.Episode.StepCount,
            state.Episode.TotalReward,
            state.Episode.NetWorthDelta);
        return state.Episode;
    }

    private void ApplyBaseline(EpisodeState state, GameSnapshot snapshot)
    {
        long? netWorth = GameplayRewardCalculator.EstimateNetWorth(snapshot, _weights);
        state.Episode = state.Episode with
        {
            StartNetWorth = netWorth,
            StartTurns = snapshot.Turns,
            EndNetWorth = netWorth,
            EndTurns = snapshot.Turns,
            VisitedSectors = snapshot.CurrentSector is { } sector ? [sector] : [],
            SectorsVisited = snapshot.CurrentSector is null ? 0 : 1
        };
        state.HasBaseline = true;
    }

    private async Task CloseOpenStepAsync(EpisodeState state, CancellationToken cancellationToken)
    {
        if (state.OpenStep is not { } step)
        {
            return;
        }

        state.OpenStep = null;
        _latestSnapshots.TryGetValue(step.AgentId, out GameSnapshot? after);
        if (after is not null && !state.HasBaseline)
        {
            ApplyBaseline(state, after);
        }

        bool newSector = after?.CurrentSector is { } sector && !state.Episode.VisitedSectors.Contains(sector);
        GameplayReward reward = GameplayRewardCalculator.Compute(step.Before, after, newSector, _weights);
        var completed = step with
        {
            After = after,
            CompletedAt = _time.GetUtcNow(),
            Reward = reward
        };
        await store.UpsertStepAsync(completed, cancellationToken).ConfigureAwait(false);
        state.LastClosedStep = completed;

        TrajectoryEpisode episode = state.Episode;
        if (after is not null)
        {
            IReadOnlyList<int> visited = newSector
                ? [.. episode.VisitedSectors, after.CurrentSector!.Value]
                : episode.VisitedSectors;
            episode = episode with
            {
                EndNetWorth = GameplayRewardCalculator.EstimateNetWorth(after, _weights) ?? episode.EndNetWorth,
                EndTurns = after.Turns ?? episode.EndTurns,
                VisitedSectors = visited,
                SectorsVisited = visited.Count
            };
        }
        if (reward.Verified)
        {
            episode = episode with
            {
                VerifiedStepCount = episode.VerifiedStepCount + 1,
                TotalReward = Math.Round(episode.TotalReward + reward.Total, 4),
                ShipLost = episode.ShipLost || reward.ShipLost,
                ScannerAcquired = episode.ScannerAcquired || reward.ScannerAcquired
            };
        }
        state.Episode = episode;
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (_initialized)
        {
            return;
        }

        await store.InitializeAsync(cancellationToken).ConfigureAwait(false);
        _initialized = true;
    }

    private static string NormalizeModel(string model) => string.IsNullOrWhiteSpace(model) ? "unknown" : model;

    private sealed class EpisodeState(TrajectoryEpisode episode)
    {
        public TrajectoryEpisode Episode { get; set; } = episode;
        public TrajectoryStep? OpenStep { get; set; }
        public TrajectoryStep? LastClosedStep { get; set; }
        public HashSet<EpisodeMilestone> Milestones { get; } = [];
        public bool HasBaseline { get; set; }
    }
}
