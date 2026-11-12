using System.Globalization;
using System.Text.Json;
using System.Text.Json.Serialization;
using LLMSurvivingProxyAgent.Core;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.Knowledge;

/// <summary>
/// SQLite persistence for the agent's cross-life memory. Entries are stored as JSON documents with indexed
/// owner/status columns; outcome counters are updated inside a transaction so concurrent workers cannot lose updates.
/// </summary>
public sealed class SqlitePlaybookStore(string connectionString) : IPlaybookStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter() }
    };

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS playbook_entries (
                entry_id TEXT PRIMARY KEY,
                agent_id TEXT NOT NULL COLLATE NOCASE,
                status TEXT NOT NULL,
                updated_at TEXT NOT NULL,
                entry_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_playbook_entries_agent_status
                ON playbook_entries(agent_id, status);
            CREATE TABLE IF NOT EXISTS agent_lives (
                episode_id TEXT PRIMARY KEY,
                agent_id TEXT NOT NULL COLLATE NOCASE,
                life_number INTEGER NOT NULL,
                ended_at TEXT NOT NULL,
                life_json TEXT NOT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_agent_lives_agent_ended
                ON agent_lives(agent_id, ended_at DESC);
            """;
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertEntryAsync(PlaybookEntry entry, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(entry);
        Validate(entry);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await WriteEntryAsync(connection, null, entry, cancellationToken).ConfigureAwait(false);
    }

    public async Task<PlaybookEntry?> GetEntryAsync(string entryId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        return await ReadEntryAsync(connection, null, entryId, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<PlaybookEntry>> GetEntriesAsync(
        string agentId,
        bool includeRetired = false,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT entry_json FROM playbook_entries
            WHERE (agent_id = $agentId OR agent_id = $all)
              AND ($includeRetired = 1 OR status = 'Active')
            ORDER BY updated_at DESC, entry_id;
            """;
        command.Parameters.AddWithValue("$agentId", agentId);
        command.Parameters.AddWithValue("$all", PlaybookEntry.AllAgents);
        command.Parameters.AddWithValue("$includeRetired", includeRetired ? 1 : 0);
        var entries = new List<PlaybookEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (JsonSerializer.Deserialize<PlaybookEntry>(reader.GetString(0), JsonOptions) is { } entry)
            {
                entries.Add(entry);
            }
        }
        return entries;
    }

    public async Task<PlaybookEntry?> RecordOutcomeAsync(
        string entryId,
        GameplayReward reward,
        bool overridden,
        DateTimeOffset observedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entryId);
        ArgumentNullException.ThrowIfNull(reward);
        if (!reward.Verified)
        {
            return await GetEntryAsync(entryId, cancellationToken).ConfigureAwait(false);
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var transaction = (SqliteTransaction)await connection
            .BeginTransactionAsync(System.Data.IsolationLevel.Serializable, cancellationToken)
            .ConfigureAwait(false);
        PlaybookEntry? entry = await ReadEntryAsync(connection, transaction, entryId, cancellationToken)
            .ConfigureAwait(false);
        if (entry is null)
        {
            return null;
        }

        if (overridden)
        {
            entry = entry with
            {
                Overrides = entry.Overrides + 1,
                OverrideRewardSum = Math.Round(entry.OverrideRewardSum + reward.Total, 4)
            };
        }
        else
        {
            int verdict = PlaybookConfidence.Classify(reward);
            entry = entry with
            {
                Applications = entry.Applications + 1,
                Successes = entry.Successes + (verdict > 0 ? 1 : 0),
                Failures = entry.Failures + (verdict < 0 ? 1 : 0),
                RewardSum = Math.Round(entry.RewardSum + reward.Total, 4),
                LastAppliedAt = observedAt
            };
            if (PlaybookConfidence.ShouldRetire(entry))
            {
                entry = entry with { Status = PlaybookStatus.Retired };
            }
        }

        entry = entry with { UpdatedAt = observedAt };
        await WriteEntryAsync(connection, transaction, entry, cancellationToken).ConfigureAwait(false);
        await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        return entry;
    }

    public async Task UpsertLifeAsync(AgentLifeRecord life, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(life);
        ArgumentException.ThrowIfNullOrWhiteSpace(life.EpisodeId);
        ArgumentException.ThrowIfNullOrWhiteSpace(life.AgentId);
        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO agent_lives(episode_id, agent_id, life_number, ended_at, life_json)
            VALUES ($episodeId, $agentId, $lifeNumber, $endedAt, $json)
            ON CONFLICT(episode_id) DO UPDATE SET
                life_number = excluded.life_number,
                ended_at = excluded.ended_at,
                life_json = excluded.life_json;
            """;
        command.Parameters.AddWithValue("$episodeId", life.EpisodeId);
        command.Parameters.AddWithValue("$agentId", life.AgentId);
        command.Parameters.AddWithValue("$lifeNumber", life.LifeNumber);
        command.Parameters.AddWithValue("$endedAt", FormatTime(life.EndedAt));
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(life, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AgentLifeRecord>> GetLivesAsync(
        string agentId,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 1,000.");
        }

        await using var connection = await OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT life_json FROM agent_lives
            WHERE agent_id = $agentId
            ORDER BY ended_at DESC, episode_id
            LIMIT $limit;
            """;
        command.Parameters.AddWithValue("$agentId", agentId);
        command.Parameters.AddWithValue("$limit", limit);
        var lives = new List<AgentLifeRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            if (JsonSerializer.Deserialize<AgentLifeRecord>(reader.GetString(0), JsonOptions) is { } life)
            {
                lives.Add(life);
            }
        }
        return lives;
    }

    private static void Validate(PlaybookEntry entry)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.EntryId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.AgentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Title);
        ArgumentException.ThrowIfNullOrWhiteSpace(entry.Guidance);
        if (entry.Tier == PlaybookTier.Rule && entry.ForbiddenAction is null or AutonomousTestAction.None)
        {
            throw new ArgumentException("A playbook rule must name the action it forbids.", nameof(entry));
        }
        if (entry.Tier == PlaybookTier.Rule && entry.Source != PlaybookSource.Coach)
        {
            throw new ArgumentException("Only a coach can create an enforced rule.", nameof(entry));
        }
        if (entry.Scope == PlaybookScope.Game && string.IsNullOrWhiteSpace(entry.GameKey))
        {
            throw new ArgumentException("A game-scoped entry needs a game key.", nameof(entry));
        }
        PlaybookSituation.Normalize(entry.AppliesWhen, out IReadOnlyList<string> invalid);
        if (invalid.Count > 0)
        {
            throw new ArgumentException($"Unknown situation tags: {string.Join(", ", invalid)}.", nameof(entry));
        }
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        return connection;
    }

    private static async Task<PlaybookEntry?> ReadEntryAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        string entryId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT entry_json FROM playbook_entries WHERE entry_id = $entryId;";
        command.Parameters.AddWithValue("$entryId", entryId);
        return await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false) is string json
            ? JsonSerializer.Deserialize<PlaybookEntry>(json, JsonOptions)
            : null;
    }

    private static async Task WriteEntryAsync(
        SqliteConnection connection,
        SqliteTransaction? transaction,
        PlaybookEntry entry,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            INSERT INTO playbook_entries(entry_id, agent_id, status, updated_at, entry_json)
            VALUES ($entryId, $agentId, $status, $updatedAt, $json)
            ON CONFLICT(entry_id) DO UPDATE SET
                agent_id = excluded.agent_id,
                status = excluded.status,
                updated_at = excluded.updated_at,
                entry_json = excluded.entry_json;
            """;
        command.Parameters.AddWithValue("$entryId", entry.EntryId);
        command.Parameters.AddWithValue("$agentId", entry.AgentId);
        command.Parameters.AddWithValue("$status", entry.Status.ToString());
        command.Parameters.AddWithValue("$updatedAt", FormatTime(entry.UpdatedAt == default ? entry.CreatedAt : entry.UpdatedAt));
        command.Parameters.AddWithValue("$json", JsonSerializer.Serialize(entry, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    private static string FormatTime(DateTimeOffset value) =>
        value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);
}

/// <summary>
/// Picks the playbook entries that apply to the current situation. All applicable rules are returned (they are
/// enforced, so none may be dropped); tips and lessons are ranked by confidence and specificity and capped so the
/// prompt stays small.
/// </summary>
public sealed class PlaybookGuidanceProvider(IPlaybookStore store) : IPlaybookGuidanceProvider
{
    public const int MaximumTips = 6;
    public const int MaximumLessons = 6;

    public async Task<PlaybookGuidance> GetGuidanceAsync(
        string agentId,
        string? gameKey,
        IReadOnlySet<string> situationTags,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(situationTags);
        IReadOnlyList<PlaybookEntry> entries = await store
            .GetEntriesAsync(agentId, includeRetired: false, cancellationToken)
            .ConfigureAwait(false);
        return Select(entries, gameKey, situationTags);
    }

    public static PlaybookGuidance Select(
        IReadOnlyList<PlaybookEntry> entries,
        string? gameKey,
        IReadOnlySet<string> situationTags)
    {
        var applicable = entries
            .Where(entry => entry.Status == PlaybookStatus.Active)
            .Where(entry => entry.Scope == PlaybookScope.Universal ||
                            !string.IsNullOrWhiteSpace(gameKey) &&
                            string.Equals(entry.GameKey, gameKey, StringComparison.OrdinalIgnoreCase))
            .Where(entry => PlaybookSituation.Matches(entry.AppliesWhen, situationTags))
            .ToArray();

        static IOrderedEnumerable<PlaybookEntry> Rank(IEnumerable<PlaybookEntry> source) =>
            source
                .OrderByDescending(entry => entry.Confidence + entry.AppliesWhen.Count * 0.05)
                .ThenByDescending(entry => entry.UpdatedAt)
                .ThenBy(entry => entry.EntryId, StringComparer.Ordinal);

        return new PlaybookGuidance(
            situationTags,
            Rank(applicable.Where(entry => entry.Tier == PlaybookTier.Rule)).ToArray(),
            Rank(applicable.Where(entry => entry is { Tier: PlaybookTier.Tip, Source: PlaybookSource.Coach }))
                .Take(MaximumTips)
                .ToArray(),
            Rank(applicable.Where(entry => entry is { Tier: PlaybookTier.Tip, Source: PlaybookSource.Distilled }))
                .Take(MaximumLessons)
                .ToArray());
    }
}
