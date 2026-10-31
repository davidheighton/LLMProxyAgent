using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Runtime;

public interface IAutonomousActionKnowledgeProvider
{
    Task<IReadOnlyList<AutonomousActionKnowledgeItem>> GetRelevantKnowledgeAsync(
        string agentId,
        CancellationToken cancellationToken = default);
}
