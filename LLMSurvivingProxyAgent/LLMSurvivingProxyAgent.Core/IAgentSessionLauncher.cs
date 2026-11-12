namespace LLMSurvivingProxyAgent.Core;

public sealed record AgentGameProfile(
    string Id,
    string Name,
    string Host,
    int Port,
    bool IsInUse);

public sealed record AgentSessionLaunchStatus(
    bool IsRunning,
    string? GameProfileName,
    string? TeamId);

public interface IAgentSessionLauncher
{
    bool IsAvailable { get; }

    Task<IReadOnlyList<AgentGameProfile>> GetGameProfilesAsync(
        CancellationToken cancellationToken = default);

    Task<AgentSessionLaunchStatus> GetStatusAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    Task SaveAssignmentAsync(
        string agentId,
        string gameProfileId,
        string? teamId,
        bool createAccountIfNeeded = false,
        CancellationToken cancellationToken = default);

    Task StartAsync(
        string agentId,
        string gameProfileId,
        string? teamId,
        bool createAccountIfNeeded = false,
        CancellationToken cancellationToken = default);
}
