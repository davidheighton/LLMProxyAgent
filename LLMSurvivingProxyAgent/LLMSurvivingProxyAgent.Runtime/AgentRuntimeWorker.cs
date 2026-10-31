using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Goals;
using LLMSurvivingProxyAgent.Reactive;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed class AgentRuntimeWorker(
    AgentRuntimeQueue queue,
    ReactiveIntelligenceService reactiveIntelligence,
    StrategicReviewService strategicReview,
    IAgentStore agentStore,
    ILogger<AgentRuntimeWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        queue.SetRunning(true);
        try
        {
            await Task.WhenAll(
                ProcessReactiveEventsAsync(stoppingToken),
                ProcessStrategicReviewsAsync(stoppingToken));
        }
        finally
        {
            queue.SetRunning(false);
            queue.FailPending(new OperationCanceledException("The agent runtime is stopping."));
        }
    }

    private async Task ProcessReactiveEventsAsync(CancellationToken cancellationToken)
    {
        await foreach (var workItem in queue.ReactiveReader.ReadAllAsync(cancellationToken))
        {
            try
            {
                var record = await reactiveIntelligence.ProcessAsync(workItem.Event, cancellationToken);
                var escalation = record.Decision.RequestsStrategicReview
                    ? " This event is escalated for deliberation when a verified snapshot is supplied."
                    : string.Empty;
                await agentStore.AppendJournalEntryAsync(
                    workItem.Event.AgentId,
                    new AgentJournalEntry(
                        record.ProcessedAt,
                        "ReactiveAssessment",
                        $"{workItem.Event.Type} via {workItem.Event.Source}: {record.Decision.Summary}{escalation}"),
                    cancellationToken);
                queue.RecordReactiveCycle();
                workItem.Completion.TrySetResult(record);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                workItem.Completion.TrySetCanceled(cancellationToken);
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Reactive runtime cycle failed for event {EventId}.",
                    workItem.Event.EventId);
                workItem.Completion.TrySetException(exception);
            }
        }
    }

    private async Task ProcessStrategicReviewsAsync(CancellationToken cancellationToken)
    {
        await foreach (var workItem in queue.StrategicReader.ReadAllAsync(cancellationToken))
        {
            try
            {
                var record = await strategicReview.ReviewAsync(
                    workItem.AgentId,
                    workItem.Snapshot,
                    cancellationToken);
                queue.RecordStrategicCycle();
                workItem.Completion.TrySetResult(record);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                workItem.Completion.TrySetCanceled(cancellationToken);
                throw;
            }
            catch (Exception exception)
            {
                logger.LogError(
                    exception,
                    "Strategic runtime cycle failed for agent {AgentId}.",
                    workItem.AgentId);
                workItem.Completion.TrySetException(exception);
            }
        }
    }
}
