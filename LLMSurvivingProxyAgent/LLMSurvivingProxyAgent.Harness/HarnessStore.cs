using System.Globalization;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.Harness;

public enum HarnessEpisodeState
{
    Pending,
    Running,
    Paused,
    Completed,
    ShipLost,
    TimedOut,
    Stopped,
    Error
}

public sealed record HarnessBatch(
    string BatchId,
    DateTimeOffset CreatedAt,
    string BaseProfilePath,
    string GamesDirectory,
    int Parallel,
    string Goal,
    double WallClockMinutes,
    string? PilotModel = null);

public sealed record HarnessEpisode(
    string EpisodeId,
    string BatchId,
    int Ordinal,
    string AgentId,
    string ProfileFileName,
    HarnessEpisodeState State,
    int Segments,
    DateTimeOffset? ResumeAfter,
    int? LastExitCode,
    string? LastError,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt)
{
    public bool IsFinished => State is not (HarnessEpisodeState.Pending or HarnessEpisodeState.Running or HarnessEpisodeState.Paused);
}

/// <summary>Durable harness scheduling state, stored beside the trajectories so a batch can be resumed.</summary>
public sealed class HarnessStore(string connectionString)
{
    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS harness_batches (
                batch_id TEXT PRIMARY KEY,
                created_at TEXT NOT NULL,
                base_profile_path TEXT NOT NULL,
                games_directory TEXT NOT NULL,
                parallel INTEGER NOT NULL,
                goal TEXT NOT NULL,
                wall_clock_minutes REAL NOT NULL,
                pilot_model TEXT NULL
            );
            CREATE TABLE IF NOT EXISTS harness_episodes (
                episode_id TEXT PRIMARY KEY,
                batch_id TEXT NOT NULL REFERENCES harness_batches(batch_id),
                ordinal INTEGER NOT NULL,
                agent_id TEXT NOT NULL,
                profile_file_name TEXT NOT NULL,
                state TEXT NOT NULL,
                segments INTEGER NOT NULL,
                resume_after TEXT NULL,
                last_exit_code INTEGER NULL,
                last_error TEXT NULL,
                created_at TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                UNIQUE(batch_id, ordinal)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);

        await using var check = connection.CreateCommand();
        check.CommandText = "SELECT COUNT(*) FROM pragma_table_info('harness_batches') WHERE name = 'pilot_model';";
        if (Convert.ToInt64(await check.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) == 0)
        {
            await using var alter = connection.CreateCommand();
            alter.CommandText = "ALTER TABLE harness_batches ADD COLUMN pilot_model TEXT NULL;";
            await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task InsertBatchAsync(HarnessBatch batch, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO harness_batches(batch_id, created_at, base_profile_path, games_directory, parallel, goal, wall_clock_minutes, pilot_model)
            VALUES ($id, $createdAt, $base, $games, $parallel, $goal, $minutes, $pilotModel);
            """;
        command.Parameters.AddWithValue("$id", batch.BatchId);
        command.Parameters.AddWithValue("$createdAt", Format(batch.CreatedAt));
        command.Parameters.AddWithValue("$base", batch.BaseProfilePath);
        command.Parameters.AddWithValue("$games", batch.GamesDirectory);
        command.Parameters.AddWithValue("$parallel", batch.Parallel);
        command.Parameters.AddWithValue("$goal", batch.Goal);
        command.Parameters.AddWithValue("$minutes", batch.WallClockMinutes);
        command.Parameters.AddWithValue("$pilotModel", (object?)batch.PilotModel ?? DBNull.Value);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<HarnessBatch?> GetBatchAsync(string batchId, CancellationToken cancellationToken = default)
    {
        IReadOnlyList<HarnessBatch> batches = await QueryBatchesAsync(
            "WHERE batch_id = $id", command => command.Parameters.AddWithValue("$id", batchId), cancellationToken)
            .ConfigureAwait(false);
        return batches.FirstOrDefault();
    }

    public Task<IReadOnlyList<HarnessBatch>> GetBatchesAsync(CancellationToken cancellationToken = default) =>
        QueryBatchesAsync("ORDER BY created_at DESC", _ => { }, cancellationToken);

    public async Task UpsertEpisodeAsync(HarnessEpisode episode, CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO harness_episodes(
                episode_id, batch_id, ordinal, agent_id, profile_file_name, state, segments, resume_after,
                last_exit_code, last_error, created_at, updated_at)
            VALUES ($id, $batch, $ordinal, $agent, $profile, $state, $segments, $resumeAfter,
                $exitCode, $error, $createdAt, $updatedAt)
            ON CONFLICT(episode_id) DO UPDATE SET
                state = excluded.state,
                segments = excluded.segments,
                resume_after = excluded.resume_after,
                last_exit_code = excluded.last_exit_code,
                last_error = excluded.last_error,
                updated_at = excluded.updated_at;
            """;
        command.Parameters.AddWithValue("$id", episode.EpisodeId);
        command.Parameters.AddWithValue("$batch", episode.BatchId);
        command.Parameters.AddWithValue("$ordinal", episode.Ordinal);
        command.Parameters.AddWithValue("$agent", episode.AgentId);
        command.Parameters.AddWithValue("$profile", episode.ProfileFileName);
        command.Parameters.AddWithValue("$state", episode.State.ToString());
        command.Parameters.AddWithValue("$segments", episode.Segments);
        command.Parameters.AddWithValue("$resumeAfter", episode.ResumeAfter is { } resume ? Format(resume) : DBNull.Value);
        command.Parameters.AddWithValue("$exitCode", (object?)episode.LastExitCode ?? DBNull.Value);
        command.Parameters.AddWithValue("$error", (object?)episode.LastError ?? DBNull.Value);
        command.Parameters.AddWithValue("$createdAt", Format(episode.CreatedAt));
        command.Parameters.AddWithValue("$updatedAt", Format(episode.UpdatedAt));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<HarnessEpisode>> GetEpisodesAsync(
        string? batchId = null,
        CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT episode_id, batch_id, ordinal, agent_id, profile_file_name, state, segments, resume_after,
                   last_exit_code, last_error, created_at, updated_at
            FROM harness_episodes
            WHERE $batch IS NULL OR batch_id = $batch
            ORDER BY batch_id, ordinal;
            """;
        command.Parameters.AddWithValue("$batch", (object?)batchId ?? DBNull.Value);
        var episodes = new List<HarnessEpisode>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            episodes.Add(new HarnessEpisode(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetInt32(2),
                reader.GetString(3),
                reader.GetString(4),
                Enum.Parse<HarnessEpisodeState>(reader.GetString(5)),
                reader.GetInt32(6),
                reader.IsDBNull(7) ? null : Parse(reader.GetString(7)),
                reader.IsDBNull(8) ? null : reader.GetInt32(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                Parse(reader.GetString(10)),
                Parse(reader.GetString(11))));
        }
        return episodes;
    }

    private async Task<IReadOnlyList<HarnessBatch>> QueryBatchesAsync(
        string clause,
        Action<SqliteCommand> bind,
        CancellationToken cancellationToken)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            SELECT batch_id, created_at, base_profile_path, games_directory, parallel, goal, wall_clock_minutes, pilot_model
            FROM harness_batches {clause};
            """;
        bind(command);
        var batches = new List<HarnessBatch>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            batches.Add(new HarnessBatch(
                reader.GetString(0),
                Parse(reader.GetString(1)),
                reader.GetString(2),
                reader.GetString(3),
                reader.GetInt32(4),
                reader.GetString(5),
                reader.GetDouble(6),
                reader.IsDBNull(7) ? null : reader.GetString(7)));
        }
        return batches;
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static string Format(DateTimeOffset value) => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);
}
