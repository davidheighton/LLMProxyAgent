namespace LLMSurvivingProxyAgent.Goals;

public interface IGoalProposalStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task SaveProposalAsync(GoalProposal proposal, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<GoalProposal>> GetProposalsAsync(
        string? agentId = null,
        GoalProposalStatus? status = null,
        int limit = 100,
        CancellationToken cancellationToken = default);
    Task<AgentObjective> SelectProposalAsync(
        string agentId,
        string proposalId,
        string selectionReason,
        CancellationToken cancellationToken = default);
    Task<AgentObjective> CompleteObjectiveAsync(
        string agentId,
        string objectiveId,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<AgentObjective>> GetObjectivesAsync(
        string? agentId = null,
        ObjectiveStatus? status = null,
        int limit = 100,
        CancellationToken cancellationToken = default);
}
