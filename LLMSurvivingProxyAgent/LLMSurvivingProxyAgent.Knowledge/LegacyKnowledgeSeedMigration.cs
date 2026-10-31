namespace LLMSurvivingProxyAgent.Knowledge;

public sealed class LegacyKnowledgeSeedMigration(
    IKnowledgeStore legacyStore,
    IKnowledgeStore seedStore,
    IKnowledgeStore? runtimeStore = null)
{
    public async Task<int> MigrateAsync(CancellationToken cancellationToken = default)
    {
        var legacyDocuments = await legacyStore.GetAllDocumentsAsync(cancellationToken);
        if (legacyDocuments.Count == 0)
        {
            return 0;
        }

        var seedDocuments = await seedStore.GetAllDocumentsAsync(cancellationToken);
        var runtimeDocuments = runtimeStore is null
            ? Array.Empty<KnowledgeDocument>()
            : await runtimeStore.GetAllDocumentsAsync(cancellationToken);
        var runtimeIds = runtimeDocuments.Select(document => document.Id).ToHashSet(StringComparer.Ordinal);
        var runtimePaths = runtimeDocuments
            .Select(document => (document.RootId, document.RelativePath))
            .ToHashSet();
        var runtimeNodeIds = runtimeDocuments.SelectMany(document => document.Nodes)
            .Select(node => node.Id)
            .ToHashSet(StringComparer.Ordinal);
        if (seedDocuments.Any(document =>
            runtimeIds.Contains(document.Id) ||
            runtimePaths.Contains((document.RootId, document.RelativePath)) ||
            document.Nodes.Any(node => runtimeNodeIds.Contains(node.Id))))
        {
            throw new InvalidOperationException(
                "Seed knowledge conflicts with runtime knowledge; migration stopped without overwriting data.");
        }

        if (seedDocuments.Count == 0)
        {
            if (runtimeDocuments.Count > 0)
            {
                if (legacyDocuments.Any(document =>
                    runtimeIds.Contains(document.Id) ||
                    runtimePaths.Contains((document.RootId, document.RelativePath)) ||
                    document.Nodes.Any(node => runtimeNodeIds.Contains(node.Id))))
                {
                    throw new InvalidOperationException(
                        "Legacy knowledge conflicts with runtime knowledge and was not migrated.");
                }
            }
            await seedStore.ImportDocumentsAsync(legacyDocuments, cancellationToken);
            return legacyDocuments.Count;
        }

        var seedById = seedDocuments.ToDictionary(document => document.Id, StringComparer.Ordinal);
        var missingOrChanged = legacyDocuments.FirstOrDefault(legacyDocument =>
            !seedById.TryGetValue(legacyDocument.Id, out var seedDocument) ||
            seedDocument.RootId != legacyDocument.RootId ||
            seedDocument.RelativePath != legacyDocument.RelativePath ||
            seedDocument.ContentHash != legacyDocument.ContentHash);
        if (missingOrChanged is not null)
        {
            throw new InvalidOperationException(
                $"Legacy knowledge migration is incomplete or conflicts with seed document '{missingOrChanged.Id}'. " +
                "No legacy knowledge was overwritten.");
        }
        return 0;
    }
}
