using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Dashboard;

public sealed class UnavailableAgentSessionController : IAgentSessionController
{
    public bool IsAvailable => false;

    public bool SupportsAgent(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        return false;
    }

    public Task<AgentSessionControlStatus> GetStatusAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult(new AgentSessionControlStatus(false, false, false, false));
    }

    public Task ConfigureConnectionAsync(string agentId, CancellationToken cancellationToken = default) =>
        NotAvailable(agentId, cancellationToken);

    public Task<bool> LoadSavedConnectionAsync(string agentId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "Native MTC session controls are available only in the embedded Control Center.");
    }

    public Task ConnectAsync(string agentId, CancellationToken cancellationToken = default) =>
        NotAvailable(agentId, cancellationToken);

    public Task DisconnectAsync(string agentId, CancellationToken cancellationToken = default) =>
        NotAvailable(agentId, cancellationToken);

    public Task RefreshCimAsync(string agentId, CancellationToken cancellationToken = default) =>
        NotAvailable(agentId, cancellationToken);

    public Task StartAgentAsync(string agentId, CancellationToken cancellationToken = default) =>
        NotAvailable(agentId, cancellationToken);

    public Task StopAgentAsync(string agentId, CancellationToken cancellationToken = default) =>
        NotAvailable(agentId, cancellationToken);

    public Task StartLearningAsync(string agentId, CancellationToken cancellationToken = default) =>
        NotAvailable(agentId, cancellationToken);

    public Task StopLearningAsync(string agentId, CancellationToken cancellationToken = default) =>
        NotAvailable(agentId, cancellationToken);

    private static Task NotAvailable(string agentId, CancellationToken cancellationToken)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        cancellationToken.ThrowIfCancellationRequested();
        throw new InvalidOperationException(
            "Native MTC session controls are available only in the embedded Control Center.");
    }
}
