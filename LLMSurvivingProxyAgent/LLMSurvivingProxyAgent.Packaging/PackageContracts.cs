using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Goals;
using LLMSurvivingProxyAgent.Knowledge;

namespace LLMSurvivingProxyAgent.Packaging;

public sealed record PackageManifest(
    string Format,
    int SchemaVersion,
    string PackageType,
    string PackageId,
    DateTimeOffset CreatedAt,
    string Source,
    string PayloadSha256,
    bool HistoryMayBeTruncated,
    IReadOnlyList<string> NotIncluded)
{
    public string? PackageRevision { get; init; }
    public IReadOnlyList<PackageSourceSummary> Sources { get; init; } = [];
}

public sealed record PackageSourceSummary(
    string RootId,
    string Kind,
    int TotalFiles,
    int MiningCandidates,
    int TooLargeFiles,
    int FilesNotMined,
    int ExcludedDirectories,
    int SkippedReparsePoints,
    int DocumentsMined,
    int NodesExtracted,
    long TotalBytes,
    IReadOnlyDictionary<string, int> FilesByExtension,
    IReadOnlyDictionary<string, int> NodesByCategory,
    bool InventoryOnly)
{
    public CorpusCoverageMetrics Coverage { get; init; } = new();
}

public enum CorpusCoverageStatus
{
    NotMeasured,
    InventoryOnly,
    Measured,
    Partial
}

public sealed record CorpusCoverageMetrics
{
    public int ArchiveFilesScanned { get; init; }
    public int ArchiveFilesTooLarge { get; init; }
    public int ArchiveEntriesFound { get; init; }
    public int ArchiveEntriesMined { get; init; }
    public int ArchiveEntriesDeduplicated { get; init; }
    public int ArchiveEntriesTooLarge { get; init; }
    public int DecisionContextDocuments { get; init; }
    public IReadOnlyDictionary<string, int> DecisionContextEvidenceBySignal { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, int> DocumentationFilesInventoriedByRole { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, int> DocumentationDocumentsMinedByRole { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, int> DecisionContextDocumentsByDocumentationRole { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, int> DecisionContextEvidenceByDocumentationRole { get; init; } =
        new Dictionary<string, int>(StringComparer.Ordinal);
    public IReadOnlyDictionary<string, IReadOnlyList<string>> DocumentationSamplesByRole { get; init; } =
        new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
    public string DocumentationRoleClassificationBasis { get; init; } =
        "Heuristic from relative paths and file names; not authoritative.";
    public int TriggerRegistrations { get; init; }
    public int UniqueTriggerKinds { get; init; }
    public int DocumentsWithTriggers { get; init; }
    public int HandlerResponseCandidates { get; init; }
    public int PossibleRecoverySignals { get; init; }
    public int ScriptArtifacts { get; init; }
    public int SubroutineDefinitions { get; init; }
    public int DocumentsWithPurposeContext { get; init; }
    public int DocumentsWithGoalContext { get; init; }
    public int DocumentsWithTypicalUsageContext { get; init; }
    public int DocumentsWithTriggerContext { get; init; }
    public int DocumentsWithStoppingConditionContext { get; init; }
    public int StoppingConditionEvidence { get; init; }
    public int DocumentsWithFailureConditionContext { get; init; }
    public int FailureConditionEvidence { get; init; }
    public int DocumentsWithPreconditionContext { get; init; }
    public int DocumentsWithValidationContext { get; init; }
    public int DocumentsWithRetryContext { get; init; }
    public int DocumentsWithResumeContext { get; init; }
    public int DocumentsWithAbortContext { get; init; }
    public int DocumentsWithEscalationContext { get; init; }
    public int DocumentsWithCheckpointContext { get; init; }
    public int DocumentsWithRecoveryContext { get; init; }
    public int DocumentsWithVerificationLoopContext { get; init; }
    public int ExplicitUrgencyAnnotations { get; init; }
    public int PreconditionCheckDocuments { get; init; }
    public int ResultVerificationDocuments { get; init; }
    public int DocumentationFilesInventoried { get; init; }
    public int DocumentationDocumentsMined { get; init; }
    public int LogFilesInventoried { get; init; }
    public int MenuStructuresObserved { get; init; }
    public int MenuOptionsObserved { get; init; }
    public int MenuDisplayObservations { get; init; }
    public CorpusCoverageStatus MenuCoverage { get; init; } = CorpusCoverageStatus.NotMeasured;
    public CorpusCoverageStatus LogContentCoverage { get; init; } = CorpusCoverageStatus.NotMeasured;
}

public sealed record CorpusInventoryReport(
    string CorpusVersion,
    DateTimeOffset CreatedAt,
    IReadOnlyList<PackageSourceSummary> Sources)
{
    public CorpusComparisonReport Comparisons { get; init; } = new([], [], [], [], [], [], []);
    public KnowledgeClassCounts? KnowledgeClassCounts { get; init; }
}

public sealed record KnowledgeClassCounts(
    int FactNodes,
    int ProceduralNodes,
    int TacticalNodes,
    int CooperativeNodes,
    int CommunicationNodes,
    int StrategicNodes,
    int StructuralNodes)
{
    public int ExperienceNodes { get; init; }

    public int TotalNodes =>
        FactNodes + ProceduralNodes + TacticalNodes +
        CooperativeNodes + CommunicationNodes + StrategicNodes + StructuralNodes +
        ExperienceNodes;
}

public sealed record CorpusComparisonReport(
    IReadOnlyList<string> SourceFamiliesScanned,
    IReadOnlyList<string> DocumentationFamiliesScanned,
    IReadOnlyList<CorpusTriggerComparison> TriggerComparisons,
    IReadOnlyList<CorpusMacroContextComparison> MacroContextComparisons,
    IReadOnlyList<CorpusDecisionContextComparison> DecisionContextComparisons,
    IReadOnlyList<CorpusPatternComparison> PatternComparisons,
    IReadOnlyList<string> Limitations)
{
    public IReadOnlyList<CorpusClaimNetwork> ClaimNetworks { get; init; } = [];
}

public sealed record CorpusClaimNetwork(
    string MacroName,
    int CodeArtifactCount,
    IReadOnlyList<CorpusCodeArtifactReference> CodeArtifacts,
    IReadOnlyList<CorpusFamilyObservation> CodeFamilies,
    int ContextEvidenceCount,
    int ContextDocumentCount,
    int ContextFamilyCount,
    IReadOnlyList<CorpusClaimObservation> ContextClaims,
    string Interpretation);

public sealed record CorpusCodeArtifactReference(
    string NodeId,
    string DocumentId,
    string RootId,
    string SourcePath,
    string SourceFamily);

public sealed record CorpusClaimObservation(
    string Signal,
    string Text,
    int LineNumber,
    string DecisionContextNodeId,
    string DocumentId,
    string RootId,
    string SourcePath,
    string SourceFamily,
    string DocumentationRole,
    string MatchedTerm,
    string MatchBasis);

public sealed record CorpusTriggerComparison(
    string EventType,
    int RegistrationCount,
    int SourceDocumentCount,
    int SourceFamilyCount,
    IReadOnlyList<CorpusFamilyObservation> Families,
    IReadOnlyList<string> NotObservedInScannedFamilies)
{
    public string ConsensusStrength { get; init; } = "Low";
    public string Interpretation { get; init; } = "Observed family coverage only; not validated doctrine.";
}

public sealed record CorpusMacroContextComparison(
    string MacroName,
    string ContextSignal,
    string Topic,
    int EvidenceCount,
    int SourceDocumentCount,
    int SourceFamilyCount,
    IReadOnlyList<CorpusFamilyObservation> Families,
    IReadOnlyList<string> NotObservedInScannedFamilies)
{
    public string ConsensusStrength { get; init; } = "Low";
    public string Interpretation { get; init; } = "Observed family coverage only; not validated doctrine.";
}

public sealed record CorpusDecisionContextComparison(
    string Signal,
    string Topic,
    int EvidenceCount,
    int SourceDocumentCount,
    int SourceFamilyCount,
    IReadOnlyList<CorpusFamilyObservation> Families,
    IReadOnlyList<string> NotObservedInScannedFamilies)
{
    public string ConsensusStrength { get; init; } = "Low";
    public string Interpretation { get; init; } = "Observed family coverage only; not validated doctrine.";
}

public sealed record CorpusPatternComparison(
    string PatternType,
    string Pattern,
    int EvidenceCount,
    int SourceDocumentCount,
    int SourceFamilyCount,
    IReadOnlyList<CorpusFamilyObservation> Families,
    IReadOnlyList<string> NotObservedInScannedFamilies)
{
    public string ConsensusStrength { get; init; } = "Low";
    public string Interpretation { get; init; } = "Observed family coverage only; not validated doctrine.";
}

public sealed record CorpusFamilyObservation(
    string Family,
    int EvidenceCount,
    int SourceDocumentCount,
    IReadOnlyList<string> SampleSources);

public sealed record SeedKnowledgePackagePayload(
    IReadOnlyList<KnowledgeDocument> Documents);

public sealed record AgentExportIdentity(
    string AgentId,
    string DisplayName,
    string Description,
    DateTimeOffset CreatedDate,
    string PreferredProvider,
    string PreferredModel,
    IReadOnlyList<string> Goals,
    IReadOnlyDictionary<string, string> Configuration,
    AgentStatistics Statistics);

public sealed record KnowledgeSeedReference(
    string ContentSha256,
    int DocumentCount,
    int NodeCount,
    int RelationCount);

public sealed record AgentExportPackagePayload(
    AgentExportIdentity Identity,
    KnowledgeSeedReference SeedKnowledge,
    IReadOnlyList<KnowledgeDocument> PersonalKnowledge,
    IReadOnlyList<AgentJournalEntry> Journal,
    IReadOnlyList<GoalProposal> Proposals,
    IReadOnlyList<AgentObjective> Objectives,
    IReadOnlyList<MacroExecutionRecord> MacroHistory,
    IReadOnlyList<ReactiveEventRecord> ReactiveHistory,
    IReadOnlyList<StrategicReviewRecord> Reviews);

public sealed record PackageImportReport(
    string PackageId,
    int DocumentsImported,
    int NodesImported,
    int RelationsImported);
