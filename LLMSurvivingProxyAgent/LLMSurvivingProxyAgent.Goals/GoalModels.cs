using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Knowledge;

namespace LLMSurvivingProxyAgent.Goals;

public enum GoalProposalStatus
{
    PendingSelection,
    Selected,
    LegacyApproved,
    LegacyRejected
}

public enum ObjectiveStatus
{
    Active,
    Completed,
    Superseded
}

public sealed record GoalEvidence(
    KnowledgeNode Node,
    IReadOnlyList<KnowledgeRelation> RelationshipPath);

public sealed record GoalProposal(
    string ProposalId,
    string AgentId,
    string Goal,
    string Rationale,
    decimal Confidence,
    IReadOnlyList<string> Assumptions,
    GameSnapshot StateSnapshot,
    IReadOnlyList<GoalEvidence> SupportingEvidence,
    GoalProposalStatus Status,
    DateTimeOffset ProposedAt,
    DateTimeOffset? SelectedAt,
    string? SelectionReason);

public sealed record GoalProposalDraft(
    string AgentId,
    string Goal,
    string Rationale,
    decimal Confidence,
    IReadOnlyList<string> Assumptions,
    GameSnapshot StateSnapshot,
    IReadOnlyList<GoalEvidence> SupportingEvidence);

public sealed record AgentObjective(
    string ObjectiveId,
    string AgentId,
    string SourceProposalId,
    string Goal,
    string Rationale,
    ObjectiveStatus Status,
    DateTimeOffset CreatedAt);
