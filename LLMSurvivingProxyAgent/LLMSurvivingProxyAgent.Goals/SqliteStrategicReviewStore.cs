using System.Text.Json;
using LLMSurvivingProxyAgent.Core;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.Goals;

public sealed class SqliteStrategicReviewStore(string connectionString) : IStrategicReviewStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS strategic_reviews (
                review_id TEXT PRIMARY KEY,
                agent_id TEXT NOT NULL REFERENCES agents(agent_id) ON DELETE CASCADE,
                reviewed_at TEXT NOT NULL,
                model_json TEXT NOT NULL,
                context_json TEXT NOT NULL,
                assessment_json TEXT NOT NULL,
                decision_outcome TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_strategic_reviews_agent_time
                ON strategic_reviews(agent_id, reviewed_at DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(
        StrategicReviewRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO strategic_reviews(
                review_id, agent_id, reviewed_at, model_json, context_json,
                assessment_json, decision_outcome
            ) VALUES (
                $reviewId, $agentId, $reviewedAt, $model, $context, $assessment, $outcome
            );
            """;
        command.Parameters.AddWithValue("$reviewId", record.ReviewId);
        command.Parameters.AddWithValue("$agentId", record.AgentId);
        command.Parameters.AddWithValue("$reviewedAt", record.ReviewedAt.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$model", Serialize(record.Model));
        command.Parameters.AddWithValue("$context", Serialize(record.Context));
        command.Parameters.AddWithValue("$assessment", Serialize(record.Assessment));
        command.Parameters.AddWithValue("$outcome", record.DecisionOutcome);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<StrategicReviewRecord>> GetRecentAsync(
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
            SELECT review_id, agent_id, reviewed_at, model_json, context_json,
                   assessment_json, decision_outcome
            FROM strategic_reviews
            WHERE $agentId IS NULL OR agent_id = $agentId
            ORDER BY reviewed_at DESC, review_id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$agentId", (object?)agentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        var records = new List<StrategicReviewRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new StrategicReviewRecord(
                reader.GetString(0),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2)),
                Deserialize<ModelConfiguration>(reader.GetString(3)),
                Deserialize<StrategicReviewContext>(reader.GetString(4)),
                Deserialize<StrategicAssessment>(reader.GetString(5)),
                reader.GetString(6)));
        }
        return records;
    }

    private static string Serialize<T>(T value) => JsonSerializer.Serialize(value, JsonOptions);

    private static T Deserialize<T>(string value) =>
        JsonSerializer.Deserialize<T>(value, JsonOptions)
        ?? throw new InvalidDataException($"Stored {typeof(T).Name} JSON is empty or invalid.");
}
