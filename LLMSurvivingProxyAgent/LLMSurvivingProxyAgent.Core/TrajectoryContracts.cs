namespace LLMSurvivingProxyAgent.Core;

public enum TrajectoryDecisionSource
{
    Policy,
    Pilot,
    PilotFallbackToPolicy,
    PilotOverriddenByPolicy,
    CoachRuleVeto
}

/// <summary>
/// One decision transition: the state at decision time, the chosen action, and the reward measured
/// against the next valid synchronized snapshot.
/// </summary>
public sealed record TrajectoryStep(
    string StepId,
    string EpisodeId,
    string AgentId,
    int StepIndex,
    DateTimeOffset DecidedAt,
    TrajectoryDecisionSource Source,
    string Action,
    int TargetSector,
    string Rationale,
    string? InteractionId,
    string? Objective,
    GameSnapshot? Before)
{
    public int Segment { get; init; } = 1;
    public GameSnapshot? After { get; init; }
    public DateTimeOffset? CompletedAt { get; init; }
    public GameplayReward? Reward { get; init; }

    /// <summary>Knowledge or playbook item the decision relied on (see <see cref="PlaybookCitation"/>).</summary>
    public string? CitedKnowledgeNodeId { get; init; }

    /// <summary>Playbook tip the Pilot knowingly deviated from, with its stated reason.</summary>
    public string? OverriddenPlaybookEntryId { get; init; }

    public string? OverrideReason { get; init; }

    /// <summary>Coarse situation tags at decision time (see <see cref="PlaybookSituation"/>).</summary>
    public IReadOnlyList<string> SituationTags { get; init; } = [];
}

public sealed record TrajectoryEpisode(
    string EpisodeId,
    string AgentId,
    string Model,
    DateTimeOffset StartedAt)
{
    public EpisodeStatus Status { get; init; } = EpisodeStatus.Running;
    public EpisodeGoal Goal { get; init; } = EpisodeGoal.None;
    public int Segment { get; init; } = 1;
    public DateTimeOffset? EndedAt { get; init; }
    public string? EndReason { get; init; }
    public int StepCount { get; init; }
    public int VerifiedStepCount { get; init; }
    public int PilotDecisionCount { get; init; }
    public int PolicyDecisionCount { get; init; }
    public double TotalReward { get; init; }
    public long? StartNetWorth { get; init; }
    public long? EndNetWorth { get; init; }
    public int? StartTurns { get; init; }
    public int? EndTurns { get; init; }
    public int SectorsVisited { get; init; }
    public IReadOnlyList<int> VisitedSectors { get; init; } = [];
    public bool ShipLost { get; init; }
    public bool ScannerAcquired { get; init; }
    public int MilestoneCount { get; init; }

    public int? TurnsUsed => StartTurns is { } start && EndTurns is { } end ? Math.Max(0, start - end) : null;

    public long? NetWorthDelta => StartNetWorth is { } start && EndNetWorth is { } end ? end - start : null;

    public double? NetWorthPerTurn => NetWorthDelta is { } delta && TurnsUsed is > 0 and var turns
        ? Math.Round((double)delta / turns, 2)
        : null;

    public bool IsTerminal => Status is not (EpisodeStatus.Running or EpisodeStatus.PausedOutOfTurns);
}

public sealed record TrajectoryMilestone(
    string EpisodeId,
    EpisodeMilestone Milestone,
    DateTimeOffset ReachedAt,
    int StepIndex,
    int Segment,
    int? TurnsUsed,
    double Bonus,
    string Evidence);

/// <summary>Settings for a harness-managed episode, supplied to MTC by command-line options.</summary>
public sealed record EpisodeOptions(
    string EpisodeId,
    EpisodeGoal Goal,
    TimeSpan WallClockCap)
{
    public const int DefaultMinimumTurns = 5;

    public int MinimumTurns { get; init; } = DefaultMinimumTurns;
    public string? ResultPath { get; init; }
    public bool ExitOnEnd { get; init; }
    public string? PilotModel { get; init; }

    /// <summary>
    /// Playbook memory lineage. Learners with the same memory id share one cross-life memory, so a stream of
    /// short-lived harness agents grows into one veteran. Defaults to the agent id.
    /// </summary>
    public string? MemoryId { get; init; }
}

public interface ITrajectoryStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task UpsertEpisodeAsync(TrajectoryEpisode episode, CancellationToken cancellationToken = default);
    Task UpsertStepAsync(TrajectoryStep step, CancellationToken cancellationToken = default);
    Task UpsertMilestoneAsync(TrajectoryMilestone milestone, CancellationToken cancellationToken = default);
    Task<TrajectoryEpisode?> GetEpisodeAsync(string episodeId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrajectoryEpisode>> GetRecentEpisodesAsync(
        string? agentId = null,
        int limit = 50,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrajectoryStep>> GetStepsAsync(
        string episodeId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<TrajectoryMilestone>> GetMilestonesAsync(
        string episodeId,
        CancellationToken cancellationToken = default);
}

public interface ITrajectoryRecorder
{
    /// <summary>
    /// Starts the named episode, or resumes it as a new segment when it already exists and is not terminal.
    /// </summary>
    Task<TrajectoryEpisode?> BeginEpisodeAsync(
        string agentId,
        string model,
        string episodeId,
        EpisodeGoal goal,
        CancellationToken cancellationToken = default);

    Task RecordDecisionAsync(
        string agentId,
        string model,
        GameSituationReport situation,
        AutonomousTestActionDecision decision,
        TrajectoryDecisionSource source,
        string? objective,
        CancellationToken cancellationToken = default,
        IReadOnlyList<string>? situationTags = null);

    void ObserveSnapshot(string agentId, GameSnapshot? snapshot);

    TrajectoryStep? GetLastClosedStep(string agentId);

    TrajectoryEpisode? GetActiveEpisode(string agentId);

    IReadOnlySet<EpisodeMilestone> GetReachedMilestones(string agentId);

    Task RecordMilestonesAsync(
        string agentId,
        IReadOnlyList<EpisodeMilestoneObservation> milestones,
        CancellationToken cancellationToken = default);

    Task<TrajectoryEpisode?> EndEpisodeAsync(
        string agentId,
        EpisodeStatus status,
        string reason,
        CancellationToken cancellationToken = default);
}

public interface IEpisodeCompletionHandler
{
    Task OnEpisodeEndedAsync(TrajectoryEpisode episode, CancellationToken cancellationToken = default);
}

/// <summary>
/// Notified by the trajectory recorder when a step's reward is measured and when an episode ends. Observers run
/// after persistence; their failures are logged and never interrupt gameplay.
/// </summary>
public interface ITrajectoryObserver
{
    Task OnStepClosedAsync(TrajectoryStep step, CancellationToken cancellationToken = default);

    Task OnEpisodeEndedAsync(TrajectoryEpisode episode, CancellationToken cancellationToken = default);
}
