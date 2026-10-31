namespace LLMSurvivingProxyAgent.Core;

public enum ReactiveEventType
{
    HostileDetected,
    FederalWarpHit,
    TradeBustDetected,
    ConnectionLost,
    TraderEnteredSector,
    PlanetUnderAttack,
    CommunicationReceived,
    WorldObservationChanged,
    Unknown
}

public enum ReactiveEventSource
{
    GameSession,
    Cim,
    PrivateMessage,
    CorporateMessage,
    SubspaceMessage,
    OperatorSimulation
}

public enum ReactiveMode
{
    Fast,
    Deliberative,
    Informational
}

public enum ReactiveDecisionOrigin
{
    DeterministicRule,
    ReflexModel,
    DeterministicFallback
}

public enum ReactiveClassificationCategory
{
    Unknown,
    Threat,
    Intelligence,
    Noise,
    Bait,
    Opportunity,
    RequestForHelp,
    PlanetStatus,
    EconomicInformation
}

public enum ReactiveUrgency
{
    Informational,
    Routine,
    Urgent,
    Emergency
}

public enum ReactiveTrustAssessment
{
    NotApplicable,
    Untrusted,
    Low,
    Medium,
    High
}

public enum ReactiveActionKind
{
    RaiseAlert,
    PrepareEvacuationPlan,
    AbortMacro,
    StartBustRecovery,
    PauseMacroExecution,
    ReconnectRequired,
    RecordAttributedObservation,
    RequestStrategicReview
}

public sealed record ReactiveEvent(
    string EventId,
    string AgentId,
    ReactiveEventType Type,
    ReactiveEventSource Source,
    DateTimeOffset OccurredAt,
    DateTimeOffset ReceivedAt,
    int? SectorId,
    string? ActorName,
    int? ActorAlignment,
    string? Message,
    string? SourceReference,
    decimal? SenderTrustAtReceipt);

public sealed record ReactiveActionIntent(ReactiveActionKind Kind, string Rationale);

public sealed record ReactiveDecision(
    ReactiveMode Mode,
    string RuleId,
    string Summary,
    bool RequestsStrategicReview,
    IReadOnlyList<ReactiveActionIntent> ActionIntents,
    string ExecutionBoundary)
{
    public ReactiveDecisionOrigin Origin { get; init; } = ReactiveDecisionOrigin.DeterministicRule;
    public ReactiveClassificationCategory? Category { get; init; }
    public ReactiveUrgency? Urgency { get; init; }
    public ReactiveTrustAssessment? TrustAssessment { get; init; }
    public decimal? Confidence { get; init; }
    public string? FallbackReason { get; init; }
}

public sealed record ReactiveEventRecord(
    ReactiveEvent Event,
    ReactiveDecision Decision,
    DateTimeOffset ProcessedAt);

public interface IReactiveEventStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ReactiveEventRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ReactiveEventRecord>> GetRecentAsync(
        string? agentId = null,
        int limit = 100,
        CancellationToken cancellationToken = default);
}
