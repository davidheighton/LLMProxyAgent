using System.IO.Compression;
using System.Text.Json;
using System.Text.Json.Serialization;
using LLMSurvivingProxyAgent.CorpusBuilder;
using LLMSurvivingProxyAgent.Knowledge;
using LLMSurvivingProxyAgent.Packaging;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class CorpusBuilderTests
{
    [Fact]
    public async Task BuildInventoriesSourcesAndProducesVersionedSeedWithoutRuntimeLogs()
    {
        using var directory = new TemporaryDirectory();
        var scripts = directory.Combine("twx");
        var runtime = directory.Combine("runtime");
        Directory.CreateDirectory(scripts);
        Directory.CreateDirectory(Path.Combine(scripts, "bin"));
        Directory.CreateDirectory(Path.Combine(scripts, "help"));
        Directory.CreateDirectory(Path.Combine(scripts, "design"));
        Directory.CreateDirectory(Path.Combine(scripts, "forum"));
        Directory.CreateDirectory(Path.Combine(scripts, "personal"));
        Directory.CreateDirectory(Path.Combine(runtime, "logs"));
        await File.WriteAllTextAsync(
            Path.Combine(scripts, "bust-recovery.cts"),
            """
            # Purpose: Preserve the trading reserve after a bust.
            # Goal: Restore the configured trading reserve.
            # Typical usage: Early-game pair trading.
            # Trigger: A bust report is received.
            # Preconditions: A safe route and destination port are known.
            # Validation: Verify the route before trading.
            # Verification loop: Re-check port stock after trading.
            # Retry pattern: Retry after an unavailable port response.
            # Resume pattern: Resume from the saved sector checkpoint.
            # Abort condition: Abort if no safe route is available.
            # Escalation condition: Alert the operator after a bust.
            # Recovery checkpoint: Save the current sector before trading.
            # Stopping condition: The configured reserve is restored.
            # Failure condition: The destination port is unavailable.
            # Urgent: Escalate when a bust is confirmed.
            setTextTrigger bust :handleBust
            :handleBust
            echo "record the event"
            echo "verify complete"
            if ($valid) echo "verified"
            """);
        await File.WriteAllTextAsync(
            Path.Combine(scripts, "SST.cts"),
            "echo \"SST trading helper\"");
        await File.WriteAllTextAsync(
            Path.Combine(scripts, "guide.md"),
            """
            # SST strategy
            Use SST when credits are low and turns are available.
            Avoid SST when competition is high.
            Prerequisites: A safe route is known.
            Expected outcome: Generate credits quickly.
            Need fighters on SubSpace; notify the corp.
            1. Notify the corp of a trapped trader.
            2. Transfer fighters to the trader.
            """);
        await File.WriteAllTextAsync(
            Path.Combine(scripts, "help", "commands.txt"),
            "Use SST when credits are low.");
        await File.WriteAllTextAsync(
            Path.Combine(scripts, "design", "agent-design.md"),
            "Objective: Improve route selection.");
        await File.WriteAllTextAsync(
            Path.Combine(scripts, "forum", "thread-1.md"),
            "Risk: Hostile space may expose ships.");
        await File.WriteAllTextAsync(
            Path.Combine(scripts, "personal", "field-notes.md"),
            "Benefit: Known routes reduce uncertainty.");
        await File.WriteAllTextAsync(
            Path.Combine(scripts, "run-history.md"),
            "Expected outcome: Resume after interruption.");
        await File.WriteAllTextAsync(
            Path.Combine(scripts, "overview.md"),
            "TradeWars source documentation.");
        await File.WriteAllTextAsync(
            Path.Combine(scripts, "bin", "generated.cs"),
            "class Generated { }");
        await File.WriteAllTextAsync(
            Path.Combine(runtime, "logs", "session.log"),
            "Private message: this runtime log must not enter a seed.");
        await File.WriteAllTextAsync(
            Path.Combine(runtime, "status.json"),
            "{\"sector\": 12}");

        var packagePath = directory.Combine("out", "trade-wars-v1.seedpkg");
        var inventoryPath = directory.Combine("out", "trade-wars-v1.inventory.json");
        var result = await new CorpusBuildService().BuildAsync(new CorpusBuildOptions(
            "TradeWars corpus",
            "1.0.0",
            packagePath,
            inventoryPath,
            [
                new CorpusRoot("twx", CorpusSourceKind.TwxScripts, scripts),
                new CorpusRoot("runtime-logs", CorpusSourceKind.RuntimeArtifacts, runtime)
            ]));

        Assert.True(File.Exists(packagePath));
        Assert.True(File.Exists(inventoryPath));
        using var inventoryJson = JsonDocument.Parse(await File.ReadAllTextAsync(inventoryPath));
        var patternComparisons = inventoryJson.RootElement
            .GetProperty("comparisons")
            .GetProperty("patternComparisons")
            .EnumerateArray()
            .ToArray();
        Assert.Contains(patternComparisons, pattern =>
            pattern.GetProperty("patternType").GetString() == "Trigger handler candidate");
        var decisionComparisons = inventoryJson.RootElement
            .GetProperty("comparisons")
            .GetProperty("decisionContextComparisons")
            .EnumerateArray()
            .ToArray();
        Assert.Contains(decisionComparisons, comparison =>
            comparison.GetProperty("signal").GetString() == "usage conditions");
        var claimNetworks = inventoryJson.RootElement
            .GetProperty("comparisons")
            .GetProperty("claimNetworks")
            .EnumerateArray()
            .ToArray();
        Assert.Contains(claimNetworks, network =>
            network.GetProperty("macroName").GetString() == "SST");
        var knowledgeClasses = Assert.IsType<KnowledgeClassCounts>(
            result.Inventory.KnowledgeClassCounts);
        Assert.Equal(result.NodesExported, knowledgeClasses.TotalNodes);
        Assert.True(knowledgeClasses.FactNodes > 0);
        Assert.True(knowledgeClasses.ProceduralNodes > 0);
        Assert.True(knowledgeClasses.TacticalNodes > 0);
        Assert.True(knowledgeClasses.CooperativeNodes > 0);
        Assert.True(knowledgeClasses.CommunicationNodes > 0);
        Assert.True(knowledgeClasses.StrategicNodes > 0);
        var serializedClasses = inventoryJson.RootElement.GetProperty("knowledgeClassCounts");
        Assert.Equal(knowledgeClasses.TotalNodes, serializedClasses.GetProperty("totalNodes").GetInt32());
        Assert.Equal(
            knowledgeClasses.CommunicationNodes,
            serializedClasses.GetProperty("communicationNodes").GetInt32());
        var runtimeSummary = Assert.Single(result.Inventory.Sources, source => source.RootId == "runtime-logs");
        Assert.True(runtimeSummary.InventoryOnly);
        Assert.Equal(0, runtimeSummary.MiningCandidates);
        Assert.Equal(2, runtimeSummary.FilesByExtension.Count);
        Assert.Equal(1, runtimeSummary.Coverage.LogFilesInventoried);
        Assert.Equal(CorpusCoverageStatus.InventoryOnly, runtimeSummary.Coverage.LogContentCoverage);
        Assert.Equal(CorpusCoverageStatus.NotMeasured, runtimeSummary.Coverage.MenuCoverage);
        var scriptSummary = Assert.Single(result.Inventory.Sources, source => source.RootId == "twx");
        Assert.Equal(1, scriptSummary.ExcludedDirectories);
        Assert.Equal(1, scriptSummary.Coverage.TriggerRegistrations);
        Assert.Equal(1, scriptSummary.Coverage.UniqueTriggerKinds);
        Assert.Equal(1, scriptSummary.Coverage.DocumentsWithTriggers);
        Assert.Equal(2, scriptSummary.Coverage.ScriptArtifacts);
        Assert.Equal(1, scriptSummary.Coverage.DocumentsWithPurposeContext);
        Assert.Equal(1, scriptSummary.Coverage.DocumentsWithGoalContext);
        Assert.Equal(1, scriptSummary.Coverage.DocumentsWithTypicalUsageContext);
        Assert.Equal(1, scriptSummary.Coverage.DocumentsWithTriggerContext);
        Assert.Equal(1, scriptSummary.Coverage.DocumentsWithStoppingConditionContext);
        Assert.Equal(1, scriptSummary.Coverage.StoppingConditionEvidence);
        Assert.Equal(1, scriptSummary.Coverage.DocumentsWithFailureConditionContext);
        Assert.Equal(1, scriptSummary.Coverage.FailureConditionEvidence);
        Assert.Equal(1, scriptSummary.Coverage.DocumentsWithPreconditionContext);
        Assert.Equal(1, scriptSummary.Coverage.DocumentsWithValidationContext);
        Assert.Equal(1, scriptSummary.Coverage.DocumentsWithVerificationLoopContext);
        Assert.Equal(1, scriptSummary.Coverage.DocumentsWithRetryContext);
        Assert.Equal(1, scriptSummary.Coverage.DocumentsWithResumeContext);
        Assert.Equal(1, scriptSummary.Coverage.DocumentsWithAbortContext);
        Assert.Equal(1, scriptSummary.Coverage.DocumentsWithEscalationContext);
        Assert.Equal(1, scriptSummary.Coverage.DocumentsWithCheckpointContext);
        Assert.Equal(1, scriptSummary.Coverage.ExplicitUrgencyAnnotations);
        Assert.Equal(1, scriptSummary.Coverage.PreconditionCheckDocuments);
        Assert.Equal(1, scriptSummary.Coverage.ResultVerificationDocuments);
        Assert.Equal(7, scriptSummary.Coverage.DocumentationFilesInventoried);
        Assert.Equal(7, scriptSummary.Coverage.DocumentationDocumentsMined);
        Assert.Equal(5, scriptSummary.Coverage.DecisionContextDocuments);
        Assert.True(scriptSummary.Coverage.DecisionContextEvidenceBySignal["usage conditions"] > 0);
        Assert.Equal(1, scriptSummary.Coverage.DocumentationFilesInventoriedByRole["TacticalGuide"]);
        Assert.Equal(1, scriptSummary.Coverage.DocumentationFilesInventoriedByRole["HelpReference"]);
        Assert.Equal(1, scriptSummary.Coverage.DocumentationFilesInventoriedByRole["DesignNotes"]);
        Assert.Equal(1, scriptSummary.Coverage.DocumentationFilesInventoriedByRole["ForumMaterial"]);
        Assert.Equal(1, scriptSummary.Coverage.DocumentationFilesInventoriedByRole["PersonalNotes"]);
        Assert.Equal(1, scriptSummary.Coverage.DocumentationFilesInventoriedByRole["RuntimeHistory"]);
        Assert.Equal(1, scriptSummary.Coverage.DocumentationFilesInventoriedByRole["GeneralDocumentation"]);
        Assert.Equal(1, scriptSummary.Coverage.DocumentationDocumentsMinedByRole["TacticalGuide"]);
        Assert.Equal(1, scriptSummary.Coverage.DecisionContextDocumentsByDocumentationRole["TacticalGuide"]);
        Assert.DoesNotContain(
            "HelpReference",
            scriptSummary.Coverage.DecisionContextDocumentsByDocumentationRole.Keys);
        Assert.True(scriptSummary.Coverage.DecisionContextEvidenceByDocumentationRole["DesignNotes"] > 0);
        Assert.Contains("guide.md", scriptSummary.Coverage.DocumentationSamplesByRole["TacticalGuide"]);
        Assert.Contains("Heuristic", scriptSummary.Coverage.DocumentationRoleClassificationBasis);
        Assert.Equal(CorpusCoverageStatus.NotMeasured, scriptSummary.Coverage.MenuCoverage);

        using var packageStream = File.OpenRead(packagePath);
        using var archive = new ZipArchive(packageStream, ZipArchiveMode.Read);
        var manifest = await DeserializeEntryAsync<PackageManifest>(archive, "manifest.json");
        var payload = await DeserializeEntryAsync<SeedKnowledgePackagePayload>(archive, "payload.json");

        Assert.Equal("1.0.0", manifest.PackageRevision);
        Assert.Equal(result.Inventory.Sources.Count, manifest.Sources.Count);
        Assert.Equal(
            1,
            manifest.Sources.Single(source => source.RootId == "twx")
                .Coverage.DocumentationDocumentsMinedByRole["TacticalGuide"]);
        Assert.Equal(result.DocumentsExported, payload.Documents.Count);
        Assert.DoesNotContain(payload.Documents, document => document.RootId == "runtime-logs");
        var guideContext = Assert.Single(
            payload.Documents.Single(document => document.FileName == "guide.md").Nodes,
            node => node.Category == KnowledgeCategory.DecisionContext &&
                node.Title == "Decision context: Usage conditions");
        Assert.Contains(guideContext.Evidence, evidence =>
            evidence.Text == "Use SST when credits are low and turns are available.");
        var scriptNode = Assert.Single(
            payload.Documents.Single(document => document.FileName == "bust-recovery.cts").Nodes,
            node => node.Category == KnowledgeCategory.MacroDefinition &&
                node.Title.StartsWith("Script:", StringComparison.Ordinal));
        Assert.Contains(scriptNode.Evidence, evidence => evidence.Signal == "purpose");
        Assert.Contains(scriptNode.Evidence, evidence => evidence.Signal == "typical usage");
        Assert.Contains(scriptNode.Evidence, evidence => evidence.Signal == "observed triggers");
        Assert.Contains(scriptNode.Evidence, evidence => evidence.Signal == "stopping conditions");
        Assert.Contains(scriptNode.Evidence, evidence => evidence.Signal == "failure conditions and risks");
        var bustComparison = Assert.Single(
            result.Inventory.Comparisons.TriggerComparisons,
            comparison => comparison.EventType == "Bust detection");
        Assert.Equal(1, bustComparison.RegistrationCount);
        Assert.Equal(["TWX"], bustComparison.Families.Select(family => family.Family).ToArray());
        var sstNetwork = Assert.Single(
            result.Inventory.Comparisons.ClaimNetworks,
            network => network.MacroName == "SST");
        Assert.Contains(sstNetwork.ContextClaims, claim =>
            claim.RootId == "twx" &&
            claim.SourcePath == "guide.md" &&
            claim.Signal == "usage conditions");
    }

    [Fact]
    public async Task BuildIncludesArchiveEntryProvenanceAndCoverageMetrics()
    {
        using var directory = new TemporaryDirectory();
        var source = directory.Combine("source");
        Directory.CreateDirectory(source);
        const string duplicate = "Goal: Preserve credits while trading.";
        await File.WriteAllTextAsync(Path.Combine(source, "existing.txt"), duplicate);
        await using (var archiveStream = File.Create(Path.Combine(source, "legacy.zip")))
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create))
        {
            var duplicateEntry = archive.CreateEntry("docs/copy.txt");
            await using (var stream = duplicateEntry.Open())
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync(duplicate);
            }

            var uniqueEntry = archive.CreateEntry("docs/strategy.txt");
            await using (var stream = uniqueEntry.Open())
            await using (var writer = new StreamWriter(stream))
            {
                await writer.WriteAsync("Avoid SST when turns are unavailable.");
            }
        }

        var result = await new CorpusBuildService().BuildAsync(new CorpusBuildOptions(
            "Legacy documentation",
            "1.0.0",
            directory.Combine("out", "legacy.seedpkg"),
            directory.Combine("out", "legacy.inventory.json"),
            [new CorpusRoot("tribal", CorpusSourceKind.Documentation, source)]));

        var summary = Assert.Single(result.Inventory.Sources);
        Assert.Equal(2, summary.MiningCandidates);
        Assert.Equal(2, summary.DocumentsMined);
        Assert.Equal(1, summary.Coverage.ArchiveFilesScanned);
        Assert.Equal(2, summary.Coverage.ArchiveEntriesFound);
        Assert.Equal(1, summary.Coverage.ArchiveEntriesMined);
        Assert.Equal(1, summary.Coverage.ArchiveEntriesDeduplicated);
    }

    private static async Task<T> DeserializeEntryAsync<T>(ZipArchive archive, string name)
    {
        var entry = archive.GetEntry(name)
            ?? throw new InvalidDataException($"Package entry '{name}' was not found.");
        await using var stream = entry.Open();
        return await JsonSerializer.DeserializeAsync<T>(
                stream,
                new JsonSerializerOptions(JsonSerializerDefaults.Web)
                {
                    Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
                })
            ?? throw new InvalidDataException($"Package entry '{name}' is empty.");
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private readonly string _path = Path.Combine(
            Path.GetTempPath(),
            $"twbot-corpus-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(_path);

        public string Combine(params string[] paths) => Path.Combine([_path, .. paths]);

        public void Dispose() => Directory.Delete(_path, recursive: true);
    }
}
