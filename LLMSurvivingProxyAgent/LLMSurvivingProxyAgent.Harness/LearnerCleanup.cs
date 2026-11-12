using System.Text.Json.Nodes;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.Harness;

/// <summary>
/// Removes a finished learner's local footprint: the agent registry entry and the cloned MTC profile, database,
/// and game folder. Event and debug logs are archived first; trajectory data and saved login credentials are kept.
/// </summary>
public enum LearnerCleanupResult
{
    Cleaned,
    AlreadyClean,
    Skipped
}

public sealed class LearnerCleanup(string agentsConnectionString, string archiveRoot, TextWriter log)
{
    public const string LearnerPrefix = "learner-";

    public async Task<LearnerCleanupResult> CleanAsync(
        HarnessEpisode episode,
        string gamesDirectory,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(episode);
        if (!episode.IsFinished)
        {
            return LearnerCleanupResult.Skipped;
        }
        if (!episode.AgentId.StartsWith(LearnerPrefix, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Refusing to clean up non-learner agent '{episode.AgentId}'.");
        }

        string profilePath = Path.Combine(gamesDirectory, episode.ProfileFileName);
        if (ProfileCloner.IsLockedByAnotherProcess(profilePath))
        {
            log.WriteLine($"Skipped cleanup of {episode.AgentId}: its profile is open in MTC.");
            return LearnerCleanupResult.Skipped;
        }

        string gameName = Path.GetFileNameWithoutExtension(episode.ProfileFileName);
        string? databasePath = null;
        if (File.Exists(profilePath) && JsonNode.Parse(File.ReadAllText(profilePath)) is JsonObject profile)
        {
            gameName = profile["Name"]?.GetValue<string>() is { Length: > 0 } name ? name : gameName;
            databasePath = profile["DatabasePath"]?.GetValue<string>();
        }

        string archive = Path.Combine(archiveRoot, episode.BatchId, episode.EpisodeId);
        bool removed = false;
        string gameFolder = Path.Combine(gamesDirectory, gameName);
        string eventFolder = Path.Combine(gameFolder, "agent");
        if (Directory.Exists(eventFolder))
        {
            Directory.CreateDirectory(archive);
            foreach (string file in Directory.EnumerateFiles(eventFolder, "*.jsonl"))
            {
                File.Move(file, Path.Combine(archive, Path.GetFileName(file)), overwrite: true);
                removed = true;
            }
        }
        string debugLog = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(gamesDirectory))!, "logs", $"{gameName}_debug.log");
        if (File.Exists(debugLog))
        {
            Directory.CreateDirectory(archive);
            File.Move(debugLog, Path.Combine(archive, Path.GetFileName(debugLog)), overwrite: true);
            removed = true;
        }

        if (Directory.Exists(gameFolder))
        {
            Directory.Delete(gameFolder, recursive: true);
            removed = true;
        }
        foreach (string file in new[] { profilePath, profilePath + ".lock", databasePath ?? Path.Combine(gamesDirectory, $"{gameName}.xdb") })
        {
            if (File.Exists(file))
            {
                File.Delete(file);
                removed = true;
            }
        }

        removed |= await DeleteAgentAsync(episode.AgentId, cancellationToken).ConfigureAwait(false);
        if (!removed)
        {
            return LearnerCleanupResult.AlreadyClean;
        }

        log.WriteLine($"Cleaned up {episode.AgentId}: registry entry and profile files removed; logs archived to {archive}.");
        return LearnerCleanupResult.Cleaned;
    }

    private async Task<bool> DeleteAgentAsync(string agentId, CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(agentsConnectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        int deleted = 0;
        await using var transaction = (SqliteTransaction)await connection.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        foreach (string table in new[] { "agent_team_status", "macro_executions", "agents" })
        {
            await using var command = connection.CreateCommand();
            command.Transaction = transaction;
            command.CommandText = $"DELETE FROM {table} WHERE agent_id = $agentId;";
            command.Parameters.AddWithValue("$agentId", agentId);
            try
            {
                deleted += await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 1 && exception.Message.Contains("no such table", StringComparison.OrdinalIgnoreCase))
            {
                // A fresh registry may not have every optional table yet.
            }
        }
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return deleted > 0;
    }
}
