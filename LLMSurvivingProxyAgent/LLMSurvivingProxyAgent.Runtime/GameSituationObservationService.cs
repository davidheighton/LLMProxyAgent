using System.Collections.Concurrent;
using System.Text.Json;
using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed class GameSituationObservationService(
    IGameSituationProvider situationProvider,
    IAgentExperienceRecorder experienceRecorder)
{
    private readonly ConcurrentDictionary<string, string> _lastRecordedStates =
        new(StringComparer.Ordinal);

    public async Task<GameSituationObservationResult> CaptureAndRecordAsync(
        string agentId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var report = await situationProvider.CaptureSituationAsync(agentId, cancellationToken);
        var rejectionReason = GetRejectionReason(report);
        if (rejectionReason is not null)
        {
            return new GameSituationObservationResult(report, false, rejectionReason);
        }

        await experienceRecorder.RecordSituationObservationAsync(
            agentId,
            sessionId,
            report,
            cancellationToken);
        return new GameSituationObservationResult(report, true, null);
    }

    public async Task<GameSituationObservationResult> CaptureAndRecordChangedAsync(
        string agentId,
        string sessionId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionId);

        var report = await situationProvider.CaptureSituationAsync(agentId, cancellationToken);
        var rejectionReason = GetRejectionReason(report);
        if (rejectionReason is not null)
        {
            return new GameSituationObservationResult(report, false, rejectionReason);
        }

        string sessionKey = $"{agentId}\n{sessionId}";
        string stateFingerprint = GetStateFingerprint(report);
        if (_lastRecordedStates.TryGetValue(sessionKey, out string? lastState) &&
            string.Equals(lastState, stateFingerprint, StringComparison.Ordinal))
        {
            return new GameSituationObservationResult(
                report,
                false,
                "The synchronized game state has not changed since the previous observation.");
        }

        await experienceRecorder.RecordSituationObservationAsync(
            agentId,
            sessionId,
            report,
            cancellationToken);
        _lastRecordedStates[sessionKey] = stateFingerprint;
        return new GameSituationObservationResult(report, true, null);
    }

    private static string? GetRejectionReason(GameSituationReport report)
    {
        if (report.Presence != GamePresenceState.Connected)
        {
            return $"Presence is {report.Presence}; a current connection is required.";
        }
        if (report.Authentication != GameAuthenticationState.Authenticated)
        {
            return "Authentication is unverified; a recognized in-game prompt is required.";
        }
        if (report.Synchronization != GameSynchronizationState.Healthy)
        {
            return $"Synchronization is {report.Synchronization}; a trusted full-status event is required.";
        }
        if (report.Snapshot is not { Authenticated: true } snapshot ||
            !GameSnapshotValidation.IsValid(snapshot))
        {
            return "A valid authenticated synchronized snapshot is unavailable.";
        }

        return null;
    }

    private static string GetStateFingerprint(GameSituationReport report)
    {
        GameSnapshot snapshot = report.Snapshot! with
        {
            ObservedAt = default,
            CurrentPromptObservedAt = null
        };
        return JsonSerializer.Serialize(new
        {
            report.Presence,
            report.Authentication,
            report.Synchronization,
            report.CurrentPrompt,
            report.UnknownFields,
            RecentServerText = report.RecentServerText.Select(item => item.Text).ToArray(),
            ScriptInputPrompt = report.ScriptInputPrompt is { } prompt
                ? new { prompt.ScriptName, prompt.Text, prompt.SingleKey }
                : null,
            Snapshot = snapshot
        });
    }
}
