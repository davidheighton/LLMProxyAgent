using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Reactive;

public static class ReactiveRuleEngine
{
    public static readonly TimeSpan MaximumFastEventAge = TimeSpan.FromSeconds(30);

    public static ReactiveDecision Classify(ReactiveEvent reactiveEvent)
    {
        ArgumentNullException.ThrowIfNull(reactiveEvent);
        if (IsFastCandidate(reactiveEvent.Type) &&
            IsGameOrSimulation(reactiveEvent.Source) &&
            reactiveEvent.ReceivedAt - reactiveEvent.OccurredAt > MaximumFastEventAge)
        {
            return Deliberative(
                "stale-fast-event",
                "The event exceeded the fast-response freshness window; preserve it as evidence and request review instead of acting on stale state.",
                ReactiveActionKind.RecordAttributedObservation,
                ReactiveActionKind.RequestStrategicReview);
        }

        return reactiveEvent.Type switch
        {
            ReactiveEventType.HostileDetected => ClassifyHostile(reactiveEvent),
            ReactiveEventType.FederalWarpHit => ClassifyThreat(
                reactiveEvent,
                "fed-warp-hit",
                "Federal warp activity reported.",
                "Prepare an evacuation plan; no route or movement is executed."),
            ReactiveEventType.TradeBustDetected => ClassifyBust(reactiveEvent),
            ReactiveEventType.ConnectionLost => ClassifyConnectionLoss(reactiveEvent),
            ReactiveEventType.PlanetUnderAttack => ClassifyThreat(
                reactiveEvent,
                "planet-under-attack",
                "Planet attack reported.",
                "Prepare an emergency response plan; no combat action is executed."),
            ReactiveEventType.TraderEnteredSector => ClassifyTraderEntry(reactiveEvent),
            ReactiveEventType.CommunicationReceived => Deliberative(
                "communication-received",
                "Preserve the message with its sender, channel, receipt time, and trust-at-receipt; do not update verified world state.",
                ReactiveActionKind.RecordAttributedObservation,
                ReactiveActionKind.RequestStrategicReview),
            ReactiveEventType.WorldObservationChanged => Deliberative(
                "world-observation-changed",
                "A world observation changed; request deliberative review without taking a game action.",
                ReactiveActionKind.RequestStrategicReview),
            _ => Deliberative(
                "unknown-event",
                "The event type is unknown; preserve it and request classification during strategic review.",
                ReactiveActionKind.RecordAttributedObservation,
                ReactiveActionKind.RequestStrategicReview)
        };
    }

    private static ReactiveDecision ClassifyHostile(ReactiveEvent reactiveEvent)
    {
        if (!IsGameOrSimulation(reactiveEvent.Source) || reactiveEvent.ActorAlignment is not < 0)
        {
            return Deliberative(
                "hostile-signal-unverified",
                "Hostile presence is not verified by a game-session observation with negative alignment; preserve the signal and request review.",
                ReactiveActionKind.RecordAttributedObservation,
                ReactiveActionKind.RequestStrategicReview);
        }
        if (reactiveEvent.SectorId is null)
        {
            return new ReactiveDecision(
                ReactiveMode.Fast,
                "hostile-location-missing",
                "Hostile presence is confirmed, but no sector was supplied; raise an alert without creating an evacuation plan.",
                true,
                [
                    new ReactiveActionIntent(ReactiveActionKind.RaiseAlert, "Confirmed hostile signal has no location."),
                    new ReactiveActionIntent(ReactiveActionKind.RequestStrategicReview, "Review the threat when deliberative processing is available.")
                ],
                "Intents are recorded only. No game command, movement, or macro cancellation is performed.");
        }

        return Fast(
            "hostile-detected",
            "Confirmed hostile presence; raise an alert and prepare an evacuation plan.",
            true,
            new ReactiveActionIntent(ReactiveActionKind.RaiseAlert, "Hostile alignment is negative in the observed sector."),
            new ReactiveActionIntent(ReactiveActionKind.PrepareEvacuationPlan, "Compute a plan only after a future executor verifies current route and safety data."));
    }

    private static ReactiveDecision ClassifyThreat(
        ReactiveEvent reactiveEvent,
        string ruleId,
        string summary,
        string planRationale)
    {
        if (!IsGameOrSimulation(reactiveEvent.Source))
        {
            return Deliberative(
                $"{ruleId}-unverified",
                $"{summary} Source is not a direct game-session observation; retain it as unverified intelligence.",
                ReactiveActionKind.RecordAttributedObservation,
                ReactiveActionKind.RequestStrategicReview);
        }
        if (reactiveEvent.SectorId is null)
        {
            return Fast(
                $"{ruleId}-location-missing",
                $"{summary} No sector was supplied; alert only and do not prepare a route plan.",
                true,
                new ReactiveActionIntent(ReactiveActionKind.RaiseAlert, "The threat event has no verified sector."));
        }

        return Fast(
            ruleId,
            summary,
            true,
            new ReactiveActionIntent(ReactiveActionKind.RaiseAlert, summary),
            new ReactiveActionIntent(ReactiveActionKind.PrepareEvacuationPlan, planRationale));
    }

    private static ReactiveDecision ClassifyBust(ReactiveEvent reactiveEvent)
    {
        if (!IsGameOrSimulation(reactiveEvent.Source))
        {
            return Deliberative(
                "trade-bust-unverified",
                "Bust signal is not from a game-session observation; preserve it and request review.",
                ReactiveActionKind.RecordAttributedObservation,
                ReactiveActionKind.RequestStrategicReview);
        }

        return Fast(
            "trade-bust-detected",
            "Bust detected; request macro abort and recovery handling.",
            false,
            new ReactiveActionIntent(ReactiveActionKind.AbortMacro, "Intent only; no macro cancellation capability is connected."),
            new ReactiveActionIntent(ReactiveActionKind.StartBustRecovery, "Recovery intent only; no game action is executed."));
    }

    private static ReactiveDecision ClassifyConnectionLoss(ReactiveEvent reactiveEvent)
    {
        if (!IsGameOrSimulation(reactiveEvent.Source))
        {
            return Deliberative(
                "connection-loss-unverified",
                "Connection-loss signal is not from a game-session observation; request review.",
                ReactiveActionKind.RecordAttributedObservation,
                ReactiveActionKind.RequestStrategicReview);
        }

        return Fast(
            "connection-lost",
            "Connection loss detected; pause local macro work and require reconnection.",
            false,
            new ReactiveActionIntent(ReactiveActionKind.PauseMacroExecution, "Intent only; no active macro is interrupted."),
            new ReactiveActionIntent(ReactiveActionKind.ReconnectRequired, "Reconnect intent only; no network connection is opened."));
    }

    private static ReactiveDecision ClassifyTraderEntry(ReactiveEvent reactiveEvent)
    {
        if (reactiveEvent.Source is not (ReactiveEventSource.GameSession or ReactiveEventSource.OperatorSimulation))
        {
            return Deliberative(
                "trader-entry-unverified",
                "Trader entry was reported by a non-session source; preserve it as attributed intelligence.",
                ReactiveActionKind.RecordAttributedObservation,
                ReactiveActionKind.RequestStrategicReview);
        }

        return new ReactiveDecision(
            ReactiveMode.Informational,
            "trader-entered-sector",
            "Record the trader observation with its source and timestamp; do not modify verified world state.",
            false,
            [new ReactiveActionIntent(ReactiveActionKind.RecordAttributedObservation, "Persist the event as an attributed observation.")],
            "The event is persisted only. No world-state overwrite or game action is performed.");
    }

    private static ReactiveDecision Deliberative(
        string ruleId,
        string summary,
        params ReactiveActionKind[] actions) =>
        new(
            ReactiveMode.Deliberative,
            ruleId,
            summary,
            actions.Contains(ReactiveActionKind.RequestStrategicReview),
            actions.Select(action => new ReactiveActionIntent(action, summary)).ToArray(),
            "The event is preserved for review. No raw command or gameplay action is executed.");

    private static ReactiveDecision Fast(
        string ruleId,
        string summary,
        bool requestsStrategicReview,
        params ReactiveActionIntent[] actions) =>
        new(
            ReactiveMode.Fast,
            ruleId,
            summary,
            requestsStrategicReview,
            requestsStrategicReview
                ? actions.Append(new ReactiveActionIntent(
                    ReactiveActionKind.RequestStrategicReview,
                    "Escalate the event after deterministic fast handling.")).ToArray()
                : actions,
            "Fast deterministic intent only. No raw command, movement, combat, live connection, or macro execution is performed.");

    private static bool IsGameOrSimulation(ReactiveEventSource source) =>
        source is ReactiveEventSource.GameSession or ReactiveEventSource.OperatorSimulation;

    private static bool IsFastCandidate(ReactiveEventType type) =>
        type is ReactiveEventType.HostileDetected or
            ReactiveEventType.FederalWarpHit or
            ReactiveEventType.TradeBustDetected or
            ReactiveEventType.ConnectionLost or
            ReactiveEventType.PlanetUnderAttack or
            ReactiveEventType.TraderEnteredSector;
}
