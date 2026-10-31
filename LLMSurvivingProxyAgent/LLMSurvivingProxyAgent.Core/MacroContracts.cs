namespace LLMSurvivingProxyAgent.Core;

public enum MacroCategory
{
    Trading,
    Navigation,
    Exploration,
    Recovery,
    ShipManagement
}

public enum MacroExecutionStatus
{
    Succeeded,
    Planned,
    Blocked,
    Failed,
    Cancelled
}

public sealed record MacroDescriptor(
    string Id,
    string Name,
    string Description,
    MacroCategory Category,
    IReadOnlyList<string> Requirements,
    string ExpectedOutcome);

public sealed record MacroSelection(string MacroId, string Rationale);

public sealed record MacroResult(
    MacroExecutionStatus Status,
    string Summary,
    string Notes,
    IReadOnlyList<int> PlanSectors);

public sealed record MacroExecutionRecord(
    string ExecutionId,
    string AgentId,
    string MacroId,
    string MacroName,
    MacroCategory Category,
    DateTimeOffset StartTime,
    DateTimeOffset EndTime,
    MacroExecutionStatus Status,
    string Result,
    string Notes,
    IReadOnlyList<int> PlanSectors);

public interface IMacroTelemetryStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(MacroExecutionRecord record, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<MacroExecutionRecord>> GetRecentAsync(
        string? agentId = null,
        int limit = 100,
        CancellationToken cancellationToken = default);
}

public interface IAgentExperienceRecorder
{
    Task RecordMacroExecutionAsync(
        MacroExecutionRecord record,
        GameSnapshot? beforeSnapshot = null,
        GameSnapshot? afterSnapshot = null,
        CancellationToken cancellationToken = default);
    Task RecordTurnObservationAsync(
        string agentId,
        string sessionId,
        int turnNumber,
        GameSnapshot beforeSnapshot,
        GameSnapshot afterSnapshot,
        CancellationToken cancellationToken = default);
    Task RecordSituationObservationAsync(
        string agentId,
        string sessionId,
        GameSituationReport report,
        CancellationToken cancellationToken = default);
}

public interface ISynchronizedGameSnapshotProvider
{
    Task<GameSnapshot?> CaptureSynchronizedSnapshotAsync(
        string agentId,
        CancellationToken cancellationToken = default);
}

public interface IGameSituationProvider
{
    Task<GameSituationReport> CaptureSituationAsync(
        string agentId,
        CancellationToken cancellationToken = default);
}
