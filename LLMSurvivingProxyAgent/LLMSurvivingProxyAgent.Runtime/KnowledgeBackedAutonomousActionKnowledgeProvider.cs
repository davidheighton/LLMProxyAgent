using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Knowledge;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed class KnowledgeBackedAutonomousActionKnowledgeProvider(
    IKnowledgeStore knowledgeStore) : IAutonomousActionKnowledgeProvider
{
    private const int SearchLimitPerTerm = 4;
    private const int MaximumKnowledgeItems = 12;
    private const int MaximumRecentExperienceItems = 3;
    private const int MaximumSourceKnowledgeItems = MaximumKnowledgeItems - MaximumRecentExperienceItems;
    private const int MaximumSummaryLength = 800;
    private const int MaximumEvidenceLength = 400;

    private static readonly (KnowledgeLayer Layer, string Query)[] SearchQueries =
    [
        (KnowledgeLayer.Facts, "exploration"),
        (KnowledgeLayer.Facts, "scanner"),
        (KnowledgeLayer.Facts, "port"),
        (KnowledgeLayer.Procedural, "scan"),
        (KnowledgeLayer.Procedural, "exploration"),
        (KnowledgeLayer.Procedural, "navigation"),
        (KnowledgeLayer.Procedural, "trade"),
        (KnowledgeLayer.Procedural, "prompt"),
        (KnowledgeLayer.Procedural, "script"),
        (KnowledgeLayer.Tactical, "scan"),
        (KnowledgeLayer.Tactical, "exploration"),
        (KnowledgeLayer.Tactical, "port"),
        (KnowledgeLayer.Tactical, "trade"),
        (KnowledgeLayer.Tactical, "script"),
        (KnowledgeLayer.Tactical, "recovery"),
        (KnowledgeLayer.Structural, "scan")
    ];

    private static readonly string[] RelevanceTerms =
    [
        "explor", "scan", "scanner", "sector", "warp", "port", "trade", "fuel", "hold", "turn", "credit", "cim",
        "prompt", "script", "macro", "menu", "routine", "recover"
    ];

    public async Task<IReadOnlyList<AutonomousActionKnowledgeItem>> GetRelevantKnowledgeAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);

        var searches = SearchQueries.Select(search => knowledgeStore.SearchLayerForAgentAsync(
            agentId,
            search.Query,
            search.Layer,
            SearchLimitPerTerm,
            cancellationToken));
        var searchResults = await Task.WhenAll(searches).ConfigureAwait(false);
        var recentExperience = await knowledgeStore.SearchLayerForAgentAsync(
            agentId,
            null,
            KnowledgeLayer.Experience,
            MaximumRecentExperienceItems,
            cancellationToken).ConfigureAwait(false);

        var sourceKnowledge = searchResults
            .SelectMany(nodes => nodes)
            .Concat(recentExperience)
            .DistinctBy(node => node.Id, StringComparer.Ordinal)
            .Select(node => new
            {
                Node = node,
                Layer = KnowledgeLayers.GetLayer(node.Category),
                Score = GetRelevanceScore(node)
            })
            .Where(item => item.Score > 0 && item.Layer != KnowledgeLayer.Experience)
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Node.Confidence)
            .ThenBy(item => item.Node.Id, StringComparer.Ordinal)
            .Take(MaximumSourceKnowledgeItems)
            .Select(item => ToKnowledgeItem(item.Node, item.Layer));
        var experienceKnowledge = recentExperience
            .DistinctBy(node => node.Id, StringComparer.Ordinal)
            .Take(MaximumRecentExperienceItems)
            .Select(node => ToKnowledgeItem(node, KnowledgeLayer.Experience));

        return sourceKnowledge
            .Concat(experienceKnowledge)
            .DistinctBy(item => item.NodeId, StringComparer.Ordinal)
            .ToArray();
    }

    private static AutonomousActionKnowledgeItem ToKnowledgeItem(
        KnowledgeNode node,
        KnowledgeLayer layer) =>
        new(
            node.Id,
            layer.ToString(),
            Truncate(node.Title, 300),
            Truncate(node.Summary, MaximumSummaryLength),
            node.Confidence,
            node.Basis.ToString(),
            node.Evidence.Take(3)
                .Select(evidence => Truncate(
                    $"line {evidence.LineNumber} ({evidence.Signal}): {evidence.Text}",
                    MaximumEvidenceLength))
                .ToArray());

    private static int GetRelevanceScore(KnowledgeNode node)
    {
        var text = $"{node.Title} {node.Summary}".ToLowerInvariant();
        return RelevanceTerms.Count(term => text.Contains(term, StringComparison.Ordinal));
    }

    private static string Truncate(string value, int maximumLength) =>
        value.Length <= maximumLength ? value : value[..maximumLength];
}
