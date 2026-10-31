using System.Net;
using System.Text;
using System.Text.Json;
using LLMSurvivingProxyAgent.Agent;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Models;
using LLMSurvivingProxyAgent.Reactive;
using LLMSurvivingProxyAgent.Runtime;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class ReactiveIntelligenceTests
{
    [Fact]
    public async Task ConfirmedHostileProducesFastRecordedOnlyEvacuationIntent()
    {
        var (directory, connectionString) = CreateDatabase();
        try
        {
            var eventStore = await CreateEventStoreAsync(connectionString);
            var service = new ReactiveIntelligenceService(eventStore);

            var record = await service.ProcessAsync(CreateEvent(
                ReactiveEventType.HostileDetected,
                ReactiveEventSource.OperatorSimulation,
                sectorId: 500,
                actorAlignment: -10));

            Assert.Equal(ReactiveMode.Fast, record.Decision.Mode);
            Assert.Equal("hostile-detected", record.Decision.RuleId);
            Assert.Contains(record.Decision.ActionIntents, item => item.Kind == ReactiveActionKind.PrepareEvacuationPlan);
            Assert.Contains(record.Decision.ActionIntents, item => item.Kind == ReactiveActionKind.RequestStrategicReview);
            Assert.Contains("No raw command", record.Decision.ExecutionBoundary, StringComparison.Ordinal);
            Assert.Equal(record.Event.EventId, Assert.Single(await eventStore.GetRecentAsync("meridian")).Event.EventId);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task CommunicationIsAttributedAndEscalatedWithoutFastThreatAction()
    {
        var (directory, connectionString) = CreateDatabase();
        try
        {
            var eventStore = await CreateEventStoreAsync(connectionString);
            var service = new ReactiveIntelligenceService(eventStore);
            var reactiveEvent = CreateEvent(
                ReactiveEventType.CommunicationReceived,
                ReactiveEventSource.PrivateMessage,
                actorName: "Unknown Trader",
                message: "Hostile reported in sector 500.",
                trustAtReceipt: 0.25m,
                sourceReference: "private-message-42");

            var record = await service.ProcessAsync(reactiveEvent);
            var stored = Assert.Single(await eventStore.GetRecentAsync("meridian"));

            Assert.Equal(ReactiveMode.Deliberative, record.Decision.Mode);
            Assert.True(record.Decision.RequestsStrategicReview);
            Assert.Contains(record.Decision.ActionIntents, item => item.Kind == ReactiveActionKind.RecordAttributedObservation);
            Assert.DoesNotContain(record.Decision.ActionIntents, item => item.Kind == ReactiveActionKind.PrepareEvacuationPlan);
            Assert.Equal(ReactiveEventSource.PrivateMessage, stored.Event.Source);
            Assert.Equal("Unknown Trader", stored.Event.ActorName);
            Assert.Equal(0.25m, stored.Event.SenderTrustAtReceipt);
            Assert.Equal("private-message-42", stored.Event.SourceReference);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task ReactiveModelUsesItsConfiguredFastModelOnlyForDeliberativeEvents()
    {
        var (directory, connectionString) = CreateDatabase();
        try
        {
            var eventStore = await CreateEventStoreAsync(connectionString);
            var agentStore = new SqliteAgentStore(connectionString);
            var agent = await agentStore.GetAgentAsync("meridian")
                ?? throw new InvalidOperationException("The test agent was not created.");
            agent.Configuration["Brains:Reactive:Model"] = "qwen3:4b";
            await agentStore.SaveAgentAsync(agent);
            var reflexProvider = new RecordingReflexReasoningProvider();
            var service = new ReactiveIntelligenceService(
                eventStore,
                reflexProvider,
                new ModelConfiguration(
                    "Ollama",
                    "qwen3:1.7b",
                    new Uri("http://localhost:11434/")),
                agentStore);

            var fastRecord = await service.ProcessAsync(CreateEvent(
                ReactiveEventType.TradeBustDetected,
                ReactiveEventSource.GameSession));
            var deliberativeRecord = await service.ProcessAsync(CreateEvent(
                ReactiveEventType.Unknown,
                ReactiveEventSource.OperatorSimulation));

            Assert.Equal(ReactiveDecisionOrigin.DeterministicRule, fastRecord.Decision.Origin);
            Assert.Equal(1, reflexProvider.CallCount);
            Assert.Equal("qwen3:4b", reflexProvider.Configuration?.Model);
            Assert.Equal(ReactiveDecisionOrigin.ReflexModel, deliberativeRecord.Decision.Origin);
            Assert.True(deliberativeRecord.Decision.RequestsStrategicReview);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task OllamaReflexProviderConstrainsConfidenceAndRecordsAcceptedAssessment()
    {
        var sourceEvent = CreateEvent(
            ReactiveEventType.Unknown,
            ReactiveEventSource.OperatorSimulation);
        string assessmentJson = JsonSerializer.Serialize(new
        {
            eventId = sourceEvent.EventId,
            category = "unknown",
            urgency = "routine",
            trustAssessment = "notApplicable",
            confidence = 0.5m,
            escalate = false,
            summary = "Unclassified event.",
            responseIntents = Array.Empty<object>()
        });
        var handler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new { message = new { content = assessmentJson } }));
        using var client = new HttpClient(handler);
        var history = new LlmInteractionHistoryStore();
        var provider = new OllamaReflexReasoningProvider(client, history);

        ReflexAssessment assessment = await provider.AssessAsync(
            new ModelConfiguration("Ollama", "qwen3:1.7b", new Uri("http://localhost:11434/")),
            new ReflexContext(sourceEvent, ReactiveRuleEngine.Classify(sourceEvent)));

        Assert.Equal(0.5m, assessment.Confidence);
        Assert.Equal("http://localhost:11434/api/chat", handler.RequestUri?.ToString());
        var interaction = Assert.Single(history.GetRecent("meridian"));
        Assert.True(interaction.Succeeded);
        using var request = JsonDocument.Parse(handler.RequestBody);
        string systemPrompt = request.RootElement.GetProperty("messages")[0]
            .GetProperty("content").GetString()!;
        Assert.Contains("decimal fraction from 0 to 1", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("escalate=true", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("requestStrategicReview", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("WorldObservationChanged event or a numeric ship-state change alone is informational or routine", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("do not infer danger from changed values", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("Only include requestStrategicReview when escalate=true", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("For informational or routine events that are not communications, do not request Strategic Review", systemPrompt, StringComparison.Ordinal);
        Assert.False(request.RootElement.GetProperty("think").GetBoolean());
        using JsonDocument userEvent = JsonDocument.Parse(
            request.RootElement.GetProperty("messages")[1].GetProperty("content").GetString()!);
        Assert.Equal(sourceEvent.EventId, userEvent.RootElement.GetProperty("eventId").GetString());
        Assert.False(userEvent.RootElement.TryGetProperty("deterministicAssessment", out _));
        JsonElement properties = request.RootElement.GetProperty("format").GetProperty("properties");
        Assert.Equal(
            sourceEvent.EventId,
            Assert.Single(properties.GetProperty("eventId").GetProperty("enum")
                .EnumerateArray()).GetString());
        Assert.False(request.RootElement.GetProperty("format").TryGetProperty("oneOf", out _));
        JsonElement confidenceSchema = properties.GetProperty("confidence");
        Assert.Equal(0, confidenceSchema.GetProperty("minimum").GetInt32());
        Assert.Equal(1, confidenceSchema.GetProperty("maximum").GetInt32());
    }

    [Fact]
    public async Task OllamaReflexProviderRejectsOutOfRangeConfidenceAndRecordsFailure()
    {
        var sourceEvent = CreateEvent(
            ReactiveEventType.Unknown,
            ReactiveEventSource.OperatorSimulation);
        string assessmentJson = JsonSerializer.Serialize(new
        {
            eventId = sourceEvent.EventId,
            category = "unknown",
            urgency = "routine",
            trustAssessment = "notApplicable",
            confidence = 20,
            escalate = false,
            summary = "Unclassified event.",
            responseIntents = Array.Empty<object>()
        });
        var handler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new { message = new { content = assessmentJson } }));
        using var client = new HttpClient(handler);
        var history = new LlmInteractionHistoryStore();
        var provider = new OllamaReflexReasoningProvider(client, history);

        await Assert.ThrowsAsync<InvalidDataException>(() => provider.AssessAsync(
            new ModelConfiguration("Ollama", "qwen3:1.7b", new Uri("http://localhost:11434/")),
            new ReflexContext(sourceEvent, ReactiveRuleEngine.Classify(sourceEvent))));

        var interaction = Assert.Single(history.GetRecent("meridian"));
        Assert.False(interaction.Succeeded);
        Assert.Contains("confidence", interaction.Error!, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BustProducesDeterministicFastRecoveryIntents()
    {
        var decision = ReactiveRuleEngine.Classify(CreateEvent(
            ReactiveEventType.TradeBustDetected,
            ReactiveEventSource.OperatorSimulation));

        Assert.Equal(ReactiveMode.Fast, decision.Mode);
        Assert.Contains(decision.ActionIntents, item => item.Kind == ReactiveActionKind.AbortMacro);
        Assert.Contains(decision.ActionIntents, item => item.Kind == ReactiveActionKind.StartBustRecovery);
        Assert.DoesNotContain(decision.ActionIntents, item => item.Kind == ReactiveActionKind.RequestStrategicReview);
    }

    [Fact]
    public void UnverifiedHostileSignalCannotTriggerFastEvacuation()
    {
        var decision = ReactiveRuleEngine.Classify(CreateEvent(
            ReactiveEventType.HostileDetected,
            ReactiveEventSource.PrivateMessage,
            sectorId: 500,
            actorAlignment: -10));

        Assert.Equal(ReactiveMode.Deliberative, decision.Mode);
        Assert.Contains(decision.ActionIntents, item => item.Kind == ReactiveActionKind.RequestStrategicReview);
        Assert.DoesNotContain(decision.ActionIntents, item => item.Kind == ReactiveActionKind.PrepareEvacuationPlan);
    }

    [Fact]
    public void ConfirmedThreatWithoutSectorDoesNotProduceEvacuationPlan()
    {
        var decision = ReactiveRuleEngine.Classify(CreateEvent(
            ReactiveEventType.FederalWarpHit,
            ReactiveEventSource.OperatorSimulation));

        Assert.Equal(ReactiveMode.Fast, decision.Mode);
        Assert.Contains(decision.ActionIntents, item => item.Kind == ReactiveActionKind.RaiseAlert);
        Assert.Contains(decision.ActionIntents, item => item.Kind == ReactiveActionKind.RequestStrategicReview);
        Assert.DoesNotContain(decision.ActionIntents, item => item.Kind == ReactiveActionKind.PrepareEvacuationPlan);
    }

    [Fact]
    public void StaleThreatEventIsEscalatedInsteadOfProducingFastResponse()
    {
        var now = DateTimeOffset.UtcNow;
        var reactiveEvent = CreateEvent(
            ReactiveEventType.HostileDetected,
            ReactiveEventSource.GameSession,
            sectorId: 500,
            actorAlignment: -10) with
        {
            OccurredAt = now - TimeSpan.FromMinutes(2),
            ReceivedAt = now
        };

        var decision = ReactiveRuleEngine.Classify(reactiveEvent);

        Assert.Equal(ReactiveMode.Deliberative, decision.Mode);
        Assert.Equal("stale-fast-event", decision.RuleId);
        Assert.DoesNotContain(decision.ActionIntents, item => item.Kind == ReactiveActionKind.PrepareEvacuationPlan);
        Assert.Contains(decision.ActionIntents, item => item.Kind == ReactiveActionKind.RequestStrategicReview);
    }

    [Fact]
    public void ReflexGateConvertsMessageAssessmentToEscalatedAuditOnlyIntents()
    {
        var sourceEvent = CreateEvent(
            ReactiveEventType.CommunicationReceived,
            ReactiveEventSource.SubspaceMessage,
            actorName: "Unknown trader",
            message: "Trader spotted moving through the eastern cluster.",
            trustAtReceipt: 0.2m);
        var assessment = new ReflexAssessment(
            sourceEvent.EventId,
            ReactiveClassificationCategory.Intelligence,
            ReactiveUrgency.Emergency,
            ReactiveTrustAssessment.Low,
            0.82m,
            true,
            "Treat as unverified intelligence requiring review.",
            [new ReflexResponseIntent(
                ReflexIntentKind.RecordAttributedObservation,
                "Preserve the report and its source.")]);

        var decision = ReflexAssessmentGate.ToDecision(sourceEvent, assessment);

        Assert.Equal(ReactiveMode.Deliberative, decision.Mode);
        Assert.Equal(ReactiveDecisionOrigin.ReflexModel, decision.Origin);
        Assert.Equal(ReactiveClassificationCategory.Intelligence, decision.Category);
        Assert.Equal(ReactiveUrgency.Emergency, decision.Urgency);
        Assert.Equal(0.82m, decision.Confidence);
        Assert.Contains(decision.ActionIntents, item => item.Kind == ReactiveActionKind.RequestStrategicReview);
        Assert.DoesNotContain(decision.ActionIntents, item => item.Kind is
            ReactiveActionKind.PrepareEvacuationPlan or
            ReactiveActionKind.AbortMacro or
            ReactiveActionKind.StartBustRecovery or
            ReactiveActionKind.PauseMacroExecution or
            ReactiveActionKind.ReconnectRequired);
        Assert.Contains("cannot update verified world state", decision.ExecutionBoundary, StringComparison.Ordinal);
    }

    [Fact]
    public void ReflexGateRejectsUrgentMessageThatDoesNotEscalate()
    {
        var sourceEvent = CreateEvent(
            ReactiveEventType.CommunicationReceived,
            ReactiveEventSource.PrivateMessage,
            actorName: "Unknown trader",
            message: "Sector 500 may be compromised.");
        var assessment = new ReflexAssessment(
            sourceEvent.EventId,
            ReactiveClassificationCategory.Threat,
            ReactiveUrgency.Emergency,
            ReactiveTrustAssessment.Untrusted,
            0.9m,
            false,
            "Potential hostile report.",
            []);

        Assert.Throws<InvalidDataException>(() => ReflexAssessmentGate.ToDecision(sourceEvent, assessment));
    }

    [Fact]
    public void ReflexGateRejectsUnsupportedIntentValues()
    {
        var sourceEvent = CreateEvent(
            ReactiveEventType.Unknown,
            ReactiveEventSource.OperatorSimulation);
        var assessment = new ReflexAssessment(
            sourceEvent.EventId,
            ReactiveClassificationCategory.Unknown,
            ReactiveUrgency.Routine,
            ReactiveTrustAssessment.NotApplicable,
            0.5m,
            false,
            "Unclassified event.",
            [new ReflexResponseIntent((ReflexIntentKind)999, "Unsupported intent.")]);

        Assert.Throws<InvalidDataException>(() => ReflexAssessmentGate.ToDecision(sourceEvent, assessment));
    }

    [Fact]
    public void ReflexGateIdentifiesEventIdMismatch()
    {
        var sourceEvent = CreateEvent(
            ReactiveEventType.Unknown,
            ReactiveEventSource.OperatorSimulation);
        var assessment = new ReflexAssessment(
            "different-event",
            ReactiveClassificationCategory.Unknown,
            ReactiveUrgency.Routine,
            ReactiveTrustAssessment.NotApplicable,
            0.5m,
            false,
            "Unclassified event.",
            []);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => ReflexAssessmentGate.ToDecision(sourceEvent, assessment));

        Assert.Contains("eventId", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReflexGateIdentifiesDuplicateIntentKinds()
    {
        var sourceEvent = CreateEvent(
            ReactiveEventType.Unknown,
            ReactiveEventSource.OperatorSimulation);
        var assessment = new ReflexAssessment(
            sourceEvent.EventId,
            ReactiveClassificationCategory.Unknown,
            ReactiveUrgency.Routine,
            ReactiveTrustAssessment.NotApplicable,
            0.5m,
            false,
            "Unclassified event.",
            [
                new ReflexResponseIntent(ReflexIntentKind.RecordAttributedObservation, "First observation."),
                new ReflexResponseIntent(ReflexIntentKind.RecordAttributedObservation, "Repeated observation.")
            ]);

        InvalidDataException exception = Assert.Throws<InvalidDataException>(
            () => ReflexAssessmentGate.ToDecision(sourceEvent, assessment));

        Assert.Contains("duplicate response intent kinds", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void ReflexGateRejectsPercentageScaleConfidence()
    {
        var sourceEvent = CreateEvent(
            ReactiveEventType.Unknown,
            ReactiveEventSource.OperatorSimulation);
        var assessment = new ReflexAssessment(
            sourceEvent.EventId,
            ReactiveClassificationCategory.Unknown,
            ReactiveUrgency.Routine,
            ReactiveTrustAssessment.NotApplicable,
            20m,
            false,
            "Unclassified event.",
            []);

        Assert.Throws<InvalidDataException>(() => ReflexAssessmentGate.ToDecision(sourceEvent, assessment));
    }

    [Fact]
    public void ReflexFallbackPreservesDeterministicFastPathAndEscalatesAmbiguousEvents()
    {
        var ambiguous = ReactiveRuleEngine.Classify(CreateEvent(
            ReactiveEventType.Unknown,
            ReactiveEventSource.OperatorSimulation));

        var fallback = ReflexAssessmentGate.CreateDeterministicFallback(
            ambiguous,
            "Local model timed out.");

        Assert.Equal(ReactiveMode.Deliberative, fallback.Mode);
        Assert.Equal(ReactiveDecisionOrigin.DeterministicFallback, fallback.Origin);
        Assert.Equal("Local model timed out.", fallback.FallbackReason);
        Assert.Contains(fallback.ActionIntents, item => item.Kind == ReactiveActionKind.RequestStrategicReview);
        Assert.Contains(fallback.ActionIntents, item => item.Kind == ReactiveActionKind.RecordAttributedObservation);

        var fast = ReactiveRuleEngine.Classify(CreateEvent(
            ReactiveEventType.TradeBustDetected,
            ReactiveEventSource.GameSession));
        Assert.Throws<ArgumentException>(() =>
            ReflexAssessmentGate.CreateDeterministicFallback(fast, "Should not replace fast rules."));
    }

    private static ReactiveEvent CreateEvent(
        ReactiveEventType type,
        ReactiveEventSource source,
        int? sectorId = null,
        string? actorName = null,
        int? actorAlignment = null,
        string? message = null,
        decimal? trustAtReceipt = null,
        string? sourceReference = null)
    {
        var now = DateTimeOffset.UtcNow;
        return new ReactiveEvent(
            Guid.NewGuid().ToString("N"),
            "meridian",
            type,
            source,
            now,
            now,
            sectorId,
            actorName,
            actorAlignment,
            message,
            sourceReference,
            trustAtReceipt);
    }

    private static async Task<SqliteReactiveEventStore> CreateEventStoreAsync(string connectionString)
    {
        var agentStore = new SqliteAgentStore(connectionString);
        await agentStore.InitializeAsync();
        await agentStore.SaveAgentAsync(new AgentContainer
        {
            AgentId = "meridian",
            DisplayName = "Meridian"
        });
        var eventStore = new SqliteReactiveEventStore(connectionString);
        await eventStore.InitializeAsync();
        return eventStore;
    }

    private static (string Directory, string ConnectionString) CreateDatabase()
    {
        var directory = Path.Combine(Path.GetTempPath(), $"twbot-reactive-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(directory, "agent.db"),
            ForeignKeys = true,
            Pooling = false
        }.ToString();
        return (directory, connectionString);
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

    private sealed class RecordingReflexReasoningProvider : IReflexReasoningProvider
    {
        public string Id => "Ollama";
        public int CallCount { get; private set; }
        public ModelConfiguration? Configuration { get; private set; }

        public Task<ReflexAssessment> AssessAsync(
            ModelConfiguration configuration,
            ReflexContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            CallCount++;
            Configuration = configuration;
            return Task.FromResult(new ReflexAssessment(
                context.Event.EventId,
                ReactiveClassificationCategory.Unknown,
                ReactiveUrgency.Routine,
                ReactiveTrustAssessment.NotApplicable,
                0.8m,
                true,
                "Unknown event requires deliberative review.",
                [
                    new ReflexResponseIntent(
                        ReflexIntentKind.RecordAttributedObservation,
                        "Preserve the event for later review.")
                ]));
        }
    }
}
