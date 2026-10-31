using System.Text.Json;
using System.Text.Json.Serialization;
using LLMSurvivingProxyAgent.Core;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.Reactive;

public sealed class SqliteReactiveEventStore(string connectionString) : IReactiveEventStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) }
    };

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS reactive_event_records (
                event_id TEXT PRIMARY KEY,
                agent_id TEXT NOT NULL REFERENCES agents(agent_id) ON DELETE CASCADE,
                event_type TEXT NOT NULL,
                source TEXT NOT NULL,
                occurred_at TEXT NOT NULL,
                received_at TEXT NOT NULL,
                event_json TEXT NOT NULL,
                decision_json TEXT NOT NULL,
                processed_at TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_reactive_event_agent_received
                ON reactive_event_records(agent_id, received_at DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(
        ReactiveEventRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO reactive_event_records(
                event_id, agent_id, event_type, source, occurred_at, received_at,
                event_json, decision_json, processed_at
            ) VALUES (
                $eventId, $agentId, $eventType, $source, $occurredAt, $receivedAt,
                $event, $decision, $processedAt
            );
            """;
        command.Parameters.AddWithValue("$eventId", record.Event.EventId);
        command.Parameters.AddWithValue("$agentId", record.Event.AgentId);
        command.Parameters.AddWithValue("$eventType", record.Event.Type.ToString());
        command.Parameters.AddWithValue("$source", record.Event.Source.ToString());
        command.Parameters.AddWithValue("$occurredAt", record.Event.OccurredAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$receivedAt", record.Event.ReceivedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$event", JsonSerializer.Serialize(record.Event, JsonOptions));
        command.Parameters.AddWithValue("$decision", JsonSerializer.Serialize(record.Decision, JsonOptions));
        command.Parameters.AddWithValue("$processedAt", record.ProcessedAt.ToUniversalTime().ToString("O"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<ReactiveEventRecord>> GetRecentAsync(
        string? agentId = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 500.");
        }
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT event_json, decision_json, processed_at
            FROM reactive_event_records
            WHERE $agentId IS NULL OR agent_id = $agentId
            ORDER BY received_at DESC, event_id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$agentId", (object?)agentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        var records = new List<ReactiveEventRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new ReactiveEventRecord(
                Deserialize<ReactiveEvent>(reader.GetString(0)),
                Deserialize<ReactiveDecision>(reader.GetString(1)),
                DateTimeOffset.Parse(reader.GetString(2))));
        }
        return records;
    }

    private static T Deserialize<T>(string value) =>
        JsonSerializer.Deserialize<T>(value, JsonOptions)
        ?? throw new InvalidDataException($"Stored {typeof(T).Name} JSON is empty or invalid.");
}
