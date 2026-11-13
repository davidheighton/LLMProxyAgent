using System.Globalization;
using System.Text;
using LLMSurvivingProxyAgent.Core;
using Microsoft.Extensions.Logging;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed record PlaybookLearnerOptions
{
    /// <summary>Identifies the current game universe for game-scoped lessons (the MTC session/profile name).</summary>
    public string? GameKey { get; init; }

    /// <summary>Memory lineage written by this learner; defaults to each episode's agent id.</summary>
    public string? MemoryId { get; init; }

    /// <summary>Model used to reflect on finished lives. Null disables distillation; credit assignment still runs.</summary>
    public ModelConfiguration? DistillationModel { get; init; }

    /// <summary>Run end-of-life reflection off the gameplay thread. Tests set this to false.</summary>
    public bool RunInBackground { get; init; } = true;

    public int MinimumVerifiedStepsForDistillation { get; init; } = 3;
    public int MaximumLessonsPerLife { get; init; } = 3;
    public int EvidenceStepsPerSide { get; init; } = 5;
}

/// <summary>
/// Closes the learning loop. Each verified step reward is credited to the playbook entry the decision cited
/// (or recorded against a tip the Pilot overrode). Each finished episode becomes a remembered life; the agent
/// then reflects on its best and worst decisions and stores new lessons at low confidence. Lessons must earn
/// confidence in later play, and distilled lessons that keep failing retire automatically.
/// </summary>
public sealed class PlaybookLearner(
    IPlaybookStore playbookStore,
    ITrajectoryStore trajectoryStore,
    ILogger<PlaybookLearner> logger,
    PlaybookLearnerOptions? options = null,
    ILessonDistiller? distiller = null,
    TimeProvider? timeProvider = null) : ITrajectoryObserver
{
    private const int MaximumTitleLength = 120;
    private const int MaximumGuidanceLength = 600;
    private const int MaximumRationaleLength = 600;
    private const int MaximumExistingEntriesOffered = 40;

    private readonly PlaybookLearnerOptions _options = options ?? new PlaybookLearnerOptions();
    private readonly TimeProvider _time = timeProvider ?? TimeProvider.System;
    private readonly object _pendingSync = new();
    private Task _pending = Task.CompletedTask;

    public async Task OnStepClosedAsync(TrajectoryStep step, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        if (step.Reward is not { Verified: true } reward)
        {
            return;
        }

        DateTimeOffset now = _time.GetUtcNow();
        if (PlaybookCitation.TryGetEntryId(step.CitedKnowledgeNodeId, out string citedEntryId))
        {
            PlaybookEntry? updated = await playbookStore
                .RecordOutcomeAsync(citedEntryId, reward, overridden: false, now, cancellationToken)
                .ConfigureAwait(false);
            if (updated?.Status == PlaybookStatus.Retired)
            {
                logger.LogInformation(
                    "Lesson {EntryId} '{Title}' retired after {Applications} uses at confidence {Confidence}.",
                    updated.EntryId,
                    updated.Title,
                    updated.Applications,
                    updated.Confidence);
            }
        }

        string? overriddenEntryId = step.OverriddenPlaybookEntryId;
        if (PlaybookCitation.TryGetEntryId(overriddenEntryId, out string parsedOverride))
        {
            overriddenEntryId = parsedOverride;
        }
        if (!string.IsNullOrWhiteSpace(overriddenEntryId) &&
            !string.Equals(overriddenEntryId, citedEntryId, StringComparison.Ordinal))
        {
            await playbookStore
                .RecordOutcomeAsync(overriddenEntryId, reward, overridden: true, now, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public Task OnEpisodeEndedAsync(TrajectoryEpisode episode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(episode);
        if (episode.StepCount == 0)
        {
            return Task.CompletedTask;
        }
        if (!_options.RunInBackground)
        {
            return LearnFromLifeAsync(episode, cancellationToken);
        }

        lock (_pendingSync)
        {
            // Reflection can take a while (model call); chain it so lives are processed in order, off the game loop.
            _pending = _pending.ContinueWith(
                    _ => LearnFromLifeAsync(episode, CancellationToken.None),
                    CancellationToken.None,
                    TaskContinuationOptions.None,
                    TaskScheduler.Default)
                .Unwrap();
        }
        return Task.CompletedTask;
    }

    /// <summary>Completes when queued end-of-life reflection has finished.</summary>
    public Task WhenIdleAsync()
    {
        lock (_pendingSync)
        {
            return _pending;
        }
    }

    public async Task<AgentLifeRecord?> LearnFromLifeAsync(
        TrajectoryEpisode episode,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(episode);
        try
        {
            IReadOnlyList<TrajectoryStep> steps = await trajectoryStore
                .GetStepsAsync(episode.EpisodeId, cancellationToken)
                .ConfigureAwait(false);
            IReadOnlyList<TrajectoryMilestone> milestones = await trajectoryStore
                .GetMilestonesAsync(episode.EpisodeId, cancellationToken)
                .ConfigureAwait(false);
            string memoryId = PlaybookMemory.Resolve(_options.MemoryId, episode.AgentId);
            IReadOnlyList<AgentLifeRecord> previous = await playbookStore
                .GetLivesAsync(memoryId, 1000, cancellationToken)
                .ConfigureAwait(false);
            AgentLifeRecord? existing = previous.FirstOrDefault(life =>
                string.Equals(life.EpisodeId, episode.EpisodeId, StringComparison.Ordinal));
            IReadOnlyList<AgentLifeRecord> earlierLives = previous
                .Where(life => !string.Equals(life.EpisodeId, episode.EpisodeId, StringComparison.Ordinal))
                .ToArray();

            AgentLifeRecord life = BuildLife(
                episode,
                existing?.LifeNumber ?? earlierLives.Count + 1,
                milestones,
                steps,
                _options.GameKey,
                _time.GetUtcNow()) with
            {
                AgentId = memoryId,
                PlayerAgentId = episode.AgentId
            };
            await playbookStore.UpsertLifeAsync(life, cancellationToken).ConfigureAwait(false);

            if (distiller is null ||
                _options.DistillationModel is null ||
                steps.Count(step => step.Reward?.Verified == true) < _options.MinimumVerifiedStepsForDistillation)
            {
                return life;
            }

            life = await DistillAsync(life, steps, earlierLives, cancellationToken).ConfigureAwait(false);
            await playbookStore.UpsertLifeAsync(life, cancellationToken).ConfigureAwait(false);
            return life;
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            logger.LogWarning(exception, "Learning from episode {EpisodeId} failed.", episode.EpisodeId);
            return null;
        }
    }

    public static AgentLifeRecord BuildLife(
        TrajectoryEpisode episode,
        int lifeNumber,
        IReadOnlyList<TrajectoryMilestone> milestones,
        IReadOnlyList<TrajectoryStep> steps,
        string? gameKey,
        DateTimeOffset now)
    {
        var lifeMilestones = milestones
            .OrderBy(milestone => milestone.StepIndex)
            .Select(milestone => new LifeMilestone(milestone.Milestone, milestone.TurnsUsed, milestone.StepIndex))
            .ToArray();
        return new AgentLifeRecord(
            episode.EpisodeId,
            episode.AgentId,
            string.IsNullOrWhiteSpace(gameKey) ? null : gameKey,
            lifeNumber,
            episode.Model,
            episode.Status,
            episode.EndReason,
            episode.StartedAt,
            episode.EndedAt ?? now,
            episode.TotalReward,
            episode.NetWorthDelta,
            episode.TurnsUsed,
            episode.StepCount,
            lifeMilestones,
            DescribeLife(episode, lifeNumber, lifeMilestones, steps));
    }

    private static string DescribeLife(
        TrajectoryEpisode episode,
        int lifeNumber,
        IReadOnlyList<LifeMilestone> milestones,
        IReadOnlyList<TrajectoryStep> steps)
    {
        var text = new StringBuilder();
        text.Append(CultureInfo.InvariantCulture, $"Life {lifeNumber} ended {episode.Status}");
        if (!string.IsNullOrWhiteSpace(episode.EndReason))
        {
            text.Append(CultureInfo.InvariantCulture, $" ({episode.EndReason})");
        }
        text.Append(CultureInfo.InvariantCulture, $" after {episode.StepCount} decisions");
        if (episode.TurnsUsed is { } turns)
        {
            text.Append(CultureInfo.InvariantCulture, $" and {turns} turns");
        }
        if (episode.NetWorthDelta is { } delta)
        {
            text.Append(CultureInfo.InvariantCulture, $"; net worth {delta:+#,0;-#,0;0}");
        }
        text.Append(CultureInfo.InvariantCulture, $"; reward {episode.TotalReward:0.##}.");
        if (milestones.Count > 0)
        {
            text.Append(" Milestones: ");
            text.Append(string.Join(", ", milestones.Select(milestone =>
                milestone.TurnsUsed is { } used
                    ? string.Create(CultureInfo.InvariantCulture, $"{milestone.Milestone} at {used} turns")
                    : milestone.Milestone.ToString())));
            text.Append('.');
        }
        else
        {
            text.Append(" No milestones reached.");
        }

        var verified = steps.Where(step => step.Reward?.Verified == true).ToArray();
        if (verified.Length > 0)
        {
            var actions = verified
                .GroupBy(step => step.Action)
                .OrderByDescending(group => group.Sum(step => step.Reward!.Total))
                .Select(group => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{group.Key} x{group.Count()} ({group.Sum(step => step.Reward!.Total):+0.##;-0.##;0})"));
            text.Append(" Reward by action: ").Append(string.Join(", ", actions)).Append('.');
        }
        if (episode.ShipLost)
        {
            text.Append(" The ship was lost.");
        }
        return text.ToString();
    }

    private async Task<AgentLifeRecord> DistillAsync(
        AgentLifeRecord life,
        IReadOnlyList<TrajectoryStep> steps,
        IReadOnlyList<AgentLifeRecord> earlierLives,
        CancellationToken cancellationToken)
    {
        var verified = steps.Where(step => step.Reward?.Verified == true).ToArray();
        LessonEvidenceStep[] best = verified
            .Where(step => step.Reward!.Total > PlaybookConfidence.SuccessRewardThreshold)
            .OrderByDescending(step => step.Reward!.Total)
            .Take(_options.EvidenceStepsPerSide)
            .Select(ToEvidence)
            .ToArray();
        LessonEvidenceStep[] worst = verified
            .Where(step => step.Reward!.Total < 0)
            .OrderBy(step => step.Reward!.Total)
            .Take(_options.EvidenceStepsPerSide)
            .Select(ToEvidence)
            .ToArray();
        if (best.Length + worst.Length == 0)
        {
            return life;
        }

        IReadOnlyList<PlaybookEntry> existingEntries = await playbookStore
            .GetEntriesAsync(life.AgentId, includeRetired: false, cancellationToken)
            .ConfigureAwait(false);
        var allLives = earlierLives.Prepend(life).ToArray();
        var request = new LessonDistillationRequest(
            life.AgentId,
            life,
            best,
            worst,
            ProgressionBenchmark.Compute(allLives),
            earlierLives.Take(5).ToArray(),
            existingEntries.Take(MaximumExistingEntriesOffered).ToArray());

        LessonDistillationResult result = await distiller!
            .DistillAsync(_options.DistillationModel!, request, cancellationToken)
            .ConfigureAwait(false);

        var offeredStepIds = best.Concat(worst).Select(step => step.StepId).ToHashSet(StringComparer.Ordinal);
        var learned = new List<string>();
        DateTimeOffset now = _time.GetUtcNow();
        foreach (DistilledLessonProposal proposal in result.Lessons.Take(_options.MaximumLessonsPerLife))
        {
            string? entryId = await ApplyProposalAsync(
                    life,
                    proposal,
                    offeredStepIds,
                    existingEntries,
                    now,
                    cancellationToken)
                .ConfigureAwait(false);
            if (entryId is not null)
            {
                learned.Add(entryId);
            }
        }

        string summary = string.IsNullOrWhiteSpace(result.LifeSummary)
            ? life.Summary
            : $"{life.Summary} Reflection: {Truncate(result.LifeSummary.Trim(), 1_000)}";
        logger.LogInformation(
            "Life {LifeNumber} ({EpisodeId}) for agent {AgentId} produced {LessonCount} lesson updates.",
            life.LifeNumber,
            life.EpisodeId,
            life.AgentId,
            learned.Count);
        return life with { Summary = summary, LessonIdsLearned = learned };
    }

    private async Task<string?> ApplyProposalAsync(
        AgentLifeRecord life,
        DistilledLessonProposal proposal,
        IReadOnlySet<string> offeredStepIds,
        IReadOnlyList<PlaybookEntry> existingEntries,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        string title = Truncate(proposal.Title?.Trim() ?? string.Empty, MaximumTitleLength);
        string guidance = Truncate(proposal.Guidance?.Trim() ?? string.Empty, MaximumGuidanceLength);
        if (title.Length == 0 || guidance.Length == 0)
        {
            return null;
        }
        string[] evidence = (proposal.EvidenceStepIds ?? [])
            .Where(offeredStepIds.Contains)
            .Distinct(StringComparer.Ordinal)
            .ToArray();
        if (evidence.Length == 0)
        {
            logger.LogInformation("Discarded lesson '{Title}': it cited no supplied evidence step.", title);
            return null;
        }

        PlaybookEntry? reinforced = existingEntries.FirstOrDefault(entry =>
            entry.Source == PlaybookSource.Distilled &&
            (string.Equals(entry.EntryId, proposal.ReinforcesEntryId, StringComparison.Ordinal) ||
             string.Equals(Normalize(entry.Guidance), Normalize(guidance), StringComparison.Ordinal)));
        if (reinforced is not null)
        {
            if (!reinforced.EvidenceEpisodeIds.Contains(life.EpisodeId, StringComparer.Ordinal))
            {
                await playbookStore.UpsertEntryAsync(
                        reinforced with
                        {
                            EvidenceEpisodeIds = [.. reinforced.EvidenceEpisodeIds.TakeLast(19), life.EpisodeId],
                            UpdatedAt = now
                        },
                        cancellationToken)
                    .ConfigureAwait(false);
            }
            return reinforced.EntryId;
        }

        IReadOnlyList<string> appliesWhen = PlaybookSituation.Normalize(proposal.AppliesWhen ?? [], out _);
        PlaybookScope scope = proposal.Scope == PlaybookScope.Game && life.GameKey is not null
            ? PlaybookScope.Game
            : PlaybookScope.Universal;
        var entry = new PlaybookEntry(
            Guid.NewGuid().ToString("N"),
            life.AgentId,
            PlaybookSource.Distilled,
            PlaybookTier.Tip,
            scope,
            scope == PlaybookScope.Game ? life.GameKey : null,
            title,
            guidance,
            Truncate(proposal.Rationale?.Trim() ?? string.Empty, MaximumRationaleLength),
            appliesWhen,
            now)
        {
            UpdatedAt = now,
            LearnedInLife = life.LifeNumber,
            EvidenceEpisodeIds = [life.EpisodeId]
        };
        await playbookStore.UpsertEntryAsync(entry, cancellationToken).ConfigureAwait(false);
        return entry.EntryId;
    }

    private static LessonEvidenceStep ToEvidence(TrajectoryStep step) =>
        new(
            step.StepId,
            step.StepIndex,
            step.Source.ToString(),
            step.Action,
            Truncate(step.Rationale, 400),
            step.Objective is null ? null : Truncate(step.Objective, 300),
            step.SituationTags,
            step.Reward!.Total,
            step.Reward.CreditsDelta,
            step.Reward.TurnsUsed,
            PlaybookCitation.TryGetEntryId(step.CitedKnowledgeNodeId, out string entryId) ? entryId : null);

    private static string Normalize(string value) =>
        string.Join(' ', value.ToLowerInvariant().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
