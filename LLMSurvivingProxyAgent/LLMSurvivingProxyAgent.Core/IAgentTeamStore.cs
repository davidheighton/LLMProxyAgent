namespace LLMSurvivingProxyAgent.Core;

public sealed record AgentTeamStatus(
    string TeamId,
    string AgentId,
    string SessionName,
    DateTimeOffset UpdatedAt,
    bool HasFreshSnapshot,
    int? CurrentSector,
    int? Turns,
    long? Credits,
    string? ActiveGoal);

public interface IAgentTeamStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task UpsertStatusAsync(
        AgentTeamStatus status,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<AgentTeamStatus>> GetRecentTeamMembersAsync(
        string teamId,
        string excludingAgentId,
        DateTimeOffset updatedSince,
        CancellationToken cancellationToken = default);
}
