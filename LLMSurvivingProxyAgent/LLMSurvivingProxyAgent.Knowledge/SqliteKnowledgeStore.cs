using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.Knowledge;

public sealed class SqliteKnowledgeStore(string connectionString) : IKnowledgeStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS knowledge_documents (
                document_id TEXT PRIMARY KEY,
                root_id TEXT NOT NULL,
                relative_path TEXT NOT NULL,
                file_name TEXT NOT NULL,
                content_hash TEXT NOT NULL,
                language TEXT NOT NULL,
                mined_at TEXT NOT NULL,
                owner_agent_id TEXT NULL,
                UNIQUE(root_id, relative_path)
            );
            CREATE TABLE IF NOT EXISTS knowledge_nodes (
                node_id TEXT PRIMARY KEY,
                document_id TEXT NOT NULL REFERENCES knowledge_documents(document_id) ON DELETE CASCADE,
                category TEXT NOT NULL,
                title TEXT NOT NULL,
                summary TEXT NOT NULL,
                confidence REAL NOT NULL CHECK(confidence >= 0 AND confidence <= 1),
                extraction_basis TEXT NOT NULL,
                evidence_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_knowledge_nodes_category_title
                ON knowledge_nodes(category, title);
            CREATE INDEX IF NOT EXISTS ix_knowledge_nodes_document
                ON knowledge_nodes(document_id);
            CREATE TABLE IF NOT EXISTS knowledge_relations (
                source_node_id TEXT NOT NULL REFERENCES knowledge_nodes(node_id) ON DELETE CASCADE,
                target_node_id TEXT NOT NULL REFERENCES knowledge_nodes(node_id) ON DELETE CASCADE,
                evidence_document_id TEXT NOT NULL REFERENCES knowledge_documents(document_id) ON DELETE CASCADE,
                relation TEXT NOT NULL,
                confidence REAL NOT NULL DEFAULT 0.5 CHECK(confidence >= 0 AND confidence <= 1),
                extraction_basis TEXT NOT NULL DEFAULT 'StaticStructure',
                evidence_json TEXT NOT NULL DEFAULT '[]',
                PRIMARY KEY(source_node_id, target_node_id, relation, evidence_document_id)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
        await EnsureOwnerColumnAsync(connection, cancellationToken);
        await EnsureRelationColumnsAsync(connection, cancellationToken);
        var ownerIndex = connection.CreateCommand();
        ownerIndex.CommandText = """
            CREATE INDEX IF NOT EXISTS ix_knowledge_documents_owner
                ON knowledge_documents(owner_agent_id);
            """;
        await ownerIndex.ExecuteNonQueryAsync(cancellationToken);

        var migrateLegacyEvidence = connection.CreateCommand();
        migrateLegacyEvidence.CommandText = """
            UPDATE knowledge_relations
            SET evidence_document_id = COALESCE(
                    (SELECT document_id
                     FROM knowledge_nodes
                     WHERE node_id = knowledge_relations.source_node_id),
                    ''
                )
            WHERE evidence_document_id = '';
            UPDATE knowledge_relations
            SET evidence_json = COALESCE(
                (SELECT evidence_json
                 FROM knowledge_nodes
                 WHERE node_id = knowledge_relations.target_node_id),
                '[]'
            )
            WHERE evidence_json = '[]';
            CREATE INDEX IF NOT EXISTS ix_knowledge_relations_source
                ON knowledge_relations(source_node_id);
            CREATE INDEX IF NOT EXISTS ix_knowledge_relations_target
                ON knowledge_relations(target_node_id);
            """;
        await migrateLegacyEvidence.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<bool> HasUnchangedDocumentAsync(
        string documentId,
        string contentHash,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM knowledge_documents
                WHERE document_id = $documentId AND content_hash = $contentHash
            );
            """;
        command.Parameters.AddWithValue("$documentId", documentId);
        command.Parameters.AddWithValue("$contentHash", contentHash);
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
    }

    public async Task ReplaceDocumentAsync(
        KnowledgeDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        await ReplaceDocumentCoreAsync(document, cancellationToken);
    }

    public async Task ReplaceAgentDocumentAsync(
        string agentId,
        KnowledgeDocument document,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(document);
        if (document.OwnerAgentId is not null && document.OwnerAgentId != agentId)
        {
            throw new ArgumentException("A knowledge document cannot be assigned to a different agent.", nameof(document));
        }
        await ReplaceDocumentCoreAsync(document with { OwnerAgentId = agentId }, cancellationToken);
    }

    private async Task ReplaceDocumentCoreAsync(
        KnowledgeDocument document,
        CancellationToken cancellationToken)
    {
        if (document.Nodes.Any(node => node.DocumentId != document.Id))
        {
            throw new ArgumentException("Every knowledge node must reference the supplied document.", nameof(document));
        }
        if (document.Relations.Any(relation => relation.EvidenceDocumentId != document.Id))
        {
            throw new ArgumentException(
                "Knowledge relations mined from a document must cite that document as their evidence source.",
                nameof(document));
        }
        if (document.Relations.Any(relation => !IsValidRelation(relation)))
        {
            throw new ArgumentException(
                "Knowledge relations require a valid type, confidence, extraction basis, and evidence.",
                nameof(document));
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var existingOwner = connection.CreateCommand();
        existingOwner.Transaction = transaction;
        existingOwner.CommandText = "SELECT owner_agent_id FROM knowledge_documents WHERE document_id = $id;";
        existingOwner.Parameters.AddWithValue("$id", document.Id);
        var ownerResult = await existingOwner.ExecuteScalarAsync(cancellationToken);
        if (ownerResult is string existingOwnerId &&
            !string.Equals(existingOwnerId, document.OwnerAgentId, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Knowledge document '{document.Id}' is owned by a different scope and cannot be reassigned.");
        }
        if (ownerResult is DBNull && document.OwnerAgentId is not null)
        {
            throw new InvalidOperationException(
                $"Knowledge document '{document.Id}' cannot be changed from shared to agent-owned knowledge.");
        }

        var saveDocument = connection.CreateCommand();
        saveDocument.Transaction = transaction;
        saveDocument.CommandText = """
            INSERT INTO knowledge_documents(
                document_id, root_id, relative_path, file_name, content_hash, language, mined_at, owner_agent_id
            ) VALUES ($id, $root, $path, $fileName, $hash, $language, $minedAt, $ownerAgentId)
            ON CONFLICT(document_id) DO UPDATE SET
                root_id = excluded.root_id,
                relative_path = excluded.relative_path,
                file_name = excluded.file_name,
                content_hash = excluded.content_hash,
                language = excluded.language,
                mined_at = excluded.mined_at,
                owner_agent_id = excluded.owner_agent_id;
            """;
        saveDocument.Parameters.AddWithValue("$id", document.Id);
        saveDocument.Parameters.AddWithValue("$root", document.RootId);
        saveDocument.Parameters.AddWithValue("$path", document.RelativePath);
        saveDocument.Parameters.AddWithValue("$fileName", document.FileName);
        saveDocument.Parameters.AddWithValue("$hash", document.ContentHash);
        saveDocument.Parameters.AddWithValue("$language", document.Language);
        saveDocument.Parameters.AddWithValue("$minedAt", document.MinedAt.ToUniversalTime().ToString("O"));
        saveDocument.Parameters.AddWithValue("$ownerAgentId", (object?)document.OwnerAgentId ?? DBNull.Value);
        await saveDocument.ExecuteNonQueryAsync(cancellationToken);

        var deleteRelations = connection.CreateCommand();
        deleteRelations.Transaction = transaction;
        deleteRelations.CommandText = "DELETE FROM knowledge_relations WHERE evidence_document_id = $documentId;";
        deleteRelations.Parameters.AddWithValue("$documentId", document.Id);
        await deleteRelations.ExecuteNonQueryAsync(cancellationToken);

        var existingNodeIds = new List<string>();
        var findExistingNodes = connection.CreateCommand();
        findExistingNodes.Transaction = transaction;
        findExistingNodes.CommandText = "SELECT node_id FROM knowledge_nodes WHERE document_id = $documentId;";
        findExistingNodes.Parameters.AddWithValue("$documentId", document.Id);
        await using (var reader = await findExistingNodes.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                existingNodeIds.Add(reader.GetString(0));
            }
        }

        var retainedNodeIds = document.Nodes.Select(node => node.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var obsoleteNodeId in existingNodeIds.Where(nodeId => !retainedNodeIds.Contains(nodeId)))
        {
            var deleteNode = connection.CreateCommand();
            deleteNode.Transaction = transaction;
            deleteNode.CommandText = "DELETE FROM knowledge_nodes WHERE node_id = $nodeId;";
            deleteNode.Parameters.AddWithValue("$nodeId", obsoleteNodeId);
            await deleteNode.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var node in document.Nodes)
        {
            var insertNode = connection.CreateCommand();
            insertNode.Transaction = transaction;
            insertNode.CommandText = """
                INSERT INTO knowledge_nodes(
                    node_id, document_id, category, title, summary, confidence, extraction_basis, evidence_json
                ) VALUES ($id, $documentId, $category, $title, $summary, $confidence, $basis, $evidence)
                ON CONFLICT(node_id) DO UPDATE SET
                    document_id = excluded.document_id,
                    category = excluded.category,
                    title = excluded.title,
                    summary = excluded.summary,
                    confidence = excluded.confidence,
                    extraction_basis = excluded.extraction_basis,
                    evidence_json = excluded.evidence_json;
                """;
            insertNode.Parameters.AddWithValue("$id", node.Id);
            insertNode.Parameters.AddWithValue("$documentId", node.DocumentId);
            insertNode.Parameters.AddWithValue("$category", node.Category.ToString());
            insertNode.Parameters.AddWithValue("$title", node.Title);
            insertNode.Parameters.AddWithValue("$summary", node.Summary);
            insertNode.Parameters.AddWithValue("$confidence", node.Confidence);
            insertNode.Parameters.AddWithValue("$basis", node.Basis.ToString());
            insertNode.Parameters.AddWithValue("$evidence", JsonSerializer.Serialize(node.Evidence, JsonOptions));
            await insertNode.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var relation in document.Relations)
        {
            var insertRelation = connection.CreateCommand();
            insertRelation.Transaction = transaction;
            insertRelation.CommandText = """
                INSERT INTO knowledge_relations(
                    source_node_id, target_node_id, evidence_document_id, relation,
                    confidence, extraction_basis, evidence_json
                ) VALUES ($source, $target, $evidenceDocumentId, $relation, $confidence, $basis, $evidence)
                ON CONFLICT(source_node_id, target_node_id, relation, evidence_document_id)
                DO UPDATE SET
                    confidence = excluded.confidence,
                    extraction_basis = excluded.extraction_basis,
                    evidence_json = excluded.evidence_json;
                """;
            insertRelation.Parameters.AddWithValue("$source", relation.SourceNodeId);
            insertRelation.Parameters.AddWithValue("$target", relation.TargetNodeId);
            insertRelation.Parameters.AddWithValue("$evidenceDocumentId", relation.EvidenceDocumentId);
            insertRelation.Parameters.AddWithValue("$relation", relation.Type.ToString());
            insertRelation.Parameters.AddWithValue("$confidence", relation.Confidence);
            insertRelation.Parameters.AddWithValue("$basis", relation.Basis.ToString());
            insertRelation.Parameters.AddWithValue("$evidence", JsonSerializer.Serialize(relation.Evidence, JsonOptions));
            await insertRelation.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
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

        var search = string.IsNullOrWhiteSpace(query)
            ? null
            : $"%{EscapeLike(query.Trim())}%";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT node_id, document_id, category, title, summary, confidence,
                   extraction_basis, evidence_json
            FROM knowledge_nodes
            WHERE ($category IS NULL OR category = $category)
              AND ($query IS NULL OR title LIKE $query ESCAPE '\' OR summary LIKE $query ESCAPE '\')
            ORDER BY category, title, node_id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$category", (object?)category?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$query", (object?)search ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        var nodes = new List<KnowledgeNode>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            nodes.Add(new KnowledgeNode(
                reader.GetString(0),
                reader.GetString(1),
                Enum.Parse<KnowledgeCategory>(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                Convert.ToDecimal(reader.GetDouble(5), CultureInfo.InvariantCulture),
                Enum.Parse<ExtractionBasis>(reader.GetString(6)),
                JsonSerializer.Deserialize<List<KnowledgeEvidence>>(reader.GetString(7), JsonOptions)
                    ?? throw new InvalidDataException("Stored knowledge evidence JSON is empty or invalid.")));
        }
        return nodes;
    }

    public Task<IReadOnlyList<KnowledgeNode>> SearchLayerAsync(
        string? query,
        KnowledgeLayer layer,
        int limit = 100,
        CancellationToken cancellationToken = default) =>
        SearchLayerCoreAsync(query, layer, limit, agentId: null, cancellationToken: cancellationToken);

    public Task<IReadOnlyList<KnowledgeNode>> SearchLayerForAgentAsync(
        string agentId,
        string? query,
        KnowledgeLayer layer,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return SearchLayerCoreAsync(query, layer, limit, agentId, cancellationToken);
    }

    private async Task<IReadOnlyList<KnowledgeNode>> SearchLayerCoreAsync(
        string? query,
        KnowledgeLayer layer,
        int limit,
        string? agentId,
        CancellationToken cancellationToken)
    {
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 1,000.");
        }

        var categories = KnowledgeLayers.GetCategories(layer);
        var categoryParameters = categories
            .Select((_, index) => $"$category{index}")
            .ToArray();
        var search = string.IsNullOrWhiteSpace(query)
            ? null
            : $"%{EscapeLike(query.Trim())}%";
        var orderBy = agentId is not null && layer == KnowledgeLayer.Experience
            ? "knowledge_documents.mined_at DESC, knowledge_nodes.node_id DESC"
            : "knowledge_nodes.category, knowledge_nodes.title, knowledge_nodes.node_id";
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT knowledge_nodes.node_id, knowledge_nodes.document_id,
                   knowledge_nodes.category, knowledge_nodes.title, knowledge_nodes.summary,
                   knowledge_nodes.confidence, knowledge_nodes.extraction_basis,
                   knowledge_nodes.evidence_json
            FROM knowledge_nodes
            JOIN knowledge_documents ON knowledge_documents.document_id = knowledge_nodes.document_id
            WHERE knowledge_nodes.category IN ({string.Join(", ", categoryParameters)})
              AND ($query IS NULL OR knowledge_nodes.title LIKE $query ESCAPE '\' OR
                   knowledge_nodes.summary LIKE $query ESCAPE '\')
              AND ($agentId IS NULL OR owner_agent_id IS NULL OR owner_agent_id = $agentId)
            ORDER BY {orderBy}
            LIMIT $limit;
            """;
        for (var index = 0; index < categories.Count; index++)
        {
            command.Parameters.AddWithValue(categoryParameters[index], categories[index].ToString());
        }
        command.Parameters.AddWithValue("$query", (object?)search ?? DBNull.Value);
        command.Parameters.AddWithValue("$agentId", (object?)agentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        var nodes = new List<KnowledgeNode>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            nodes.Add(new KnowledgeNode(
                reader.GetString(0),
                reader.GetString(1),
                Enum.Parse<KnowledgeCategory>(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                Convert.ToDecimal(reader.GetDouble(5), CultureInfo.InvariantCulture),
                Enum.Parse<ExtractionBasis>(reader.GetString(6)),
                JsonSerializer.Deserialize<List<KnowledgeEvidence>>(reader.GetString(7), JsonOptions)
                    ?? throw new InvalidDataException("Stored knowledge evidence JSON is empty or invalid.")));
        }
        return nodes;
    }

    public async Task<KnowledgeNode?> GetNodeAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT node_id, document_id, category, title, summary, confidence,
                   extraction_basis, evidence_json
            FROM knowledge_nodes
            WHERE node_id = $nodeId;
            """;
        command.Parameters.AddWithValue("$nodeId", nodeId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }
        return new KnowledgeNode(
            reader.GetString(0),
            reader.GetString(1),
            Enum.Parse<KnowledgeCategory>(reader.GetString(2)),
            reader.GetString(3),
            reader.GetString(4),
            Convert.ToDecimal(reader.GetDouble(5), CultureInfo.InvariantCulture),
            Enum.Parse<ExtractionBasis>(reader.GetString(6)),
            JsonSerializer.Deserialize<List<KnowledgeEvidence>>(reader.GetString(7), JsonOptions)
                ?? throw new InvalidDataException("Stored knowledge evidence JSON is empty or invalid."));
    }

    public async Task<IReadOnlyList<KnowledgeDocumentSummary>> GetDocumentsAsync(
        string? rootId = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 1,000.");
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT documents.document_id, documents.root_id, documents.relative_path,
                   documents.file_name, documents.content_hash, documents.language,
                   documents.mined_at, COUNT(nodes.node_id)
            FROM knowledge_documents AS documents
            LEFT JOIN knowledge_nodes AS nodes ON nodes.document_id = documents.document_id
            WHERE $rootId IS NULL OR documents.root_id = $rootId
            GROUP BY documents.document_id
            ORDER BY documents.root_id, documents.relative_path
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$rootId", (object?)rootId ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        var documents = new List<KnowledgeDocumentSummary>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            documents.Add(new KnowledgeDocumentSummary(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetString(5),
                DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
                reader.GetInt32(7)));
        }
        return documents;
    }

    public async Task<IReadOnlyList<KnowledgeCategorySourceCount>> GetCategorySourceCountsAsync(
        KnowledgeCategory category,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT nodes.title, documents.root_id, COUNT(DISTINCT documents.document_id)
            FROM knowledge_nodes AS nodes
            JOIN knowledge_documents AS documents ON documents.document_id = nodes.document_id
            WHERE nodes.category = $category
            GROUP BY nodes.title, documents.root_id
            ORDER BY nodes.title, documents.root_id;
            """;
        command.Parameters.AddWithValue("$category", category.ToString());

        var counts = new List<KnowledgeCategorySourceCount>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            counts.Add(new KnowledgeCategorySourceCount(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2)));
        }
        return counts;
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> GetAllDocumentsAsync(
        CancellationToken cancellationToken = default)
    {
        return await GetDocumentsByOwnerAsync(null, cancellationToken);
    }

    public async Task<IReadOnlyList<KnowledgeDocument>> GetAgentDocumentsAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return await GetDocumentsByOwnerAsync(agentId, cancellationToken);
    }

    private async Task<IReadOnlyList<KnowledgeDocument>> GetDocumentsByOwnerAsync(
        string? ownerAgentId,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT document_id, root_id, relative_path, file_name, content_hash, language, mined_at,
                   owner_agent_id
            FROM knowledge_documents
            WHERE $ownerAgentId IS NULL OR owner_agent_id = $ownerAgentId
            ORDER BY root_id, relative_path;
            """;
        command.Parameters.AddWithValue("$ownerAgentId", (object?)ownerAgentId ?? DBNull.Value);

        var metadata = new List<(
            string Id,
            string RootId,
            string RelativePath,
            string FileName,
            string ContentHash,
            string Language,
            DateTimeOffset MinedAt,
            string? OwnerAgentId)>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                metadata.Add((
                    reader.GetString(0),
                    reader.GetString(1),
                    reader.GetString(2),
                    reader.GetString(3),
                    reader.GetString(4),
                    reader.GetString(5),
                    DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
                    reader.IsDBNull(7) ? null : reader.GetString(7)));
            }
        }

        var documents = new List<KnowledgeDocument>(metadata.Count);
        foreach (var document in metadata)
        {
            documents.Add(new KnowledgeDocument(
                document.Id,
                document.RootId,
                document.RelativePath,
                document.FileName,
                document.ContentHash,
                document.Language,
                document.MinedAt,
                await GetDocumentNodesAsync(connection, document.Id, cancellationToken),
                await GetDocumentRelationsAsync(connection, document.Id, cancellationToken))
            {
                OwnerAgentId = document.OwnerAgentId
            });
        }
        return documents;
    }

    public async Task ImportDocumentsAsync(
        IReadOnlyList<KnowledgeDocument> documents,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(documents);
        if (documents.Count == 0)
        {
            throw new ArgumentException("At least one knowledge document is required.", nameof(documents));
        }

        var documentIds = new HashSet<string>(StringComparer.Ordinal);
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var document in documents)
        {
            if (string.IsNullOrWhiteSpace(document.Id) ||
                string.IsNullOrWhiteSpace(document.RootId) ||
                string.IsNullOrWhiteSpace(document.RelativePath) ||
                string.IsNullOrWhiteSpace(document.FileName) ||
                string.IsNullOrWhiteSpace(document.ContentHash) ||
                string.IsNullOrWhiteSpace(document.Language) ||
                document.OwnerAgentId is not null ||
                !documentIds.Add(document.Id))
            {
                throw new ArgumentException("The knowledge package contains invalid or duplicate document metadata.", nameof(documents));
            }
            foreach (var node in document.Nodes)
            {
                if (string.IsNullOrWhiteSpace(node.Id) ||
                    node.DocumentId != document.Id ||
                    string.IsNullOrWhiteSpace(node.Title) ||
                    node.Confidence is < 0 or > 1 ||
                    !Enum.IsDefined(node.Category) ||
                    !Enum.IsDefined(node.Basis) ||
                    node.Evidence is null ||
                    !nodeIds.Add(node.Id))
                {
                    throw new ArgumentException("The knowledge package contains an invalid or duplicate node.", nameof(documents));
                }
            }
            foreach (var relation in document.Relations)
            {
                if (relation.EvidenceDocumentId != document.Id ||
                    !IsValidRelation(relation))
                {
                    throw new ArgumentException("The knowledge package contains an invalid relation.", nameof(documents));
                }
            }
        }
        if (documents.Select(document => (document.RootId, document.RelativePath))
            .Distinct()
            .Count() != documents.Count)
        {
            throw new ArgumentException("The knowledge package contains duplicate document paths.", nameof(documents));
        }

        foreach (var relation in documents.SelectMany(document => document.Relations))
        {
            if (!nodeIds.Contains(relation.SourceNodeId) || !nodeIds.Contains(relation.TargetNodeId))
            {
                throw new ArgumentException("Every imported relation must reference nodes included in the package.", nameof(documents));
            }
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        foreach (var document in documents)
        {
            var exists = connection.CreateCommand();
            exists.Transaction = transaction;
            exists.CommandText = """
                SELECT EXISTS(
                    SELECT 1 FROM knowledge_documents
                    WHERE document_id = $id OR (root_id = $root AND relative_path = $path)
                );
                """;
            exists.Parameters.AddWithValue("$id", document.Id);
            exists.Parameters.AddWithValue("$root", document.RootId);
            exists.Parameters.AddWithValue("$path", document.RelativePath);
            if (Convert.ToInt32(await exists.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) != 0)
            {
                throw new InvalidOperationException(
                    $"Knowledge document '{document.Id}' already exists. Import does not overwrite existing knowledge.");
            }
        }

        foreach (var document in documents)
        {
            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO knowledge_documents(
                    document_id, root_id, relative_path, file_name, content_hash, language, mined_at
                ) VALUES ($id, $root, $path, $fileName, $hash, $language, $minedAt);
                """;
            insert.Parameters.AddWithValue("$id", document.Id);
            insert.Parameters.AddWithValue("$root", document.RootId);
            insert.Parameters.AddWithValue("$path", document.RelativePath);
            insert.Parameters.AddWithValue("$fileName", document.FileName);
            insert.Parameters.AddWithValue("$hash", document.ContentHash);
            insert.Parameters.AddWithValue("$language", document.Language);
            insert.Parameters.AddWithValue("$minedAt", document.MinedAt.ToUniversalTime().ToString("O"));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var node in documents.SelectMany(document => document.Nodes))
        {
            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO knowledge_nodes(
                    node_id, document_id, category, title, summary, confidence, extraction_basis, evidence_json
                ) VALUES ($id, $documentId, $category, $title, $summary, $confidence, $basis, $evidence);
                """;
            insert.Parameters.AddWithValue("$id", node.Id);
            insert.Parameters.AddWithValue("$documentId", node.DocumentId);
            insert.Parameters.AddWithValue("$category", node.Category.ToString());
            insert.Parameters.AddWithValue("$title", node.Title);
            insert.Parameters.AddWithValue("$summary", node.Summary);
            insert.Parameters.AddWithValue("$confidence", node.Confidence);
            insert.Parameters.AddWithValue("$basis", node.Basis.ToString());
            insert.Parameters.AddWithValue("$evidence", JsonSerializer.Serialize(node.Evidence, JsonOptions));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        foreach (var relation in documents.SelectMany(document => document.Relations))
        {
            var insert = connection.CreateCommand();
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO knowledge_relations(
                    source_node_id, target_node_id, evidence_document_id, relation,
                    confidence, extraction_basis, evidence_json
                ) VALUES ($source, $target, $evidenceDocumentId, $relation, $confidence, $basis, $evidence);
                """;
            insert.Parameters.AddWithValue("$source", relation.SourceNodeId);
            insert.Parameters.AddWithValue("$target", relation.TargetNodeId);
            insert.Parameters.AddWithValue("$evidenceDocumentId", relation.EvidenceDocumentId);
            insert.Parameters.AddWithValue("$relation", relation.Type.ToString());
            insert.Parameters.AddWithValue("$confidence", relation.Confidence);
            insert.Parameters.AddWithValue("$basis", relation.Basis.ToString());
            insert.Parameters.AddWithValue("$evidence", JsonSerializer.Serialize(relation.Evidence, JsonOptions));
            await insert.ExecuteNonQueryAsync(cancellationToken);
        }
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<IReadOnlyList<KnowledgeNode>> GetDocumentNodesAsync(
        SqliteConnection connection,
        string documentId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT node_id, category, title, summary, confidence, extraction_basis, evidence_json
            FROM knowledge_nodes
            WHERE document_id = $documentId
            ORDER BY category, title, node_id;
            """;
        command.Parameters.AddWithValue("$documentId", documentId);

        var nodes = new List<KnowledgeNode>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            nodes.Add(new KnowledgeNode(
                reader.GetString(0),
                documentId,
                Enum.Parse<KnowledgeCategory>(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                Convert.ToDecimal(reader.GetDouble(4), CultureInfo.InvariantCulture),
                Enum.Parse<ExtractionBasis>(reader.GetString(5)),
                JsonSerializer.Deserialize<List<KnowledgeEvidence>>(reader.GetString(6), JsonOptions)
                    ?? throw new InvalidDataException("Stored knowledge evidence JSON is empty or invalid.")));
        }
        return nodes;
    }

    private static async Task<IReadOnlyList<KnowledgeRelation>> GetDocumentRelationsAsync(
        SqliteConnection connection,
        string documentId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT source_node_id, target_node_id, relation, confidence, extraction_basis, evidence_json
            FROM knowledge_relations
            WHERE evidence_document_id = $documentId
            ORDER BY relation, source_node_id, target_node_id;
            """;
        command.Parameters.AddWithValue("$documentId", documentId);

        var relations = new List<KnowledgeRelation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            relations.Add(new KnowledgeRelation(
                documentId,
                reader.GetString(0),
                reader.GetString(1),
                Enum.Parse<KnowledgeRelationType>(reader.GetString(2)),
                Convert.ToDecimal(reader.GetDouble(3), CultureInfo.InvariantCulture),
                Enum.Parse<ExtractionBasis>(reader.GetString(4)),
                JsonSerializer.Deserialize<List<KnowledgeEvidence>>(reader.GetString(5), JsonOptions)
                    ?? throw new InvalidDataException("Stored knowledge relation evidence JSON is empty or invalid.")));
        }
        return relations;
    }

    public async Task<IReadOnlyList<KnowledgeRelation>> GetRelationsAsync(
        string nodeId,
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
        SELECT evidence_document_id, source_node_id, target_node_id, relation,
               confidence, extraction_basis, evidence_json
        FROM knowledge_relations
        WHERE source_node_id = $nodeId OR target_node_id = $nodeId
        ORDER BY relation, evidence_document_id, source_node_id, target_node_id;
        """;
        command.Parameters.AddWithValue("$nodeId", nodeId);

        var relations = new List<KnowledgeRelation>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            relations.Add(new KnowledgeRelation(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                Enum.Parse<KnowledgeRelationType>(reader.GetString(3)),
                Convert.ToDecimal(reader.GetDouble(4), CultureInfo.InvariantCulture),
                Enum.Parse<ExtractionBasis>(reader.GetString(5)),
                JsonSerializer.Deserialize<List<KnowledgeEvidence>>(reader.GetString(6), JsonOptions)
                    ?? throw new InvalidDataException("Stored knowledge relation evidence JSON is empty or invalid.")));
        }
        return relations;
    }

    public async Task AddRelationAsync(
        KnowledgeRelation relation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(relation);
        if (!IsValidRelation(relation))
        {
            throw new ArgumentException(
                "Knowledge relations require a valid type, confidence, extraction basis, and evidence.",
                nameof(relation));
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO knowledge_relations(
                source_node_id, target_node_id, evidence_document_id, relation,
                confidence, extraction_basis, evidence_json
            ) VALUES ($source, $target, $evidenceDocumentId, $relation, $confidence, $basis, $evidence)
            ON CONFLICT(source_node_id, target_node_id, relation, evidence_document_id)
            DO UPDATE SET
                confidence = excluded.confidence,
                extraction_basis = excluded.extraction_basis,
                evidence_json = excluded.evidence_json;
            """;
        command.Parameters.AddWithValue("$source", relation.SourceNodeId);
        command.Parameters.AddWithValue("$target", relation.TargetNodeId);
        command.Parameters.AddWithValue("$evidenceDocumentId", relation.EvidenceDocumentId);
        command.Parameters.AddWithValue("$relation", relation.Type.ToString());
        command.Parameters.AddWithValue("$confidence", relation.Confidence);
        command.Parameters.AddWithValue("$basis", relation.Basis.ToString());
        command.Parameters.AddWithValue("$evidence", JsonSerializer.Serialize(relation.Evidence, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

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

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            WITH RECURSIVE traversal(node_id, depth, path_nodes, path_edges) AS (
                SELECT $rootNodeId, 0, json_array($rootNodeId), json_array()
                UNION ALL
                SELECT
                    relation.target_node_id,
                    traversal.depth + 1,
                    json_insert(traversal.path_nodes, '$[#]', relation.target_node_id),
                    json_insert(
                        traversal.path_edges,
                        '$[#]',
                        json_object(
                            'evidenceDocumentId', relation.evidence_document_id,
                            'sourceNodeId', relation.source_node_id,
                            'targetNodeId', relation.target_node_id,
                            'type', relation.relation,
                            'confidence', relation.confidence,
                            'basis', relation.extraction_basis,
                            'evidence', json(relation.evidence_json)
                        )
                    )
                FROM traversal
                JOIN knowledge_relations AS relation
                    ON relation.source_node_id = traversal.node_id
                WHERE traversal.depth < $maxDepth
                  AND NOT EXISTS (
                      SELECT 1
                      FROM json_each(traversal.path_nodes) AS visited
                      WHERE visited.value = relation.target_node_id
                  )
                LIMIT $walkLimit
            )
            SELECT nodes.node_id, nodes.document_id, nodes.category, nodes.title, nodes.summary,
                   nodes.confidence, nodes.extraction_basis, nodes.evidence_json,
                   traversal.depth, traversal.path_edges
            FROM traversal
            JOIN knowledge_nodes AS nodes ON nodes.node_id = traversal.node_id
            ORDER BY traversal.depth, nodes.title, nodes.node_id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$rootNodeId", rootNodeId);
        command.Parameters.AddWithValue("$maxDepth", maxDepth);
        command.Parameters.AddWithValue("$walkLimit", Math.Min(10_000, limit * maxDepth + 1));
        command.Parameters.AddWithValue("$limit", limit);

        var results = new List<KnowledgeGraphResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var node = new KnowledgeNode(
                reader.GetString(0),
                reader.GetString(1),
                Enum.Parse<KnowledgeCategory>(reader.GetString(2)),
                reader.GetString(3),
                reader.GetString(4),
                Convert.ToDecimal(reader.GetDouble(5), CultureInfo.InvariantCulture),
                Enum.Parse<ExtractionBasis>(reader.GetString(6)),
                JsonSerializer.Deserialize<List<KnowledgeEvidence>>(reader.GetString(7), JsonOptions)
                    ?? throw new InvalidDataException("Stored knowledge evidence JSON is empty or invalid."));
            var path = JsonSerializer.Deserialize<List<KnowledgeRelation>>(reader.GetString(9), JsonOptions)
                ?? throw new InvalidDataException("Stored knowledge graph path JSON is empty or invalid.");
            results.Add(new KnowledgeGraphResult(node, reader.GetInt32(8), path));
        }
        return results;
    }

    private static async Task EnsureRelationColumnsAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var inspect = connection.CreateCommand();
        inspect.CommandText = "PRAGMA table_info(knowledge_relations);";
        var existingColumns = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        await using (var reader = await inspect.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                existingColumns.Add(reader.GetString(1));
            }
        }

        var migrations = new[]
        {
            (Name: "confidence", Sql: "ALTER TABLE knowledge_relations ADD COLUMN confidence REAL NOT NULL DEFAULT 0.5 CHECK(confidence >= 0 AND confidence <= 1);"),
            (Name: "extraction_basis", Sql: "ALTER TABLE knowledge_relations ADD COLUMN extraction_basis TEXT NOT NULL DEFAULT 'StaticStructure';"),
            (Name: "evidence_json", Sql: "ALTER TABLE knowledge_relations ADD COLUMN evidence_json TEXT NOT NULL DEFAULT '[]';")
        };
        if (!existingColumns.Contains("evidence_document_id"))
        {
            await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
            var rebuild = connection.CreateCommand();
            rebuild.Transaction = transaction;
            rebuild.CommandText = """
                ALTER TABLE knowledge_relations RENAME TO knowledge_relations_legacy;
                CREATE TABLE knowledge_relations (
                    source_node_id TEXT NOT NULL REFERENCES knowledge_nodes(node_id) ON DELETE CASCADE,
                    target_node_id TEXT NOT NULL REFERENCES knowledge_nodes(node_id) ON DELETE CASCADE,
                    evidence_document_id TEXT NOT NULL REFERENCES knowledge_documents(document_id) ON DELETE CASCADE,
                    relation TEXT NOT NULL,
                    confidence REAL NOT NULL DEFAULT 0.5 CHECK(confidence >= 0 AND confidence <= 1),
                    extraction_basis TEXT NOT NULL DEFAULT 'StaticStructure',
                    evidence_json TEXT NOT NULL DEFAULT '[]',
                    PRIMARY KEY(source_node_id, target_node_id, relation, evidence_document_id)
                );
                INSERT INTO knowledge_relations(
                    source_node_id, target_node_id, evidence_document_id, relation,
                    confidence, extraction_basis, evidence_json
                )
                SELECT legacy.source_node_id,
                       legacy.target_node_id,
                       source_node.document_id,
                       legacy.relation,
                       0.5,
                       'StaticStructure',
                       COALESCE(target_node.evidence_json, '[]')
                FROM knowledge_relations_legacy AS legacy
                JOIN knowledge_nodes AS source_node ON source_node.node_id = legacy.source_node_id
                LEFT JOIN knowledge_nodes AS target_node ON target_node.node_id = legacy.target_node_id;
                DROP TABLE knowledge_relations_legacy;
                """;
            await rebuild.ExecuteNonQueryAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return;
        }

        foreach (var migration in migrations.Where(migration => !existingColumns.Contains(migration.Name)))
        {
            var command = connection.CreateCommand();
            command.CommandText = migration.Sql;
            await command.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static async Task EnsureOwnerColumnAsync(
        SqliteConnection connection,
        CancellationToken cancellationToken)
    {
        var inspect = connection.CreateCommand();
        inspect.CommandText = "PRAGMA table_info(knowledge_documents);";
        var hasOwnerColumn = false;
        await using (var reader = await inspect.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                if (reader.GetString(1).Equals("owner_agent_id", StringComparison.OrdinalIgnoreCase))
                {
                    hasOwnerColumn = true;
                    break;
                }
            }
        }
        if (!hasOwnerColumn)
        {
            var addColumn = connection.CreateCommand();
            addColumn.CommandText = "ALTER TABLE knowledge_documents ADD COLUMN owner_agent_id TEXT NULL;";
            await addColumn.ExecuteNonQueryAsync(cancellationToken);
        }
    }

    private static bool IsValidRelation(KnowledgeRelation relation) =>
        !string.IsNullOrWhiteSpace(relation.EvidenceDocumentId) &&
        !string.IsNullOrWhiteSpace(relation.SourceNodeId) &&
        !string.IsNullOrWhiteSpace(relation.TargetNodeId) &&
        relation.Confidence is >= 0 and <= 1 &&
        Enum.IsDefined(relation.Type) &&
        Enum.IsDefined(relation.Basis) &&
        relation.Evidence is { Count: > 0 };

    private static string EscapeLike(string value) =>
        value.Replace("\\", "\\\\", StringComparison.Ordinal)
            .Replace("%", "\\%", StringComparison.Ordinal)
            .Replace("_", "\\_", StringComparison.Ordinal);
}
