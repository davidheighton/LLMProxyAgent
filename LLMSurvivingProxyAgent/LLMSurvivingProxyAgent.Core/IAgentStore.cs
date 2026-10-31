namespace LLMSurvivingProxyAgent.Core;

public interface IAgentStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentContainer>> GetAgentsAsync(CancellationToken cancellationToken = default);
    Task<AgentContainer?> GetAgentAsync(
        string agentId,
        CancellationToken cancellationToken = default);
    Task SaveAgentAsync(AgentContainer agent, CancellationToken cancellationToken = default);
    Task AppendJournalEntryAsync(
        string agentId,
        AgentJournalEntry entry,
        CancellationToken cancellationToken = default);
}
