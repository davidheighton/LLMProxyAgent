using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Knowledge;

namespace LLMSurvivingProxyAgent.Packaging;

public sealed class SeedKnowledgePackageService(
    IKnowledgeStore seedStore,
    IKnowledgeStore runtimeStore)
{
    private const string PackageType = "knowledge-seed";
    private const int MaximumDocuments = 100_000;
    private const int MaximumNodes = 2_000_000;
    private const int MaximumRelations = 5_000_000;

    public async Task<byte[]> ExportAsync(
        string source,
        CancellationToken cancellationToken = default) =>
        await ExportVersionedAsync(source, "unversioned", [], cancellationToken);

    public async Task<byte[]> ExportVersionedAsync(
        string source,
        string corpusVersion,
        IReadOnlyList<PackageSourceSummary> sources,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(corpusVersion);
        ArgumentNullException.ThrowIfNull(sources);
        var documents = await seedStore.GetAllDocumentsAsync(cancellationToken);
        ValidateDocuments(documents);
        return PackageArchive.Create(
            PackageType,
            source,
            new SeedKnowledgePackagePayload(documents),
            historyMayBeTruncated: false,
            notIncluded: [],
            packageRevision: corpusVersion,
            sources: sources);
    }

    public async Task<PackageImportReport> ImportAsync(
        Stream package,
        CancellationToken cancellationToken = default)
    {
        var (manifest, payload) = await PackageArchive.ReadAsync<SeedKnowledgePackagePayload>(
            package,
            PackageType,
            cancellationToken);
        ValidateDocuments(payload.Documents);
        var runtimeDocuments = await runtimeStore.GetAllDocumentsAsync(cancellationToken);
        var runtimeDocumentIds = runtimeDocuments.Select(document => document.Id).ToHashSet(StringComparer.Ordinal);
        var runtimeNodeIds = runtimeDocuments.SelectMany(document => document.Nodes)
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);
        var runtimePaths = runtimeDocuments.Select(document => (document.RootId, document.RelativePath))
            .ToHashSet();
        if (payload.Documents.Any(document =>
            runtimeDocumentIds.Contains(document.Id) ||
            runtimePaths.Contains((document.RootId, document.RelativePath)) ||
            document.Nodes.Any(node => runtimeNodeIds.Contains(node.Id))))
        {
            throw new InvalidOperationException(
                "The seed package conflicts with existing runtime knowledge; no seed data was imported.");
        }
        await seedStore.ImportDocumentsAsync(payload.Documents, cancellationToken);
        return new PackageImportReport(
            manifest.PackageId,
            payload.Documents.Count,
            payload.Documents.Sum(document => document.Nodes.Count),
            payload.Documents.Sum(document => document.Relations.Count));
    }

    private static void ValidateDocuments(IReadOnlyList<KnowledgeDocument>? documents)
    {
        if (documents is null ||
            documents.Count is < 1 or > MaximumDocuments)
        {
            throw new InvalidDataException("A knowledge seed must contain between 1 and 100,000 documents.");
        }

        var documentIds = new HashSet<string>(StringComparer.Ordinal);
        var documentPaths = new HashSet<(string RootId, string RelativePath)>();
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        var relationIds = new HashSet<(string EvidenceDocumentId, string SourceNodeId, string TargetNodeId, KnowledgeRelationType Type)>();
        var nodeCount = 0;
        var relationCount = 0;

        foreach (var document in documents)
        {
            if (document is null ||
                string.IsNullOrWhiteSpace(document.Id) ||
                string.IsNullOrWhiteSpace(document.RootId) ||
                string.IsNullOrWhiteSpace(document.RelativePath) ||
                string.IsNullOrWhiteSpace(document.FileName) ||
                string.IsNullOrWhiteSpace(document.ContentHash) ||
                string.IsNullOrWhiteSpace(document.Language) ||
                document.OwnerAgentId is not null ||
                document.MinedAt == default ||
                !documentIds.Add(document.Id) ||
                !documentPaths.Add((document.RootId, document.RelativePath)) ||
                IsUnsafeRelativePath(document.RelativePath) ||
                document.Nodes is null ||
                document.Relations is null)
            {
                throw new InvalidDataException("The knowledge seed contains invalid or duplicate document metadata.");
            }

            nodeCount = checked(nodeCount + document.Nodes.Count);
            relationCount = checked(relationCount + document.Relations.Count);
            if (nodeCount > MaximumNodes || relationCount > MaximumRelations)
            {
                throw new InvalidDataException("The knowledge seed exceeds the document, node, or relation limits.");
            }

            foreach (var node in document.Nodes)
            {
                if (node is null ||
                    string.IsNullOrWhiteSpace(node.Id) ||
                    node.DocumentId != document.Id ||
                    !Enum.IsDefined(node.Category) ||
                    !Enum.IsDefined(node.Basis) ||
                    string.IsNullOrWhiteSpace(node.Title) ||
                    string.IsNullOrWhiteSpace(node.Summary) ||
                    node.Confidence is < 0 or > 1 ||
                    node.Evidence is null ||
                    node.Evidence.Any(evidence =>
                        evidence is null ||
                        evidence.LineNumber < 1 ||
                        string.IsNullOrWhiteSpace(evidence.Signal) ||
                        string.IsNullOrWhiteSpace(evidence.Text) ||
                        evidence.SourceLocator?.Length > 500 ||
                        !IsValidDetails(evidence.Details) ||
                        evidence.SourceUrl is not null && !IsSafeSourceUrl(evidence.SourceUrl)) ||
                    !nodeIds.Add(node.Id))
                {
                    throw new InvalidDataException("The knowledge seed contains an invalid or duplicate knowledge node.");
                }
            }

            foreach (var relation in document.Relations)
            {
                if (relation is null ||
                    relation.EvidenceDocumentId != document.Id ||
                    string.IsNullOrWhiteSpace(relation.SourceNodeId) ||
                    string.IsNullOrWhiteSpace(relation.TargetNodeId) ||
                    !Enum.IsDefined(relation.Type) ||
                    !Enum.IsDefined(relation.Basis) ||
                    relation.Confidence is < 0 or > 1 ||
                    relation.Evidence is null ||
                    relation.Evidence.Count == 0 ||
                    !relationIds.Add((
                        relation.EvidenceDocumentId,
                        relation.SourceNodeId,
                        relation.TargetNodeId,
                        relation.Type)) ||
                    relation.Evidence.Any(evidence =>
                        evidence is null ||
                        evidence.LineNumber < 1 ||
                        string.IsNullOrWhiteSpace(evidence.Signal) ||
                        string.IsNullOrWhiteSpace(evidence.Text) ||
                        evidence.SourceLocator?.Length > 500 ||
                        !IsValidDetails(evidence.Details) ||
                        evidence.SourceUrl is not null && !IsSafeSourceUrl(evidence.SourceUrl)))
                {
                    throw new InvalidDataException("The knowledge seed contains an invalid knowledge relation.");
                }
            }
        }

        foreach (var relation in documents.SelectMany(document => document.Relations))
        {
            if (!nodeIds.Contains(relation.SourceNodeId) || !nodeIds.Contains(relation.TargetNodeId))
            {
                throw new InvalidDataException("A knowledge relation refers to a node outside the package.");
            }
        }
    }

    private static bool IsUnsafeRelativePath(string path)
    {
        var normalized = path.Replace('\\', '/');
        return Path.IsPathRooted(path) ||
            normalized.StartsWith("/", StringComparison.Ordinal) ||
            normalized.Split('/').Any(segment => segment is "." or "..") ||
            normalized.Contains(':');
    }

    private static bool IsSafeSourceUrl(string value) =>
        Uri.TryCreate(value, UriKind.Absolute, out var uri) &&
        uri.Scheme is "http" or "https" &&
        !string.IsNullOrWhiteSpace(uri.Host) &&
        string.IsNullOrEmpty(uri.UserInfo);

    private static bool IsValidDetails(KnowledgeEvidenceDetails? details) =>
        details is null ||
        new[]
        {
            details.Message,
            details.MessageTemplate,
            details.Channel,
            details.Participants,
            details.Trigger,
            details.Action,
            details.Intent,
            details.Rationale,
            details.RequiredAction,
            details.ExpectedResponse,
            details.Role
        }.All(value => value is null || value.Length <= 500) &&
        (details.Steps is null ||
         details.Steps.Count <= 24 &&
         details.Steps.All(step => !string.IsNullOrWhiteSpace(step) && step.Length <= 500)) &&
        IsValidAgentExperience(details.AgentExperience);

    private static bool IsValidAgentExperience(AgentExperienceEvidence? experience)
    {
        if (experience is null)
        {
            return true;
        }
        if (string.IsNullOrWhiteSpace(experience.ExperienceId) ||
            experience.ExperienceId.Length > 200 ||
            string.IsNullOrWhiteSpace(experience.AgentId) ||
            experience.AgentId.Length > 200 ||
            experience.StartedAt == default ||
            experience.EndedAt < experience.StartedAt ||
            experience.Result is null ||
            experience.Result.Length > 10_000 ||
            experience.Notes is null ||
            experience.Notes.Length > 10_000 ||
            experience.PlanSectors is null ||
            experience.PlanSectors.Count > 1_000 ||
            experience.PlanSectors.Any(sector => sector < 1) ||
            (experience.BeforeSnapshot is not null &&
             (!GameSnapshotValidation.IsValid(experience.BeforeSnapshot) ||
              experience.BeforeSnapshot.ObservedAt > experience.StartedAt)) ||
            (experience.AfterSnapshot is not null &&
             (!GameSnapshotValidation.IsValid(experience.AfterSnapshot) ||
              experience.AfterSnapshot.ObservedAt < experience.EndedAt)) ||
            (experience.BeforeSnapshot is not null &&
             experience.AfterSnapshot is not null &&
             experience.BeforeSnapshot.ObservedAt > experience.AfterSnapshot.ObservedAt) ||
            experience.GameOutcomeMeasured)
        {
            return false;
        }

        return experience.EventKind switch
        {
            "MacroExecution" =>
                experience.SessionId is null &&
                experience.TurnNumber is null &&
                experience.SituationReport is null &&
                Enum.TryParse<MacroExecutionStatus>(experience.Status, out var status) &&
                Enum.IsDefined(status),
            "TurnObservation" =>
                experience.Status == "Observed" &&
                !string.IsNullOrWhiteSpace(experience.SessionId) &&
                experience.SessionId.Length <= 200 &&
                experience.TurnNumber is > 0 &&
                experience.SituationReport is null &&
                experience.PlanSectors.Count == 0 &&
                experience.BeforeSnapshot is not null &&
                experience.AfterSnapshot is not null &&
                experience.AfterSnapshot.ObservedAt > experience.BeforeSnapshot.ObservedAt,
            "SituationObservation" =>
                experience.Status == "Observed" &&
                !string.IsNullOrWhiteSpace(experience.SessionId) &&
                experience.SessionId.Length <= 200 &&
                experience.TurnNumber is null &&
                experience.PlanSectors.Count == 0 &&
                experience.BeforeSnapshot is null &&
                experience.AfterSnapshot is not null &&
                experience.SituationReport is { } report &&
                report.Presence == GamePresenceState.Connected &&
                report.Authentication == GameAuthenticationState.Authenticated &&
                report.Synchronization == GameSynchronizationState.Healthy &&
                report.Snapshot == experience.AfterSnapshot &&
                report.CapturedAt >= experience.AfterSnapshot.ObservedAt &&
                experience.AfterSnapshot.Authenticated == true &&
                !string.IsNullOrWhiteSpace(report.CurrentPrompt) &&
                report.UnknownFields is not null &&
                report.UnknownFields.Count <= 100 &&
                report.UnknownFields.All(field =>
                    !string.IsNullOrWhiteSpace(field) && field.Length <= 200) &&
                report.UnknownFields.Distinct(StringComparer.Ordinal).Count() == report.UnknownFields.Count,
            _ => false
        };
    }
}
