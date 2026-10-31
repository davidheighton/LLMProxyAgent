using System.Globalization;
using System.Text.Json;
using LLMSurvivingProxyAgent.Core;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.Macros;

public sealed class SqliteMacroTelemetryStore(string connectionString) : IMacroTelemetryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS macro_executions (
                execution_id TEXT PRIMARY KEY,
                agent_id TEXT NOT NULL,
                macro_id TEXT NOT NULL,
                macro_name TEXT NOT NULL,
                category TEXT NOT NULL,
                start_time TEXT NOT NULL,
                end_time TEXT NOT NULL,
                status TEXT NOT NULL,
                result TEXT NOT NULL,
                notes TEXT NOT NULL,
                plan_sectors_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_macro_executions_agent_start
                ON macro_executions(agent_id, start_time DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task SaveAsync(
        MacroExecutionRecord record,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(record);
        var commandText = """
            INSERT INTO macro_executions(
                execution_id, agent_id, macro_id, macro_name, category, start_time,
                end_time, status, result, notes, plan_sectors_json
            ) VALUES (
                $executionId, $agentId, $macroId, $macroName, $category, $start,
                $end, $status, $result, $notes, $sectors
            );
            """;
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = commandText;
        command.Parameters.AddWithValue("$executionId", record.ExecutionId);
        command.Parameters.AddWithValue("$agentId", record.AgentId);
        command.Parameters.AddWithValue("$macroId", record.MacroId);
        command.Parameters.AddWithValue("$macroName", record.MacroName);
        command.Parameters.AddWithValue("$category", record.Category.ToString());
        command.Parameters.AddWithValue("$start", record.StartTime.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$end", record.EndTime.ToUniversalTime().ToString("O"));
        command.Parameters.AddWithValue("$status", record.Status.ToString());
        command.Parameters.AddWithValue("$result", record.Result);
        command.Parameters.AddWithValue("$notes", record.Notes);
        command.Parameters.AddWithValue("$sectors", JsonSerializer.Serialize(record.PlanSectors, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<MacroExecutionRecord>> GetRecentAsync(
        string? agentId = null,
        int limit = 100,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 1,000.");
        }

        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken);
        var command = connection.CreateCommand();
        command.CommandText = """
            SELECT execution_id, agent_id, macro_id, macro_name, category, start_time,
                   end_time, status, result, notes, plan_sectors_json
            FROM macro_executions
            WHERE $agentId IS NULL OR agent_id = $agentId
            ORDER BY start_time DESC, execution_id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$agentId", (object?)agentId ?? DBNull.Value);
        command.Parameters.AddWithValue("$limit", limit);

        var records = new List<MacroExecutionRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            records.Add(new MacroExecutionRecord(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetString(3),
                Enum.Parse<MacroCategory>(reader.GetString(4)),
                DateTimeOffset.Parse(reader.GetString(5), CultureInfo.InvariantCulture),
                DateTimeOffset.Parse(reader.GetString(6), CultureInfo.InvariantCulture),
                Enum.Parse<MacroExecutionStatus>(reader.GetString(7)),
                reader.GetString(8),
                reader.GetString(9),
                JsonSerializer.Deserialize<List<int>>(reader.GetString(10), JsonOptions)
                    ?? throw new InvalidDataException("Stored macro route JSON is empty or invalid.")));
        }
        return records;
    }
}
