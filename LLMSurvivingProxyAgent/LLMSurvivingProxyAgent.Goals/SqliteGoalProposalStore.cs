using System.Globalization;
using System.Text.Json;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.Goals;

public sealed class SqliteGoalProposalStore(string connectionString) : IGoalProposalStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS goal_proposals (
                proposal_id TEXT PRIMARY KEY,
                agent_id TEXT NOT NULL REFERENCES agents(agent_id) ON DELETE CASCADE,
                goal TEXT NOT NULL,
                rationale TEXT NOT NULL,
                confidence REAL NOT NULL CHECK(confidence >= 0 AND confidence <= 1),
                assumptions_json TEXT NOT NULL,
                state_snapshot_json TEXT NOT NULL,
                evidence_json TEXT NOT NULL,
                status TEXT NOT NULL,
                proposed_at TEXT NOT NULL,
                decided_at TEXT,
                decision_note TEXT
            );
            CREATE INDEX IF NOT EXISTS ix_goal_proposals_agent_status
                ON goal_proposals(agent_id, status, proposed_at);
            CREATE TABLE IF NOT EXISTS agent_objectives (
                objective_id TEXT PRIMARY KEY,
                agent_id TEXT NOT NULL REFERENCES agents(agent_id) ON DELETE CASCADE,
                source_proposal_id TEXT NOT NULL UNIQUE REFERENCES goal_proposals(proposal_id) ON DELETE CASCADE,
                goal TEXT NOT NULL,
                rationale TEXT NOT NULL,
                status TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_agent_objectives_agent_status
                ON agent_objectives(agent_id, status, created_at);
            UPDATE goal_proposals
            SET status = 'PendingSelection'
            WHERE status = 'PendingApproval';
            UPDATE goal_proposals
            SET status = 'LegacyApproved'
            WHERE status = 'Approved';
            UPDATE goal_proposals
            SET status = 'LegacyRejected'
            WHERE status = 'Rejected';
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveProposalAsync(
        GoalProposal proposal,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proposal);
        if (proposal.Status != GoalProposalStatus.PendingSelection ||
            proposal.SelectedAt is not null ||
            proposal.SelectionReason is not null)
        {
            throw new ArgumentException("New proposals must be awaiting automatic selection.", nameof(proposal));
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO goal_proposals(
                proposal_id, agent_id, goal, rationale, confidence, assumptions_json,
                state_snapshot_json, evidence_json, status, proposed_at, decided_at, decision_note
            ) VALUES (
                $id, $agentId, $goal, $rationale, $confidence, $assumptions,
                $snapshot, $evidence, $status, $proposedAt, NULL, NULL
            );
            """;
        command.Parameters.AddWithValue("$id", proposal.ProposalId);
        command.Parameters.AddWithValue("$agentId", proposal.AgentId);
        command.Parameters.AddWithValue("$goal", proposal.Goal);
        command.Parameters.AddWithValue("$rationale", proposal.Rationale);
        command.Parameters.AddWithValue("$confidence", proposal.Confidence);
        command.Parameters.AddWithValue("$assumptions", Serialize(proposal.Assumptions));
        command.Parameters.AddWithValue("$snapshot", Serialize(proposal.StateSnapshot));
        command.Parameters.AddWithValue("$evidence", Serialize(proposal.SupportingEvidence));
        command.Parameters.AddWithValue("$status", proposal.Status.ToString());
        command.Parameters.AddWithValue("$proposedAt", proposal.ProposedAt.ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<GoalProposal>> GetProposalsAsync(
        string? agentId = null,
        GoalProposalStatus? status = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ValidateLimit(limit);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT proposal_id, agent_id, goal, rationale, confidence, assumptions_json,
                   state_snapshot_json, evidence_json, status, proposed_at, decided_at, decision_note
            FROM goal_proposals
            WHERE ($agentId IS NULL OR agent_id = $agentId)
              AND ($status IS NULL OR status = $status)
            ORDER BY proposed_at DESC, proposal_id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$agentId", (object?)agentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (object?)status?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        var proposals = new List<GoalProposal>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            proposals.Add(new GoalProposal(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                Convert.ToDecimal(reader.GetDouble(4), CultureInfo.InvariantCulture),
                Deserialize<List<string>>(reader.GetString(5)),
                Deserialize<LLMSurvivingProxyAgent.Core.GameSnapshot>(reader.GetString(6)),
                Deserialize<List<GoalEvidence>>(reader.GetString(7)),
                Enum.Parse<GoalProposalStatus>(reader.GetString(8)),
                DateTimeOffset.Parse(reader.GetString(9), CultureInfo.InvariantCulture),
                reader.IsDBNull(10) ? null : DateTimeOffset.Parse(reader.GetString(10), CultureInfo.InvariantCulture),
                reader.IsDBNull(11) ? null : reader.GetString(11)));
        }
        return proposals;
    }

    public async Task<AgentObjective> SelectProposalAsync(
        string agentId,
        string proposalId,
        string selectionReason,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(proposalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(selectionReason);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var proposalCommand = connection.CreateCommand();
        proposalCommand.Transaction = transaction;
        proposalCommand.CommandText = """
            SELECT goal, rationale, status FROM goal_proposals
            WHERE proposal_id = $proposalId AND agent_id = $agentId;
            """;
        proposalCommand.Parameters.AddWithValue("$proposalId", proposalId);
        proposalCommand.Parameters.AddWithValue("$agentId", agentId);
        string goal;
        string rationale;
        string status;
        await using (var reader = await proposalCommand.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                throw new KeyNotFoundException($"Proposal '{proposalId}' was not found for agent '{agentId}'.");
            }
            goal = reader.GetString(0);
            rationale = reader.GetString(1);
            status = reader.GetString(2);
        }
        if (status != GoalProposalStatus.PendingSelection.ToString())
        {
            throw new InvalidOperationException($"Proposal '{proposalId}' is not pending selection (current status: {status}).");
        }

        var supersede = connection.CreateCommand();
        supersede.Transaction = transaction;
        supersede.CommandText = """
            UPDATE agent_objectives SET status = $supersededStatus
            WHERE agent_id = $agentId AND status = $activeStatus;
            """;
        supersede.Parameters.AddWithValue("$supersededStatus", ObjectiveStatus.Superseded.ToString());
        supersede.Parameters.AddWithValue("$agentId", agentId);
        supersede.Parameters.AddWithValue("$activeStatus", ObjectiveStatus.Active.ToString());
        await supersede.ExecuteNonQueryAsync(cancellationToken);

        var selectedAt = DateTimeOffset.UtcNow;
        var updateProposal = connection.CreateCommand();
        updateProposal.Transaction = transaction;
        updateProposal.CommandText = """
            UPDATE goal_proposals
            SET status = $selectedStatus, decided_at = $selectedAt, decision_note = $selectionReason
            WHERE proposal_id = $proposalId AND agent_id = $agentId
              AND status = $pendingStatus;
            """;
        updateProposal.Parameters.AddWithValue("$selectedStatus", GoalProposalStatus.Selected.ToString());
        updateProposal.Parameters.AddWithValue("$selectedAt", selectedAt.ToString("O"));
        updateProposal.Parameters.AddWithValue("$selectionReason", selectionReason);
        updateProposal.Parameters.AddWithValue("$proposalId", proposalId);
        updateProposal.Parameters.AddWithValue("$agentId", agentId);
        updateProposal.Parameters.AddWithValue("$pendingStatus", GoalProposalStatus.PendingSelection.ToString());
        if (await updateProposal.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            throw new InvalidOperationException($"Proposal '{proposalId}' changed during objective selection.");
        }

        var objective = new AgentObjective(
            Guid.NewGuid().ToString("N"),
            agentId,
            proposalId,
            goal,
            rationale,
            ObjectiveStatus.Active,
            selectedAt);
        var insertObjective = connection.CreateCommand();
        insertObjective.Transaction = transaction;
        insertObjective.CommandText = """
            INSERT INTO agent_objectives(
                objective_id, agent_id, source_proposal_id, goal, rationale, status, created_at
            ) VALUES ($objectiveId, $agentId, $proposalId, $goal, $rationale, $status, $createdAt);
            """;
        insertObjective.Parameters.AddWithValue("$objectiveId", objective.ObjectiveId);
        insertObjective.Parameters.AddWithValue("$agentId", objective.AgentId);
        insertObjective.Parameters.AddWithValue("$proposalId", objective.SourceProposalId);
        insertObjective.Parameters.AddWithValue("$goal", objective.Goal);
        insertObjective.Parameters.AddWithValue("$rationale", objective.Rationale);
        insertObjective.Parameters.AddWithValue("$status", objective.Status.ToString());
        insertObjective.Parameters.AddWithValue("$createdAt", objective.CreatedAt.ToString("O"));
        await insertObjective.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return objective;
    }

    public async Task<AgentObjective> CompleteObjectiveAsync(
        string agentId,
        string objectiveId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(objectiveId);

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);
        var complete = connection.CreateCommand();
        complete.Transaction = transaction;
        complete.CommandText = """
            UPDATE agent_objectives
            SET status = $completedStatus
            WHERE objective_id = $objectiveId AND agent_id = $agentId AND status = $activeStatus;
            """;
        complete.Parameters.AddWithValue("$completedStatus", ObjectiveStatus.Completed.ToString());
        complete.Parameters.AddWithValue("$objectiveId", objectiveId);
        complete.Parameters.AddWithValue("$agentId", agentId);
        complete.Parameters.AddWithValue("$activeStatus", ObjectiveStatus.Active.ToString());
        if (await complete.ExecuteNonQueryAsync(cancellationToken) != 1)
        {
            var lookup = connection.CreateCommand();
            lookup.Transaction = transaction;
            lookup.CommandText = """
                SELECT status FROM agent_objectives
                WHERE objective_id = $objectiveId AND agent_id = $agentId;
                """;
            lookup.Parameters.AddWithValue("$objectiveId", objectiveId);
            lookup.Parameters.AddWithValue("$agentId", agentId);
            var currentStatus = await lookup.ExecuteScalarAsync(cancellationToken);
            if (currentStatus is null)
            {
                throw new KeyNotFoundException($"Objective '{objectiveId}' was not found for agent '{agentId}'.");
            }
            throw new InvalidOperationException(
                $"Objective '{objectiveId}' is not active (current status: {currentStatus}).");
        }

        var query = connection.CreateCommand();
        query.Transaction = transaction;
        query.CommandText = """
            SELECT objective_id, agent_id, source_proposal_id, goal, rationale, status, created_at
            FROM agent_objectives WHERE objective_id = $objectiveId AND agent_id = $agentId;
            """;
        query.Parameters.AddWithValue("$objectiveId", objectiveId);
        query.Parameters.AddWithValue("$agentId", agentId);
        await using var reader = await query.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new InvalidDataException($"Completed objective '{objectiveId}' could not be reloaded.");
        }
        var completed = new AgentObjective(
            reader.GetString(0),
            reader.GetString(1),
            reader.GetString(2),
            reader.GetString(3),
            reader.GetString(4),
            Enum.Parse<ObjectiveStatus>(reader.GetString(5)),
            DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture));
        await reader.DisposeAsync();
        await transaction.CommitAsync(cancellationToken);
        return completed;
    }

    public async Task<IReadOnlyList<AgentObjective>> GetObjectivesAsync(
        string? agentId = null,
        ObjectiveStatus? status = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        ValidateLimit(limit);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT objective_id, agent_id, source_proposal_id, goal, rationale, status, created_at
            FROM agent_objectives
            WHERE ($agentId IS NULL OR agent_id = $agentId)
              AND ($status IS NULL OR status = $status)
            ORDER BY created_at DESC, objective_id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$agentId", (object?)agentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$status", (object?)status?.ToString() ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        var objectives = new List<AgentObjective>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            objectives.Add(new AgentObjective(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                Enum.Parse<ObjectiveStatus>(reader.GetString(5)),
                DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture)));
        }
        return objectives;
    }

    private static void ValidateLimit(int limit)
    {
        if (limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 500.");
        }
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static T Deserialize<T>(string value) =>
        JsonSerializer.Deserialize<T>(value, JsonOptions)
        ?? throw new InvalidDataException($"Stored {typeof(T).Name} JSON is empty or invalid.");
}
