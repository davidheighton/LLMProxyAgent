namespace LLMSurvivingProxyAgent.Knowledge;

public sealed class CompositeKnowledgeStore(
    IKnowledgeStore seedStore,
    IKnowledgeStore runtimeStore) : IKnowledgeStore
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await seedStore.InitializeAsync(cancellationToken);
        await runtimeStore.InitializeAsync(cancellationToken);
    }

    public async Task<bool> HasUnchangedDocumentAsync(
        string documentId,
        string contentHash,
        CancellationToken cancellationToken = default) =>
        await seedStore.HasUnchangedDocumentAsync(documentId, contentHash, cancellationToken) ||
        await runtimeStore.HasUnchangedDocumentAsync(documentId, contentHash, cancellationToken);

    public async Task ReplaceDocumentAsync(
        KnowledgeDocument document,
        CancellationToken cancellationToken = default)
    {
        if (document.OwnerAgentId is not null)
        {
            throw new InvalidOperationException(
                "Agent-owned knowledge must be saved with ReplaceAgentDocumentAsync.");
        }
        var seedDocuments = await seedStore.GetAllDocumentsAsync(cancellationToken);
        if (seedDocuments.Any(seedDocument =>
            seedDocument.Id == document.Id ||
            (seedDocument.RootId == document.RootId &&
             seedDocument.RelativePath == document.RelativePath) ||
            document.Nodes.Any(node => seedDocument.Nodes.Any(seedNode => seedNode.Id == node.Id))))
        {
            throw new InvalidOperationException(
                "Runtime knowledge cannot replace or shadow immutable seed knowledge.");
        }
        await runtimeStore.ReplaceDocumentAsync(document, cancellationToken);
    }

    public async Task ReplaceAgentDocumentAsync(
        string agentId,
        KnowledgeDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(document);
        var seedDocuments = await seedStore.GetAllDocumentsAsync(cancellationToken);
        if (seedDocuments.Any(seedDocument =>
            seedDocument.Id == document.Id ||
            (seedDocument.RootId == document.RootId &&
             seedDocument.RelativePath == document.RelativePath) ||
            document.Nodes.Any(node => seedDocument.Nodes.Any(seedNode => seedNode.Id == node.Id))))
        {
            throw new InvalidOperationException(
                "Agent-owned runtime knowledge cannot replace or shadow immutable seed knowledge.");
        }
        await runtimeStore.ReplaceAgentDocumentAsync(agentId, document, cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeNode>> SearchAsync(
        string? query = null,
        KnowledgeCategory? category = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 1,000.");
        }
        var seedNodes = await seedStore.SearchAsync(query, category, limit, cancellationToken);
        var runtimeNodes = await runtimeStore.SearchAsync(query, category, limit, cancellationToken);
        return seedNodes.Concat(runtimeNodes)
            .DistinctBy(node => node.Id, StringComparer.Ordinal)
            .OrderBy(node => node.Category)
            .ThenBy(node => node.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    public async Task<IReadOnlyList<KnowledgeNode>> SearchLayerAsync(
        string? query,
        KnowledgeLayer layer,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 1,000.");
        }
        var seedNodes = await seedStore.SearchLayerAsync(query, layer, limit, cancellationToken);
        var runtimeNodes = await runtimeStore.SearchLayerAsync(query, layer, limit, cancellationToken);
        return seedNodes.Concat(runtimeNodes)
            .DistinctBy(node => node.Id, StringComparer.Ordinal)
            .OrderBy(node => node.Category)
            .ThenBy(node => node.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    public async Task<IReadOnlyList<KnowledgeNode>> SearchLayerForAgentAsync(
        string agentId,
        string? query,
        KnowledgeLayer layer,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 1,000.");
        }
        var seedNodes = await seedStore.SearchLayerForAgentAsync(
            agentId,
            query,
            layer,
            limit,
            cancellationToken);
        var runtimeNodes = await runtimeStore.SearchLayerForAgentAsync(
            agentId,
            query,
            layer,
            limit,
            cancellationToken);
        var nodes = seedNodes.Concat(runtimeNodes)
            .DistinctBy(node => node.Id, StringComparer.Ordinal)
            .ToArray();
        if (layer == KnowledgeLayer.Experience)
        {
            return nodes
                .OrderByDescending(node => node.Evidence
                    .Select(evidence => evidence.Details?.AgentExperience?.EndedAt)
                    .FirstOrDefault())
                .ThenByDescending(node => node.Evidence
                    .Select(evidence => evidence.Details?.AgentExperience?.TurnNumber)
                    .FirstOrDefault())
                .ThenBy(node => node.Id, StringComparer.Ordinal)
                .Take(limit)
                .ToArray();
        }

        return nodes
            .OrderBy(node => node.Category)
            .ThenBy(node => node.Title, StringComparer.OrdinalIgnoreCase)
            .ThenBy(node => node.Id, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    public async Task<KnowledgeNode?> GetNodeAsync(
        string nodeId,
        CancellationToken cancellationToken = default) =>
        await seedStore.GetNodeAsync(nodeId, cancellationToken) ??
        await runtimeStore.GetNodeAsync(nodeId, cancellationToken);

    public async Task<IReadOnlyList<KnowledgeDocumentSummary>> GetDocumentsAsync(
        string? rootId = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 1,000.");
        }
        var seedDocuments = await seedStore.GetDocumentsAsync(rootId, limit, cancellationToken);
        var runtimeDocuments = await runtimeStore.GetDocumentsAsync(rootId, limit, cancellationToken);
        return seedDocuments.Concat(runtimeDocuments)
            .DistinctBy(document => document.Id, StringComparer.Ordinal)
            .OrderBy(document => document.RootId, StringComparer.Ordinal)
            .ThenBy(document => document.RelativePath, StringComparer.Ordinal)
            .Take(limit)
            .ToArray();
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> GetAllDocumentsAsync(
        CancellationToken cancellationToken = default)
    {
        var seedDocuments = await seedStore.GetAllDocumentsAsync(cancellationToken);
        var runtimeDocuments = await runtimeStore.GetAllDocumentsAsync(cancellationToken);
        var duplicateId = seedDocuments.Select(document => document.Id)
            .Intersect(runtimeDocuments.Select(document => document.Id), StringComparer.Ordinal)
            .FirstOrDefault();
        if (duplicateId is not null)
        {
            throw new InvalidDataException($"Knowledge document '{duplicateId}' exists in both seed and runtime stores.");
        }
        var combined = seedDocuments.Concat(runtimeDocuments).ToArray();
        if (combined.GroupBy(document => (document.RootId, document.RelativePath)).Any(group => group.Count() > 1))
        {
            throw new InvalidDataException("A source path exists in both seed and runtime knowledge stores.");
        }
        var duplicateNodeId = combined.SelectMany(document => document.Nodes)
            .GroupBy(node => node.Id, StringComparer.Ordinal)
            .FirstOrDefault(group => group.Count() > 1)?.Key;
        if (duplicateNodeId is not null)
        {
            throw new InvalidDataException($"Knowledge node '{duplicateNodeId}' exists in both seed and runtime stores.");
        }
        return combined
            .OrderBy(document => document.RootId, StringComparer.Ordinal)
            .ThenBy(document => document.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> GetAgentDocumentsAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        var seedDocuments = await seedStore.GetAgentDocumentsAsync(agentId, cancellationToken);
        var runtimeDocuments = await runtimeStore.GetAgentDocumentsAsync(agentId, cancellationToken);
        return seedDocuments.Concat(runtimeDocuments)
            .OrderBy(document => document.RootId, StringComparer.Ordinal)
            .ThenBy(document => document.RelativePath, StringComparer.Ordinal)
            .ToArray();
    }

    public Task ImportDocumentsAsync(
        IReadOnlyList<KnowledgeDocument> documents,
        CancellationToken cancellationToken = default) =>
        throw new InvalidOperationException(
            "Seed knowledge must be imported through the seed package service; runtime imports are not supported.");

    public async Task<IReadOnlyList<KnowledgeCategorySourceCount>> GetCategorySourceCountsAsync(
        KnowledgeCategory category,
        CancellationToken cancellationToken = default)
    {
        var seedCounts = await seedStore.GetCategorySourceCountsAsync(category, cancellationToken);
        var runtimeCounts = await runtimeStore.GetCategorySourceCountsAsync(category, cancellationToken);
        return seedCounts.Concat(runtimeCounts)
            .GroupBy(count => (count.Title, count.RootId))
            .Select(group => new KnowledgeCategorySourceCount(
                group.Key.Title,
                group.Key.RootId,
                group.Sum(count => count.SourceCount)))
            .OrderBy(count => count.Title, StringComparer.Ordinal)
            .ThenBy(count => count.RootId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task<IReadOnlyList<KnowledgeRelation>> GetRelationsAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        var seedRelations = await seedStore.GetRelationsAsync(nodeId, cancellationToken);
        var runtimeRelations = await runtimeStore.GetRelationsAsync(nodeId, cancellationToken);
        return seedRelations.Concat(runtimeRelations)
            .DistinctBy(
                relation => (relation.EvidenceDocumentId, relation.SourceNodeId, relation.TargetNodeId, relation.Type))
            .OrderBy(relation => relation.Type)
            .ThenBy(relation => relation.EvidenceDocumentId, StringComparer.Ordinal)
            .ThenBy(relation => relation.SourceNodeId, StringComparer.Ordinal)
            .ThenBy(relation => relation.TargetNodeId, StringComparer.Ordinal)
            .ToArray();
    }

    public async Task AddRelationAsync(
        KnowledgeRelation relation,
        CancellationToken cancellationToken = default) =>
        await runtimeStore.AddRelationAsync(relation, cancellationToken);

    public async Task<IReadOnlyList<KnowledgeGraphResult>> TraverseAsync(
        string rootNodeId,
        int maxDepth = 2,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(rootNodeId);
        if (maxDepth is < 1 or > 5)
        {
            throw new ArgumentOutOfRangeException(nameof(maxDepth), "Maximum graph depth must be between 1 and 5.");
        }
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 1,000.");
        }

        var root = await GetNodeAsync(rootNodeId, cancellationToken);
        if (root is null)
        {
            return [];
        }

        var results = new List<KnowledgeGraphResult>
        {
            new(root, 0, [])
        };
        var queue = new Queue<(KnowledgeNode Node, int Depth, IReadOnlyList<KnowledgeRelation> Path, HashSet<string> Visited)>();
        queue.Enqueue((root, 0, [], new HashSet<string>([rootNodeId], StringComparer.Ordinal)));
        while (queue.Count > 0 && results.Count < limit)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var current = queue.Dequeue();
            if (current.Depth >= maxDepth)
            {
                continue;
            }

            var relations = await GetRelationsAsync(current.Node.Id, cancellationToken);
            foreach (var relation in relations
                .Where(candidate => candidate.SourceNodeId == current.Node.Id)
                .OrderBy(candidate => candidate.Type)
                .ThenBy(candidate => candidate.TargetNodeId, StringComparer.Ordinal))
            {
                if (current.Visited.Contains(relation.TargetNodeId))
                {
                    continue;
                }
                var target = await GetNodeAsync(relation.TargetNodeId, cancellationToken);
                if (target is null)
                {
                    continue;
                }
                var path = current.Path.Append(relation).ToArray();
                results.Add(new KnowledgeGraphResult(target, current.Depth + 1, path));
                if (results.Count >= limit)
                {
                    break;
                }
                var visited = new HashSet<string>(current.Visited, StringComparer.Ordinal)
                {
                    relation.TargetNodeId
                };
                queue.Enqueue((target, current.Depth + 1, path, visited));
            }
        }
        return results;
    }
}
