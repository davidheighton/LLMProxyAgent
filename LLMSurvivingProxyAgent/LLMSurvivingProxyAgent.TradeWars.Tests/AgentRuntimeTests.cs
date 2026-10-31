using LLMSurvivingProxyAgent.Agent;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Goals;
using LLMSurvivingProxyAgent.Knowledge;
using LLMSurvivingProxyAgent.Macros;
using LLMSurvivingProxyAgent.Models;
using LLMSurvivingProxyAgent.Reactive;
using LLMSurvivingProxyAgent.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class AgentRuntimeTests
{
    [Fact]
    public async Task ReactiveLaneCompletesWhileStrategicModelLaneIsWaiting()
    {
        var temporaryDirectory = Path.Combine(Path.GetTempPath(), $"twbot-runtime-{Guid.NewGuid():N}");
        Directory.CreateDirectory(temporaryDirectory);
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(temporaryDirectory, "runtime.db"),
            ForeignKeys = true,
            Pooling = false
        }.ToString();
        AgentRuntimeWorker? worker = null;
        ILoggerFactory? loggerFactory = null;
        var provider = new GatedReasoningProvider();

        try
        {
            var agentStore = new SqliteAgentStore(connectionString);
            await agentStore.InitializeAsync();
            await agentStore.SaveAgentAsync(new AgentContainer
            {
                AgentId = "meridian",
                DisplayName = "Meridian",
                PreferredProvider = "Ollama",
                PreferredModel = "qwen3:4b"
            });

            var knowledgeStore = new SqliteKnowledgeStore(connectionString);
            var goalStore = new SqliteGoalProposalStore(connectionString);
            var macroStore = new SqliteMacroTelemetryStore(connectionString);
            var reactiveStore = new SqliteReactiveEventStore(connectionString);
            var reviewStore = new SqliteStrategicReviewStore(connectionString);
            await knowledgeStore.InitializeAsync();
            await goalStore.InitializeAsync();
            await macroStore.InitializeAsync();
            await reactiveStore.InitializeAsync();
            await reviewStore.InitializeAsync();

            var proposalService = new GoalProposalService(goalStore);
            var reviewService = new StrategicReviewService(
                agentStore,
                knowledgeStore,
                macroStore,
                reactiveStore,
                goalStore,
                proposalService,
                reviewStore,
                provider,
                new Uri("http://localhost:11434/"));
            var queue = new AgentRuntimeQueue(capacity: 2);
            loggerFactory = LoggerFactory.Create(_ => { });
            worker = new AgentRuntimeWorker(
                queue,
                new ReactiveIntelligenceService(reactiveStore),
                reviewService,
                agentStore,
                loggerFactory.CreateLogger<AgentRuntimeWorker>());
            await worker.StartAsync(CancellationToken.None);

            var snapshot = new GameSnapshot(null, null, null, null, DateTimeOffset.UtcNow);
            var strategicTask = queue.SubmitStrategicReviewAsync("meridian", snapshot);
            await provider.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));

            var now = DateTimeOffset.UtcNow;
            var reactiveTask = queue.SubmitReactiveEventAsync(new ReactiveEvent(
                "runtime-hostile",
                "meridian",
                ReactiveEventType.HostileDetected,
                ReactiveEventSource.OperatorSimulation,
                now,
                now,
                12,
                "hostile-trader",
                -100,
                null,
                "operator-simulation",
                null));
            var reactiveRecord = await reactiveTask.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(ReactiveMode.Fast, reactiveRecord.Decision.Mode);
            Assert.False(strategicTask.IsCompleted);
            Assert.Contains(
                (await agentStore.GetAgentsAsync()).Single().Journal,
                entry => entry.Kind == "ReactiveAssessment" &&
                         entry.Message.Contains("HostileDetected", StringComparison.Ordinal));

            provider.Release();
            var strategicRecord = await strategicTask.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(ObjectiveDecision.Hold, strategicRecord.Assessment.ObjectiveDecision);
            Assert.Equal(0, queue.GetStatus().ReactiveQueueDepth);
            Assert.Equal(0, queue.GetStatus().StrategicQueueDepth);
            Assert.NotNull(queue.GetStatus().LastReactiveCycleAt);
            Assert.NotNull(queue.GetStatus().LastStrategicCycleAt);
        }
        finally
        {
            provider.Release();
            if (worker is not null)
            {
                await worker.StopAsync(CancellationToken.None);
                worker.Dispose();
            }
            loggerFactory?.Dispose();
            Directory.Delete(temporaryDirectory, recursive: true);
        }
    }

    private sealed class GatedReasoningProvider : IStrategicReasoningProvider
    {
        private readonly TaskCompletionSource _release =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public string Id => "Ollama";

        public void Release() => _release.TrySetResult();

        public async Task<StrategicAssessment> AssessAsync(
            ModelConfiguration configuration,
            StrategicReviewContext context,
            CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            await _release.Task.WaitAsync(cancellationToken);
            return new StrategicAssessment(
                ProgressAssessment.InsufficientData,
                "The available snapshot contains insufficient verified telemetry.",
                [],
                [],
                [],
                [],
                [],
                [],
                "Hold the current objective state pending more evidence.",
                ObjectiveDecision.Hold,
                null,
                null,
                []);
        }
    }
}
