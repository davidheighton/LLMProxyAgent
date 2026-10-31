using System.Text.Json;
using System.Text.Json.Serialization;
using LLMSurvivingProxyAgent.Knowledge;
using LLMSurvivingProxyAgent.Packaging;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.CorpusBuilder;

public sealed class CorpusBuildService
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    private readonly CorpusInventoryService _inventoryService = new();
    private readonly CorpusComparisonAnalysisService _comparisonAnalysisService = new();

    public CorpusInventoryReport Inspect(
        IReadOnlyList<CorpusRoot> roots,
        string version)
    {
        ValidateRoots(roots);
        ValidateVersion(version);
        var summaries = roots.Select(root =>
                _inventoryService.Inspect(
                    root,
                    root.Kind == CorpusSourceKind.RuntimeArtifacts,
                    GetNestedRootPaths(root, roots)))
            .ToArray();
        return new CorpusInventoryReport(version, DateTimeOffset.UtcNow, summaries);
    }

    public async Task<CorpusInventoryReport> WriteInventoryAsync(
        IReadOnlyList<CorpusRoot> roots,
        string version,
        string outputPath,
        CancellationToken cancellationToken = default)
    {
        var inventory = Inspect(roots, version);
        var destination = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(destination)!);
        await WriteNewFileAsync(destination, JsonSerializer.SerializeToUtf8Bytes(inventory, JsonOptions), cancellationToken);
        return inventory;
    }

    public async Task<CorpusBuildResult> BuildAsync(
        CorpusBuildOptions options,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(options);
        ArgumentException.ThrowIfNullOrWhiteSpace(options.Source);
        ValidateVersion(options.Version);
        ValidateRoots(options.Roots);

        var packagePath = Path.GetFullPath(options.PackagePath);
        var inventoryPath = Path.GetFullPath(options.InventoryPath);
        if (string.Equals(packagePath, inventoryPath, StringComparison.OrdinalIgnoreCase))
        {
            throw new ArgumentException("Package and inventory outputs must use different paths.", nameof(options));
        }
        if (File.Exists(packagePath) ||
            File.Exists(inventoryPath) ||
            Directory.Exists(packagePath) ||
            Directory.Exists(inventoryPath))
        {
            throw new IOException("A requested corpus output already exists; choose new output paths.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(packagePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(inventoryPath)!);

        var stagingDirectory = Path.Combine(Path.GetTempPath(), $"twbot-corpus-{Guid.NewGuid():N}");
        Directory.CreateDirectory(stagingDirectory);
        try
        {
            var initialInventory = options.Roots.ToDictionary(
                root => root.Id,
                root => _inventoryService.Inspect(
                    root,
                    root.Kind == CorpusSourceKind.RuntimeArtifacts,
                    GetNestedRootPaths(root, options.Roots)),
                StringComparer.Ordinal);
            var stagingPath = Path.Combine(stagingDirectory, "staging.db");
            var stagingConnectionString = new SqliteConnectionStringBuilder
            {
                DataSource = stagingPath,
                ForeignKeys = true,
                Pooling = false
            }.ToString();
            var store = new SqliteKnowledgeStore(stagingConnectionString);
            await store.InitializeAsync(cancellationToken);

            var miningRoots = options.Roots
                .Where(root => root.Kind is not (CorpusSourceKind.RuntimeArtifacts or CorpusSourceKind.SavedGameLogs))
                .Select(root => new KnowledgeSourceRoot(root.Id, Path.GetFullPath(root.Path)))
                .ToArray();
            List<KnowledgeMiningReport> miningReports = miningRoots.Length > 0
                ? (await new KnowledgeMiningService(store, miningRoots)
                    .MineConfiguredRootsAsync(cancellationToken)).ToList()
                : [];
            var logMiner = new SavedGameLogMiningService(store);
            foreach (var root in options.Roots.Where(root => root.Kind == CorpusSourceKind.SavedGameLogs))
            {
                miningReports.Add(await logMiner.MineRootAsync(root, cancellationToken));
            }
            var documents = await store.GetAllDocumentsAsync(cancellationToken);
            var inventory = BuildInventory(options, documents, miningReports, initialInventory);
            var sourceSummary = inventory.Sources;
            var package = await new SeedKnowledgePackageService(store, store)
                .ExportVersionedAsync(options.Source, options.Version, sourceSummary, cancellationToken);
            var inventoryBytes = JsonSerializer.SerializeToUtf8Bytes(inventory, JsonOptions);
            await WriteOutputsAsync(packagePath, package, inventoryPath, inventoryBytes, cancellationToken);

            return new CorpusBuildResult(
                packagePath,
                inventoryPath,
                inventory,
                documents.Count,
                documents.Sum(document => document.Nodes.Count));
        }
        finally
        {
            Directory.Delete(stagingDirectory, recursive: true);
        }
    }

    private CorpusInventoryReport BuildInventory(
        CorpusBuildOptions options,
        IReadOnlyList<KnowledgeDocument> documents,
        IReadOnlyList<KnowledgeMiningReport> miningReports,
        IReadOnlyDictionary<string, PackageSourceSummary> initialInventory)
    {
        var reportsByRoot = miningReports.ToDictionary(report => report.RootId, StringComparer.Ordinal);
        var sources = new List<PackageSourceSummary>(options.Roots.Count);
        foreach (var root in options.Roots)
        {
            var baseSummary = initialInventory[root.Id];
            var rootDocuments = documents.Where(document => document.RootId == root.Id).ToArray();
            var rootNodes = rootDocuments.SelectMany(document => document.Nodes).ToArray();
            var categories = rootNodes
                .GroupBy(node => node.Category.ToString(), StringComparer.Ordinal)
                .OrderBy(group => group.Key, StringComparer.Ordinal)
                .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal);
            var triggerNodes = rootNodes
                .Where(node => node.Category == KnowledgeCategory.ReactiveTriggerPattern)
                .ToArray();
            var scriptNodes = rootNodes
                .Where(node => node.Category == KnowledgeCategory.MacroDefinition &&
                    node.Title.StartsWith("Script:", StringComparison.Ordinal))
                .ToArray();
            var decisionNodes = rootNodes
                .Where(node => node.Category == KnowledgeCategory.DecisionContext)
                .ToArray();
            var menuNodes = rootNodes
                .Where(node => node.Category == KnowledgeCategory.MenuStructure)
                .ToArray();
            var documentationByRole = rootDocuments
                .Where(DocumentationSourceClassifier.IsMinedDocumentation)
                .GroupBy(DocumentationSourceClassifier.Classify)
                .OrderBy(group => group.Key)
                .ToArray();
            var decisionContextDocumentsByRole = documentationByRole
                .Select(group => new
                {
                    Role = group.Key.ToString(),
                    Count = group.Count(document =>
                        decisionNodes.Any(node => node.DocumentId == document.Id))
                })
                .Where(item => item.Count > 0)
                .ToDictionary(item => item.Role, item => item.Count, StringComparer.Ordinal);
            var decisionContextEvidenceByRole = documentationByRole
                .Select(group => new
                {
                    Role = group.Key.ToString(),
                    Count = group.Sum(document => decisionNodes
                        .Where(node => node.DocumentId == document.Id)
                        .Sum(node => node.Evidence.Count))
                })
                .Where(item => item.Count > 0)
                .ToDictionary(item => item.Role, item => item.Count, StringComparer.Ordinal);
            var mining = root.Kind == CorpusSourceKind.RuntimeArtifacts
                ? null
                : reportsByRoot[root.Id];
            var coverage = baseSummary.Coverage with
            {
                ArchiveFilesScanned = mining?.ArchiveFilesScanned ?? 0,
                ArchiveFilesTooLarge = mining?.ArchiveFilesTooLarge ?? 0,
                ArchiveEntriesFound = mining?.ArchiveEntriesFound ?? 0,
                ArchiveEntriesMined = mining?.ArchiveEntriesMined ?? 0,
                ArchiveEntriesDeduplicated = mining?.ArchiveEntriesDeduplicated ?? 0,
                ArchiveEntriesTooLarge = mining?.ArchiveEntriesTooLarge ?? 0,
                DecisionContextDocuments = decisionNodes
                    .Select(node => node.DocumentId)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                DecisionContextEvidenceBySignal = decisionNodes
                    .SelectMany(node => node.Evidence)
                    .GroupBy(evidence => evidence.Signal, StringComparer.Ordinal)
                    .OrderBy(group => group.Key, StringComparer.Ordinal)
                    .ToDictionary(group => group.Key, group => group.Count(), StringComparer.Ordinal),
                DocumentationDocumentsMinedByRole = documentationByRole
                    .ToDictionary(
                        group => group.Key.ToString(),
                        group => group.Count(),
                        StringComparer.Ordinal),
                DecisionContextDocumentsByDocumentationRole = decisionContextDocumentsByRole,
                DecisionContextEvidenceByDocumentationRole = decisionContextEvidenceByRole,
                TriggerRegistrations = triggerNodes.Sum(node =>
                    node.Evidence.Count(evidence => evidence.Signal == "trigger registration")),
                UniqueTriggerKinds = triggerNodes
                    .Select(node => node.Title)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                DocumentsWithTriggers = triggerNodes
                    .Select(node => node.DocumentId)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                HandlerResponseCandidates = triggerNodes.Sum(node =>
                    node.Evidence.Count(evidence => evidence.Signal == "handler response candidate")),
                PossibleRecoverySignals = triggerNodes.Sum(node =>
                    node.Evidence.Count(evidence => evidence.Signal == "possible failure or recovery handling")),
                ScriptArtifacts = scriptNodes
                    .Select(node => node.DocumentId)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                SubroutineDefinitions = rootNodes.Count(node =>
                    node.Category == KnowledgeCategory.MacroDefinition &&
                    node.Title.StartsWith("Subroutine:", StringComparison.Ordinal)),
                DocumentsWithPurposeContext = CountDocumentsWithContext(scriptNodes, "purpose"),
                DocumentsWithGoalContext = CountDocumentsWithContext(scriptNodes, "observed goals"),
                DocumentsWithTypicalUsageContext = CountDocumentsWithContext(scriptNodes, "typical usage"),
                DocumentsWithTriggerContext = CountDocumentsWithContext(scriptNodes, "observed triggers"),
                DocumentsWithStoppingConditionContext = CountDocumentsWithContext(scriptNodes, "stopping conditions"),
                StoppingConditionEvidence = scriptNodes.Sum(node =>
                    node.Evidence.Count(evidence => evidence.Signal == "stopping conditions")),
                DocumentsWithFailureConditionContext = CountDocumentsWithContext(scriptNodes, "failure conditions and risks"),
                FailureConditionEvidence = scriptNodes.Sum(node =>
                    node.Evidence.Count(evidence => evidence.Signal == "failure conditions and risks")),
                DocumentsWithPreconditionContext = CountDocumentsWithContext(scriptNodes, "preconditions"),
                DocumentsWithValidationContext = CountDocumentsWithContext(scriptNodes, "validation checks"),
                DocumentsWithRetryContext = CountDocumentsWithContext(scriptNodes, "retry patterns"),
                DocumentsWithResumeContext = CountDocumentsWithContext(scriptNodes, "resume patterns"),
                DocumentsWithAbortContext = CountDocumentsWithContext(scriptNodes, "abort conditions"),
                DocumentsWithEscalationContext = CountDocumentsWithContext(scriptNodes, "escalation conditions"),
                DocumentsWithCheckpointContext = CountDocumentsWithContext(scriptNodes, "recovery checkpoints"),
                DocumentsWithRecoveryContext = CountDocumentsWithContext(scriptNodes, "recovery patterns"),
                DocumentsWithVerificationLoopContext = CountDocumentsWithContext(scriptNodes, "verification loops"),
                ExplicitUrgencyAnnotations = triggerNodes.Sum(node =>
                    node.Evidence.Count(evidence => evidence.Signal == "explicit urgency annotation")),
                PreconditionCheckDocuments = CountDocumentsWithPattern(rootNodes, "Precondition checks"),
                ResultVerificationDocuments = CountDocumentsWithPattern(rootNodes, "Result verification"),
                DocumentationDocumentsMined = rootDocuments.Count(document =>
                {
                    var extension = Path.GetExtension(document.FileName);
                    return string.Equals(extension, ".md", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(extension, ".txt", StringComparison.OrdinalIgnoreCase) ||
                           string.Equals(extension, ".tid", StringComparison.OrdinalIgnoreCase);
                }),
                MenuStructuresObserved = menuNodes.Length,
                MenuOptionsObserved = menuNodes
                    .SelectMany(node => node.Evidence)
                    .Where(evidence => evidence.Signal == "menu option")
                    .Select(evidence => evidence.Text)
                    .Distinct(StringComparer.Ordinal)
                    .Count(),
                MenuDisplayObservations = mining?.MenuDisplayObservations ?? 0,
                MenuCoverage = root.Kind == CorpusSourceKind.SavedGameLogs
                    ? CorpusCoverageStatus.Measured
                    : CorpusCoverageStatus.NotMeasured,
                LogContentCoverage = root.Kind == CorpusSourceKind.SavedGameLogs
                    ? CorpusCoverageStatus.Partial
                    : baseSummary.Coverage.LogContentCoverage
            };
            if (root.Kind == CorpusSourceKind.RuntimeArtifacts)
            {
                sources.Add(baseSummary with { NodesByCategory = categories, Coverage = coverage });
                continue;
            }

            ArgumentNullException.ThrowIfNull(mining);
            sources.Add(baseSummary with
            {
                TooLargeFiles = mining.FilesTooLarge,
                DocumentsMined = mining.FilesMined,
                NodesExtracted = mining.NodesCreated,
                NodesByCategory = categories,
                Coverage = coverage
            });
        }
        return new CorpusInventoryReport(options.Version, DateTimeOffset.UtcNow, sources)
        {
            Comparisons = _comparisonAnalysisService.Analyze(documents, sources),
            KnowledgeClassCounts = CountKnowledgeClasses(documents)
        };
    }

    private static KnowledgeClassCounts CountKnowledgeClasses(
        IReadOnlyList<KnowledgeDocument> documents)
    {
        var nodes = documents.SelectMany(document => document.Nodes).ToArray();
        var counts = new KnowledgeClassCounts(
            Count(KnowledgeLayer.Facts),
            Count(KnowledgeLayer.Procedural),
            Count(KnowledgeLayer.Tactical),
            Count(KnowledgeLayer.Cooperative),
            Count(KnowledgeLayer.Communication),
            Count(KnowledgeLayer.Strategic),
            Count(KnowledgeLayer.Structural))
        {
            ExperienceNodes = Count(KnowledgeLayer.Experience)
        };
        if (counts.TotalNodes != nodes.Length)
        {
            throw new InvalidOperationException(
                "Every mined knowledge category must be assigned to exactly one inventory class.");
        }
        return counts;

        int Count(KnowledgeLayer layer) =>
            nodes.Count(node => KnowledgeLayers.GetLayer(node.Category) == layer);
    }

    private static int CountDocumentsWithContext(
        IReadOnlyList<KnowledgeNode> scriptNodes,
        string signal) =>
        scriptNodes
            .Where(node => node.Evidence.Any(evidence => evidence.Signal == signal))
            .Select(node => node.DocumentId)
            .Distinct(StringComparer.Ordinal)
            .Count();

    private static int CountDocumentsWithPattern(
        IReadOnlyList<KnowledgeNode> nodes,
        string title) =>
        nodes
            .Where(node => node.Category == KnowledgeCategory.MacroConstructionPattern &&
                node.Title == title)
            .Select(node => node.DocumentId)
            .Distinct(StringComparer.Ordinal)
            .Count();

    private static IReadOnlyList<string> GetNestedRootPaths(
        CorpusRoot root,
        IReadOnlyList<CorpusRoot> roots)
    {
        var rootPath = Path.GetFullPath(root.Path);
        return roots
            .Where(candidate => !ReferenceEquals(candidate, root))
            .Select(candidate => Path.GetFullPath(candidate.Path))
            .Where(candidatePath =>
            {
                var relativePath = Path.GetRelativePath(rootPath, candidatePath);
                return relativePath != "." &&
                    !Path.IsPathRooted(relativePath) &&
                    relativePath != ".." &&
                    !relativePath.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal);
            })
            .ToArray();
    }

    private static void ValidateRoots(IReadOnlyList<CorpusRoot> roots)
    {
        ArgumentNullException.ThrowIfNull(roots);
        if (roots.Count == 0)
        {
            throw new ArgumentException("At least one corpus root is required.", nameof(roots));
        }
        if (roots.Any(root =>
            root is null ||
            string.IsNullOrWhiteSpace(root.Id) ||
            !Enum.IsDefined(root.Kind) ||
            string.IsNullOrWhiteSpace(root.Path)))
        {
            throw new ArgumentException("Every corpus root requires a valid ID, kind, and path.", nameof(roots));
        }
        if (roots.Select(root => root.Id).Distinct(StringComparer.Ordinal).Count() != roots.Count)
        {
            throw new ArgumentException("Corpus root IDs must be unique.", nameof(roots));
        }
    }

    private static void ValidateVersion(string version)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(version);
        if (version.Length > 64 ||
            !version.All(character => char.IsAsciiLetterOrDigit(character) || character is '.' or '-' or '_'))
        {
            throw new ArgumentException("Corpus version must be at most 64 ASCII letters, digits, dots, dashes, or underscores.", nameof(version));
        }
    }

    private static async Task WriteOutputsAsync(
        string packagePath,
        byte[] package,
        string inventoryPath,
        byte[] inventory,
        CancellationToken cancellationToken)
    {
        var packageTemp = packagePath + $".{Guid.NewGuid():N}.tmp";
        var inventoryTemp = inventoryPath + $".{Guid.NewGuid():N}.tmp";
        var inventoryPublished = false;
        try
        {
            await File.WriteAllBytesAsync(packageTemp, package, cancellationToken);
            await File.WriteAllBytesAsync(inventoryTemp, inventory, cancellationToken);
            File.Move(inventoryTemp, inventoryPath);
            inventoryPublished = true;
            File.Move(packageTemp, packagePath);
        }
        catch
        {
            if (inventoryPublished && File.Exists(inventoryPath))
            {
                File.Delete(inventoryPath);
            }
            throw;
        }
        finally
        {
            if (File.Exists(packageTemp))
            {
                File.Delete(packageTemp);
            }
            if (File.Exists(inventoryTemp))
            {
                File.Delete(inventoryTemp);
            }
        }
    }

    private static async Task WriteNewFileAsync(
        string path,
        byte[] bytes,
        CancellationToken cancellationToken)
    {
        await using var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        await output.WriteAsync(bytes, cancellationToken);
    }
}
