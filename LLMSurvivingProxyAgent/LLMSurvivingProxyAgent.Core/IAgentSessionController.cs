namespace LLMSurvivingProxyAgent.Core;

public sealed record AgentSessionControlStatus(
    bool IsConnected,
    bool IsAgentRunning,
    bool IsLearning,
    bool CanConnect);

public interface IAgentSessionController
{
    bool IsAvailable { get; }
    bool SupportsAgent(string agentId);

    Task<AgentSessionControlStatus> GetStatusAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    Task ConfigureConnectionAsync(string agentId, CancellationToken cancellationToken = default);
    Task<bool> LoadSavedConnectionAsync(string agentId, CancellationToken cancellationToken = default);
    Task ConnectAsync(string agentId, CancellationToken cancellationToken = default);
    Task DisconnectAsync(string agentId, CancellationToken cancellationToken = default);
    Task RefreshCimAsync(string agentId, CancellationToken cancellationToken = default);
    Task StartAgentAsync(string agentId, CancellationToken cancellationToken = default);
    Task StopAgentAsync(string agentId, CancellationToken cancellationToken = default);
    Task StartLearningAsync(string agentId, CancellationToken cancellationToken = default);
    Task StopLearningAsync(string agentId, CancellationToken cancellationToken = default);
}
