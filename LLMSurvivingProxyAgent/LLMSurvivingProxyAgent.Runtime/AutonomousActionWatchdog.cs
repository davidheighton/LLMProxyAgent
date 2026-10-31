using System.Collections.Concurrent;
using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed record AutonomousActionWatchdogObservation(
    int UnchangedCycles,
    string? RecoveryReason,
    DateTimeOffset? NextReviewEligibleAt = null);

public sealed class AutonomousActionWatchdog
{
    public const int RequiredUnchangedCycles = 3;

    public static readonly TimeSpan ReviewRequestCooldown = TimeSpan.FromMinutes(3);
    private readonly ConcurrentDictionary<string, WatchdogState> _states =
        new(StringComparer.OrdinalIgnoreCase);

    public AutonomousActionWatchdogObservation Observe(
        string agentId,
        GameSituationReport situation,
        string? previousAction,
        DateTimeOffset observedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(situation);
        if (situation.Snapshot is not { } snapshot ||
            !GameSnapshotValidation.IsValid(snapshot) ||
            situation.Synchronization != GameSynchronizationState.Healthy ||
            !string.Equals(situation.CurrentPrompt, "Command", StringComparison.OrdinalIgnoreCase) ||
            situation.CurrentPromptObservedAt is not { } promptAt)
        {
            return new AutonomousActionWatchdogObservation(0, null);
        }

        WatchdogState state = _states.GetOrAdd(agentId, static _ => new WatchdogState());
        lock (state.Sync)
        {
            ProgressSnapshot current = ProgressSnapshot.From(situation, snapshot);
            if (state.LastPromptAt == promptAt)
            {
                if (state.LastSnapshot is { } repeatedPromptSnapshot &&
                    repeatedPromptSnapshot != current)
                {
                    state.LastSnapshot = current;
                    state.UnchangedCycles = 0;
                    state.LastReviewRequestedAt = null;
                }

                return new AutonomousActionWatchdogObservation(
                    state.UnchangedCycles,
                    null,
                    GetNextReviewEligibleAt(state));
            }

            if (state.LastSnapshot is { } previousSnapshot && previousSnapshot == current)
            {
                if (!string.IsNullOrWhiteSpace(previousAction))
                {
                    state.UnchangedCycles++;
                }
            }
            else
            {
                state.UnchangedCycles = 0;
                state.LastReviewRequestedAt = null;
            }

            state.LastPromptAt = promptAt;
            state.LastSnapshot = current;
            if (state.UnchangedCycles < RequiredUnchangedCycles ||
                state.LastReviewRequestedAt is { } lastRequestedAt &&
                observedAt - lastRequestedAt < ReviewRequestCooldown)
            {
                return new AutonomousActionWatchdogObservation(
                    state.UnchangedCycles,
                    null,
                    GetNextReviewEligibleAt(state));
            }

            state.LastReviewRequestedAt = observedAt;
            string action = string.IsNullOrWhiteSpace(previousAction)
                ? "No action recorded"
                : previousAction;
            string reason =
                $"Action watchdog observed {state.UnchangedCycles} consecutive Command cycles without verified state progress " +
                $"after '{action}' in sector {snapshot.CurrentSector}. Review the active objective, identify the blocking " +
                "precondition or missing information, and choose a concrete recovery or information-gathering step.";
            return new AutonomousActionWatchdogObservation(
                state.UnchangedCycles,
                reason,
                observedAt + ReviewRequestCooldown);
        }
    }

    public void Reset(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        _states.TryRemove(agentId, out _);
    }

    private static DateTimeOffset? GetNextReviewEligibleAt(WatchdogState state)
        => state.LastReviewRequestedAt is { } lastRequestedAt
            ? lastRequestedAt + ReviewRequestCooldown
            : null;

    private sealed class WatchdogState
    {
        public object Sync { get; } = new();
        public DateTimeOffset? LastPromptAt { get; set; }
        public ProgressSnapshot? LastSnapshot { get; set; }
        public int UnchangedCycles { get; set; }
        public DateTimeOffset? LastReviewRequestedAt { get; set; }
    }

    private sealed record ProgressSnapshot(
        int? CurrentSector,
        long? SynchronizedCredits,
        int? Turns,
        string? ShipType,
        int? ShipSector,
        int? ShipFighters,
        int? HoldsEmpty,
        int? HoldsTotal,
        int? FuelOre,
        int? Organics,
        int? Equipment,
        int? Shields,
        long? Experience,
        long? Alignment,
        bool? HoloScannerInstalled,
        bool? DensityScannerInstalled,
        string? CurrentPrompt,
        string? ScriptName,
        string? ScriptPrompt)
    {
        public static ProgressSnapshot From(GameSituationReport situation, GameSnapshot snapshot) =>
            new(
                snapshot.CurrentSector,
                snapshot.SynchronizedCredits,
                snapshot.Turns,
                snapshot.Ship?.ShipType,
                snapshot.Ship?.SectorId,
                snapshot.Ship?.Fighters,
                snapshot.HoldsEmpty,
                snapshot.HoldsTotal,
                snapshot.FuelOre,
                snapshot.Organics,
                snapshot.Equipment,
                snapshot.Shields,
                snapshot.Experience,
                snapshot.Alignment,
                snapshot.HoloScannerInstalled,
                snapshot.DensityScannerInstalled,
                situation.CurrentPrompt,
                situation.ScriptInputPrompt?.ScriptName,
                situation.ScriptInputPrompt?.Text);
    }
}
