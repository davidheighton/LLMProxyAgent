using System.Globalization;
using LLMSurvivingProxyAgent.Core;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.Agent;

public sealed class SqliteAgentTeamStore(string connectionString) : IAgentTeamStore
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS agent_team_status (
                team_id TEXT NOT NULL COLLATE NOCASE,
                agent_id TEXT NOT NULL COLLATE NOCASE,
                session_name TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                has_fresh_snapshot INTEGER NOT NULL,
                current_sector INTEGER NULL,
                turns INTEGER NULL,
                credits INTEGER NULL,
                active_goal TEXT NULL,
                PRIMARY KEY (team_id, agent_id)
            );
            CREATE INDEX IF NOT EXISTS ix_agent_team_status_updated
                ON agent_team_status(team_id, updated_at);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task UpsertStatusAsync(
        AgentTeamStatus status,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(status);
        ArgumentException.ThrowIfNullOrWhiteSpace(status.TeamId);
        ArgumentException.ThrowIfNullOrWhiteSpace(status.AgentId);
        if (status.TeamId.Length > 64 || status.AgentId.Length > 64)
        {
            throw new ArgumentException("Team and agent IDs must be at most 64 characters.", nameof(status));
        }

        if (status.ActiveGoal is { Length: > 500 })
        {
            throw new ArgumentException("The active team goal must be at most 500 characters.", nameof(status));
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agent_team_status (
                team_id, agent_id, session_name, updated_at, has_fresh_snapshot,
                current_sector, turns, credits, active_goal
            ) VALUES (
                $teamId, $agentId, $sessionName, $updatedAt, $hasFreshSnapshot,
                $currentSector, $turns, $credits, $activeGoal
            )
            ON CONFLICT(team_id, agent_id) DO UPDATE SET
                session_name = excluded.session_name,
                updated_at = excluded.updated_at,
                has_fresh_snapshot = excluded.has_fresh_snapshot,
                current_sector = excluded.current_sector,
                turns = excluded.turns,
                credits = excluded.credits,
                active_goal = excluded.active_goal;
            """;
        command.Parameters.AddWithValue("$teamId", status.TeamId);
        command.Parameters.AddWithValue("$agentId", status.AgentId);
        command.Parameters.AddWithValue("$sessionName", status.SessionName);
        command.Parameters.AddWithValue("$updatedAt", status.UpdatedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$hasFreshSnapshot", status.HasFreshSnapshot);
        command.Parameters.AddWithValue("$currentSector", (object?)status.CurrentSector ?? DBNull.Value);
        command.Parameters.AddWithValue("$turns", (object?)status.Turns ?? DBNull.Value);
        command.Parameters.AddWithValue("$credits", (object?)status.Credits ?? DBNull.Value);
        command.Parameters.AddWithValue("$activeGoal", (object?)status.ActiveGoal ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentTeamStatus>> GetRecentTeamMembersAsync(
        string teamId,
        string excludingAgentId,
        DateTimeOffset updatedSince,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(teamId);
        ArgumentException.ThrowIfNullOrWhiteSpace(excludingAgentId);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT team_id, agent_id, session_name, updated_at, has_fresh_snapshot,
                   current_sector, turns, credits, active_goal
            FROM agent_team_status
            WHERE team_id = $teamId
              AND agent_id <> $excludingAgentId
              AND updated_at >= $updatedSince
            ORDER BY updated_at DESC
            LIMIT 16;
            """;
        command.Parameters.AddWithValue("$teamId", teamId);
        command.Parameters.AddWithValue("$excludingAgentId", excludingAgentId);
        command.Parameters.AddWithValue("$updatedSince", updatedSince.ToUniversalTime().ToString("O"));

        var statuses = new List<AgentTeamStatus>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            statuses.Add(new AgentTeamStatus(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                DateTimeOffset.Parse(
                    reader.GetString(3),
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.RoundtripKind),
                reader.GetBoolean(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.IsDBNull(6) ? null : reader.GetInt32(6),
                reader.IsDBNull(7) ? null : reader.GetInt64(7),
                reader.IsDBNull(8) ? null : reader.GetString(8)));
        }

        return statuses;
    }
}
