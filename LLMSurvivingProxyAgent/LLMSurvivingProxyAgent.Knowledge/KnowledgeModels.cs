using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Knowledge;

public enum KnowledgeCategory
{
    WorldKnowledge,
    GameplayMechanic,
    TacticalGuidance,
    StrategicGuidance,
    MacroDefinition,
    MacroConstructionPattern,
    ReactiveTriggerPattern,
    MenuStructure,
    DecisionContext,
    CommunicationPattern,
    CooperativeProcedure,
    CorpProtocol,
    EmergencyResponse,
    ResourceTransfer,
    RoleDefinition,
    TacticalPattern,
    AgentExperience
}

public enum ExtractionBasis
{
    ExplicitText,
    StaticStructure,
    FilenameHeuristic,
    ExternalSourceReview,
    RuntimeObservation
}

public enum KnowledgeRelationType
{
    CallsSubroutine,
    DemonstratesPattern,
    Supports,
    Requires,
    Enables,
    MitigatesRisk,
    Contradicts
}

public sealed record KnowledgeEvidence(
    int LineNumber,
    string Signal,
    string Text)
{
    public string? SourceUrl { get; init; }
    public string? SourceLocator { get; init; }
    public KnowledgeEvidenceDetails? Details { get; init; }
}

public sealed record KnowledgeEvidenceDetails
{
    public string? Message { get; init; }
    public string? MessageTemplate { get; init; }
    public string? Channel { get; init; }
    public string? Participants { get; init; }
    public string? Trigger { get; init; }
    public string? Action { get; init; }
    public string? Intent { get; init; }
    public string? Rationale { get; init; }
    public string? RequiredAction { get; init; }
    public string? ExpectedResponse { get; init; }
    public string? Role { get; init; }
    public IReadOnlyList<string>? Steps { get; init; }
    public AgentExperienceEvidence? AgentExperience { get; init; }
}

public sealed record AgentExperienceEvidence(
    string ExperienceId,
    string AgentId,
    string EventKind,
    string Status,
    DateTimeOffset StartedAt,
    DateTimeOffset EndedAt,
    string Result,
    string Notes,
    IReadOnlyList<int> PlanSectors,
    bool GameOutcomeMeasured,
    GameSnapshot? BeforeSnapshot = null,
    GameSnapshot? AfterSnapshot = null,
    string? SessionId = null,
    int? TurnNumber = null)
{
    public GameSituationReport? SituationReport { get; init; }
}

public sealed record KnowledgeNode(
    string Id,
    string DocumentId,
    KnowledgeCategory Category,
    string Title,
    string Summary,
    decimal Confidence,
    ExtractionBasis Basis,
    IReadOnlyList<KnowledgeEvidence> Evidence);

public sealed record KnowledgeRelation(
    string EvidenceDocumentId,
    string SourceNodeId,
    string TargetNodeId,
    KnowledgeRelationType Type,
    decimal Confidence,
    ExtractionBasis Basis,
    IReadOnlyList<KnowledgeEvidence> Evidence);

public sealed record KnowledgeGraphResult(
    KnowledgeNode Node,
    int Depth,
    IReadOnlyList<KnowledgeRelation> Path);

public sealed record KnowledgeDocument(
    string Id,
    string RootId,
    string RelativePath,
    string FileName,
    string ContentHash,
    string Language,
    DateTimeOffset MinedAt,
    IReadOnlyList<KnowledgeNode> Nodes,
    IReadOnlyList<KnowledgeRelation> Relations)
{
    public string? OwnerAgentId { get; init; }
}

public sealed record KnowledgeDocumentSummary(
    string Id,
    string RootId,
    string RelativePath,
    string FileName,
    string ContentHash,
    string Language,
    DateTimeOffset MinedAt,
    int NodeCount);

public sealed record KnowledgeMiningReport(
    string RootId,
    int FilesFound,
    int FilesMined,
    int FilesUnchanged,
    int FilesTooLarge,
    int NodesCreated)
{
    public int MenuStructuresObserved { get; init; }
    public int MenuOptionsObserved { get; init; }
    public int MenuDisplayObservations { get; init; }
    public int ArchiveFilesScanned { get; init; }
    public int ArchiveFilesTooLarge { get; init; }
    public int ArchiveEntriesFound { get; init; }
    public int ArchiveEntriesMined { get; init; }
    public int ArchiveEntriesDeduplicated { get; init; }
    public int ArchiveEntriesTooLarge { get; init; }
}

public sealed record KnowledgeSourceRoot(string Id, string Path);

public sealed record KnowledgeCategorySourceCount(
    string Title,
    string RootId,
    int SourceCount);
