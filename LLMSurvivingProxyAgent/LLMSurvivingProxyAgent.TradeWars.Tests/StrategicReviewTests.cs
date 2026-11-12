using LLMSurvivingProxyAgent.Agent;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Goals;
using LLMSurvivingProxyAgent.Knowledge;
using LLMSurvivingProxyAgent.Macros;
using LLMSurvivingProxyAgent.Models;
using LLMSurvivingProxyAgent.Reactive;
using LLMSurvivingProxyAgent.Runtime;
using Microsoft.Data.Sqlite;
using System.Net;
using System.Text;
using System.Text.Json;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class StrategicReviewTests
{
    [Fact]
    public void SynchronizedShipLocationOverridesConflictingMtcSector()
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        var snapshot = new GameSnapshot(369, 500, 3, null, observedAt);
        var integratedGameClient = new IntegratedGameClientSnapshot(
            observedAt,
            TerminalConnected: true,
            TwxDatabaseAvailable: true,
            ScriptServiceAvailable: true,
            SectorCount: 1_000,
            CurrentSector: 551,
            StarDockSector: 1,
            LastPortCimAt: observedAt,
            RelevantSectors:
            [
                new IntegratedGameSectorSnapshot(
                    551,
                    "Yes",
                    "Federation",
                    "",
                    0,
                    [],
                    "BBS",
                    [],
                    [],
                    [],
                    "",
                    "",
                    "")
            ],
            RunningScripts: [],
            ScriptCapabilities: []);

        IntegratedGameClientSnapshot corrected =
            IntegratedGameClientLocationPolicy.UseSynchronizedShipLocation(snapshot, integratedGameClient)!;

        Assert.Equal(369, corrected.CurrentSector);
        Assert.Equal(1, corrected.StarDockSector);
        Assert.Empty(corrected.RelevantSectors);
    }

    [Fact]
    public async Task OllamaProviderUsesChatEndpointAndParsesStructuredAssessment()
    {
        var assessmentJson = JsonSerializer.Serialize(new
        {
            progress = "insufficientData",
            summary = "More verified observations are needed.",
            evidenceForProgress = Array.Empty<string>(),
            evidenceAgainstProgress = Array.Empty<string>(),
            stuckSignals = Array.Empty<string>(),
            uncertainAssumptions = new[] { "The snapshot is accurate." },
            overlookedOpportunities = Array.Empty<string>(),
            alternativeObjectives = Array.Empty<string>(),
            recommendation = "Hold until more history is available.",
            objectiveDecision = "hold",
            selectedProposalId = (string?)null,
            newObjective = (object?)null,
            citedKnowledgeNodeIds = Array.Empty<string>(),
            plannedScripts = Array.Empty<object>()
        });
        var responseJson = JsonSerializer.Serialize(new { message = new { content = assessmentJson } });
        var handler = new RecordingHttpHandler(responseJson);
        using var client = new HttpClient(handler);
        var interactionHistory = new LlmInteractionHistoryStore();
        var provider = new OllamaReasoningProvider(client, interactionHistory);
        var context = new StrategicReviewContext(
            new GameSnapshot(null, null, null, null, DateTimeOffset.UtcNow),
            [],
            new ReviewObjective("active-objective", "Learn prompts", "Observe prompt transitions.", 0.8m),
            [
                new ReviewObjective("proposal-a", "Learn trading", "Observe a verified port.", 0.8m),
                new ReviewObjective("proposal-b", "Learn movement", "Observe safe navigation.", 0.8m)
            ],
            [],
            EmptyFailureDetection(),
            EmptyReviewHistory(),
            [])
        {
            AgentId = "meridian",
            IntegratedGameClient = new IntegratedGameClientSnapshot(
                DateTimeOffset.UtcNow,
                TerminalConnected: true,
                TwxDatabaseAvailable: true,
                ScriptServiceAvailable: true,
                SectorCount: 1_000,
                CurrentSector: 551,
                StarDockSector: 1,
                LastPortCimAt: DateTimeOffset.UtcNow,
                [],
                [],
                [])
            {
                ScriptCatalogStatus = "Loaded 1 safe relative TWX script references.",
                ScriptCatalogTotalEntries = 1,
                ScriptCatalogEntries =
                [
                    new IntegratedGameScriptCatalogEntry("trading-cashing/2-worldtrade.ts", "Trading")
                ]
            }
        };

        var result = await provider.AssessAsync(
            new ModelConfiguration("Ollama", "qwen3:4b", new Uri("http://localhost:11434/")),
            context);

        Assert.Equal("http://localhost:11434/api/chat", handler.RequestUri?.ToString());
        Assert.Equal(ProgressAssessment.InsufficientData, result.Progress);
        Assert.Equal(ObjectiveDecision.Hold, result.ObjectiveDecision);
        Assert.False(string.IsNullOrWhiteSpace(result.InteractionId));
        var interaction = Assert.Single(interactionHistory.GetRecent("meridian"));
        Assert.Equal("Strategic Review", interaction.Role);
        Assert.Equal("qwen3:4b", interaction.Model);
        Assert.True(interaction.Succeeded);
        Assert.Contains("[system]", interaction.Prompt, StringComparison.Ordinal);
        Assert.Contains("Learn prompts", interaction.Prompt, StringComparison.Ordinal);
        Assert.Contains("Hold until more history is available.", interaction.Response, StringComparison.Ordinal);
        Assert.Contains("raw game commands", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains(
            "message patterns rather than captured exchanges",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "Objective fields are mutually exclusive",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "starDockShopper is only for verified StarDock hardware purchases",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "reaching StarDock safely",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "objective before World Trade",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "Density Scanner ownership is sufficient for safe exploring and World Trade",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            "requires verified Holo Scanner ownership at StarDock",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "Do not trade in an Escape Pod",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains(
            "Unknown exploration status is not a scan or safety evidence",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains("trading-cashing/2-worldtrade.ts", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("unreviewed discovery metadata", handler.RequestBody, StringComparison.Ordinal);

        using var request = JsonDocument.Parse(handler.RequestBody);
        var format = request.RootElement.GetProperty("format");
        Assert.Equal("object", format.GetProperty("type").GetString());
        Assert.False(format.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            "string",
            format.GetProperty("properties")
                .GetProperty("alternativeObjectives")
                .GetProperty("items")
                .GetProperty("type")
                .GetString());
        Assert.Contains(
            "selectProposal",
            format.GetProperty("properties")
                .GetProperty("objectiveDecision")
                .GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            ["proposal-a", "proposal-b"],
            format.GetProperty("properties")
                .GetProperty("selectedProposalId")
                .GetProperty("anyOf")[0]
                .GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()));
        JsonElement[] objectiveCombinations = format.GetProperty("anyOf").EnumerateArray().ToArray();
        JsonElement proposeAndSelect = Assert.Single(objectiveCombinations.Where(combination =>
            combination.GetProperty("properties")
                .GetProperty("objectiveDecision")
                .GetProperty("const")
                .GetString() == "proposeAndSelect"));
        Assert.Equal(
            "null",
            proposeAndSelect.GetProperty("properties")
                .GetProperty("selectedProposalId")
                .GetProperty("type")
                .GetString());
        Assert.Equal(
            "object",
            proposeAndSelect.GetProperty("properties")
                .GetProperty("newObjective")
                .GetProperty("type")
                .GetString());
        JsonElement selectProposal = Assert.Single(objectiveCombinations.Where(combination =>
            combination.GetProperty("properties")
                .GetProperty("objectiveDecision")
                .GetProperty("const")
                .GetString() == "selectProposal"));
        Assert.Equal(
            "null",
            selectProposal.GetProperty("properties")
                .GetProperty("newObjective")
                .GetProperty("type")
                .GetString());
        Assert.Equal(
            "string",
            selectProposal.GetProperty("properties")
                .GetProperty("selectedProposalId")
                .GetProperty("type")
                .GetString());
        Assert.Equal(
            ["starDockShopper", "worldTrade"],
            format.GetProperty("properties")
                .GetProperty("plannedScripts")
                .GetProperty("items")
                .GetProperty("properties")
                .GetProperty("script")
                .GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()));

        string invalidAssessmentJson = JsonSerializer.Serialize(new
        {
            progress = "insufficientData",
            summary = "More verified observations are needed.",
            evidenceForProgress = Array.Empty<string>(),
            evidenceAgainstProgress = Array.Empty<string>(),
            stuckSignals = Array.Empty<string>(),
            uncertainAssumptions = Array.Empty<string>(),
            overlookedOpportunities = Array.Empty<string>(),
            alternativeObjectives = Array.Empty<string>(),
            recommendation = "Continue the active objective.",
            objectiveDecision = "continue",
            selectedProposalId = "proposal-a",
            newObjective = new
            {
                goal = "Buy port commodities with the Dock Shopper.",
                rationale = "The response mixed an objective transition into continue.",
                confidence = 0.8m,
                assumptions = Array.Empty<string>(),
                supportingKnowledgeNodeIds = Array.Empty<string>()
            },
            citedKnowledgeNodeIds = Array.Empty<string>(),
            plannedScripts = Array.Empty<object>()
        });
        var invalidHandler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new { message = new { content = invalidAssessmentJson } }));
        using var invalidClient = new HttpClient(invalidHandler);
        var invalidHistory = new LlmInteractionHistoryStore();
        var invalidProvider = new OllamaReasoningProvider(invalidClient, invalidHistory);

        await Assert.ThrowsAsync<InvalidDataException>(() => invalidProvider.AssessAsync(
            new ModelConfiguration("Ollama", "qwen3:4b", new Uri("http://localhost:11434/")),
            context));

        var invalidInteraction = Assert.Single(invalidHistory.GetRecent("meridian"));
        Assert.False(invalidInteraction.Succeeded);
        Assert.Contains("inconsistent", invalidInteraction.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void ReviewHistoryAnalysisCountsRepeatedReflectionsAndShowsMacroTelemetryWindow()
    {
        var now = DateTimeOffset.UtcNow;
        var repeatedAssessment = CreateAssessment(ObjectiveDecision.Hold) with
        {
            Summary = "Credits have remained flat.",
            Recommendation = "Explore adjacent sectors."
        };
        var priorReviews = Enumerable.Range(1, 6)
            .Select(cycle => CreateReviewRecord(
                new GameSnapshot(12, 51_000, 100, null, now.AddHours(-cycle)),
                "proposal-a",
                now.AddHours(-cycle),
                repeatedAssessment,
                stalled: true))
            .ToArray();
        var macroRecord = new MacroExecutionRecord(
            "macro-execution",
            "meridian",
            "explore-adjacent-sectors",
            "Explore Adjacent Sectors",
            MacroCategory.Exploration,
            now.AddMinutes(-30),
            now.AddMinutes(-29),
            MacroExecutionStatus.Planned,
            "A route plan was recorded.",
            "Offline plan only.",
            [12, 13]);

        var result = ReviewHistoryAnalyzer.Analyze(priorReviews, [macroRecord], now);

        Assert.Equal(6, result.ReviewsAnalyzed);
        Assert.Equal(6, result.RepeatedSummaryCount);
        Assert.Equal(6, result.RepeatedRecommendationCount);
        Assert.Equal("Explore adjacent sectors.", result.MostRepeatedRecommendation);
        Assert.Equal(6, result.ConsecutiveStalledReviews);
        Assert.False(result.NoRecordedMacroActivitySinceLastReview);
        Assert.Equal("explore-adjacent-sectors", Assert.Single(result.MacroActivitySinceLastReview).MacroId);
        Assert.Contains("do not prove live", result.MacroTelemetryLimitations, StringComparison.Ordinal);
    }

    [Fact]
    public void FailureDetectionFindsRepeatedCreditAndObjectiveStallsWithoutInventingMissingTelemetry()
    {
        var now = DateTimeOffset.UtcNow;
        var priorReviews = Enumerable.Range(1, 3)
            .Select(cycle => CreateReviewRecord(
                new GameSnapshot(12, 50_000, 100, null, now.AddHours(-cycle)),
                "proposal-a",
                now.AddHours(-cycle)))
            .ToArray();

        var result = StallDetectionService.Evaluate(
            new GameSnapshot(12, 50_000, 100, null, now),
            priorReviews,
            "proposal-a",
            now.AddDays(-2));

        Assert.True(result.IsStalled);
        Assert.Equal(3, result.RequiredUnchangedCycles);
        Assert.All(result.Indicators, indicator => Assert.True(indicator.IsStalled));
        Assert.Contains(result.UnavailableSignals, message => message.Contains("New-sector"));
        Assert.Contains(result.UnavailableSignals, message => message.Contains("profitability"));
    }

    [Fact]
    public void FailureDetectionDoesNotUseStaleSnapshotsOrInferStagnationFromOneRepeatedValue()
    {
        var now = DateTimeOffset.UtcNow;
        var priorReviews = Enumerable.Range(1, 3)
            .Select(cycle => CreateReviewRecord(
                new GameSnapshot(12, cycle == 2 ? 49_000 : 50_000, 100, null, now.AddHours(-cycle)),
                cycle == 1 ? "proposal-a" : "proposal-b",
                now.AddHours(-cycle)))
            .ToArray();

        var current = StallDetectionService.Evaluate(
            new GameSnapshot(12, 50_000, 100, null, now),
            priorReviews,
            "proposal-a",
            now.AddDays(-2));
        var stale = StallDetectionService.Evaluate(
            new GameSnapshot(12, 50_000, 100, null, now.AddDays(-1)),
            priorReviews,
            "proposal-a",
            now.AddDays(-2));

        Assert.False(current.IsStalled);
        Assert.Equal(1, current.Indicators.Single(item => item.Metric == "Synchronized credits").UnchangedCycles);
        Assert.True(stale.SnapshotPredatesHistory);
        Assert.False(stale.IsStalled);
    }

    [Fact]
    public async Task ReviewSelectsOnlyAModelNamedPendingObjectiveAndPersistsAssessment()
    {
        var (directory, connectionString) = CreateDatabase();
        try
        {
            var agentStore = new SqliteAgentStore(connectionString);
            await agentStore.InitializeAsync();
            await agentStore.SaveAgentAsync(CreateAgent());
            var knowledgeStore = new SqliteKnowledgeStore(connectionString);
            await knowledgeStore.InitializeAsync();
            var reviewQuery = "TradeWars strategy and objectives";
            var reviewCategories = new[]
            {
                KnowledgeCategory.WorldKnowledge,
                KnowledgeCategory.MacroDefinition,
                KnowledgeCategory.TacticalPattern,
                KnowledgeCategory.CommunicationPattern,
                KnowledgeCategory.CooperativeProcedure,
                KnowledgeCategory.StrategicGuidance,
                KnowledgeCategory.MenuStructure,
                KnowledgeCategory.AgentExperience
            };
            var reviewNodes = reviewCategories.Select((category, index) =>
            {
                var evidence = new KnowledgeEvidence(1, "test", $"{reviewQuery} evidence");
                if (category == KnowledgeCategory.AgentExperience)
                {
                    var startedAt = DateTimeOffset.UtcNow.AddMinutes(-1);
                    var endedAt = DateTimeOffset.UtcNow;
                    evidence = evidence with
                    {
                        Details = new KnowledgeEvidenceDetails
                        {
                            AgentExperience = new AgentExperienceEvidence(
                                "review-experience",
                                "meridian",
                                "MacroExecution",
                                nameof(MacroExecutionStatus.Planned),
                                startedAt,
                                endedAt,
                                "A route was planned.",
                                "No game outcome measured.",
                                [12, 13],
                                GameOutcomeMeasured: false,
                                BeforeSnapshot: new GameSnapshot(
                                    12,
                                    50_000,
                                    100,
                                    null,
                                    startedAt.AddSeconds(-1)),
                                AfterSnapshot: new GameSnapshot(
                                    12,
                                    50_250,
                                    99,
                                    null,
                                    endedAt.AddSeconds(1)))
                        }
                    };
                }

                return new KnowledgeNode(
                    $"review-layer-node-{index}",
                    "review-layer-document",
                    category,
                    $"{reviewQuery} {category}",
                    $"{reviewQuery} evidence",
                    0.7m,
                    ExtractionBasis.ExplicitText,
                    [evidence]);
            }).ToArray();
            await knowledgeStore.ImportDocumentsAsync(
            [
                new KnowledgeDocument(
                    "review-layer-document",
                    "review-seed",
                    "review/layers.txt",
                    "layers.txt",
                    "review-layer-hash",
                    "text",
                    DateTimeOffset.UtcNow,
                    reviewNodes,
                    [])
            ]);
            var goalStore = new SqliteGoalProposalStore(connectionString);
            await goalStore.InitializeAsync();
            var reviewStore = new SqliteStrategicReviewStore(connectionString);
            await reviewStore.InitializeAsync();
            var macroStore = new SqliteMacroTelemetryStore(connectionString);
            await macroStore.InitializeAsync();
            var reactiveStore = new SqliteReactiveEventStore(connectionString);
            await reactiveStore.InitializeAsync();
            var proposalService = new GoalProposalService(goalStore);
            var proposal = await proposalService.ProposeAsync(CreateDraft());
            var provider = new FakeReasoningProvider(context => CreateAssessment(
                ObjectiveDecision.SelectProposal,
                selectedProposalId: context.PendingObjectives.Single().ProposalId));
            var service = new StrategicReviewService(
                agentStore,
                knowledgeStore,
                macroStore,
                reactiveStore,
                goalStore,
                proposalService,
                reviewStore,
                provider,
                new Uri("http://localhost:11434/"));

            var receivedAt = DateTimeOffset.UtcNow;
            var communicationEvent = new ReactiveEvent(
                "comm-review-context",
                "meridian",
                ReactiveEventType.CommunicationReceived,
                ReactiveEventSource.CorporateMessage,
                receivedAt,
                receivedAt,
                500,
                "trusted-ally",
                null,
                "Sector 500 may be compromised.",
                "corp-channel-17",
                0.65m);
            var communicationDecision = ReactiveRuleEngine.Classify(communicationEvent);
            await reactiveStore.SaveAsync(new ReactiveEventRecord(
                communicationEvent,
                communicationDecision,
                receivedAt));

            var record = await service.ReviewAsync(
                "meridian",
                new GameSnapshot(12, 50_000, 100, null, DateTimeOffset.UtcNow));

            Assert.Equal(proposal.ProposalId, Assert.Single(
                await goalStore.GetObjectivesAsync("meridian", ObjectiveStatus.Active)).SourceProposalId);
            Assert.Contains(proposal.ProposalId, record.DecisionOutcome);
            Assert.Equal("Ollama", record.Model.Provider);
            Assert.Equal("qwen3:4b", record.Model.Model);
            Assert.Equal(proposal.ProposalId, Assert.Single(record.Context.PendingObjectives).ProposalId);
            Assert.Equal(8, record.Context.Knowledge.Count);
            Assert.Contains(record.Context.Knowledge, item => item.Layer == nameof(KnowledgeLayer.Facts));
            Assert.Contains(record.Context.Knowledge, item => item.Layer == nameof(KnowledgeLayer.Procedural));
            Assert.Contains(record.Context.Knowledge, item => item.Layer == nameof(KnowledgeLayer.Tactical));
            Assert.Contains(record.Context.Knowledge, item => item.Layer == nameof(KnowledgeLayer.Communication));
            Assert.Contains(record.Context.Knowledge, item => item.Layer == nameof(KnowledgeLayer.Cooperative));
            Assert.Contains(record.Context.Knowledge, item => item.Layer == nameof(KnowledgeLayer.Strategic));
            Assert.Contains(record.Context.Knowledge, item => item.Layer == nameof(KnowledgeLayer.Structural));
            var experienceContext = Assert.Single(
                record.Context.Knowledge,
                item => item.Layer == nameof(KnowledgeLayer.Experience));
            Assert.Contains(
                experienceContext.Evidence,
                evidence => evidence.Contains("synchronizedCredits=50000", StringComparison.Ordinal));
            Assert.Contains(
                experienceContext.Evidence,
                evidence => evidence.Contains("synchronizedCredits=50250", StringComparison.Ordinal));
            Assert.Equal(1, record.Context.ReactiveActivity.EventsAnalyzed);
            var recentReactiveEvent = Assert.Single(record.Context.ReactiveActivity.RecentEvents);
            Assert.Equal(ReactiveEventSource.CorporateMessage, recentReactiveEvent.Source);
            Assert.Equal(0.65m, recentReactiveEvent.TrustAtReceipt);
            Assert.Equal(communicationEvent.Message, recentReactiveEvent.Message);
            Assert.Equal(1, Assert.Single(
                record.Context.ReactiveActivity.CountsByMode,
                item => item.Name == ReactiveMode.Deliberative.ToString()).Count);
            var persistedReview = Assert.Single(await reviewStore.GetRecentAsync("meridian"));
            Assert.Equal(record.ReviewId, persistedReview.ReviewId);
            Assert.Equal(
                record.Context.Knowledge.Select(item => item.Layer),
                persistedReview.Context.Knowledge.Select(item => item.Layer));
            Assert.Contains(
                (await agentStore.GetAgentsAsync()).Single().Journal,
                entry => entry.Kind == "StrategicAssessment" &&
                         entry.Message.Contains(record.Assessment.Summary, StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AutonomousObjectiveProviderUsesStrategicReviewToCreateAndSelectAnObjective()
    {
        var (directory, connectionString) = CreateDatabase();
        try
        {
            var agentStore = new SqliteAgentStore(connectionString);
            await agentStore.InitializeAsync();
            AgentContainer agent = CreateAgent();
            agent.Configuration["Brains:Strategic:Model"] = "qwen3:30b";
            await agentStore.SaveAgentAsync(agent);
            var knowledgeStore = new SqliteKnowledgeStore(connectionString);
            await knowledgeStore.InitializeAsync();
            var evidence = new KnowledgeEvidence(
                21,
                "scout procedure",
                "TradeWars strategy and objectives include scanner-guided exploration.");
            var node = new KnowledgeNode(
                "scanner-objective-evidence",
                "scanner-objective-document",
                KnowledgeCategory.TacticalGuidance,
                "TradeWars strategy and objectives: scanner exploration",
                "TradeWars strategy and objectives should use scanner observations before navigation.",
                0.95m,
                ExtractionBasis.ExplicitText,
                [evidence]);
            await knowledgeStore.ImportDocumentsAsync(
            [
                new KnowledgeDocument(
                    "scanner-objective-document",
                    "review-seed",
                    "review/scanner.txt",
                    "scanner.txt",
                    "scanner-objective-hash",
                    "text",
                    DateTimeOffset.UtcNow,
                    [node],
                    [])
            ]);
            var goalStore = new SqliteGoalProposalStore(connectionString);
            await goalStore.InitializeAsync();
            var reviewStore = new SqliteStrategicReviewStore(connectionString);
            await reviewStore.InitializeAsync();
            var macroStore = new SqliteMacroTelemetryStore(connectionString);
            await macroStore.InitializeAsync();
            var reactiveStore = new SqliteReactiveEventStore(connectionString);
            await reactiveStore.InitializeAsync();
            var proposalService = new GoalProposalService(goalStore);
            int reviewCount = 0;
            var reasoningProvider = new FakeReasoningProvider(context =>
                Interlocked.Increment(ref reviewCount) switch
                {
                    1 => CreateAssessment(
                        ObjectiveDecision.ProposeAndSelect,
                        citedKnowledgeNodeIds: ["scanner-objective-evidence"]) with
                    {
                        NewObjective = new NewObjectiveProposal(
                            "Explore and map adjacent sectors safely.",
                            "Use the scanner procedure in the supplied knowledge before expanding the map.",
                            0.9m,
                            [],
                            ["scanner-objective-evidence"]),
                        PlannedScripts =
                        [
                            new AutonomousScriptPlan(
                                AutonomousScriptId.StarDockShopper,
                                "Use the approved scanner script to learn the upgrade flow.")
                        ]
                    },
                    2 => CreateAssessment(ObjectiveDecision.Complete),
                    _ => CreateAssessment(ObjectiveDecision.Hold)
                });
            var strategicReviewService = new StrategicReviewService(
                agentStore,
                knowledgeStore,
                macroStore,
                reactiveStore,
                goalStore,
                proposalService,
                reviewStore,
                reasoningProvider,
                new Uri("http://localhost:11434/"));
            var reviewClock = new ManualTimeProvider(DateTimeOffset.UtcNow);
            var objectiveProvider = new StrategicReviewAutonomousActionObjectiveProvider(
                goalStore,
                proposalService,
                strategicReviewService,
                new ModelConfiguration(
                    "Ollama",
                    "qwen3:8b",
                    new Uri("http://localhost:11434/")),
                timeProvider: reviewClock);

            DateTimeOffset observedAt = DateTimeOffset.UtcNow;
            var integratedClient = new IntegratedGameClientSnapshot(
                observedAt,
                TerminalConnected: true,
                TwxDatabaseAvailable: true,
                ScriptServiceAvailable: true,
                SectorCount: 1_000,
                CurrentSector: 1,
                StarDockSector: 1,
                LastPortCimAt: observedAt,
                [],
                [],
                [])
            {
                ScriptCatalogStatus = "Loaded 1 safe relative TWX script references.",
                ScriptCatalogTotalEntries = 1,
                ScriptCatalogEntries =
                [
                    new IntegratedGameScriptCatalogEntry("trading-cashing/2-worldtrade.ts", "Trading")
                ]
            };
            var objective = await objectiveProvider.GetOrCreateActiveObjectiveAsync(
                "meridian",
                new GameSituationReport(
                    observedAt,
                    GamePresenceState.Connected,
                    GameAuthenticationState.Authenticated,
                    GameSynchronizationState.Healthy,
                    "Command",
                    observedAt,
                    new GameSnapshot(1, 1_000, 10, null, observedAt),
                    [])
                {
                    IntegratedGameClient = integratedClient
                });

            Assert.Equal("Explore and map adjacent sectors safely.", objective?.Goal);
            Assert.Equal(
                AutonomousScriptId.StarDockShopper,
                Assert.Single(objective!.PlannedScripts).Script);
            Assert.Equal(
                "Explore and map adjacent sectors safely.",
                Assert.Single(await goalStore.GetObjectivesAsync("meridian", ObjectiveStatus.Active)).Goal);
            Assert.Equal(
                "qwen3:30b",
                Assert.Single(await reviewStore.GetRecentAsync("meridian")).Model.Model);
            Assert.Equal(
                "trading-cashing/2-worldtrade.ts",
                Assert.Single(await reviewStore.GetRecentAsync("meridian"))
                    .Context.IntegratedGameClient?.ScriptCatalogEntries.Single().File);

            objectiveProvider.RequestStrategicReview(
                "meridian",
                "Synchronization recovered after the previous review.");
            var heldObjective = await objectiveProvider.GetOrCreateActiveObjectiveAsync(
                "meridian",
                new GameSnapshot(2, 1_000, 9, null, DateTimeOffset.UtcNow));

            Assert.Equal("Explore and map adjacent sectors safely.", heldObjective?.Goal);
            Assert.Equal(1, reviewCount);

            reviewClock.Advance(TimeSpan.FromMinutes(15));
            var nextObjective = await objectiveProvider.GetOrCreateActiveObjectiveAsync(
                "meridian",
                new GameSnapshot(2, 1_000, 9, null, DateTimeOffset.UtcNow));

            Assert.Equal("Establish identity", nextObjective?.Goal);
            Assert.Single(await goalStore.GetObjectivesAsync("meridian", ObjectiveStatus.Active));
            Assert.Equal(
                "Explore and map adjacent sectors safely.",
                Assert.Single(await goalStore.GetObjectivesAsync("meridian", ObjectiveStatus.Completed)).Goal);
            var reviews = await reviewStore.GetRecentAsync("meridian", limit: 10);
            Assert.Equal(3, reviews.Count);
            Assert.Contains(
                reviews,
                review => review.Context.ReviewReason ==
                    "Synchronization recovered after the previous review.");
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AutonomousObjectiveProviderFallsBackWhenStrategicReviewSelectsAnUnavailableProposal()
    {
        var (directory, connectionString) = CreateDatabase();
        try
        {
            var agentStore = new SqliteAgentStore(connectionString);
            await agentStore.InitializeAsync();
            await agentStore.SaveAgentAsync(CreateAgent());
            var knowledgeStore = new SqliteKnowledgeStore(connectionString);
            await knowledgeStore.InitializeAsync();
            var goalStore = new SqliteGoalProposalStore(connectionString);
            await goalStore.InitializeAsync();
            var proposalService = new GoalProposalService(goalStore);
            var reviewStore = new SqliteStrategicReviewStore(connectionString);
            await reviewStore.InitializeAsync();
            var macroStore = new SqliteMacroTelemetryStore(connectionString);
            await macroStore.InitializeAsync();
            var reactiveStore = new SqliteReactiveEventStore(connectionString);
            await reactiveStore.InitializeAsync();
            var strategicReviewService = new StrategicReviewService(
                agentStore,
                knowledgeStore,
                macroStore,
                reactiveStore,
                goalStore,
                proposalService,
                reviewStore,
                new FakeReasoningProvider(_ => CreateAssessment(
                    ObjectiveDecision.ProposeAndSelect,
                    selectedProposalId: "unexpected-proposal",
                    newObjective: new NewObjectiveProposal(
                        "Learn economic readiness",
                        "The new learning objective appears relevant.",
                        0.8m,
                        [],
                        []))),
                new Uri("http://localhost:11434/"));
            var objectiveProvider = new StrategicReviewAutonomousActionObjectiveProvider(
                goalStore,
                proposalService,
                strategicReviewService,
                new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")));

            var objective = await objectiveProvider.GetOrCreateActiveObjectiveAsync(
                "meridian",
                new GameSnapshot(1, 1_000, 10, null, DateTimeOffset.UtcNow));

            Assert.Equal("Establish identity", objective?.Goal);
            Assert.Equal(
                "Establish identity",
                Assert.Single(await goalStore.GetObjectivesAsync("meridian", ObjectiveStatus.Active)).Goal);
            Assert.Empty(await reviewStore.GetRecentAsync("meridian"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task AutonomousObjectiveProviderKeepsActiveObjectiveWhenStrategicReviewIsInvalid()
    {
        var (directory, connectionString) = CreateDatabase();
        try
        {
            var agentStore = new SqliteAgentStore(connectionString);
            await agentStore.InitializeAsync();
            await agentStore.SaveAgentAsync(CreateAgent());
            var knowledgeStore = new SqliteKnowledgeStore(connectionString);
            await knowledgeStore.InitializeAsync();
            var goalStore = new SqliteGoalProposalStore(connectionString);
            await goalStore.InitializeAsync();
            var proposalService = new GoalProposalService(goalStore);
            var reviewStore = new SqliteStrategicReviewStore(connectionString);
            await reviewStore.InitializeAsync();
            var macroStore = new SqliteMacroTelemetryStore(connectionString);
            await macroStore.InitializeAsync();
            var reactiveStore = new SqliteReactiveEventStore(connectionString);
            await reactiveStore.InitializeAsync();
            var snapshot = new GameSnapshot(1, 1_000, 10, null, DateTimeOffset.UtcNow);
            var initialProposal = await proposalService.ProposeAsync(new GoalProposalDraft(
                "meridian",
                "Continue learning session synchronization.",
                "Keep the current verified learning objective.",
                0.8m,
                [],
                snapshot,
                []));
            await proposalService.SelectProposalAsync(
                "meridian",
                initialProposal.ProposalId,
                "Establish the active objective before reviewing.");
            var strategicReviewService = new StrategicReviewService(
                agentStore,
                knowledgeStore,
                macroStore,
                reactiveStore,
                goalStore,
                proposalService,
                reviewStore,
                new FakeReasoningProvider(_ => CreateAssessment(
                    ObjectiveDecision.SelectProposal,
                    selectedProposalId: "not-a-pending-proposal")),
                new Uri("http://localhost:11434/"));
            var objectiveProvider = new StrategicReviewAutonomousActionObjectiveProvider(
                goalStore,
                proposalService,
                strategicReviewService,
                new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")));

            var objective = await objectiveProvider.GetOrCreateActiveObjectiveAsync("meridian", snapshot);

            Assert.Equal("Continue learning session synchronization.", objective?.Goal);
            Assert.Equal(
                "Continue learning session synchronization.",
                Assert.Single(await goalStore.GetObjectivesAsync("meridian", ObjectiveStatus.Active)).Goal);
            Assert.Empty(await reviewStore.GetRecentAsync("meridian"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReviewRejectsKnowledgeCitationsNotPresentInItsContext()
    {
        var (directory, connectionString) = CreateDatabase();
        try
        {
            var agentStore = new SqliteAgentStore(connectionString);
            await agentStore.InitializeAsync();
            await agentStore.SaveAgentAsync(CreateAgent());
            var knowledgeStore = new SqliteKnowledgeStore(connectionString);
            await knowledgeStore.InitializeAsync();
            var goalStore = new SqliteGoalProposalStore(connectionString);
            await goalStore.InitializeAsync();
            var reviewStore = new SqliteStrategicReviewStore(connectionString);
            await reviewStore.InitializeAsync();
            var macroStore = new SqliteMacroTelemetryStore(connectionString);
            await macroStore.InitializeAsync();
            var reactiveStore = new SqliteReactiveEventStore(connectionString);
            await reactiveStore.InitializeAsync();
            var provider = new FakeReasoningProvider(_ => CreateAssessment(
                ObjectiveDecision.Hold,
                citedKnowledgeNodeIds: ["unknown-node"]));
            var service = new StrategicReviewService(
                agentStore,
                knowledgeStore,
                macroStore,
                reactiveStore,
                goalStore,
                new GoalProposalService(goalStore),
                reviewStore,
                provider,
                new Uri("http://localhost:11434/"));

            await Assert.ThrowsAsync<StrategicAssessmentValidationException>(() => service.ReviewAsync(
                "meridian",
                new GameSnapshot(null, null, null, null, DateTimeOffset.UtcNow)));
            Assert.Empty(await reviewStore.GetRecentAsync("meridian"));
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    private static AgentContainer CreateAgent() => new()
    {
        AgentId = "meridian",
        DisplayName = "Meridian",
        PreferredProvider = "Ollama",
        PreferredModel = "qwen3:4b"
    };

    private static GoalProposalDraft CreateDraft()
    {
        var node = new KnowledgeNode(
            "node",
            "document",
            KnowledgeCategory.TacticalGuidance,
            "Trading guidance",
            "Candidate guidance.",
            0.8m,
            ExtractionBasis.ExplicitText,
            [new KnowledgeEvidence(1, "text", "Evaluate the available trading opportunity.")]);
        return new GoalProposalDraft(
            "meridian",
            "Improve economic readiness",
            "A candidate objective for review.",
            0.4m,
            [],
            new GameSnapshot(12, 50_000, 100, null, DateTimeOffset.UtcNow),
            [new GoalEvidence(node, [])]);
    }

    private static StrategicAssessment CreateAssessment(
        ObjectiveDecision decision,
        string? selectedProposalId = null,
        IReadOnlyList<string>? citedKnowledgeNodeIds = null,
        NewObjectiveProposal? newObjective = null) =>
        new(
            ProgressAssessment.InsufficientData,
            "There is not enough history to assess progress.",
            [],
            [],
            [],
            ["The manually entered snapshot is accurate."],
            ["Compare with another verified snapshot."],
            ["Explore a different objective if the state changes."],
            "The candidate is relevant to the current state.",
            decision,
            selectedProposalId,
            newObjective,
            citedKnowledgeNodeIds ?? []);

    private static FailureDetectionSummary EmptyFailureDetection() =>
        new(3, false, null, false, [], []);

    private static ReviewHistoryAnalysis EmptyReviewHistory() =>
        new(0, [], null, 0, null, 0, 0, null, [], null, "No prior review history.");

    private static StrategicReviewRecord CreateReviewRecord(
        GameSnapshot snapshot,
        string? activeObjectiveProposalId,
        DateTimeOffset reviewedAt,
        StrategicAssessment? assessment = null,
        bool stalled = false)
    {
        var failureDetection = new FailureDetectionSummary(3, false, null, stalled, [], []);
        var context = new StrategicReviewContext(
            snapshot,
            [],
            activeObjectiveProposalId is null
                ? null
                : new ReviewObjective(activeObjectiveProposalId, "Goal", "Reason", null),
            [],
            [],
            failureDetection,
            EmptyReviewHistory(),
            []);
        return new StrategicReviewRecord(
            Guid.NewGuid().ToString("N"),
            "meridian",
            reviewedAt,
            new ModelConfiguration("Ollama", "qwen3:4b", new Uri("http://localhost:11434/")),
            context,
            assessment ?? CreateAssessment(ObjectiveDecision.Hold),
            "Held.");
    }

    private static (string Directory, string ConnectionString) CreateDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"twbot-review-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "agent.db"),
            ForeignKeys = true,
            Pooling = false
        }.ToString();
        return (directory, connectionString);
    }

    private sealed class FakeReasoningProvider(
        Func<StrategicReviewContext, StrategicAssessment> assessmentFactory) : IStrategicReasoningProvider
    {
        public string Id => "Ollama";

        public Task<StrategicAssessment> AssessAsync(
            ModelConfiguration configuration,
            StrategicReviewContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(assessmentFactory(context));
    }

    private sealed class ManualTimeProvider(DateTimeOffset initialTime) : TimeProvider
    {
        private DateTimeOffset _now = initialTime;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan amount) => _now += amount;
    }

    private sealed class RecordingHttpHandler(string responseJson) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseJson, Encoding.UTF8, "application/json")
            };
        }
    }
}
