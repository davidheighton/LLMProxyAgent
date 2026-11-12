using System.Text.Json.Serialization;

namespace LLMSurvivingProxyAgent.Core;

public enum ProgressAssessment
{
    Unknown,
    Improving,
    Flat,
    Declining,
    InsufficientData
}

public enum ObjectiveDecision
{
    Unspecified,
    Continue,
    SelectProposal,
    ProposeAndSelect,
    Complete,
    Hold
}

public sealed record ReviewObjective(string ProposalId, string Goal, string Rationale, decimal? Confidence)
{
    public string? ObjectiveId { get; init; }
}

public sealed record ReviewKnowledgeItem(
    string NodeId,
    string DocumentId,
    string Title,
    string Summary,
    string Category,
    decimal Confidence,
    string Basis,
    IReadOnlyList<string> Evidence)
{
    public string Layer { get; init; } = "Unknown";
}

public sealed record StallIndicator(
    string Metric,
    bool IsStalled,
    int UnchangedCycles,
    DateTimeOffset? Since,
    TimeSpan? Duration,
    string Evidence);

public sealed record FailureDetectionSummary(
    int RequiredUnchangedCycles,
    bool SnapshotPredatesHistory,
    DateTimeOffset? LatestPriorSnapshotAt,
    bool IsStalled,
    IReadOnlyList<StallIndicator> Indicators,
    IReadOnlyList<string> UnavailableSignals);

public sealed record StrategicReviewHistoryItem(
    string ReviewId,
    DateTimeOffset ReviewedAt,
    ReviewObjective? ActiveObjective,
    ProgressAssessment Progress,
    string Summary,
    string Recommendation,
    ObjectiveDecision ObjectiveDecision,
    string DecisionOutcome,
    bool StallDetected);

public sealed record ReactiveEventCount(string Name, int Count);

public sealed record ReactiveReviewEventSummary(
    string EventId,
    ReactiveEventType Type,
    ReactiveEventSource Source,
    DateTimeOffset OccurredAt,
    int? SectorId,
    string? ActorName,
    decimal? TrustAtReceipt,
    string? Message,
    ReactiveMode Mode,
    string RuleId,
    string DecisionSummary,
    bool RequestsStrategicReview);

public sealed record ReactiveActivityAnalysis(
    DateTimeOffset? ActivityWindowStart,
    int EventsAnalyzed,
    IReadOnlyList<ReactiveEventCount> CountsByType,
    IReadOnlyList<ReactiveEventCount> CountsByMode,
    IReadOnlyList<ReactiveReviewEventSummary> RecentEvents,
    bool MayBeTruncated,
    string Limitations)
{
    public static ReactiveActivityAnalysis Empty { get; } = new(
        null,
        0,
        [],
        [],
        [],
        false,
        "No reactive activity is available.");
}

public sealed record ReviewHistoryAnalysis(
    int ReviewsAnalyzed,
    IReadOnlyList<StrategicReviewHistoryItem> RecentReviews,
    string? MostRepeatedSummary,
    int RepeatedSummaryCount,
    string? MostRepeatedRecommendation,
    int RepeatedRecommendationCount,
    int ConsecutiveStalledReviews,
    DateTimeOffset? MacroActivityWindowStart,
    IReadOnlyList<MacroExecutionRecord> MacroActivitySinceLastReview,
    bool? NoRecordedMacroActivitySinceLastReview,
    string MacroTelemetryLimitations);

public sealed record StrategicReviewContext(
    GameSnapshot Snapshot,
    IReadOnlyList<GameSnapshot> ProgressHistory,
    ReviewObjective? ActiveObjective,
    IReadOnlyList<ReviewObjective> PendingObjectives,
    IReadOnlyList<AgentJournalEntry> RecentJournal,
    FailureDetectionSummary FailureDetection,
    ReviewHistoryAnalysis ReviewHistory,
    IReadOnlyList<ReviewKnowledgeItem> Knowledge)
{
    [JsonIgnore]
    public string AgentId { get; init; } = string.Empty;
    public ReactiveActivityAnalysis ReactiveActivity { get; init; } = ReactiveActivityAnalysis.Empty;
    public string ReviewReason { get; init; } = "Operator-requested review.";
    public IntegratedGameClientSnapshot? IntegratedGameClient { get; init; }

    /// <summary>Cross-life memory: how past lives progressed. Playbook entries are in <see cref="Knowledge"/>.</summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public StrategicVeteranMemory? VeteranMemory { get; init; }
}

public sealed record StrategicVeteranMemory(
    IReadOnlyList<string> SituationTags,
    int LivesLived,
    IReadOnlyList<ProgressionBenchmark> ProgressionBenchmarks,
    IReadOnlyList<StrategicLifeBrief> RecentLives);

public sealed record StrategicLifeBrief(
    int LifeNumber,
    EpisodeStatus Status,
    int? TurnsUsed,
    long? NetWorthDelta,
    IReadOnlyList<LifeMilestone> Milestones,
    string Summary);

public sealed record NewObjectiveProposal(
    string Goal,
    string Rationale,
    decimal Confidence,
    IReadOnlyList<string> Assumptions,
    IReadOnlyList<string> SupportingKnowledgeNodeIds);

public sealed record StrategicAssessment(
    ProgressAssessment Progress,
    string Summary,
    IReadOnlyList<string> EvidenceForProgress,
    IReadOnlyList<string> EvidenceAgainstProgress,
    IReadOnlyList<string> StuckSignals,
    IReadOnlyList<string> UncertainAssumptions,
    IReadOnlyList<string> OverlookedOpportunities,
    IReadOnlyList<string> AlternativeObjectives,
    string Recommendation,
    ObjectiveDecision ObjectiveDecision,
    string? SelectedProposalId,
    NewObjectiveProposal? NewObjective,
    IReadOnlyList<string> CitedKnowledgeNodeIds)
{
    public IReadOnlyList<AutonomousScriptPlan> PlannedScripts { get; init; } = [];
    [JsonIgnore]
    public string? InteractionId { get; init; }
}

public sealed record StrategicReviewRecord(
    string ReviewId,
    string AgentId,
    DateTimeOffset ReviewedAt,
    ModelConfiguration Model,
    StrategicReviewContext Context,
    StrategicAssessment Assessment,
    string DecisionOutcome);

public interface IStrategicReasoningProvider
{
    string Id { get; }
    Task<StrategicAssessment> AssessAsync(
        ModelConfiguration configuration,
        StrategicReviewContext context,
        CancellationToken cancellationToken = default);
}

public interface IStrategicReviewStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(StrategicReviewRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<StrategicReviewRecord>> GetRecentAsync(
        string? agentId = null,
        int limit = 100,
        CancellationToken cancellationToken = default);
}
