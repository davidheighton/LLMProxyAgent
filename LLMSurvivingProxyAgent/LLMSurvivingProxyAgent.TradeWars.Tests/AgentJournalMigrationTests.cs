using LLMSurvivingProxyAgent.Agent;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class AgentJournalMigrationTests
{
    [Fact]
    public async Task ExistingMemoryEntriesMigrateToJournalOnce()
    {
        var databasePath = Path.Combine(Path.GetTempPath(), $"agent-{Guid.NewGuid():N}.db");
        var connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = databasePath,
            Pooling = false
        }.ToString();

        try
        {
            await using (var connection = new SqliteConnection(connectionString))
            {
                await connection.OpenAsync();
                var command = connection.CreateCommand();
                command.CommandText = """
                    CREATE TABLE agents (
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
                    CREATE TABLE agent_memories (
                        agent_id TEXT NOT NULL,
                        memory_key TEXT NOT NULL,
                        value TEXT NOT NULL,
                        updated_at TEXT NOT NULL,
                        PRIMARY KEY (agent_id, memory_key)
                    );
                    INSERT INTO agents VALUES (
                        'meridian', 'Meridian', 'Player', '2026-10-22T00:00:00.0000000+00:00',
                        'qwen3:4b', 'Ollama', 'Running', '[]', '{}', '{}'
                    );
                    INSERT INTO agent_memories VALUES (
                        'meridian', 'trade-note', 'Avoid low-margin route', '2026-10-22T01:00:00.0000000+00:00'
                    );
                    """;
                await command.ExecuteNonQueryAsync();
            }

            var store = new SqliteAgentStore(connectionString);
            await store.InitializeAsync();
            await store.InitializeAsync();
            var agent = Assert.Single(await store.GetAgentsAsync());

            var migratedEntry = Assert.Single(agent.Journal);
            Assert.Equal("LegacyMemory", migratedEntry.Kind);
            Assert.Equal("trade-note: Avoid low-margin route", migratedEntry.Message);
        }
        finally
        {
            foreach (var suffix in new[] { "", "-shm", "-wal" })
            {
                var path = databasePath + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }
}
