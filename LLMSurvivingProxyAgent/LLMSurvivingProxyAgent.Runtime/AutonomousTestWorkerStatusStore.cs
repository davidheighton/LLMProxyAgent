using System.Collections.Concurrent;
using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed record AutonomousTestWorkerStatus(
    string AgentId,
    bool IsConnected,
    string Activity,
    DateTimeOffset UpdatedAt,
    GameSituationReport? Situation,
    string? LastAction,
    string? LastRationale,
    int WatchdogUnchangedCycles = 0,
    DateTimeOffset? WatchdogNextReviewAt = null,
    string ModelProvider = "",
    string ModelName = "",
    string ModelThinkingMode = "",
    DateTimeOffset? ModelRequestStartedAt = null,
    DateTimeOffset? LastModelRequestAt = null,
    TimeSpan? LastModelRequestDuration = null,
    int ModelRequestsCompleted = 0,
    int ModelRequestFailures = 0,
    string? LastModelRequestError = null,
    DateTimeOffset? LastHeartbeatAt = null);

public sealed class AutonomousTestWorkerStatusStore
{
    private readonly ConcurrentDictionary<string, AutonomousTestWorkerStatus> _statuses =
        new(StringComparer.OrdinalIgnoreCase);

    public AutonomousTestWorkerStatus GetStatus(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return _statuses.TryGetValue(agentId, out var status)
            ? status
            : new AutonomousTestWorkerStatus(
                agentId,
                IsConnected: false,
                Activity: "Offline",
                UpdatedAt: DateTimeOffset.MinValue,
                Situation: null,
                LastAction: null,
                LastRationale: null);
    }

    public void MarkConnected(string agentId) =>
        Update(agentId, current => current with
        {
            IsConnected = true,
            Activity = "Waiting for authenticated Command prompt",
            UpdatedAt = DateTimeOffset.UtcNow,
            Situation = null,
            LastAction = null,
            LastRationale = null,
            WatchdogUnchangedCycles = 0,
            WatchdogNextReviewAt = null,
            LastHeartbeatAt = null
        });

    public void MarkHeartbeat(string agentId) =>
        Update(agentId, current => current with
        {
            IsConnected = true,
            UpdatedAt = DateTimeOffset.UtcNow,
            LastHeartbeatAt = DateTimeOffset.UtcNow
        });

    public void MarkDisconnected(string agentId) =>
        Update(agentId, current => current with
        {
            IsConnected = false,
            Activity = "Disconnected",
            UpdatedAt = DateTimeOffset.UtcNow
        });

    public void SetActivity(
        string agentId,
        string activity,
        GameSituationReport? situation = null) =>
        Update(agentId, current => current with
        {
            Activity = activity,
            UpdatedAt = DateTimeOffset.UtcNow,
            Situation = situation ?? current.Situation
        });

    public void RecordDecision(
        string agentId,
        string activity,
        string action,
        string rationale,
        GameSituationReport situation) =>
        Update(agentId, current => current with
        {
            IsConnected = situation.Presence == GamePresenceState.Connected,
            Activity = activity,
            UpdatedAt = DateTimeOffset.UtcNow,
            Situation = situation,
            LastAction = action,
            LastRationale = rationale
        });

    public void SetWatchdogStatus(
        string agentId,
        AutonomousActionWatchdogObservation observation) =>
        Update(agentId, current => current with
        {
            WatchdogUnchangedCycles = observation.UnchangedCycles,
            WatchdogNextReviewAt = observation.NextReviewEligibleAt
        });

    public void SetModelConfiguration(
        string agentId,
        string provider,
        string model,
        string thinkingMode) =>
        Update(agentId, current => current with
        {
            ModelProvider = provider,
            ModelName = model,
            ModelThinkingMode = thinkingMode
        });

    public void MarkModelRequestStarted(string agentId, DateTimeOffset startedAt) =>
        Update(agentId, current => current with
        {
            ModelRequestStartedAt = startedAt,
            LastModelRequestAt = startedAt,
            LastModelRequestError = null
        });

    public void RecordModelRequestCompleted(
        string agentId,
        DateTimeOffset startedAt,
        DateTimeOffset completedAt,
        bool succeeded,
        bool cancelled,
        string? error) =>
        Update(agentId, current => current with
        {
            ModelRequestStartedAt = null,
            LastModelRequestAt = startedAt,
            LastModelRequestDuration = completedAt - startedAt,
            ModelRequestsCompleted = current.ModelRequestsCompleted + (succeeded ? 1 : 0),
            ModelRequestFailures = current.ModelRequestFailures + (!succeeded && !cancelled ? 1 : 0),
            LastModelRequestError = cancelled ? "Cancelled" : error
        });

    private void Update(
        string agentId,
        Func<AutonomousTestWorkerStatus, AutonomousTestWorkerStatus> update)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(update);
        _statuses.AddOrUpdate(
            agentId,
            key => update(new AutonomousTestWorkerStatus(
                key,
                IsConnected: false,
                Activity: "Offline",
                UpdatedAt: DateTimeOffset.MinValue,
                Situation: null,
                LastAction: null,
                LastRationale: null)),
            (_, current) => update(current));
    }
}
