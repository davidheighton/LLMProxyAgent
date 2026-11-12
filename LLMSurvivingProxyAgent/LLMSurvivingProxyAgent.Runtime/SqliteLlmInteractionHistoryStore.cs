using System.Globalization;
using LLMSurvivingProxyAgent.Core;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.Runtime;

/// <summary>
/// Durable LLM interaction history. Every record is written through to SQLite so prompts and replies
/// survive restarts; reads are served by the bounded in-memory cache, which is reloaded at startup.
/// </summary>
public sealed class SqliteLlmInteractionHistoryStore : ILlmInteractionHistoryStore
{
    private readonly string _connectionString;
    private readonly LlmInteractionHistoryStore _cache = new();
    private readonly object _writeSync = new();

    public SqliteLlmInteractionHistoryStore(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        _connectionString = connectionString;
        Initialize();
        LoadRecentIntoCache();
    }

    public void Record(LlmInteractionRecord interaction)
    {
        _cache.Record(interaction);
        lock (_writeSync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                INSERT INTO llm_interactions(
                    interaction_id, agent_id, role, provider, model, attempt, started_at, completed_at,
                    prompt, response, succeeded, error, applied_outcome, in_progress
                ) VALUES (
                    $id, $agentId, $role, $provider, $model, $attempt, $startedAt, $completedAt,
                    $prompt, $response, $succeeded, $error, $appliedOutcome, $inProgress
                )
                ON CONFLICT(interaction_id) DO UPDATE SET
                    completed_at = excluded.completed_at,
                    prompt = excluded.prompt,
                    response = excluded.response,
                    succeeded = excluded.succeeded,
                    error = excluded.error,
                    applied_outcome = COALESCE(excluded.applied_outcome, llm_interactions.applied_outcome),
                    in_progress = excluded.in_progress;
                """;
            command.Parameters.AddWithValue("$id", interaction.InteractionId);
            command.Parameters.AddWithValue("$agentId", interaction.AgentId);
            command.Parameters.AddWithValue("$role", interaction.Role);
            command.Parameters.AddWithValue("$provider", interaction.Provider);
            command.Parameters.AddWithValue("$model", interaction.Model);
            command.Parameters.AddWithValue("$attempt", interaction.Attempt);
            command.Parameters.AddWithValue("$startedAt", interaction.StartedAt.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$completedAt", interaction.CompletedAt.ToUniversalTime().ToString("O"));
            command.Parameters.AddWithValue("$prompt", interaction.Prompt);
            command.Parameters.AddWithValue("$response", interaction.Response);
            command.Parameters.AddWithValue("$succeeded", interaction.Succeeded ? 1 : 0);
            command.Parameters.AddWithValue("$error", (object?)interaction.Error ?? DBNull.Value);
            command.Parameters.AddWithValue("$appliedOutcome", (object?)interaction.AppliedOutcome ?? DBNull.Value);
            command.Parameters.AddWithValue("$inProgress", interaction.IsInProgress ? 1 : 0);
            command.ExecuteNonQuery();
        }
    }

    public void RecordAppliedOutcome(string interactionId, string outcome)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(interactionId);
        ArgumentException.ThrowIfNullOrWhiteSpace(outcome);

        int updated;
        lock (_writeSync)
        {
            using var connection = new SqliteConnection(_connectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                UPDATE llm_interactions SET applied_outcome = $outcome WHERE interaction_id = $id;
                """;
            command.Parameters.AddWithValue("$id", interactionId);
            command.Parameters.AddWithValue("$outcome", outcome);
            updated = command.ExecuteNonQuery();
        }

        try
        {
            _cache.RecordAppliedOutcome(interactionId, outcome);
        }
        catch (KeyNotFoundException) when (updated > 0)
        {
            // The interaction aged out of the bounded cache but its durable record was updated.
        }
    }

    public IReadOnlyList<LlmInteractionRecord> GetRecent(string agentId, int limit = 50) =>
        _cache.GetRecent(agentId, limit);

    public IReadOnlyList<LlmInteractionRecord> GetRecentByRole(string agentId, string role, int limit = 50) =>
        _cache.GetRecentByRole(agentId, role, limit);

    private void Initialize()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS llm_interactions (
                interaction_id TEXT PRIMARY KEY,
                agent_id TEXT NOT NULL COLLATE NOCASE,
                role TEXT NOT NULL COLLATE NOCASE,
                provider TEXT NOT NULL,
                model TEXT NOT NULL,
                attempt INTEGER NOT NULL,
                started_at TEXT NOT NULL,
                completed_at TEXT NOT NULL,
                prompt TEXT NOT NULL,
                response TEXT NOT NULL,
                succeeded INTEGER NOT NULL,
                error TEXT NULL,
                applied_outcome TEXT NULL,
                in_progress INTEGER NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_llm_interactions_agent_role_started
                ON llm_interactions(agent_id, role, started_at DESC);
            """;
        command.ExecuteNonQuery();
    }

    private void LoadRecentIntoCache()
    {
        using var connection = new SqliteConnection(_connectionString);
        connection.Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT interaction_id, agent_id, role, provider, model, attempt, started_at, completed_at,
                   prompt, response, succeeded, error, applied_outcome, in_progress
            FROM (
                SELECT *, ROW_NUMBER() OVER (
                    PARTITION BY agent_id, role ORDER BY started_at DESC, interaction_id DESC) AS row_number
                FROM llm_interactions
            )
            WHERE row_number <= $limit
            ORDER BY started_at ASC, interaction_id ASC;
            """;
        command.Parameters.AddWithValue("$limit", LlmInteractionHistoryStore.MaximumInteractionsPerAgent);
        using var reader = command.ExecuteReader();
        while (reader.Read())
        {
            bool inProgress = reader.GetInt64(13) != 0;
            _cache.Record(new LlmInteractionRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetString(4),
                reader.GetInt32(5),
                DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(7), CultureInfo.InvariantCulture),
                reader.GetString(8),
                reader.GetString(9),
                reader.GetInt64(10) != 0,
                // An in-progress record found at startup was interrupted by the previous shutdown.
                inProgress && reader.IsDBNull(11) ? "Interrupted before completion." : reader.IsDBNull(11) ? null : reader.GetString(11),
                reader.IsDBNull(12) ? null : reader.GetString(12))
            {
                IsInProgress = false
            });
        }
    }
}
