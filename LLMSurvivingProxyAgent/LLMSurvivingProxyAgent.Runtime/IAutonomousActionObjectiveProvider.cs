using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Runtime;

public interface IAutonomousActionObjectiveProvider
{
    Task<AutonomousActionObjective?> GetOrCreateActiveObjectiveAsync(
        string agentId,
        GameSnapshot snapshot,
        CancellationToken cancellationToken = default);

    Task<AutonomousActionObjective?> GetOrCreateActiveObjectiveAsync(
        string agentId,
        GameSituationReport situation,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(situation);
        if (situation.Snapshot is not { } snapshot)
        {
            throw new ArgumentException(
                "An active objective requires a synchronized game snapshot.",
                nameof(situation));
        }

        return GetOrCreateActiveObjectiveAsync(agentId, snapshot, cancellationToken);
    }

    void RequestStrategicReview(string agentId, string reason)
    {
    }
}
