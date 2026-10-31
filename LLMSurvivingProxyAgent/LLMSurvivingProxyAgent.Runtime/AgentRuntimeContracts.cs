using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Runtime;

public interface IAgentRuntime
{
    Task<ReactiveEventRecord> SubmitReactiveEventAsync(
        ReactiveEvent reactiveEvent,
        CancellationToken cancellationToken = default);

    Task<StrategicReviewRecord> SubmitStrategicReviewAsync(
        string agentId,
        GameSnapshot snapshot,
        CancellationToken cancellationToken = default);

    AgentRuntimeStatus GetStatus();
}

public sealed record AgentRuntimeStatus(
    bool IsRunning,
    int ReactiveQueueDepth,
    int StrategicQueueDepth,
    DateTimeOffset? LastReactiveCycleAt,
    DateTimeOffset? LastStrategicCycleAt);
