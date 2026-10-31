using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed class GameSituationObservationService(
    IGameSituationProvider situationProvider,
    IAgentExperienceRecorder experienceRecorder)
{
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
}
