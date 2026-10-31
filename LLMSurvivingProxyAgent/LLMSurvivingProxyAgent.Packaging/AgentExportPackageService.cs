using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Goals;
using LLMSurvivingProxyAgent.Knowledge;

namespace LLMSurvivingProxyAgent.Packaging;

public sealed class AgentExportPackageService(
    IAgentStore agentStore,
    IGoalProposalStore goalStore,
    IMacroTelemetryStore macroStore,
    IReactiveEventStore reactiveStore,
    IStrategicReviewStore reviewStore,
    IKnowledgeStore runtimeKnowledgeStore,
    IKnowledgeStore seedKnowledgeStore)
{
    private const int HistoryLimit = 500;
    private const string PackageType = "agent-export";
    private static readonly string[] SensitiveConfigurationTerms =
    [
        "secret",
        "password",
        "token",
        "credential",
        "apikey",
        "api-key",
        "connectionstring",
        "privatekey"
    ];

    public async Task<byte[]> ExportAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        var agent = await agentStore.GetAgentAsync(agentId, cancellationToken)
            ?? throw new KeyNotFoundException($"Agent '{agentId}' was not found.");

        var proposalTask = goalStore.GetProposalsAsync(agentId, limit: HistoryLimit, cancellationToken: cancellationToken);
        var objectiveTask = goalStore.GetObjectivesAsync(agentId, limit: HistoryLimit, cancellationToken: cancellationToken);
        var macroTask = macroStore.GetRecentAsync(agentId, HistoryLimit, cancellationToken);
        var reactiveTask = reactiveStore.GetRecentAsync(agentId, HistoryLimit, cancellationToken);
        var reviewTask = reviewStore.GetRecentAsync(agentId, HistoryLimit, cancellationToken);
        var personalKnowledgeTask = runtimeKnowledgeStore.GetAgentDocumentsAsync(agentId, cancellationToken);
        var seedDocumentsTask = seedKnowledgeStore.GetAllDocumentsAsync(cancellationToken);
        await Task.WhenAll(
            proposalTask,
            objectiveTask,
            macroTask,
            reactiveTask,
            reviewTask,
            personalKnowledgeTask,
            seedDocumentsTask);

        var proposals = await proposalTask;
        var objectives = await objectiveTask;
        var macros = await macroTask;
        var reactive = await reactiveTask;
        var reviews = await reviewTask;
        var personalKnowledge = await personalKnowledgeTask;
        var seedDocuments = await seedDocumentsTask;
        var seedPayload = new SeedKnowledgePackagePayload(seedDocuments);
        var identity = new AgentExportIdentity(
            agent.AgentId,
            agent.DisplayName,
            agent.Description,
            agent.CreatedDate,
            agent.PreferredProvider,
            agent.PreferredModel,
            agent.Goals.ToArray(),
            agent.Configuration
                .Where(pair => !IsSensitiveConfiguration(pair.Key))
                .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.OrdinalIgnoreCase),
            new AgentStatistics
            {
                DecisionsMade = agent.Statistics.DecisionsMade,
                MacrosExecuted = agent.Statistics.MacrosExecuted,
                SuccessfulMacros = agent.Statistics.SuccessfulMacros
            });

        var payload = new AgentExportPackagePayload(
            identity,
            new KnowledgeSeedReference(
                PackageArchive.ComputePayloadSha256(seedPayload),
                seedDocuments.Count,
                seedDocuments.Sum(document => document.Nodes.Count),
                seedDocuments.Sum(document => document.Relations.Count)),
            personalKnowledge,
            agent.Journal.ToArray(),
            proposals,
            objectives,
            macros,
            reactive,
            reviews);
        var historyMayBeTruncated =
            proposals.Count == HistoryLimit ||
            objectives.Count == HistoryLimit ||
            macros.Count == HistoryLimit ||
            reactive.Count == HistoryLimit ||
            reviews.Count == HistoryLimit;
        return PackageArchive.Create(
            PackageType,
            $"Agent:{agent.AgentId}",
            payload,
            historyMayBeTruncated,
            [
                "The shared knowledge seed data is not included; its payload SHA-256 fingerprint and counts are included.",
                "Runtime knowledge without this agent as its owner is excluded.",
                "Historical review and proposal snapshots may contain seed-derived excerpts.",
                "Trust networks, relationships, and standalone communication records are not persisted by the current application."
            ]);
    }

    private static bool IsSensitiveConfiguration(string key)
    {
        var normalizedKey = key.Replace("_", string.Empty, StringComparison.Ordinal)
            .Replace("-", string.Empty, StringComparison.Ordinal)
            .Replace(".", string.Empty, StringComparison.Ordinal);
        return SensitiveConfigurationTerms.Any(term =>
            normalizedKey.Contains(term.Replace("-", string.Empty, StringComparison.Ordinal), StringComparison.OrdinalIgnoreCase));
    }
}
