using System.Text.Json;
using LLMSurvivingProxyAgent.Core;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.Agent;

public sealed class SqliteAgentStore(string connectionString) : IAgentStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS agents (
                agent_id TEXT PRIMARY KEY,
                display_name TEXT NOT NULL,
                description TEXT NOT NULL,
                created_date TEXT NOT NULL,
                preferred_model TEXT NOT NULL,
                preferred_provider TEXT NOT NULL,
                status TEXT NOT NULL,
                goals_json TEXT NOT NULL,
                configuration_json TEXT NOT NULL,
                statistics_json TEXT NOT NULL
            );
            CREATE TABLE IF NOT EXISTS agent_memories (
                agent_id TEXT NOT NULL REFERENCES agents(agent_id) ON DELETE CASCADE,
                memory_key TEXT NOT NULL,
                value TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                PRIMARY KEY (agent_id, memory_key)
            );
            CREATE TABLE IF NOT EXISTS agent_journal (
                journal_id INTEGER PRIMARY KEY AUTOINCREMENT,
                agent_id TEXT NOT NULL REFERENCES agents(agent_id) ON DELETE CASCADE,
                occurred_at TEXT NOT NULL,
                kind TEXT NOT NULL,
                message TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_agent_journal_agent_time
                ON agent_journal(agent_id, occurred_at);
            CREATE TABLE IF NOT EXISTS agent_schema_migrations (
                migration_id TEXT PRIMARY KEY
            );
            UPDATE agents SET status = 'Offline' WHERE status <> 'Offline';
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);

        var migrateLegacyMemory = connection.CreateCommand();
        migrateLegacyMemory.Transaction = transaction;
        migrateLegacyMemory.CommandText = """
            INSERT INTO agent_journal(agent_id, occurred_at, kind, message)
            SELECT memory.agent_id, memory.updated_at, 'LegacyMemory',
                   memory.memory_key || ': ' || memory.value
            FROM agent_memories AS memory
            WHERE NOT EXISTS (
                SELECT 1 FROM agent_schema_migrations
                WHERE migration_id = 'agent-memory-to-journal-v1'
            );
            INSERT OR IGNORE INTO agent_schema_migrations(migration_id)
            VALUES ('agent-memory-to-journal-v1');
            """;
        await migrateLegacyMemory.ExecuteNonQueryAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<AgentContainer>> GetAgentsAsync(
        CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT agent_id, display_name, description, created_date, preferred_model,
                   preferred_provider, status, goals_json, configuration_json, statistics_json
            FROM agents
            ORDER BY created_date, display_name;
            """;

        var agents = new List<AgentContainer>();
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                agents.Add(new AgentContainer
                {
                    AgentId = reader.GetString(0),
                    DisplayName = reader.GetString(1),
                    Description = reader.GetString(2),
                    CreatedDate = DateTimeOffset.Parse(reader.GetString(3)),
                    PreferredModel = reader.GetString(4),
                    PreferredProvider = reader.GetString(5),
                    Status = Enum.Parse<AgentStatus>(reader.GetString(6)),
                    Goals = Deserialize<List<string>>(reader.GetString(7)),
                    Configuration = Deserialize<Dictionary<string, string>>(reader.GetString(8)),
                    Statistics = Deserialize<AgentStatistics>(reader.GetString(9))
                });
            }
        }

        foreach (var agent in agents)
        {
            agent.Journal.AddRange(await GetJournalAsync(connection, agent.AgentId, cancellationToken));
        }

        return agents;
    }

    public async Task<AgentContainer?> GetAgentAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT agent_id, display_name, description, created_date, preferred_model,
                   preferred_provider, status, goals_json, configuration_json, statistics_json
            FROM agents
            WHERE agent_id = $agentId;
            """;
        command.Parameters.AddWithValue("$agentId", agentId);
        AgentContainer agent;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken))
            {
                return null;
            }

            agent = new AgentContainer
            {
                AgentId = reader.GetString(0),
                DisplayName = reader.GetString(1),
                Description = reader.GetString(2),
                CreatedDate = DateTimeOffset.Parse(reader.GetString(3)),
                PreferredModel = reader.GetString(4),
                PreferredProvider = reader.GetString(5),
                Status = Enum.Parse<AgentStatus>(reader.GetString(6)),
                Goals = Deserialize<List<string>>(reader.GetString(7)),
                Configuration = Deserialize<Dictionary<string, string>>(reader.GetString(8)),
                Statistics = Deserialize<AgentStatistics>(reader.GetString(9))
            };
        }
        agent.Journal.AddRange(await GetJournalAsync(connection, agent.AgentId, cancellationToken));
        return agent;
    }

    public async Task SaveAgentAsync(
        AgentContainer agent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(agent);
        if (string.IsNullOrWhiteSpace(agent.AgentId))
        {
            throw new ArgumentException("An agent ID is required.", nameof(agent));
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var command = connection.CreateCommand();
        command.Transaction = (SqliteTransaction)transaction;
        command.CommandText = """
            INSERT INTO agents (
                agent_id, display_name, description, created_date, preferred_model,
                preferred_provider, status, goals_json, configuration_json, statistics_json
            ) VALUES (
                $id, $name, $description, $created, $model, $provider, $status,
                $goals, $configuration, $statistics
            )
            ON CONFLICT(agent_id) DO UPDATE SET
                display_name = excluded.display_name,
                description = excluded.description,
                preferred_model = excluded.preferred_model,
                preferred_provider = excluded.preferred_provider,
                status = excluded.status,
                goals_json = excluded.goals_json,
                configuration_json = excluded.configuration_json,
                statistics_json = excluded.statistics_json;
            """;
        command.Parameters.AddWithValue("$id", agent.AgentId);
        command.Parameters.AddWithValue("$name", agent.DisplayName);
        command.Parameters.AddWithValue("$description", agent.Description);
        command.Parameters.AddWithValue("$created", agent.CreatedDate.ToString("O"));
        command.Parameters.AddWithValue("$model", agent.PreferredModel);
        command.Parameters.AddWithValue("$provider", agent.PreferredProvider);
        command.Parameters.AddWithValue("$status", agent.Status.ToString());
        command.Parameters.AddWithValue("$goals", Serialize(agent.Goals));
        command.Parameters.AddWithValue("$configuration", Serialize(agent.Configuration));
        command.Parameters.AddWithValue("$statistics", Serialize(agent.Statistics));
        await command.ExecuteNonQueryAsync(cancellationToken);

        var deleteJournal = connection.CreateCommand();
        deleteJournal.Transaction = (SqliteTransaction)transaction;
        deleteJournal.CommandText = "DELETE FROM agent_journal WHERE agent_id = $id;";
        deleteJournal.Parameters.AddWithValue("$id", agent.AgentId);
        await deleteJournal.ExecuteNonQueryAsync(cancellationToken);

        foreach (var entry in agent.Journal)
        {
            var insertEntry = connection.CreateCommand();
            insertEntry.Transaction = (SqliteTransaction)transaction;
            insertEntry.CommandText = """
                INSERT INTO agent_journal(agent_id, occurred_at, kind, message)
                VALUES ($id, $occurredAt, $kind, $message);
                """;
            insertEntry.Parameters.AddWithValue("$id", agent.AgentId);
            insertEntry.Parameters.AddWithValue("$occurredAt", entry.OccurredAt.ToString("O"));
            insertEntry.Parameters.AddWithValue("$kind", entry.Kind);
            insertEntry.Parameters.AddWithValue("$message", entry.Message);
            await insertEntry.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task AppendJournalEntryAsync(
        string agentId,
        AgentJournalEntry entry,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(entry);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Kind);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Message);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agent_journal(agent_id, occurred_at, kind, message)
            VALUES ($agentId, $occurredAt, $kind, $message);
            """;
        command.Parameters.AddWithValue("$agentId", agentId);
        command.Parameters.AddWithValue("$occurredAt", entry.OccurredAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$kind", entry.Kind);
        command.Parameters.AddWithValue("$message", entry.Message);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<List<AgentJournalEntry>> GetJournalAsync(
        SqliteConnection connection,
        string agentId,
        CancellationToken cancellationToken)
    {
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT occurred_at, kind, message FROM agent_journal
            WHERE agent_id = $id ORDER BY journal_id;
            """;
        command.Parameters.AddWithValue("$id", agentId);

        var entries = new List<AgentJournalEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            entries.Add(new AgentJournalEntry(
                DateTimeOffset.Parse(reader.GetString(0)),
                reader.GetString(1),
                reader.GetString(2)));
        }
        return entries;
    }

    private static string Serialize<T>(T value) =>
        JsonSerializer.Serialize(value, JsonOptions);

    private static T Deserialize<T>(string value) =>
        JsonSerializer.Deserialize<T>(value, JsonOptions)
        ?? throw new InvalidDataException($"Stored {typeof(T).Name} JSON is empty or invalid.");
}
