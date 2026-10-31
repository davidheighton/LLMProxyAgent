namespace LLMSurvivingProxyAgent.Knowledge;

public interface IKnowledgeStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<bool> HasUnchangedDocumentAsync(
        string documentId,
        string contentHash,
        CancellationToken cancellationToken = default);
    Task ReplaceDocumentAsync(
        KnowledgeDocument document,
        CancellationToken cancellationToken = default);
    Task ReplaceAgentDocumentAsync(
        string agentId,
        KnowledgeDocument document,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<KnowledgeNode>> SearchAsync(
        string? query = null,
        KnowledgeCategory? category = null,
        int limit = 100,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<KnowledgeNode>> SearchLayerAsync(
        string? query,
        KnowledgeLayer layer,
        int limit = 100,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<KnowledgeNode>> SearchLayerForAgentAsync(
        string agentId,
        string? query,
        KnowledgeLayer layer,
        int limit = 100,
        CancellationToken cancellationToken = default);
    Task<KnowledgeNode?> GetNodeAsync(
        string nodeId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<KnowledgeDocumentSummary>> GetDocumentsAsync(
        string? rootId = null,
        int limit = 100,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<KnowledgeDocument>> GetAllDocumentsAsync(
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<KnowledgeDocument>> GetAgentDocumentsAsync(
        string agentId,
        CancellationToken cancellationToken = default);
    Task ImportDocumentsAsync(
        IReadOnlyList<KnowledgeDocument> documents,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<KnowledgeCategorySourceCount>> GetCategorySourceCountsAsync(
        KnowledgeCategory category,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<KnowledgeRelation>> GetRelationsAsync(
        string nodeId,
        CancellationToken cancellationToken = default);
    Task AddRelationAsync(
        KnowledgeRelation relation,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<KnowledgeGraphResult>> TraverseAsync(
        string rootNodeId,
        int maxDepth = 2,
        int limit = 100,
        CancellationToken cancellationToken = default);
}
