using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Reactive;

public sealed class ReactiveIntelligenceService(
    IReactiveEventStore eventStore,
    IReflexReasoningProvider? reflexProvider = null,
    ModelConfiguration? reflexModel = null,
    IAgentStore? agentStore = null)
{
    public async Task<ReactiveEventRecord> ProcessAsync(
        ReactiveEvent reactiveEvent,
        CancellationToken cancellationToken = default)
    {
        ValidateEvent(reactiveEvent);
        ReactiveDecision deterministicAssessment = ReactiveRuleEngine.Classify(reactiveEvent);
        ReactiveDecision decision = deterministicAssessment;
        if (deterministicAssessment.Mode != ReactiveMode.Fast &&
            (reflexProvider is not null || reflexModel is not null))
        {
            if (reflexProvider is null || reflexModel is null)
            {
                throw new InvalidOperationException(
                    "Reactive reasoning requires both a reflex provider and its model configuration.");
            }

            try
            {
                ModelConfiguration effectiveModel = reflexModel;
                if (agentStore is not null &&
                    await agentStore.GetAgentAsync(reactiveEvent.AgentId, cancellationToken)
                        .ConfigureAwait(false) is { } agent &&
                    agent.Configuration.TryGetValue("Brains:Reactive:Model", out string? configuredReactiveModel) &&
                    !string.IsNullOrWhiteSpace(configuredReactiveModel))
                {
                    effectiveModel = effectiveModel with { Model = configuredReactiveModel.Trim() };
                }

                ReflexAssessment assessment = await reflexProvider.AssessAsync(
                    effectiveModel,
                    new ReflexContext(reactiveEvent, deterministicAssessment),
                    cancellationToken).ConfigureAwait(false);
                decision = ReflexAssessmentGate.ToDecision(reactiveEvent, assessment);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                decision = ReflexAssessmentGate.CreateDeterministicFallback(
                    deterministicAssessment,
                    exception.Message);
            }
        }

        var record = new ReactiveEventRecord(reactiveEvent, decision, DateTimeOffset.UtcNow);
        await eventStore.SaveAsync(record, cancellationToken);
        return record;
    }

    private static void ValidateEvent(ReactiveEvent reactiveEvent)
    {
        ArgumentNullException.ThrowIfNull(reactiveEvent);
        ArgumentException.ThrowIfNullOrWhiteSpace(reactiveEvent.EventId);
        ArgumentException.ThrowIfNullOrWhiteSpace(reactiveEvent.AgentId);
        if (reactiveEvent.EventId.Length > 100 ||
            reactiveEvent.AgentId.Length > 100 ||
            !Enum.IsDefined(reactiveEvent.Type) ||
            !Enum.IsDefined(reactiveEvent.Source))
        {
            throw new ArgumentException("The event has an invalid identifier, type, or source.", nameof(reactiveEvent));
        }
        if (reactiveEvent.SectorId is <= 0 ||
            reactiveEvent.OccurredAt > DateTimeOffset.UtcNow.AddMinutes(5) ||
            reactiveEvent.ReceivedAt > DateTimeOffset.UtcNow.AddMinutes(5) ||
            reactiveEvent.OccurredAt > reactiveEvent.ReceivedAt.AddMinutes(5))
        {
            throw new ArgumentException("The event contains an invalid sector or future-dated timestamp.", nameof(reactiveEvent));
        }
        if (reactiveEvent.ActorName?.Length > 200 ||
            reactiveEvent.Message?.Length > 4_000 ||
            reactiveEvent.SourceReference?.Length > 1_000 ||
            reactiveEvent.SenderTrustAtReceipt is < 0 or > 1)
        {
            throw new ArgumentException("The event exceeds an allowed field limit.", nameof(reactiveEvent));
        }
        if (reactiveEvent.Type == ReactiveEventType.TraderEnteredSector && reactiveEvent.SectorId is null)
        {
            throw new ArgumentException("Trader-entered events require a sector.", nameof(reactiveEvent));
        }
        if (reactiveEvent.Type == ReactiveEventType.CommunicationReceived &&
            (string.IsNullOrWhiteSpace(reactiveEvent.ActorName) ||
             string.IsNullOrWhiteSpace(reactiveEvent.Message) ||
             reactiveEvent.Source is not (ReactiveEventSource.PrivateMessage or
                 ReactiveEventSource.CorporateMessage or ReactiveEventSource.SubspaceMessage)))
        {
            throw new ArgumentException(
                "Communication events require a sender, message, and private, corporate, or subspace source.",
                nameof(reactiveEvent));
        }
    }
}
