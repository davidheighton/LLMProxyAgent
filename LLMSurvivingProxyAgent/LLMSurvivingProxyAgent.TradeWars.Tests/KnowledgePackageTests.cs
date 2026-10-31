using System.IO.Compression;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using LLMSurvivingProxyAgent.Agent;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Goals;
using LLMSurvivingProxyAgent.Knowledge;
using LLMSurvivingProxyAgent.Macros;
using LLMSurvivingProxyAgent.Packaging;
using LLMSurvivingProxyAgent.Reactive;
using LLMSurvivingProxyAgent.Runtime;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class KnowledgePackageTests
{
    [Fact]
    public async Task SeedPackageRoundTripsCrossDocumentRelationsAndRejectsOverwrite()
    {
        using var directory = new TemporaryDirectory();
        var source = new SqliteKnowledgeStore(ConnectionString(directory.File("source.db")));
        var target = new SqliteKnowledgeStore(ConnectionString(directory.File("target.db")));
        var sourceRuntime = new SqliteKnowledgeStore(ConnectionString(directory.File("source-runtime.db")));
        var targetRuntime = new SqliteKnowledgeStore(ConnectionString(directory.File("target-runtime.db")));
        await source.InitializeAsync();
        await target.InitializeAsync();
        await sourceRuntime.InitializeAsync();
        await targetRuntime.InitializeAsync();

        var documents = CreateDocuments();
        await source.ImportDocumentsAsync(documents);
        var exporter = new SeedKnowledgePackageService(source, sourceRuntime);
        var package = await exporter.ExportAsync("offline-test-seed");
        var importer = new SeedKnowledgePackageService(target, targetRuntime);

        var report = await importer.ImportAsync(new MemoryStream(package));
        var restored = await target.GetAllDocumentsAsync();
        Assert.Equal(2, report.DocumentsImported);
        Assert.Equal(2, report.NodesImported);
        Assert.Single(restored[0].Relations);
        Assert.Equal("node-b", restored[0].Relations[0].TargetNodeId);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            importer.ImportAsync(new MemoryStream(package)));
        Assert.Equal(2, (await target.GetAllDocumentsAsync()).Count);
    }

    [Fact]
    public async Task SeedPackageRoundTripsDocumentationDecisionContextWithSchemaThree()
    {
        using var directory = new TemporaryDirectory();
        var source = new SqliteKnowledgeStore(ConnectionString(directory.File("source.db")));
        var sourceRuntime = new SqliteKnowledgeStore(ConnectionString(directory.File("source-runtime.db")));
        var target = new SqliteKnowledgeStore(ConnectionString(directory.File("target.db")));
        var targetRuntime = new SqliteKnowledgeStore(ConnectionString(directory.File("target-runtime.db")));
        await source.InitializeAsync();
        await sourceRuntime.InitializeAsync();
        await target.InitializeAsync();
        await targetRuntime.InitializeAsync();

        var evidence = new KnowledgeEvidence(2, "benefits", "A guide says this tactic may increase income.")
        {
            Details = new KnowledgeEvidenceDetails
            {
                Message = "need figs",
                MessageTemplate = "'Need figs sector {dynamic}*",
                Channel = "SubSpace",
                Intent = "explicit resource request",
                ExpectedResponse = "fighters",
                Steps = ["Identify request", "Respond with the named resource"]
            }
        };
        var node = new KnowledgeNode(
            "decision-context",
            "guide-doc",
            KnowledgeCategory.DecisionContext,
            "Decision context: Benefits",
            "Documentation-derived context.",
            0.52m,
            ExtractionBasis.ExplicitText,
            [evidence]);
        var document = new KnowledgeDocument(
            "guide-doc",
            "documentation",
            "guides/tactics.md",
            "tactics.md",
            "guide-hash",
            "Markdown",
            DateTimeOffset.UtcNow,
            [node],
            []);
        await source.ImportDocumentsAsync([document]);
        var package = await new SeedKnowledgePackageService(source, sourceRuntime)
            .ExportVersionedAsync("test", "1.8.0", []);
        using var manifest = JsonDocument.Parse(ReadEntry(package, "manifest.json"));

        Assert.Equal(3, manifest.RootElement.GetProperty("schemaVersion").GetInt32());
        var report = await new SeedKnowledgePackageService(target, targetRuntime)
            .ImportAsync(new MemoryStream(package));
        var imported = Assert.Single(await target.GetAllDocumentsAsync());

        Assert.Equal(1, report.NodesImported);
        Assert.Equal(KnowledgeCategory.DecisionContext, Assert.Single(imported.Nodes).Category);
        Assert.Equal("benefits", Assert.Single(imported.Nodes[0].Evidence).Signal);
        var importedDetails = Assert.Single(imported.Nodes[0].Evidence).Details;
        Assert.Equal(evidence.Details?.Message, importedDetails?.Message);
        Assert.Equal(evidence.Details?.MessageTemplate, importedDetails?.MessageTemplate);
        Assert.Equal(evidence.Details?.ExpectedResponse, importedDetails?.ExpectedResponse);
        Assert.Equal(evidence.Details?.Steps?.ToArray(), importedDetails?.Steps?.ToArray());
    }

    [Fact]
    public async Task SeedPackageStillImportsLegacySchemaVersionOne()
    {
        using var directory = new TemporaryDirectory();
        var source = new SqliteKnowledgeStore(ConnectionString(directory.File("source.db")));
        var runtime = new SqliteKnowledgeStore(ConnectionString(directory.File("runtime.db")));
        var target = new SqliteKnowledgeStore(ConnectionString(directory.File("target.db")));
        await source.InitializeAsync();
        await runtime.InitializeAsync();
        await target.InitializeAsync();
        await source.ImportDocumentsAsync(CreateDocuments());
        var package = await new SeedKnowledgePackageService(source, runtime).ExportAsync("test");
        var legacyPackage = RewriteEntry(package, "manifest.json", bytes =>
            System.Text.Encoding.UTF8.GetBytes(
                System.Text.Encoding.UTF8.GetString(bytes).Replace(
                    "\"schemaVersion\":3",
                    "\"schemaVersion\":1",
                    StringComparison.Ordinal)));

        var report = await new SeedKnowledgePackageService(target, runtime)
            .ImportAsync(new MemoryStream(legacyPackage));

        Assert.Equal(2, report.DocumentsImported);
    }

    [Fact]
    public async Task SeedPackageStillImportsLegacySchemaVersionTwo()
    {
        using var directory = new TemporaryDirectory();
        var source = new SqliteKnowledgeStore(ConnectionString(directory.File("source.db")));
        var runtime = new SqliteKnowledgeStore(ConnectionString(directory.File("runtime.db")));
        var target = new SqliteKnowledgeStore(ConnectionString(directory.File("target.db")));
        await source.InitializeAsync();
        await runtime.InitializeAsync();
        await target.InitializeAsync();
        await source.ImportDocumentsAsync(CreateDocuments());
        var package = await new SeedKnowledgePackageService(source, runtime).ExportAsync("test");
        var schemaTwoPackage = RewriteEntry(package, "manifest.json", bytes =>
            System.Text.Encoding.UTF8.GetBytes(
                System.Text.Encoding.UTF8.GetString(bytes).Replace(
                    "\"schemaVersion\":3",
                    "\"schemaVersion\":2",
                    StringComparison.Ordinal)));

        var report = await new SeedKnowledgePackageService(target, runtime)
            .ImportAsync(new MemoryStream(schemaTwoPackage));

        Assert.Equal(2, report.DocumentsImported);
    }

    [Fact]
    public async Task SeedPackageRejectsPayloadTamperingBeforeImport()
    {
        using var directory = new TemporaryDirectory();
        var seed = new SqliteKnowledgeStore(ConnectionString(directory.File("seed.db")));
        var runtime = new SqliteKnowledgeStore(ConnectionString(directory.File("runtime.db")));
        await seed.InitializeAsync();
        await runtime.InitializeAsync();
        await seed.ImportDocumentsAsync(CreateDocuments());
        var package = await new SeedKnowledgePackageService(seed, runtime).ExportAsync("test");
        var altered = RewriteEntry(package, "payload.json", bytes =>
        {
            bytes[0] ^= 0x01;
            return bytes;
        });
        var target = new SqliteKnowledgeStore(ConnectionString(directory.File("target.db")));
        await target.InitializeAsync();

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new SeedKnowledgePackageService(
                target,
                runtime).ImportAsync(new MemoryStream(altered)));
    }

    [Fact]
    public async Task SeedPackageRejectsUnsupportedSchemaVersion()
    {
        using var directory = new TemporaryDirectory();
        var seed = new SqliteKnowledgeStore(ConnectionString(directory.File("seed.db")));
        var runtime = new SqliteKnowledgeStore(ConnectionString(directory.File("runtime.db")));
        var target = new SqliteKnowledgeStore(ConnectionString(directory.File("target.db")));
        await seed.InitializeAsync();
        await runtime.InitializeAsync();
        await target.InitializeAsync();
        await seed.ImportDocumentsAsync(CreateDocuments());
        var package = await new SeedKnowledgePackageService(seed, runtime).ExportAsync("test");
        var altered = RewriteEntry(package, "manifest.json", bytes =>
            System.Text.Encoding.UTF8.GetBytes(
                System.Text.Encoding.UTF8.GetString(bytes).Replace(
                    "\"schemaVersion\":3",
                    "\"schemaVersion\":99",
                    StringComparison.Ordinal)));

        await Assert.ThrowsAsync<InvalidDataException>(() =>
            new SeedKnowledgePackageService(target, runtime).ImportAsync(new MemoryStream(altered)));
        Assert.Empty(await target.GetAllDocumentsAsync());
    }

    [Fact]
    public async Task CompositeKnowledgeStoreReadsBothStoresAndWritesOnlyRuntimeKnowledge()
    {
        using var directory = new TemporaryDirectory();
        var seed = new SqliteKnowledgeStore(ConnectionString(directory.File("seed.db")));
        var runtime = new SqliteKnowledgeStore(ConnectionString(directory.File("runtime.db")));
        await seed.InitializeAsync();
        await runtime.InitializeAsync();
        await seed.ImportDocumentsAsync(CreateDocuments());
        var combined = new CompositeKnowledgeStore(seed, runtime);
        var runtimeDocument = new KnowledgeDocument(
            "runtime-doc",
            "agent",
            "notes/learned.txt",
            "learned.txt",
            "runtime-hash",
            "text",
            DateTimeOffset.UtcNow,
            [
                new KnowledgeNode(
                    "runtime-node",
                    "runtime-doc",
                    KnowledgeCategory.StrategicGuidance,
                    "Learned",
                    "Agent-specific knowledge",
                    0.7m,
                    ExtractionBasis.ExplicitText,
                    [])
            ],
            []);

        await combined.ReplaceDocumentAsync(runtimeDocument);
        var result = await combined.TraverseAsync("node-a", maxDepth: 2);

        Assert.Equal("node-b", (await combined.GetNodeAsync("node-b"))?.Id);
        Assert.Equal(3, (await combined.GetAllDocumentsAsync()).Count);
        Assert.Equal(new[] { "node-a", "node-b" }, result.Select(item => item.Node.Id));
        Assert.DoesNotContain(
            (await seed.GetAllDocumentsAsync()).SelectMany(document => document.Nodes),
            node => node.Id == "runtime-node");
        Assert.Contains(
            (await runtime.GetAllDocumentsAsync()).SelectMany(document => document.Nodes),
            node => node.Id == "runtime-node");
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            combined.ImportDocumentsAsync([runtimeDocument]));
    }

    [Fact]
    public async Task AgentScopedLayerSearchDoesNotExposeAnotherAgentsExperience()
    {
        using var directory = new TemporaryDirectory();
        var seed = new SqliteKnowledgeStore(ConnectionString(directory.File("seed.db")));
        var runtime = new SqliteKnowledgeStore(ConnectionString(directory.File("runtime.db")));
        await seed.InitializeAsync();
        await runtime.InitializeAsync();
        var combined = new CompositeKnowledgeStore(seed, runtime);
        await combined.ReplaceAgentDocumentAsync(
            "meridian",
            CreateAgentExperienceDocument("meridian", "meridian-experience"));
        await combined.ReplaceAgentDocumentAsync(
            "atlas",
            CreateAgentExperienceDocument("atlas", "atlas-experience"));

        var meridianExperience = await combined.SearchLayerForAgentAsync(
            "meridian",
            null,
            KnowledgeLayer.Experience);
        var atlasExperience = await combined.SearchLayerForAgentAsync(
            "atlas",
            null,
            KnowledgeLayer.Experience);

        Assert.Equal("meridian-experience", Assert.Single(meridianExperience).Id);
        Assert.Equal("atlas-experience", Assert.Single(atlasExperience).Id);
    }

    [Fact]
    public void KnowledgeCategoriesMapToExactlyOneLayer()
    {
        var categories = Enum.GetValues<KnowledgeCategory>();
        var mappedCategories = Enum.GetValues<KnowledgeLayer>()
            .Where(layer => layer != KnowledgeLayer.Unknown)
            .SelectMany(KnowledgeLayers.GetCategories)
            .ToArray();

        Assert.Equal(categories.Length, mappedCategories.Length);
        Assert.Equal(categories.Order(), mappedCategories.Order());
        Assert.All(categories, category =>
            Assert.NotEqual(KnowledgeLayer.Unknown, KnowledgeLayers.GetLayer(category)));
    }

    [Fact]
    public async Task CompositeKnowledgeStoreSearchesWithinTheRequestedLayer()
    {
        using var directory = new TemporaryDirectory();
        var seed = new SqliteKnowledgeStore(ConnectionString(directory.File("seed.db")));
        var runtime = new SqliteKnowledgeStore(ConnectionString(directory.File("runtime.db")));
        await seed.InitializeAsync();
        await runtime.InitializeAsync();
        await seed.ImportDocumentsAsync(CreateDocuments());
        var combined = new CompositeKnowledgeStore(seed, runtime);

        var facts = await combined.SearchLayerAsync("First", KnowledgeLayer.Facts, limit: 10);
        var tactical = await combined.SearchLayerAsync("Second", KnowledgeLayer.Tactical, limit: 10);
        var unrelated = await combined.SearchLayerAsync("First", KnowledgeLayer.Communication, limit: 10);

        Assert.Equal("node-a", Assert.Single(facts).Id);
        Assert.Equal("node-b", Assert.Single(tactical).Id);
        Assert.Empty(unrelated);
        Assert.All(facts, node => Assert.Equal(KnowledgeLayer.Facts, KnowledgeLayers.GetLayer(node.Category)));
        Assert.All(tactical, node => Assert.Equal(KnowledgeLayer.Tactical, KnowledgeLayers.GetLayer(node.Category)));
    }

    [Fact]
    public async Task V1133SeedPassesAutomatedBetaEvidenceAndLayerGate()
    {
        var packagePath = Path.Combine(
            AppContext.BaseDirectory,
            "testdata",
            "trade-wars-v1.13.3.seedpkg");
        Assert.True(File.Exists(packagePath), $"Beta candidate package not found: {packagePath}");
        var package = await File.ReadAllBytesAsync(packagePath);
        var payloadText = Encoding.UTF8.GetString(ReadEntry(package, "payload.json"));
        Assert.DoesNotContain("CORPPW", payloadText, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("SSCHANNEL", payloadText, StringComparison.OrdinalIgnoreCase);

        using var directory = new TemporaryDirectory();
        var seed = new SqliteKnowledgeStore(ConnectionString(directory.File("seed.db")));
        var runtime = new SqliteKnowledgeStore(ConnectionString(directory.File("runtime.db")));
        await seed.InitializeAsync();
        await runtime.InitializeAsync();
        var import = await new SeedKnowledgePackageService(seed, runtime)
            .ImportAsync(new MemoryStream(package));

        Assert.Equal(2_344, import.DocumentsImported);
        Assert.Equal(11_025, import.NodesImported);
        var nodes = (await seed.GetAllDocumentsAsync())
            .SelectMany(document => document.Nodes)
            .ToArray();
        Assert.All(nodes, node => Assert.NotEmpty(node.Evidence));

        var expectedCounts = new Dictionary<KnowledgeLayer, int>
        {
            [KnowledgeLayer.Facts] = 2_843,
            [KnowledgeLayer.Procedural] = 7_262,
            [KnowledgeLayer.Tactical] = 456,
            [KnowledgeLayer.Communication] = 183,
            [KnowledgeLayer.Cooperative] = 31,
            [KnowledgeLayer.Strategic] = 247,
            [KnowledgeLayer.Structural] = 3,
            [KnowledgeLayer.Experience] = 0
        };
        foreach (var (layer, expectedCount) in expectedCounts)
        {
            Assert.Equal(
                expectedCount,
                nodes.Count(node => KnowledgeLayers.GetLayer(node.Category) == layer));
        }

        var composite = new CompositeKnowledgeStore(seed, runtime);
        foreach (var layer in expectedCounts.Keys)
        {
            var results = await composite.SearchLayerAsync(query: null, layer, limit: 4);
            Assert.InRange(results.Count, expectedCounts[layer] == 0 ? 0 : 1, 4);
            Assert.All(results, node => Assert.Equal(layer, KnowledgeLayers.GetLayer(node.Category)));
        }

        var messageEvidence = nodes
            .Where(node => node.Category == KnowledgeCategory.CommunicationPattern)
            .SelectMany(node => node.Evidence)
            .Where(evidence => evidence.Signal == "explicit outbound SubSpace message template")
            .ToArray();
        Assert.Equal(271, messageEvidence.Length);
        Assert.Equal(
            137,
            messageEvidence.Select(evidence => evidence.Details?.MessageTemplate)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Count());
        Assert.All(messageEvidence, evidence =>
        {
            Assert.False(string.IsNullOrWhiteSpace(evidence.Details?.MessageTemplate));
            Assert.Null(evidence.Details?.RequiredAction);
            Assert.Null(evidence.Details?.ExpectedResponse);
        });
    }

    [Fact]
    public async Task LegacyKnowledgeMigrationIsIdempotentAndKeepsSeedSeparate()
    {
        using var directory = new TemporaryDirectory();
        var legacy = new SqliteKnowledgeStore(ConnectionString(directory.File("legacy.db")));
        var seed = new SqliteKnowledgeStore(ConnectionString(directory.File("seed.db")));
        await legacy.InitializeAsync();
        await seed.InitializeAsync();
        await legacy.ImportDocumentsAsync(CreateDocuments());

        var migration = new LegacyKnowledgeSeedMigration(legacy, seed);
        Assert.Equal(2, await migration.MigrateAsync());
        Assert.Equal(0, await migration.MigrateAsync());
        Assert.Equal(2, (await seed.GetAllDocumentsAsync()).Count);
        Assert.Equal(2, (await legacy.GetAllDocumentsAsync()).Count);
    }

    [Fact]
    public async Task AgentExportPreservesJournalAndOmitsCredentialConfiguration()
    {
        using var directory = new TemporaryDirectory();
        var connectionString = ConnectionString(directory.File("agent.db"));
        var agentStore = new SqliteAgentStore(connectionString);
        await agentStore.InitializeAsync();
        await agentStore.SaveAgentAsync(new AgentContainer
        {
            AgentId = "meridian",
            DisplayName = "Meridian",
            Description = "Persistent player",
            PreferredProvider = "Ollama",
            PreferredModel = "qwen3:4b",
            Goals = ["survive"],
            Configuration = new Dictionary<string, string>
            {
                ["theme"] = "dark",
                ["apiToken"] = "must-not-export"
            },
            Journal =
            [
                new AgentJournalEntry(
                    DateTimeOffset.Parse("2025-01-03T00:00:00Z"),
                    "System",
                    "Agent restored.")
            ]
        });

        var goalStore = new SqliteGoalProposalStore(connectionString);
        var macroStore = new SqliteMacroTelemetryStore(connectionString);
        var reactiveStore = new SqliteReactiveEventStore(connectionString);
        var reviewStore = new SqliteStrategicReviewStore(connectionString);
        var seedStore = new SqliteKnowledgeStore(ConnectionString(directory.File("seed-knowledge.db")));
        var runtimeStore = new SqliteKnowledgeStore(ConnectionString(directory.File("runtime-knowledge.db")));
        await goalStore.InitializeAsync();
        await macroStore.InitializeAsync();
        await reactiveStore.InitializeAsync();
        await reviewStore.InitializeAsync();
        await seedStore.InitializeAsync();
        await runtimeStore.InitializeAsync();
        await seedStore.ImportDocumentsAsync(CreateDocuments());
        var personalDocument = new KnowledgeDocument(
            "personal-discovery",
            "meridian",
            "learned/route.txt",
            "route.txt",
            "personal-hash",
            "text",
            DateTimeOffset.Parse("2025-01-04T00:00:00Z"),
            [
                new KnowledgeNode(
                    "personal-node",
                    "personal-discovery",
                    KnowledgeCategory.WorldKnowledge,
                    "Personal route",
                    "An agent-specific observation",
                    0.9m,
                    ExtractionBasis.ExplicitText,
                    [new KnowledgeEvidence(4, "observation", "verified by Meridian")])
            ],
            []);
        await runtimeStore.ReplaceAgentDocumentAsync("meridian", personalDocument);
        var experienceRecorder = new AgentExperienceKnowledgeRecorder(runtimeStore);
        var experienceStartedAt = DateTimeOffset.Parse("2025-01-05T00:00:00Z");
        var experienceEndedAt = DateTimeOffset.Parse("2025-01-05T00:00:01Z");
        await experienceRecorder.RecordMacroExecutionAsync(new MacroExecutionRecord(
            "exported-experience",
            "meridian",
            "find-nearest-stardock",
            "Find nearest StarDock",
            MacroCategory.Navigation,
            experienceStartedAt,
            experienceEndedAt,
            MacroExecutionStatus.Planned,
            "A route plan was recorded.",
            "Offline plan only.",
            [12, 13]),
            new GameSnapshot(12, 100_000, 100, null, experienceStartedAt.AddSeconds(-1)),
            new GameSnapshot(12, 100_500, 99, null, experienceEndedAt.AddSeconds(1)));
        var turnStartedAt = DateTimeOffset.Parse("2025-01-05T00:02:00Z");
        await experienceRecorder.RecordTurnObservationAsync(
            "meridian",
            "meridian-alpha-1",
            1,
            new GameSnapshot(12, 100_500, 99, null, turnStartedAt),
            new GameSnapshot(12, 100_500, 98, null, turnStartedAt.AddSeconds(1)));
        await runtimeStore.ReplaceDocumentAsync(
            personalDocument with
            {
                Id = "shared-runtime-note",
                RootId = "operator",
                RelativePath = "shared/note.txt",
                FileName = "note.txt",
                ContentHash = "shared-hash",
                OwnerAgentId = null,
                Nodes =
                [
                    personalDocument.Nodes[0] with
                    {
                        Id = "shared-node",
                        DocumentId = "shared-runtime-note"
                    }
                ]
            });
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            runtimeStore.ReplaceDocumentAsync(personalDocument with { OwnerAgentId = null }));

        var bytes = await new AgentExportPackageService(
            agentStore,
            goalStore,
            macroStore,
            reactiveStore,
            reviewStore,
            runtimeStore,
            seedStore).ExportAsync("meridian");
        var manifestBytes = ReadEntry(bytes, "manifest.json");
        var manifest = JsonSerializer.Deserialize<PackageManifest>(
            manifestBytes,
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Missing package manifest.");
        var payload = JsonSerializer.Deserialize<AgentExportPackagePayload>(
            ReadEntry(bytes, "payload.json"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web)
            {
                Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
            })
            ?? throw new InvalidDataException("Missing agent export payload.");

        Assert.Equal("agent-export", manifest.PackageType);
        Assert.Equal("Meridian", payload.Identity.DisplayName);
        Assert.Single(payload.Journal);
        Assert.Equal("dark", payload.Identity.Configuration["theme"]);
        Assert.DoesNotContain("apiToken", payload.Identity.Configuration.Keys);
        Assert.Equal(2, payload.SeedKnowledge.DocumentCount);
        Assert.Equal(64, payload.SeedKnowledge.ContentSha256.Length);
        var seedPackage = await new SeedKnowledgePackageService(seedStore, runtimeStore).ExportAsync("test-seed");
        var seedManifest = JsonSerializer.Deserialize<PackageManifest>(
            ReadEntry(seedPackage, "manifest.json"),
            new JsonSerializerOptions(JsonSerializerDefaults.Web))
            ?? throw new InvalidDataException("Missing seed package manifest.");
        Assert.Equal(seedManifest.PayloadSha256, payload.SeedKnowledge.ContentSha256);
        Assert.Equal(3, payload.PersonalKnowledge.Count);
        var personalKnowledge = Assert.Single(
            payload.PersonalKnowledge,
            document => document.Id == "personal-discovery");
        Assert.Equal("personal-discovery", personalKnowledge.Id);
        Assert.Equal("meridian", personalKnowledge.OwnerAgentId);
        var experienceNodes = payload.PersonalKnowledge.SelectMany(document => document.Nodes)
            .Where(node => node.Category == KnowledgeCategory.AgentExperience)
            .ToArray();
        Assert.Equal(2, experienceNodes.Length);
        var experienceNode = Assert.Single(
            experienceNodes,
            node => node.Evidence[0].Details?.AgentExperience?.EventKind == "MacroExecution");
        Assert.False(Assert.IsType<AgentExperienceEvidence>(
            experienceNode.Evidence[0].Details?.AgentExperience).GameOutcomeMeasured);
        Assert.Equal(
            100_500,
            experienceNode.Evidence[0].Details?.AgentExperience?.AfterSnapshot?.SynchronizedCredits);
        var turnExperienceNode = Assert.Single(
            experienceNodes,
            node => node.Evidence[0].Details?.AgentExperience?.EventKind == "TurnObservation");
        Assert.Equal(
            "meridian-alpha-1",
            turnExperienceNode.Evidence[0].Details?.AgentExperience?.SessionId);
        Assert.False(Assert.IsType<AgentExperienceEvidence>(
            turnExperienceNode.Evidence[0].Details?.AgentExperience).GameOutcomeMeasured);
        Assert.Contains(manifest.NotIncluded, item => item.Contains("Trust networks", StringComparison.Ordinal));
    }

    [Fact]
    public async Task KnowledgeStoreMigratesLegacyDocumentsWithoutAssigningAnOwner()
    {
        using var directory = new TemporaryDirectory();
        var connectionString = ConnectionString(directory.File("legacy-knowledge.db"));
        await using (var connection = new Microsoft.Data.Sqlite.SqliteConnection(connectionString))
        {
            await connection.OpenAsync();
            var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE knowledge_documents (
                    document_id TEXT PRIMARY KEY,
                    root_id TEXT NOT NULL,
                    relative_path TEXT NOT NULL,
                    file_name TEXT NOT NULL,
                    content_hash TEXT NOT NULL,
                    language TEXT NOT NULL,
                    mined_at TEXT NOT NULL,
                    UNIQUE(root_id, relative_path)
                );
                INSERT INTO knowledge_documents(
                    document_id, root_id, relative_path, file_name, content_hash, language, mined_at
                ) VALUES ('legacy-doc', 'legacy', 'old.txt', 'old.txt', 'legacy-hash', 'text',
                    '2025-01-01T00:00:00.0000000+00:00');
                """;
            await command.ExecuteNonQueryAsync();
        }

        var store = new SqliteKnowledgeStore(connectionString);
        await store.InitializeAsync();
        var migrated = Assert.Single(await store.GetAllDocumentsAsync());

        Assert.Null(migrated.OwnerAgentId);
        Assert.Empty(await store.GetAgentDocumentsAsync("meridian"));
    }

    private static IReadOnlyList<KnowledgeDocument> CreateDocuments()
    {
        var evidence = new KnowledgeEvidence(3, "script-structure", "verified source line");
        var firstNode = new KnowledgeNode(
            "node-a",
            "doc-a",
            KnowledgeCategory.WorldKnowledge,
            "Node A",
            "First node",
            0.9m,
            ExtractionBasis.ExplicitText,
            [evidence]);
        var secondNode = new KnowledgeNode(
            "node-b",
            "doc-b",
            KnowledgeCategory.TacticalGuidance,
            "Node B",
            "Second node",
            0.8m,
            ExtractionBasis.StaticStructure,
            [evidence]);
        return
        [
            new KnowledgeDocument(
                "doc-a", "seed", "scripts/a.txt", "a.txt", "hash-a", "text",
                DateTimeOffset.Parse("2025-01-01T00:00:00Z"),
                [firstNode],
                [
                    new KnowledgeRelation(
                        "doc-a", "node-a", "node-b", KnowledgeRelationType.Supports, 0.75m,
                        ExtractionBasis.ExplicitText, [evidence])
                ]),
            new KnowledgeDocument(
                "doc-b", "seed", "scripts/b.txt", "b.txt", "hash-b", "text",
                DateTimeOffset.Parse("2025-01-02T00:00:00Z"),
                [secondNode],
                [])
        ];
    }

    private static KnowledgeDocument CreateAgentExperienceDocument(string agentId, string nodeId)
    {
        var documentId = $"{nodeId}-document";
        return new KnowledgeDocument(
            documentId,
            "agent-experience",
            $"{agentId}/{nodeId}.json",
            $"{nodeId}.json",
            $"{nodeId}-hash",
            "json",
            DateTimeOffset.UtcNow,
            [
                new KnowledgeNode(
                    nodeId,
                    documentId,
                    KnowledgeCategory.AgentExperience,
                    $"Experience for {agentId}",
                    "Recorded macro telemetry.",
                    1m,
                    ExtractionBasis.RuntimeObservation,
                    [])
            ],
            []);
    }

    private static string ConnectionString(string path) => new Microsoft.Data.Sqlite.SqliteConnectionStringBuilder
    {
        DataSource = path,
        ForeignKeys = true,
        Pooling = false
    }.ToString();

    private static byte[] ReadEntry(byte[] package, string entryName)
    {
        using var source = new MemoryStream(package);
        using var archive = new ZipArchive(source, ZipArchiveMode.Read);
        using var entryStream = archive.GetEntry(entryName)?.Open()
            ?? throw new InvalidDataException($"Missing package entry '{entryName}'.");
        using var result = new MemoryStream();
        entryStream.CopyTo(result);
        return result.ToArray();
    }

    private static byte[] RewriteEntry(byte[] package, string entryName, Func<byte[], byte[]> transform)
    {
        using var source = new MemoryStream(package);
        using var input = new ZipArchive(source, ZipArchiveMode.Read);
        using var destination = new MemoryStream();
        using (var output = new ZipArchive(destination, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var inputEntry in input.Entries)
            {
                using var entryStream = inputEntry.Open();
                using var contents = new MemoryStream();
                entryStream.CopyTo(contents);
                var bytes = inputEntry.FullName == entryName
                    ? transform(contents.ToArray())
                    : contents.ToArray();
                using var outputStream = output.CreateEntry(inputEntry.FullName).Open();
                outputStream.Write(bytes);
            }
        }
        return destination.ToArray();
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            $"twbot-packages-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public string File(string name) => System.IO.Path.Combine(Path, name);

        public void Dispose() => Directory.Delete(Path, recursive: true);
    }
}
