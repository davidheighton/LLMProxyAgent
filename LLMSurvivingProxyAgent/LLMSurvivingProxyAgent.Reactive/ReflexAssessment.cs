using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Reactive;

public sealed record ReflexContext(
    ReactiveEvent Event,
    ReactiveDecision DeterministicAssessment);

public enum ReflexIntentKind
{
    RecordAttributedObservation,
    RaiseAlert,
    RequestStrategicReview
}

public sealed record ReflexResponseIntent(ReflexIntentKind Kind, string Rationale);

public sealed record ReflexAssessment(
    string EventId,
    ReactiveClassificationCategory Category,
    ReactiveUrgency Urgency,
    ReactiveTrustAssessment TrustAssessment,
    decimal Confidence,
    bool Escalate,
    string Summary,
    IReadOnlyList<ReflexResponseIntent> ResponseIntents);

public interface IReflexReasoningProvider
{
    string Id { get; }
    Task<ReflexAssessment> AssessAsync(
        ModelConfiguration configuration,
        ReflexContext context,
        CancellationToken cancellationToken = default);
}

public static class ReflexAssessmentGate
{
    private const int MaximumSummaryLength = 500;
    private const int MaximumIntentCount = 3;
    private const int MaximumIntentRationaleLength = 300;

    public static ReactiveDecision ToDecision(
        ReactiveEvent sourceEvent,
        ReflexAssessment assessment)
    {
        ArgumentNullException.ThrowIfNull(sourceEvent);
        ArgumentNullException.ThrowIfNull(assessment);
        if (assessment.Confidence is < 0 or > 1)
        {
            throw new InvalidDataException(
                "The reflex assessment confidence must be a decimal fraction from 0 to 1.");
        }

        if (!string.Equals(sourceEvent.EventId, assessment.EventId, StringComparison.Ordinal))
        {
            throw new InvalidDataException("The reflex assessment eventId does not match its source event.");
        }
        if (!Enum.IsDefined(assessment.Category) ||
            !Enum.IsDefined(assessment.Urgency) ||
            !Enum.IsDefined(assessment.TrustAssessment))
        {
            throw new InvalidDataException("The reflex assessment contains an unsupported classification value.");
        }
        if (string.IsNullOrWhiteSpace(assessment.Summary))
        {
            throw new InvalidDataException("The reflex assessment summary is empty.");
        }
        if (assessment.Summary.Length > MaximumSummaryLength)
        {
            throw new InvalidDataException(
                $"The reflex assessment summary exceeds {MaximumSummaryLength} characters.");
        }
        if (assessment.ResponseIntents is null)
        {
            throw new InvalidDataException("The reflex assessment responseIntents value is null.");
        }
        if (assessment.ResponseIntents.Count > MaximumIntentCount)
        {
            throw new InvalidDataException(
                $"The reflex assessment contains more than {MaximumIntentCount} response intents.");
        }
        foreach (ReflexResponseIntent? intent in assessment.ResponseIntents)
        {
            if (intent is null)
            {
                throw new InvalidDataException("The reflex assessment contains a null response intent.");
            }
            if (!Enum.IsDefined(intent.Kind))
            {
                throw new InvalidDataException("The reflex assessment contains an unsupported response intent.");
            }
            if (string.IsNullOrWhiteSpace(intent.Rationale))
            {
                throw new InvalidDataException("The reflex assessment contains an empty intent rationale.");
            }
            if (intent.Rationale.Length > MaximumIntentRationaleLength)
            {
                throw new InvalidDataException(
                    $"A reflex intent rationale exceeds {MaximumIntentRationaleLength} characters.");
            }
        }
        if (assessment.ResponseIntents.Select(intent => intent.Kind).Distinct().Count() != assessment.ResponseIntents.Count)
        {
            throw new InvalidDataException("The reflex assessment contains duplicate response intent kinds.");
        }

        var messageSource = sourceEvent.Type == ReactiveEventType.CommunicationReceived ||
            sourceEvent.Source is ReactiveEventSource.PrivateMessage or
                ReactiveEventSource.CorporateMessage or ReactiveEventSource.SubspaceMessage;
        if ((messageSource || assessment.Urgency is ReactiveUrgency.Urgent or ReactiveUrgency.Emergency) &&
            !assessment.Escalate)
        {
            throw new InvalidDataException(
                "Communication and urgent reflex assessments must be escalated; the reflex model cannot authorize a fast response.");
        }
        if (!assessment.Escalate &&
            assessment.ResponseIntents.Any(intent => intent.Kind == ReflexIntentKind.RequestStrategicReview))
        {
            throw new InvalidDataException("A non-escalated reflex assessment cannot request strategic review.");
        }

        var actionIntents = assessment.ResponseIntents
            .Select(intent => new ReactiveActionIntent(
                intent.Kind switch
                {
                    ReflexIntentKind.RecordAttributedObservation => ReactiveActionKind.RecordAttributedObservation,
                    ReflexIntentKind.RaiseAlert => ReactiveActionKind.RaiseAlert,
                    ReflexIntentKind.RequestStrategicReview => ReactiveActionKind.RequestStrategicReview,
                    _ => throw new InvalidDataException("The reflex assessment requested a disallowed response intent.")
                },
                intent.Rationale))
            .ToList();
        if (assessment.Escalate &&
            actionIntents.All(intent => intent.Kind != ReactiveActionKind.RequestStrategicReview))
        {
            actionIntents.Add(new ReactiveActionIntent(
                ReactiveActionKind.RequestStrategicReview,
                "The reflex assessment was explicitly escalated for strategic review."));
        }

        return new ReactiveDecision(
            assessment.Escalate ? ReactiveMode.Deliberative : ReactiveMode.Informational,
            $"reflex-{assessment.Category.ToString().ToLowerInvariant()}",
            assessment.Summary,
            assessment.Escalate,
            actionIntents,
            "Reflex output is an audited classification and intent only. It cannot update verified world state, change objectives, or execute commands.")
        {
            Origin = ReactiveDecisionOrigin.ReflexModel,
            Category = assessment.Category,
            Urgency = assessment.Urgency,
            TrustAssessment = assessment.TrustAssessment,
            Confidence = assessment.Confidence
        };
    }

    public static ReactiveDecision CreateDeterministicFallback(
        ReactiveDecision deterministicAssessment,
        string failureReason)
    {
        ArgumentNullException.ThrowIfNull(deterministicAssessment);
        ArgumentException.ThrowIfNullOrWhiteSpace(failureReason);
        if (deterministicAssessment.Mode == ReactiveMode.Fast)
        {
            throw new ArgumentException(
                "The reflex model must not replace an existing deterministic fast-path decision.",
                nameof(deterministicAssessment));
        }

        var responseIntents = deterministicAssessment.ActionIntents
            .Where(intent => intent.Kind is ReactiveActionKind.RecordAttributedObservation or
                ReactiveActionKind.RaiseAlert or ReactiveActionKind.RequestStrategicReview)
            .ToList();
        if (responseIntents.All(intent => intent.Kind != ReactiveActionKind.RecordAttributedObservation))
        {
            responseIntents.Add(new ReactiveActionIntent(
                ReactiveActionKind.RecordAttributedObservation,
                "Preserve the source event without treating its claims as verified facts."));
        }
        if (responseIntents.All(intent => intent.Kind != ReactiveActionKind.RequestStrategicReview))
        {
            responseIntents.Add(new ReactiveActionIntent(
                ReactiveActionKind.RequestStrategicReview,
                "The reflex assessment was unavailable; escalate rather than infer a response."));
        }

        var boundedReason = failureReason.Length <= 300 ? failureReason : failureReason[..300];
        return new ReactiveDecision(
            ReactiveMode.Deliberative,
            $"{deterministicAssessment.RuleId}-reflex-fallback",
            $"{deterministicAssessment.Summary} Reflex assessment unavailable; deterministic escalation used.",
            true,
            responseIntents,
            "Fallback intents are audited only. No verified world state, objectives, or game commands are changed.")
        {
            Origin = ReactiveDecisionOrigin.DeterministicFallback,
            Category = deterministicAssessment.Category,
            Urgency = deterministicAssessment.Urgency,
            TrustAssessment = deterministicAssessment.TrustAssessment,
            Confidence = deterministicAssessment.Confidence,
            FallbackReason = boundedReason
        };
    }
}
