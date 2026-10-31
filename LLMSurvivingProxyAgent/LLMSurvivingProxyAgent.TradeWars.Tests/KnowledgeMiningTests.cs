using System.IO.Compression;
using System.Text;
using LLMSurvivingProxyAgent.Knowledge;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class KnowledgeMiningTests
{
    [Fact]
    public void DocumentationRolesDistinguishTribalTacticalGuidesFromHelpReferences()
    {
        Assert.Equal(
            DocumentationSourceRole.TacticalGuide,
            DocumentationSourceClassifier.ClassifyPath("SLICE-10.TXT"));
        Assert.Equal(
            DocumentationSourceRole.HelpReference,
            DocumentationSourceClassifier.ClassifyPath("TLXTW204.DOC"));
        Assert.Equal(
            DocumentationSourceRole.HelpReference,
            DocumentationSourceClassifier.ClassifyPath("Trade Wars 2002 v3 - What's New.html"));
        Assert.Equal(
            DocumentationSourceRole.HelpReference,
            DocumentationSourceClassifier.ClassifyPath("CompNavigation.html"));
        Assert.Equal(
            DocumentationSourceRole.HelpReference,
            DocumentationSourceClassifier.ClassifyPath("CorporateMenuMap.html"));
        Assert.Equal(
            DocumentationSourceRole.TacticalGuide,
            DocumentationSourceClassifier.ClassifyPath("twxwiki/tiddlers/SST_Strategy_Guide.tid"));
        Assert.Equal(
            DocumentationSourceRole.HelpReference,
            DocumentationSourceClassifier.ClassifyPath("twxwiki/tiddlers/generated-mombot/bust.tid"));
    }

    [Fact]
    public async Task MineConfiguredRootsExtractsReviewableNodesAndSkipsUnchangedFiles()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"twbot-knowledge-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(temporaryDirectory, "sources");
        Directory.CreateDirectory(sourceDirectory);
        var sourcePath = Path.Combine(sourceDirectory, "TradeRoutine.cts");
        await File.WriteAllTextAsync(sourcePath, """
            # password = "very-secret"
            # Avoid trading through hostile sectors; verify the route first.
            # Goal: preserve credits and bootstrap port trading.
            # Preconditions: the command prompt is active and turns are available.
            # Validation: verify the route before trading.
            # Verification loop: re-check the port inventory after selling.
            # Retry pattern: retry after the port reports no stock.
            # Resume pattern: resume from the saved sector checkpoint.
            # Abort condition: abort when the bust signal is confirmed.
            # Escalation condition: alert the operator when a hostile is detected.
            # Recovery checkpoint: save the current sector before trading.
            # Recovery pattern: recover by returning to the last verified sector.
            # Sector 12 warps to 13, 14.
            # urgent response should be considered quickly
            setTextLineTrigger HOSTILE :HandleHostile "Hostile enters sector"
            pause
            :HandleHostile
            killtrigger HOSTILE
            setVar $failed TRUE
            goto :TradePair
            if ($location = 0)
            saveVar $tradeMode
            gosub :TradePair
            :TradePair
            waitFor "Command"
            """);

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(temporaryDirectory, "knowledge.db"),
                ForeignKeys = true,
                Pooling = false
            }.ToString();
            var store = new SqliteKnowledgeStore(connectionString);
            await store.InitializeAsync();
            var mining = new KnowledgeMiningService(
                store,
                [new KnowledgeSourceRoot("test-scripts", sourceDirectory)]);

            var firstReport = Assert.Single(await mining.MineConfiguredRootsAsync());
            Assert.Equal(1, firstReport.FilesMined);
            Assert.Equal(0, firstReport.FilesUnchanged);

            var nodes = await store.SearchAsync(limit: 100);
            Assert.Contains(nodes, node => node.Category == KnowledgeCategory.GameplayMechanic && node.Title == "Port trading");
            Assert.Contains(nodes, node => node.Category == KnowledgeCategory.TacticalGuidance);
            Assert.Contains(nodes, node => node.Category == KnowledgeCategory.StrategicGuidance);
            Assert.Contains(nodes, node => node.Category == KnowledgeCategory.WorldKnowledge);
            var triggerPattern = Assert.Single(nodes, node =>
                node.Category == KnowledgeCategory.ReactiveTriggerPattern &&
                node.Title == "Trigger: Text line");
            Assert.Contains(triggerPattern.Evidence, evidence => evidence.Signal == "configured condition and arguments");
            Assert.Contains(triggerPattern.Evidence, evidence => evidence.Signal == "handler target");
            Assert.Contains(triggerPattern.Evidence, evidence => evidence.Signal == "handler response candidate");
            Assert.Contains(triggerPattern.Evidence, evidence => evidence.Signal == "possible failure or recovery handling");
            Assert.Contains(triggerPattern.Evidence, evidence => evidence.Signal == "explicit urgency annotation");
            Assert.Contains("Urgency, runtime firing, outcome", triggerPattern.Summary, StringComparison.Ordinal);

            var macro = Assert.Single(nodes, node =>
                node.Category == KnowledgeCategory.MacroDefinition &&
                node.Title == "Script: Trade Routine");
            var redactedSource = Assert.Single(macro.Evidence, evidence => evidence.LineNumber == 1);
            Assert.Contains("[REDACTED]", redactedSource.Text);
            Assert.DoesNotContain("very-secret", redactedSource.Text, StringComparison.Ordinal);
            Assert.Contains(macro.Evidence, evidence => evidence.Signal == "observed goals");
            Assert.Contains(macro.Evidence, evidence => evidence.Signal == "preconditions");
            Assert.Contains(macro.Evidence, evidence => evidence.Signal == "validation checks");
            Assert.Contains(macro.Evidence, evidence => evidence.Signal == "verification loops");
            Assert.Contains(macro.Evidence, evidence => evidence.Signal == "retry patterns");
            Assert.Contains(macro.Evidence, evidence => evidence.Signal == "resume patterns");
            Assert.Contains(macro.Evidence, evidence => evidence.Signal == "abort conditions");
            Assert.Contains(macro.Evidence, evidence => evidence.Signal == "escalation conditions");
            Assert.Contains(macro.Evidence, evidence => evidence.Signal == "recovery checkpoints");
            Assert.Contains(macro.Evidence, evidence => evidence.Signal == "recovery patterns");

            var patterns = nodes.Where(node => node.Category == KnowledgeCategory.MacroConstructionPattern).ToArray();
            Assert.Contains(patterns, node => node.Title == "Precondition checks");
            Assert.Contains(patterns, node => node.Title == "Persistent configuration");
            Assert.Contains(patterns, node => node.Title == "Prompt and event handling");
            var macroRelations = await store.GetRelationsAsync(macro.Id);
            Assert.All(patterns, pattern => Assert.Contains(
                macroRelations,
                relation => relation.SourceNodeId == macro.Id &&
                            relation.TargetNodeId == pattern.Id &&
                            relation.Type == KnowledgeRelationType.DemonstratesPattern &&
                            relation.Confidence == pattern.Confidence &&
                            relation.Basis == pattern.Basis &&
                            relation.Evidence.SequenceEqual(pattern.Evidence)));
            Assert.Contains(macroRelations, relation =>
                relation.Type == KnowledgeRelationType.CallsSubroutine &&
                relation.Evidence.Count > 0);
            Assert.Contains(macroRelations, relation =>
                relation.TargetNodeId == triggerPattern.Id &&
                relation.Type == KnowledgeRelationType.DemonstratesPattern);

            var graph = await store.TraverseAsync(macro.Id, maxDepth: 1);
            Assert.Contains(graph, result => result.Node.Id == macro.Id && result.Depth == 0);
            Assert.Contains(graph, result => result.Depth == 1 &&
                                            result.Path[0].Type == KnowledgeRelationType.DemonstratesPattern);

            var tacticalNode = Assert.Single(nodes, node => node.Category == KnowledgeCategory.TacticalGuidance);
            Assert.Contains(tacticalNode.Evidence, evidence => evidence.LineNumber == 2);

            var secondReport = Assert.Single(await mining.MineConfiguredRootsAsync());
            Assert.Equal(0, secondReport.FilesMined);
            Assert.Equal(1, secondReport.FilesUnchanged);

            Assert.Contains(await store.SearchAsync("Port trading"), node => node.Title == "Port trading");
            var triggerSourceCount = Assert.Single(await store.GetCategorySourceCountsAsync(
                KnowledgeCategory.ReactiveTriggerPattern));
            Assert.Equal("test-scripts", triggerSourceCount.RootId);
            Assert.Equal(1, triggerSourceCount.SourceCount);
            Assert.Single(await store.GetDocumentsAsync(rootId: "test-scripts"));

            await File.WriteAllTextAsync(sourcePath, "# This file now describes no game behavior.\n");
            var changedReport = Assert.Single(await mining.MineConfiguredRootsAsync());
            Assert.Equal(1, changedReport.FilesMined);
            Assert.Empty(await store.GetRelationsAsync(macro.Id));
            Assert.DoesNotContain(await store.SearchAsync("Port trading"), node => node.Title == "Port trading");
            Assert.Empty(await store.GetCategorySourceCountsAsync(KnowledgeCategory.ReactiveTriggerPattern));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task MineConfiguredRootsExtractsCooperativeDoctrineWithoutInventingResponsesOrLeakingSettings()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"twbot-cooperation-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(temporaryDirectory, "sources");
        Directory.CreateDirectory(sourceDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(sourceDirectory, "Cooperation.txt"),
            """
            FURB - Buys and delivers a Ship to a Corpy to attack for holds.
            1. Notify the corp of a trapped trader.
            2. Transfer fighters to the trapped trader.
            If fighters fall below 1000, retreat because the grid is unsafe.
            Need figs on SubSpace; reply with fighters.
            Send out a Save Me hail if photoned, stuck or podded while gridded.
            Option to broadcast bust information on radio for teammates.
            The corp will be made or joined; ships and planets are owned by the corp.
            Scout: Gathers intel and reports to the corp.
            CORPPW:private-password
            SSCHANNEL:12345
            """);
        await File.WriteAllTextAsync(
            Path.Combine(sourceDirectory, "Safety.cts"),
            """
            if ($fighters < 1000) goto :retreat
            :retreat
            """);
        await File.WriteAllTextAsync(
            Path.Combine(sourceDirectory, "Messages.cts"),
            """
            send "'<" & $subspace & ">[Busted:" & $CURRENT_SECTOR & "]<" & $subspace & ">*"
            send "'" $pbotdisplayname ": photoned " $intrudername " in " $deploysector " from " $offensivesector.warp[$offensivepass] ".*"
            send "'Photon mode engaged: photons locked and loaded.*"
            send "'Need figs sector " $sector "*"
            send "'BotName - type help for commands*"
            send "'Fighters to drop cannot exceed total ship fighters*"
            send "a*"
            """);
        await File.WriteAllTextAsync(
            Path.Combine(sourceDirectory, "KeywordNoise.txt"),
            "Help. Corp. Rescue. Fighters. Attack.\nSubSpace message. Radio.\n");

        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(temporaryDirectory, "knowledge.db"),
                ForeignKeys = true,
                Pooling = false
            }.ToString();
            var store = new SqliteKnowledgeStore(connectionString);
            await store.InitializeAsync();
            var mining = new KnowledgeMiningService(
                store,
                [new KnowledgeSourceRoot("cooperation", sourceDirectory)]);

            await mining.MineConfiguredRootsAsync();
            var nodes = await store.SearchAsync(limit: 200);

            var furb = Assert.Single(nodes, node =>
                node.Category == KnowledgeCategory.CooperativeProcedure &&
                node.Title == "Cooperative procedure: FURB provisioning");
            Assert.Contains("Buys and delivers", Assert.Single(furb.Evidence).Text);
            Assert.DoesNotContain(furb.Evidence, evidence => evidence.Details?.Steps is { Count: > 0 });

            var orderedProcedure = Assert.Single(nodes, node =>
                node.Category == KnowledgeCategory.CooperativeProcedure &&
                node.Title == "Cooperative procedure: Cooperation");
            Assert.Equal(
                ["Notify the corp of a trapped trader.", "Transfer fighters to the trapped trader."],
                orderedProcedure.Evidence[0].Details?.Steps?.ToArray() ?? []);

            var communication = Assert.Single(nodes, node =>
                node.Category == KnowledgeCategory.CommunicationPattern &&
                node.Title == "Communication pattern: fighters request");
            var communicationDetails = Assert.Single(communication.Evidence).Details;
            Assert.Equal("SubSpace", communicationDetails?.Channel);
            Assert.Equal("Respond to the explicit request for fighters", communicationDetails?.RequiredAction);
            Assert.Equal("fighters", communicationDetails?.ExpectedResponse);

            Assert.Contains(nodes, node =>
                node.Category == KnowledgeCategory.EmergencyResponse &&
                node.Evidence.Any(evidence => evidence.Details?.Trigger == "photoned"));
            Assert.Contains(nodes, node =>
                node.Category == KnowledgeCategory.EmergencyResponse &&
                node.Evidence.Any(evidence => evidence.Details?.Trigger == "bust"));
            Assert.Contains(nodes, node =>
                node.Category == KnowledgeCategory.ResourceTransfer &&
                node.Title == "Resource transfer: ship provisioning");
            Assert.Contains(nodes, node =>
                node.Category == KnowledgeCategory.CorpProtocol);
            Assert.Contains(nodes, node =>
                node.Category == KnowledgeCategory.RoleDefinition &&
                node.Title == "Role definition: Scout");

            var proseTactic = Assert.Single(nodes, node =>
                node.Category == KnowledgeCategory.TacticalPattern &&
                node.Evidence.Any(evidence => evidence.Signal == "explicit condition and action"));
            var proseDetails = Assert.Single(proseTactic.Evidence).Details;
            Assert.Equal("fighters fall below 1000", proseDetails?.Trigger);
            Assert.Equal("retreat", proseDetails?.Action);
            Assert.Equal("the grid is unsafe", proseDetails?.Rationale);

            var staticTactic = Assert.Single(nodes, node =>
                node.Category == KnowledgeCategory.TacticalPattern &&
                node.Evidence.Any(evidence => evidence.Signal == "explicit safety branch"));
            Assert.Equal("$fighters < 1000", Assert.Single(staticTactic.Evidence).Details?.Trigger);
            Assert.Equal("goto :retreat", Assert.Single(staticTactic.Evidence).Details?.Action);
            var safetyMacro = Assert.Single(nodes, node =>
                node.Category == KnowledgeCategory.MacroDefinition &&
                node.Title == "Script: Safety");
            Assert.Contains(
                await store.GetRelationsAsync(safetyMacro.Id),
                relation => relation.TargetNodeId == staticTactic.Id &&
                    relation.Type == KnowledgeRelationType.DemonstratesPattern);

            var cooperativeNodes = nodes.Where(node => node.Category is
                KnowledgeCategory.CommunicationPattern or
                KnowledgeCategory.CooperativeProcedure or
                KnowledgeCategory.CorpProtocol or
                KnowledgeCategory.EmergencyResponse or
                KnowledgeCategory.ResourceTransfer or
                KnowledgeCategory.RoleDefinition or
                KnowledgeCategory.TacticalPattern);
            Assert.DoesNotContain(
                cooperativeNodes.SelectMany(node => node.Evidence),
                evidence => evidence.Text.Contains("private-password", StringComparison.Ordinal) ||
                    evidence.Text.Contains("12345", StringComparison.Ordinal));
            Assert.DoesNotContain(nodes, node =>
                (node.Category is KnowledgeCategory.CommunicationPattern or
                    KnowledgeCategory.CooperativeProcedure or
                    KnowledgeCategory.CorpProtocol or
                    KnowledgeCategory.EmergencyResponse or
                    KnowledgeCategory.ResourceTransfer or
                    KnowledgeCategory.RoleDefinition or
                    KnowledgeCategory.TacticalPattern) &&
                node.Evidence.Any(evidence =>
                    evidence.Text.Contains("Help.", StringComparison.Ordinal) ||
                    evidence.Text.Contains("SubSpace message.", StringComparison.Ordinal)));

            var bustMessage = Assert.Single(nodes, node =>
                node.Category == KnowledgeCategory.CommunicationPattern &&
                node.Title == "Communication pattern: SubSpace bust report");
            var bustDetails = Assert.Single(bustMessage.Evidence).Details;
            Assert.Equal(
                "'<{dynamic}>[Busted:{dynamic}]<{dynamic}>*",
                bustDetails?.MessageTemplate);
            Assert.Equal("SubSpace", bustDetails?.Channel);
            Assert.Equal("bust report", bustDetails?.Intent);
            Assert.Null(bustDetails?.RequiredAction);
            Assert.Null(bustDetails?.ExpectedResponse);
            Assert.DoesNotContain("$CURRENT_SECTOR", bustDetails?.MessageTemplate);

            var photonMessage = Assert.Single(nodes, node =>
                node.Category == KnowledgeCategory.CommunicationPattern &&
                node.Title == "Communication pattern: SubSpace threat report");
            Assert.Contains(
                "{dynamic}: photoned {dynamic}",
                Assert.Single(photonMessage.Evidence).Details?.MessageTemplate);
            Assert.Contains(nodes, node =>
                node.Category == KnowledgeCategory.CommunicationPattern &&
                node.Title == "Communication pattern: SubSpace photon status" &&
                node.Evidence.Any(evidence =>
                    evidence.Details?.MessageTemplate?.Contains(
                        "photons locked and loaded",
                        StringComparison.OrdinalIgnoreCase) == true));

            Assert.Contains(nodes, node =>
                node.Category == KnowledgeCategory.CommunicationPattern &&
                node.Title == "Communication pattern: SubSpace resource request" &&
                node.Evidence.Any(evidence =>
                    evidence.Signal == "explicit outbound SubSpace message template"));
            Assert.DoesNotContain(nodes, node =>
                node.Category == KnowledgeCategory.CommunicationPattern &&
                node.Evidence.Any(evidence =>
                    evidence.Details?.MessageTemplate?.Contains(
                        "type help for commands",
                        StringComparison.OrdinalIgnoreCase) == true ||
                    evidence.Details?.MessageTemplate?.Contains(
                        "total ship fighters",
                        StringComparison.OrdinalIgnoreCase) == true));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task CategorySourceCountsAggregateDistinctDocumentsByRoot()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"twbot-knowledge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(temporaryDirectory, "knowledge.db"),
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        try
        {
            var store = new SqliteKnowledgeStore(connectionString);
            await store.InitializeAsync();
            var pattern = Node("pattern-a", KnowledgeCategory.ReactiveTriggerPattern, "Trigger: Text line");
            await store.ReplaceDocumentAsync(Document("doc-a", pattern) with { RootId = "pink-scripts" });
            await store.ReplaceDocumentAsync(Document("doc-b", pattern with { Id = "pattern-b" }) with { RootId = "pink-scripts" });
            await store.ReplaceDocumentAsync(Document("doc-c", pattern with { Id = "pattern-c" }) with { RootId = "mombot" });

            var counts = await store.GetCategorySourceCountsAsync(KnowledgeCategory.ReactiveTriggerPattern);

            Assert.Collection(
                counts,
                count =>
                {
                    Assert.Equal("mombot", count.RootId);
                    Assert.Equal("Trigger: Text line", count.Title);
                    Assert.Equal(1, count.SourceCount);
                },
                count =>
                {
                    Assert.Equal("pink-scripts", count.RootId);
                    Assert.Equal("Trigger: Text line", count.Title);
                    Assert.Equal(2, count.SourceCount);
                });
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task MarkdownMiningExtractsDecisionContextAsCitedProse()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"twbot-decision-context-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var guidePath = Path.Combine(temporaryDirectory, "sst-guide.md");
        await File.WriteAllTextAsync(guidePath, """
            # SST tactical guide
            Use SST when credits are low and turns are available.
            Avoid SST when competition is high.
            Goal: Generate credits quickly.
            Reason: It uses known safe routes.
            Assumptions: Safe routes are known.
            Prerequisites: Turns and trading holds are available.
            Risk: Busts may reduce credits.
            Benefit: Fast income generation.
            Expected outcome: Generate credits quickly.
            Alternative: Pair trading.
            Counterexample: Do not use SST in hostile space.
            Stop when the reserve is restored.
            Escalation condition: Alert the operator on hostile detection.
            """);

        try
        {
            var store = new SqliteKnowledgeStore(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(temporaryDirectory, "knowledge.db"),
                ForeignKeys = true,
                Pooling = false
            }.ToString());
            await store.InitializeAsync();
            var mining = new KnowledgeMiningService(
                store,
                [new KnowledgeSourceRoot("guides", temporaryDirectory)]);

            var report = Assert.Single(await mining.MineConfiguredRootsAsync());
            var document = Assert.Single(await store.GetAllDocumentsAsync());
            var decisionNodes = document.Nodes
                .Where(node => node.Category == KnowledgeCategory.DecisionContext)
                .ToArray();

            Assert.Equal(1, report.FilesMined);
            Assert.Contains(decisionNodes, node =>
                node.Title == "Decision context: Usage conditions" &&
                node.Evidence.Any(evidence => evidence.LineNumber == 2 &&
                    evidence.Text == "Use SST when credits are low and turns are available."));
            Assert.Contains(decisionNodes, node => node.Title == "Decision context: Avoidance conditions");
            Assert.Contains(decisionNodes, node => node.Title == "Decision context: Objective");
            Assert.Contains(decisionNodes, node => node.Title == "Decision context: Intent");
            Assert.Contains(decisionNodes, node => node.Title == "Decision context: Assumptions");
            Assert.Contains(decisionNodes, node => node.Title == "Decision context: Prerequisites");
            Assert.Contains(decisionNodes, node => node.Title == "Decision context: Risks");
            Assert.Contains(decisionNodes, node => node.Title == "Decision context: Benefits");
            Assert.Contains(decisionNodes, node => node.Title == "Decision context: Expected outcomes");
            Assert.Contains(decisionNodes, node => node.Title == "Decision context: Alternatives");
            Assert.Contains(decisionNodes, node => node.Title == "Decision context: Counterexamples");
            Assert.Contains(decisionNodes, node => node.Title == "Decision context: Stopping conditions");
            Assert.Contains(decisionNodes, node => node.Title == "Decision context: Escalation conditions");
            Assert.All(decisionNodes, node =>
            {
                Assert.Equal(ExtractionBasis.ExplicitText, node.Basis);
                Assert.Contains("not independently validated", node.Summary, StringComparison.OrdinalIgnoreCase);
                Assert.All(node.Evidence, evidence => Assert.True(evidence.LineNumber > 0));
            });
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task HtmlAndTextEncodedLegacyWordDocumentsMineAsCitedProse()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"twbot-document-mining-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var htmlPath = Path.Combine(temporaryDirectory, "tactics.html");
        var wordPath = Path.Combine(temporaryDirectory, "tactics.doc");
        await File.WriteAllTextAsync(htmlPath, """
            <html>
            <head><script>Use SST when credits are low and turns are available.</script></head>
            <body>
            <p>Use SST when credits are low and turns are available.</p>
            <p>Avoid SST when hostile sectors are active.</p>
            </body>
            </html>
            """);
        await File.WriteAllTextAsync(wordPath, """
            TLXTW tactical notes
            Use SST when credits are low and turns are available.
            Avoid SST when hostile sectors are active.
            """);

        try
        {
            var store = new SqliteKnowledgeStore(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(temporaryDirectory, "knowledge.db"),
                ForeignKeys = true,
                Pooling = false
            }.ToString());
            await store.InitializeAsync();
            var mining = new KnowledgeMiningService(
                store,
                [new KnowledgeSourceRoot("tribal", temporaryDirectory)]);

            var report = Assert.Single(await mining.MineConfiguredRootsAsync());
            var documents = await store.GetAllDocumentsAsync();
            var htmlDocument = Assert.Single(documents, document => document.FileName == "tactics.html");
            var wordDocument = Assert.Single(documents, document => document.FileName == "tactics.doc");

            Assert.Equal(2, report.FilesMined);
            Assert.Equal("HTML", htmlDocument.Language);
            Assert.Equal("WordDocumentText", wordDocument.Language);
            Assert.Contains(htmlDocument.Nodes, node =>
                node.Category == KnowledgeCategory.DecisionContext &&
                node.Title == "Decision context: Usage conditions" &&
                node.Evidence.Any(evidence => evidence.LineNumber == 4 &&
                    evidence.Text == "Use SST when credits are low and turns are available."));
            Assert.DoesNotContain(htmlDocument.Nodes.SelectMany(node => node.Evidence), evidence =>
                evidence.Text.Contains("<script>", StringComparison.OrdinalIgnoreCase) ||
                evidence.Text.Contains("script>Use SST", StringComparison.OrdinalIgnoreCase));
            Assert.Contains(wordDocument.Nodes, node =>
                node.Category == KnowledgeCategory.DecisionContext &&
                node.Title == "Decision context: Usage conditions" &&
                node.Evidence.Any(evidence => evidence.LineNumber == 2));
            Assert.Contains(wordDocument.Nodes, node =>
                node.Category == KnowledgeCategory.DecisionContext &&
                node.Title == "Decision context: Avoidance conditions" &&
                node.Evidence.Any(evidence => evidence.LineNumber == 3));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task BinaryLegacyWordDocumentsFailWithConversionGuidance()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"twbot-binary-doc-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllBytesAsync(
            Path.Combine(temporaryDirectory, "binary.doc"),
            [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1, 0x00]);

        try
        {
            var store = new SqliteKnowledgeStore(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(temporaryDirectory, "knowledge.db"),
                ForeignKeys = true,
                Pooling = false
            }.ToString());
            await store.InitializeAsync();
            var mining = new KnowledgeMiningService(
                store,
                [new KnowledgeSourceRoot("tribal", temporaryDirectory)]);

            var exception = await Assert.ThrowsAsync<InvalidDataException>(
                () => mining.MineConfiguredRootsAsync());

            Assert.Contains("convert the document", exception.Message, StringComparison.OrdinalIgnoreCase);
            Assert.Empty(await store.GetAllDocumentsAsync());
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task HelpReferenceDocumentsDoNotCreateDecisionContextFromGenericProse()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"twbot-help-reference-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "Command Menu.html"),
            """
            <html>
            <body>
            <p>Use with caution, because you might miss an important message.</p>
            <p>This selection provides an easy and efficient way to trade commodities.</p>
            </body>
            </html>
            """);

        try
        {
            var store = new SqliteKnowledgeStore(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(temporaryDirectory, "knowledge.db"),
                ForeignKeys = true,
                Pooling = false
            }.ToString());
            await store.InitializeAsync();
            var mining = new KnowledgeMiningService(
                store,
                [new KnowledgeSourceRoot("tribal", temporaryDirectory)]);

            await mining.MineConfiguredRootsAsync();

            var document = Assert.Single(await store.GetAllDocumentsAsync());
            Assert.Equal(
                DocumentationSourceRole.HelpReference,
                DocumentationSourceClassifier.ClassifyPath(document.RelativePath));
            Assert.DoesNotContain(document.Nodes, node => node.Category == KnowledgeCategory.DecisionContext);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task GenericHelpKeywordsDoNotBecomeDecisionContextEvidence()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"twbot-decision-context-noise-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(Path.Combine(temporaryDirectory, "commands.txt"), """
            BUST - Pops planets to reach desired experience.
            Menu option V allows you to reset the display.
            Move to the target sector.
            The command checks status and repeats until complete.
            This command clears an avoid if the sector number is supplied.
            """);

        try
        {
            var store = new SqliteKnowledgeStore(new SqliteConnectionStringBuilder
            {
                DataSource = Path.Combine(temporaryDirectory, "knowledge.db"),
                ForeignKeys = true,
                Pooling = false
            }.ToString());
            await store.InitializeAsync();
            var mining = new KnowledgeMiningService(
                store,
                [new KnowledgeSourceRoot("guides", temporaryDirectory)]);

            await mining.MineConfiguredRootsAsync();

            var document = Assert.Single(await store.GetAllDocumentsAsync());
            Assert.DoesNotContain(document.Nodes, node =>
                node.Category == KnowledgeCategory.DecisionContext);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task MiningArchiveEntriesPreservesProvenanceAndDeduplicatesExactCopies()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"twbot-knowledge-{Guid.NewGuid():N}");
        var sourceDirectory = Path.Combine(temporaryDirectory, "sources");
        Directory.CreateDirectory(sourceDirectory);
        var duplicateText = "# Purpose: Preserve credits while trading.\n";
        await File.WriteAllTextAsync(Path.Combine(sourceDirectory, "existing.ts"), duplicateText);
        var archivePath = Path.Combine(sourceDirectory, "legacy.zip");
        await using (var archiveStream = File.Create(archivePath))
        using (var archive = new ZipArchive(archiveStream, ZipArchiveMode.Create))
        {
            await WriteArchiveEntryAsync(archive, "scripts/copy.ts", duplicateText);
            await WriteArchiveEntryAsync(
                archive,
                "guides/sst.txt",
                "Avoid SST when credits are low and turns are scarce.");
            await WriteArchiveEntryAsync(archive, "../outside.ts", "This path must not be mined.");
            await WriteArchiveEntryAsync(archive, "ignored.exe", "This is not an allowed text source.");
            await WriteArchiveEntryAsync(
                archive,
                "large.txt",
                new string('x', (int)KnowledgeSourcePolicy.MaximumSourceBytes + 1));
        }

        try
        {
            var store = await CreateKnowledgeStoreAsync(temporaryDirectory);
            var mining = new KnowledgeMiningService(
                store,
                [new KnowledgeSourceRoot("pink", sourceDirectory)]);

            var report = Assert.Single(await mining.MineConfiguredRootsAsync());
            var documents = await store.GetAllDocumentsAsync();

            Assert.Equal(2, report.FilesFound);
            Assert.Equal(2, report.FilesMined);
            Assert.Equal(3, report.ArchiveEntriesFound);
            Assert.Equal(1, report.ArchiveEntriesMined);
            Assert.Equal(1, report.ArchiveEntriesDeduplicated);
            Assert.Equal(1, report.ArchiveEntriesTooLarge);
            Assert.Contains(documents, document =>
                document.RelativePath == "legacy.zip!/guides/sst.txt");
            Assert.DoesNotContain(documents, document =>
                document.RelativePath.Contains("copy.ts", StringComparison.Ordinal));
            Assert.DoesNotContain(documents, document =>
                document.RelativePath.Contains("outside.ts", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task MiningTiddlersAndTelixInputsPreservesMetadataAndOnlyUsesScriptComments()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"twbot-knowledge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "SST_Strategy_Guide.tid"),
            """
            created: 20261024120000000
            modified: 20261024120000000
            title: SST Strategy
            tags: strategy tactics
            type: text/vnd.tiddlywiki

            ! SST
            Use SST when credits are low and turns are available.
            Avoid SST when competition is high.
            """);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "TRADER.KEY"),
            "1=\"@Attkport\"\n20011=\"@Denscan\"\n");
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "CRAI.SLT"),
            """
            // Avoid: trading through hostile sectors risks fighters.
            send "Attack hostile sector"
            """);
        Assert.True(KnowledgeSourcePolicy.IsSupportedSourceFile("TRADER.KEY", "tribal"));
        Assert.False(KnowledgeSourcePolicy.IsSupportedSourceFile("TRADER.KEY", "pink"));

        try
        {
            var store = await CreateKnowledgeStoreAsync(temporaryDirectory);
            var mining = new KnowledgeMiningService(
                store,
                [new KnowledgeSourceRoot("tribal", temporaryDirectory)]);

            await mining.MineConfiguredRootsAsync();

            var documents = await store.GetAllDocumentsAsync();
            var tiddler = Assert.Single(documents, document => document.FileName == "SST_Strategy_Guide.tid");
            Assert.Equal("TiddlyWiki", tiddler.Language);
            var useConditions = Assert.Single(tiddler.Nodes, node =>
                node.Category == KnowledgeCategory.DecisionContext &&
                node.Title == "Decision context: Usage conditions");
            Assert.Contains(useConditions.Evidence, evidence =>
                evidence.LineNumber == 8 &&
                evidence.SourceLocator!.Contains("title=SST Strategy", StringComparison.Ordinal) &&
                evidence.SourceLocator.Contains("tags=strategy tactics", StringComparison.Ordinal) &&
                evidence.SourceLocator.Contains("type=text/vnd.tiddlywiki", StringComparison.Ordinal));

            var keyMap = Assert.Single(documents, document => document.FileName == "TRADER.KEY");
            Assert.Equal("TelixKeyMap", keyMap.Language);
            Assert.Contains(keyMap.Nodes, node =>
                node.Title == "Telix key binding: 1" &&
                node.Summary.Contains("@Attkport", StringComparison.Ordinal));

            var telixScript = Assert.Single(documents, document => document.FileName == "CRAI.SLT");
            Assert.Equal("TelixScript", telixScript.Language);
            Assert.Contains(telixScript.Nodes, node =>
                node.Category == KnowledgeCategory.DecisionContext &&
                node.Title == "Decision context: Avoidance conditions");
            Assert.DoesNotContain(telixScript.Nodes.SelectMany(node => node.Evidence), evidence =>
                evidence.Text.Contains("send \"Attack", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task MiningHistoricalPinkSuffixesUsesTheirOriginalTwxLanguage()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"twbot-knowledge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "pbot.ts.backup"),
            "# Historical Pink script\nsetVar $enabled TRUE\n");
        await File.WriteAllTextAsync(
            Path.Combine(temporaryDirectory, "1_Login.ts.msfucked"),
            "# Historical modified login script\nconnect\n");

        try
        {
            var store = await CreateKnowledgeStoreAsync(temporaryDirectory);
            var mining = new KnowledgeMiningService(
                store,
                [new KnowledgeSourceRoot("pink", temporaryDirectory)]);

            var report = Assert.Single(await mining.MineConfiguredRootsAsync());
            var documents = await store.GetAllDocumentsAsync();

            Assert.Equal(2, report.FilesMined);
            Assert.All(documents, document => Assert.Equal("TWXInclude", document.Language));
            Assert.Contains(documents, document => document.FileName == "pbot.ts");
            Assert.Contains(documents, document => document.FileName == "1_Login.ts");
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task MiningSkipsOversizedFilesAndReportsTheSkip()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"twbot-knowledge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        await File.WriteAllBytesAsync(
            Path.Combine(temporaryDirectory, "large.txt"),
            new byte[4 * 1024 * 1024 + 1]);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(temporaryDirectory, "knowledge.db"),
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        try
        {
            var store = new SqliteKnowledgeStore(connectionString);
            await store.InitializeAsync();
            var mining = new KnowledgeMiningService(
                store,
                [new KnowledgeSourceRoot("oversized", temporaryDirectory)]);

            var report = Assert.Single(await mining.MineConfiguredRootsAsync());

            Assert.Equal(1, report.FilesFound);
            Assert.Equal(0, report.FilesMined);
            Assert.Equal(1, report.FilesTooLarge);
            Assert.Empty(await store.GetDocumentsAsync());
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static async Task<SqliteKnowledgeStore> CreateKnowledgeStoreAsync(string directory)
    {
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, $"knowledge-{Guid.NewGuid():N}.db"),
            ForeignKeys = true,
            Pooling = false
        }.ToString();
        var store = new SqliteKnowledgeStore(connectionString);
        await store.InitializeAsync();
        return store;
    }

    private static async Task WriteArchiveEntryAsync(ZipArchive archive, string name, string content)
    {
        var entry = archive.CreateEntry(name);
        await using var stream = entry.Open();
        var bytes = Encoding.UTF8.GetBytes(content);
        await stream.WriteAsync(bytes);
    }

    [Fact]
    public async Task TraverseReturnsEvidenceBearingPathsWithinRequestedDepth()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"twbot-knowledge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(temporaryDirectory, "knowledge.db"),
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        try
        {
            var store = new SqliteKnowledgeStore(connectionString);
            await store.InitializeAsync();
            var evidence = new[] { new KnowledgeEvidence(4, "explicit relationship", "A supports B.") };
            await store.ReplaceDocumentAsync(Document("source-a", Node("a", KnowledgeCategory.TacticalGuidance, "Tactic A")));
            await store.ReplaceDocumentAsync(Document("source-b", Node("b", KnowledgeCategory.StrategicGuidance, "Strategy B")));
            await store.ReplaceDocumentAsync(Document("source-c", Node("c", KnowledgeCategory.GameplayMechanic, "Mechanic C")));
            await store.ReplaceDocumentAsync(new KnowledgeDocument(
                "evidence-doc",
                "test",
                "evidence.txt",
                "evidence.txt",
                "hash",
                "Text",
                DateTimeOffset.UtcNow,
                [],
                []));
            await store.AddRelationAsync(new KnowledgeRelation(
                "evidence-doc",
                "a",
                "b",
                KnowledgeRelationType.Supports,
                0.8m,
                ExtractionBasis.ExplicitText,
                evidence));
            await store.AddRelationAsync(new KnowledgeRelation(
                "evidence-doc",
                "b",
                "c",
                KnowledgeRelationType.Enables,
                0.7m,
                ExtractionBasis.ExplicitText,
                evidence));
            await store.ReplaceDocumentAsync(Document(
                "source-a",
                Node("a", KnowledgeCategory.TacticalGuidance, "Tactic A updated")));

            var results = await store.TraverseAsync("a", maxDepth: 2);
            var depthTwo = Assert.Single(results, result => result.Node.Id == "c");

            Assert.Equal(2, depthTwo.Depth);
            Assert.Equal(
                [KnowledgeRelationType.Supports, KnowledgeRelationType.Enables],
                depthTwo.Path.Select(relation => relation.Type));
            Assert.All(depthTwo.Path, relation => Assert.NotEmpty(relation.Evidence));
            Assert.All(depthTwo.Path, relation => Assert.Equal("evidence-doc", relation.EvidenceDocumentId));
            Assert.Single(await store.GetRelationsAsync("a"));
            Assert.DoesNotContain(await store.TraverseAsync("a", maxDepth: 1), result => result.Node.Id == "c");
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    [Fact]
    public async Task InitializeMigratesLegacyRelationsWithEvidenceFromTheirTargetNode()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"twbot-knowledge-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(temporaryDirectory, "knowledge.db"),
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        try
        {
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE knowledge_documents (
                        document_id TEXT PRIMARY KEY, root_id TEXT NOT NULL, relative_path TEXT NOT NULL,
                        file_name TEXT NOT NULL, content_hash TEXT NOT NULL, language TEXT NOT NULL,
                        mined_at TEXT NOT NULL, UNIQUE(root_id, relative_path)
                    );
                    CREATE TABLE knowledge_nodes (
                        node_id TEXT PRIMARY KEY,
                        document_id TEXT NOT NULL REFERENCES knowledge_documents(document_id) ON DELETE CASCADE,
                        category TEXT NOT NULL, title TEXT NOT NULL, summary TEXT NOT NULL,
                        confidence REAL NOT NULL, extraction_basis TEXT NOT NULL, evidence_json TEXT NOT NULL
                    );
                    CREATE TABLE knowledge_relations (
                        source_node_id TEXT NOT NULL REFERENCES knowledge_nodes(node_id) ON DELETE CASCADE,
                        target_node_id TEXT NOT NULL REFERENCES knowledge_nodes(node_id) ON DELETE CASCADE,
                        relation TEXT NOT NULL,
                        PRIMARY KEY(source_node_id, target_node_id, relation)
                    );
                    INSERT INTO knowledge_documents VALUES (
                        'legacy-doc', 'test', 'legacy.cts', 'legacy.cts', 'hash', 'TWXScript', '2026-01-01T00:00:00Z'
                    );
                    INSERT INTO knowledge_nodes VALUES (
                        'legacy-source', 'legacy-doc', 'MacroDefinition', 'Legacy script', 'Source', 0.4,
                        'FilenameHeuristic', '[]'
                    );
                    INSERT INTO knowledge_nodes VALUES (
                        'legacy-target', 'legacy-doc', 'MacroConstructionPattern', 'Pattern', 'Pattern summary', 0.65,
                        'StaticStructure',
                        '[{"lineNumber":7,"signal":"matched source evidence","text":"if route is valid"}]'
                    );
                    INSERT INTO knowledge_relations VALUES (
                        'legacy-source', 'legacy-target', 'DemonstratesPattern'
                    );
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var store = new SqliteKnowledgeStore(connectionString);
            await store.InitializeAsync();
            var relation = Assert.Single(await store.GetRelationsAsync("legacy-source"));

            Assert.Equal(KnowledgeRelationType.DemonstratesPattern, relation.Type);
            Assert.Equal(0.5m, relation.Confidence);
            Assert.Equal(ExtractionBasis.StaticStructure, relation.Basis);
            Assert.Contains(relation.Evidence, item => item.LineNumber == 7);
        }
        finally
        {
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private static KnowledgeNode Node(string id, KnowledgeCategory category, string title) =>
        new(
            id,
            id,
            category,
            title,
            title,
            0.8m,
            ExtractionBasis.ExplicitText,
            [new KnowledgeEvidence(1, "test evidence", title)]);

    private static KnowledgeDocument Document(string documentId, KnowledgeNode node) =>
        new(
            documentId,
            "test",
            $"{documentId}.txt",
            $"{documentId}.txt",
            "hash",
            "Text",
            DateTimeOffset.UtcNow,
            [node with { DocumentId = documentId }],
            []);
}
