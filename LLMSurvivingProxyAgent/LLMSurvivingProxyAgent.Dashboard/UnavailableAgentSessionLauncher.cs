using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Dashboard;

public sealed class UnavailableAgentSessionLauncher : IAgentSessionLauncher
{
    public bool IsAvailable => false;

    public Task<IReadOnlyList<AgentGameProfile>> GetGameProfilesAsync(
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IReadOnlyList<AgentGameProfile>>([]);
    }

    public Task<AgentSessionLaunchStatus> GetStatusAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AgentSessionLaunchStatus(false, null, null));
    }

    public Task SaveAssignmentAsync(
        string agentId,
        string gameProfileId,
        string? teamId,
        bool createAccountIfNeeded = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameProfileId);
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "Saving isolated game assignments is available only in the embedded MTC Control Center.");
    }

    public Task StartAsync(
        string agentId,
        string gameProfileId,
        string? teamId,
        bool createAccountIfNeeded = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(gameProfileId);
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "Starting isolated game sessions is available only in the embedded MTC Control Center.");
    }
}
