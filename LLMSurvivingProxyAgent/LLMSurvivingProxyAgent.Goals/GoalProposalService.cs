using LLMSurvivingProxyAgent.Knowledge;

namespace LLMSurvivingProxyAgent.Goals;

public sealed class GoalProposalService(
    IGoalProposalStore store)
{
    public async Task<GoalProposal> ProposeAsync(
        GoalProposalDraft draft,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(draft);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.AgentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Goal);
        ArgumentException.ThrowIfNullOrWhiteSpace(draft.Rationale);
        ArgumentNullException.ThrowIfNull(draft.StateSnapshot);
        ArgumentNullException.ThrowIfNull(draft.Assumptions);
        ArgumentNullException.ThrowIfNull(draft.SupportingEvidence);

        if (draft.Goal.Length > 200)
        {
            throw new ArgumentException("A proposed goal cannot exceed 200 characters.", nameof(draft));
        }
        if (draft.Rationale.Length > 2_000)
        {
            throw new ArgumentException("A proposal rationale cannot exceed 2,000 characters.", nameof(draft));
        }
        if (draft.Confidence is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(draft), "Confidence must be between 0 and 1.");
        }
        if (draft.Assumptions.Count > 20 || draft.Assumptions.Any(string.IsNullOrWhiteSpace))
        {
            throw new ArgumentException("A proposal can contain up to 20 non-empty assumptions.", nameof(draft));
        }
        if (draft.Assumptions.Any(assumption => assumption.Length > 500))
        {
            throw new ArgumentException("A proposal assumption cannot exceed 500 characters.", nameof(draft));
        }
        if (draft.SupportingEvidence.Count > 50)
        {
            throw new ArgumentException("A proposal can cite up to 50 knowledge records.", nameof(draft));
        }
        if (draft.StateSnapshot.CurrentSector is <= 0 ||
            draft.StateSnapshot.SynchronizedCredits is < 0 ||
            draft.StateSnapshot.Turns is < 0 ||
            draft.StateSnapshot.ObservedAt > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            throw new ArgumentException("The state snapshot contains invalid or future-dated game values.", nameof(draft));
        }
        if (draft.StateSnapshot.Ship is { } ship &&
            (string.IsNullOrWhiteSpace(ship.ShipType) ||
             ship.SectorId <= 0 ||
             ship.Fighters < 0 ||
             (draft.StateSnapshot.CurrentSector is { } currentSector && ship.SectorId != currentSector) ||
             (ship.ObservedAt is { } shipObservedAt && shipObservedAt > draft.StateSnapshot.ObservedAt)))
        {
            throw new ArgumentException("The ship observation contains invalid or future-dated values.", nameof(draft));
        }

        ValidateEvidence(draft.SupportingEvidence);
        var proposal = new GoalProposal(
            Guid.NewGuid().ToString("N"),
            draft.AgentId,
            draft.Goal.Trim(),
            draft.Rationale.Trim(),
            draft.Confidence,
            draft.Assumptions.Select(assumption => assumption.Trim()).ToArray(),
            draft.StateSnapshot,
            draft.SupportingEvidence,
            GoalProposalStatus.PendingSelection,
            DateTimeOffset.UtcNow,
            null,
            null);
        await store.SaveProposalAsync(proposal, cancellationToken);
        return proposal;
    }

    public Task<AgentObjective> SelectProposalAsync(
        string agentId,
        string proposalId,
        string selectionReason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectionReason);
        if (selectionReason.Length > 2_000)
        {
            throw new ArgumentException("A selection reason cannot exceed 2,000 characters.", nameof(selectionReason));
        }
        return store.SelectProposalAsync(agentId, proposalId, selectionReason, cancellationToken);
    }

    public Task<AgentObjective> CompleteObjectiveAsync(
        string agentId,
        string objectiveId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectiveId);
        return store.CompleteObjectiveAsync(agentId, objectiveId, cancellationToken);
    }

    private static void ValidateEvidence(IReadOnlyList<GoalEvidence> evidence)
    {
        var nodeIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in evidence)
        {
            if (item is null ||
                item.Node is null ||
                string.IsNullOrWhiteSpace(item.Node.Id) ||
                string.IsNullOrWhiteSpace(item.Node.DocumentId) ||
                string.IsNullOrWhiteSpace(item.Node.Title) ||
                item.Node.Evidence is not { Count: > 0 } ||
                item.Node.Evidence.Any(source => source is null ||
                    source.LineNumber < 1 ||
                    string.IsNullOrWhiteSpace(source.Text)) ||
                item.Node.Confidence is < 0 or > 1 ||
                !Enum.IsDefined(item.Node.Category) ||
                !Enum.IsDefined(item.Node.Basis) ||
                item.RelationshipPath is null)
            {
                throw new ArgumentException("Every cited knowledge record must include identified, evidenced content.");
            }
            if (!nodeIds.Add(item.Node.Id))
            {
                throw new ArgumentException("A knowledge record cannot be cited more than once.");
            }

            var previousTarget = (string?)null;
            foreach (var relation in item.RelationshipPath)
            {
                if (relation is null ||
                    string.IsNullOrWhiteSpace(relation.SourceNodeId) ||
                    string.IsNullOrWhiteSpace(relation.TargetNodeId) ||
                    relation.Evidence is not { Count: > 0 } ||
                    relation.Evidence.Any(source => source is null ||
                        source.LineNumber < 1 ||
                        string.IsNullOrWhiteSpace(source.Text)) ||
                    relation.Confidence is < 0 or > 1 ||
                    !Enum.IsDefined(relation.Type) ||
                    !Enum.IsDefined(relation.Basis) ||
                    string.IsNullOrWhiteSpace(relation.EvidenceDocumentId))
                {
                    throw new ArgumentException("Every cited graph relationship must include valid evidence.");
                }
                if (previousTarget is not null && relation.SourceNodeId != previousTarget)
                {
                    throw new ArgumentException("A cited relationship path must be continuous.");
                }
                previousTarget = relation.TargetNodeId;
            }
            if (item.RelationshipPath.Count > 0 &&
                item.RelationshipPath[^1].TargetNodeId != item.Node.Id)
            {
                throw new ArgumentException("A relationship path must terminate at its cited knowledge record.");
            }
        }
    }
}
