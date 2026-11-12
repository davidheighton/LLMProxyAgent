using System.Globalization;
using System.Text.Json;
using LLMSurvivingProxyAgent.Core;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed class SqliteTrajectoryStore(string connectionString) : ITrajectoryStore
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private const string EpisodeColumns = """
        episode_id, agent_id, model, started_at, ended_at, end_reason, step_count,
        verified_step_count, pilot_decision_count, policy_decision_count, total_reward,
        start_net_worth, end_net_worth, start_turns, end_turns, sectors_visited, ship_lost,
        scanner_acquired, status, goal, segment, visited_sectors_json, milestone_count
        """;

    // Columns added after the first schema version; applied to existing databases by InitializeAsync.
    private static readonly (string Table, string Column, string Definition)[] Migrations =
    [
        ("trajectory_episodes", "status", "TEXT NOT NULL DEFAULT 'Running'"),
        ("trajectory_episodes", "goal", "TEXT NOT NULL DEFAULT 'None'"),
        ("trajectory_episodes", "segment", "INTEGER NOT NULL DEFAULT 1"),
        ("trajectory_episodes", "visited_sectors_json", "TEXT NOT NULL DEFAULT '[]'"),
        ("trajectory_episodes", "milestone_count", "INTEGER NOT NULL DEFAULT 0"),
        ("trajectory_steps", "segment", "INTEGER NOT NULL DEFAULT 1"),
        ("trajectory_steps", "cited_knowledge_node_id", "TEXT NULL"),
        ("trajectory_steps", "overridden_playbook_entry_id", "TEXT NULL"),
        ("trajectory_steps", "override_reason", "TEXT NULL"),
        ("trajectory_steps", "situation_tags_json", "TEXT NOT NULL DEFAULT '[]'")
    ];

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using (var command = connection.CreateCommand())
        {
            command.CommandText = """
                CREATE TABLE IF NOT EXISTS trajectory_episodes (
                    episode_id TEXT PRIMARY KEY,
                    agent_id TEXT NOT NULL COLLATE NOCASE,
                    model TEXT NOT NULL,
                    started_at TEXT NOT NULL,
                    ended_at TEXT NULL,
                    end_reason TEXT NULL,
                    step_count INTEGER NOT NULL,
                    verified_step_count INTEGER NOT NULL,
                    pilot_decision_count INTEGER NOT NULL,
                    policy_decision_count INTEGER NOT NULL,
                    total_reward REAL NOT NULL,
                    start_net_worth INTEGER NULL,
                    end_net_worth INTEGER NULL,
                    start_turns INTEGER NULL,
                    end_turns INTEGER NULL,
                    sectors_visited INTEGER NOT NULL,
                    ship_lost INTEGER NOT NULL,
                    scanner_acquired INTEGER NOT NULL
                );
                CREATE INDEX IF NOT EXISTS ix_trajectory_episodes_agent_started
                    ON trajectory_episodes(agent_id, started_at DESC);
                CREATE TABLE IF NOT EXISTS trajectory_steps (
                    step_id TEXT PRIMARY KEY,
                    episode_id TEXT NOT NULL REFERENCES trajectory_episodes(episode_id) ON DELETE CASCADE,
                    agent_id TEXT NOT NULL COLLATE NOCASE,
                    step_index INTEGER NOT NULL,
                    decided_at TEXT NOT NULL,
                    source TEXT NOT NULL,
                    action TEXT NOT NULL,
                    target_sector INTEGER NOT NULL,
                    rationale TEXT NOT NULL,
                    interaction_id TEXT NULL,
                    objective TEXT NULL,
                    before_json TEXT NULL,
                    after_json TEXT NULL,
                    completed_at TEXT NULL,
                    reward_verified INTEGER NULL,
                    reward_total REAL NULL,
                    reward_json TEXT NULL,
                    UNIQUE(episode_id, step_index)
                );
                CREATE INDEX IF NOT EXISTS ix_trajectory_steps_action_source
                    ON trajectory_steps(action, source);
                CREATE TABLE IF NOT EXISTS trajectory_milestones (
                    episode_id TEXT NOT NULL REFERENCES trajectory_episodes(episode_id) ON DELETE CASCADE,
                    milestone TEXT NOT NULL,
                    reached_at TEXT NOT NULL,
                    step_index INTEGER NOT NULL,
                    segment INTEGER NOT NULL,
                    turns_used INTEGER NULL,
                    bonus REAL NOT NULL,
                    evidence TEXT NOT NULL,
                    PRIMARY KEY(episode_id, milestone)
                );
                """;
            await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
        }

        foreach ((string table, string column, string definition) in Migrations)
        {
            if (!await ColumnExistsAsync(connection, table, column, cancellationToken).ConfigureAwait(false))
            {
                await using var alter = connection.CreateCommand();
                alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
                await alter.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
            }
        }
    }

    public async Task UpsertEpisodeAsync(TrajectoryEpisode episode, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(episode);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"""
            INSERT INTO trajectory_episodes({EpisodeColumns}) VALUES (
                $id, $agentId, $model, $startedAt, $endedAt, $endReason, $steps,
                $verifiedSteps, $pilot, $policy, $reward,
                $startNetWorth, $endNetWorth, $startTurns, $endTurns, $sectors, $shipLost,
                $scanner, $status, $goal, $segment, $visited, $milestones
            )
            ON CONFLICT(episode_id) DO UPDATE SET
                ended_at = excluded.ended_at,
                end_reason = excluded.end_reason,
                step_count = excluded.step_count,
                verified_step_count = excluded.verified_step_count,
                pilot_decision_count = excluded.pilot_decision_count,
                policy_decision_count = excluded.policy_decision_count,
                total_reward = excluded.total_reward,
                start_net_worth = excluded.start_net_worth,
                end_net_worth = excluded.end_net_worth,
                start_turns = excluded.start_turns,
                end_turns = excluded.end_turns,
                sectors_visited = excluded.sectors_visited,
                ship_lost = excluded.ship_lost,
                scanner_acquired = excluded.scanner_acquired,
                status = excluded.status,
                goal = excluded.goal,
                segment = excluded.segment,
                visited_sectors_json = excluded.visited_sectors_json,
                milestone_count = excluded.milestone_count;
            """;
        command.Parameters.AddWithValue("$id", episode.EpisodeId);
        command.Parameters.AddWithValue("$agentId", episode.AgentId);
        command.Parameters.AddWithValue("$model", episode.Model);
        command.Parameters.AddWithValue("$startedAt", FormatTime(episode.StartedAt));
        command.Parameters.AddWithValue("$endedAt", DbValue(episode.EndedAt is { } ended ? FormatTime(ended) : null));
        command.Parameters.AddWithValue("$endReason", DbValue(episode.EndReason));
        command.Parameters.AddWithValue("$steps", episode.StepCount);
        command.Parameters.AddWithValue("$verifiedSteps", episode.VerifiedStepCount);
        command.Parameters.AddWithValue("$pilot", episode.PilotDecisionCount);
        command.Parameters.AddWithValue("$policy", episode.PolicyDecisionCount);
        command.Parameters.AddWithValue("$reward", episode.TotalReward);
        command.Parameters.AddWithValue("$startNetWorth", DbValue(episode.StartNetWorth));
        command.Parameters.AddWithValue("$endNetWorth", DbValue(episode.EndNetWorth));
        command.Parameters.AddWithValue("$startTurns", DbValue(episode.StartTurns));
        command.Parameters.AddWithValue("$endTurns", DbValue(episode.EndTurns));
        command.Parameters.AddWithValue("$sectors", episode.SectorsVisited);
        command.Parameters.AddWithValue("$shipLost", episode.ShipLost ? 1 : 0);
        command.Parameters.AddWithValue("$scanner", episode.ScannerAcquired ? 1 : 0);
        command.Parameters.AddWithValue("$status", episode.Status.ToString());
        command.Parameters.AddWithValue("$goal", episode.Goal.ToString());
        command.Parameters.AddWithValue("$segment", episode.Segment);
        command.Parameters.AddWithValue("$visited", JsonSerializer.Serialize(episode.VisitedSectors, JsonOptions));
        command.Parameters.AddWithValue("$milestones", episode.MilestoneCount);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertStepAsync(TrajectoryStep step, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(step);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trajectory_steps(
                step_id, episode_id, agent_id, step_index, decided_at, source, action, target_sector,
                rationale, interaction_id, objective, before_json, after_json, completed_at,
                reward_verified, reward_total, reward_json, segment,
                cited_knowledge_node_id, overridden_playbook_entry_id, override_reason, situation_tags_json
            ) VALUES (
                $id, $episodeId, $agentId, $index, $decidedAt, $source, $action, $target,
                $rationale, $interactionId, $objective, $before, $after, $completedAt,
                $rewardVerified, $rewardTotal, $reward, $segment,
                $cited, $overridden, $overrideReason, $tags
            )
            ON CONFLICT(step_id) DO UPDATE SET
                after_json = excluded.after_json,
                completed_at = excluded.completed_at,
                reward_verified = excluded.reward_verified,
                reward_total = excluded.reward_total,
                reward_json = excluded.reward_json;
            """;
        command.Parameters.AddWithValue("$id", step.StepId);
        command.Parameters.AddWithValue("$episodeId", step.EpisodeId);
        command.Parameters.AddWithValue("$agentId", step.AgentId);
        command.Parameters.AddWithValue("$index", step.StepIndex);
        command.Parameters.AddWithValue("$decidedAt", FormatTime(step.DecidedAt));
        command.Parameters.AddWithValue("$source", step.Source.ToString());
        command.Parameters.AddWithValue("$action", step.Action);
        command.Parameters.AddWithValue("$target", step.TargetSector);
        command.Parameters.AddWithValue("$rationale", step.Rationale);
        command.Parameters.AddWithValue("$interactionId", DbValue(step.InteractionId));
        command.Parameters.AddWithValue("$objective", DbValue(step.Objective));
        command.Parameters.AddWithValue("$before", DbValue(Serialize(step.Before)));
        command.Parameters.AddWithValue("$after", DbValue(Serialize(step.After)));
        command.Parameters.AddWithValue("$completedAt", DbValue(step.CompletedAt is { } completed ? FormatTime(completed) : null));
        command.Parameters.AddWithValue("$rewardVerified", DbValue(step.Reward is { } reward ? reward.Verified ? 1 : 0 : null));
        command.Parameters.AddWithValue("$rewardTotal", DbValue(step.Reward?.Total));
        command.Parameters.AddWithValue("$reward", DbValue(Serialize(step.Reward)));
        command.Parameters.AddWithValue("$segment", step.Segment);
        command.Parameters.AddWithValue("$cited", DbValue(step.CitedKnowledgeNodeId));
        command.Parameters.AddWithValue("$overridden", DbValue(step.OverriddenPlaybookEntryId));
        command.Parameters.AddWithValue("$overrideReason", DbValue(step.OverrideReason));
        command.Parameters.AddWithValue("$tags", JsonSerializer.Serialize(step.SituationTags, JsonOptions));
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task UpsertMilestoneAsync(TrajectoryMilestone milestone, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(milestone);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            INSERT INTO trajectory_milestones(
                episode_id, milestone, reached_at, step_index, segment, turns_used, bonus, evidence
            ) VALUES ($episodeId, $milestone, $reachedAt, $stepIndex, $segment, $turnsUsed, $bonus, $evidence)
            ON CONFLICT(episode_id, milestone) DO NOTHING;
            """;
        command.Parameters.AddWithValue("$episodeId", milestone.EpisodeId);
        command.Parameters.AddWithValue("$milestone", milestone.Milestone.ToString());
        command.Parameters.AddWithValue("$reachedAt", FormatTime(milestone.ReachedAt));
        command.Parameters.AddWithValue("$stepIndex", milestone.StepIndex);
        command.Parameters.AddWithValue("$segment", milestone.Segment);
        command.Parameters.AddWithValue("$turnsUsed", DbValue(milestone.TurnsUsed));
        command.Parameters.AddWithValue("$bonus", milestone.Bonus);
        command.Parameters.AddWithValue("$evidence", milestone.Evidence);
        await command.ExecuteNonQueryAsync(cancellationToken).ConfigureAwait(false);
    }

    public async Task<TrajectoryEpisode?> GetEpisodeAsync(string episodeId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(episodeId);
        IReadOnlyList<TrajectoryEpisode> episodes = await QueryEpisodesAsync(
            "WHERE episode_id = $episodeId",
            command => command.Parameters.AddWithValue("$episodeId", episodeId),
            cancellationToken).ConfigureAwait(false);
        return episodes.FirstOrDefault();
    }

    public Task<IReadOnlyList<TrajectoryEpisode>> GetRecentEpisodesAsync(
        string? agentId = null,
        int limit = 50,
        CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 1000)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be between 1 and 1,000.");
        }

        return QueryEpisodesAsync(
            "WHERE $agentId IS NULL OR agent_id = $agentId ORDER BY started_at DESC, episode_id LIMIT $limit",
            command =>
            {
                command.Parameters.AddWithValue("$agentId", DbValue(agentId));
                command.Parameters.AddWithValue("$limit", limit);
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<TrajectoryStep>> GetStepsAsync(
        string episodeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(episodeId);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT step_id, episode_id, agent_id, step_index, decided_at, source, action, target_sector,
                   rationale, interaction_id, objective, before_json, after_json, completed_at, reward_json,
                   segment, cited_knowledge_node_id, overridden_playbook_entry_id, override_reason,
                   situation_tags_json
            FROM trajectory_steps
            WHERE episode_id = $episodeId
            ORDER BY step_index;
            """;
        command.Parameters.AddWithValue("$episodeId", episodeId);
        var steps = new List<TrajectoryStep>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            steps.Add(new TrajectoryStep(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                reader.GetInt32(3),
                ParseTime(reader.GetString(4)),
                Enum.Parse<TrajectoryDecisionSource>(reader.GetString(5)),
                reader.GetString(6),
                reader.GetInt32(7),
                reader.GetString(8),
                reader.IsDBNull(9) ? null : reader.GetString(9),
                reader.IsDBNull(10) ? null : reader.GetString(10),
                Deserialize<GameSnapshot>(reader, 11))
            {
                After = Deserialize<GameSnapshot>(reader, 12),
                CompletedAt = reader.IsDBNull(13) ? null : ParseTime(reader.GetString(13)),
                Reward = Deserialize<GameplayReward>(reader, 14),
                Segment = reader.GetInt32(15),
                CitedKnowledgeNodeId = reader.IsDBNull(16) ? null : reader.GetString(16),
                OverriddenPlaybookEntryId = reader.IsDBNull(17) ? null : reader.GetString(17),
                OverrideReason = reader.IsDBNull(18) ? null : reader.GetString(18),
                SituationTags = JsonSerializer.Deserialize<List<string>>(reader.GetString(19), JsonOptions) ?? []
            });
        }
        return steps;
    }

    public async Task<IReadOnlyList<TrajectoryMilestone>> GetMilestonesAsync(
        string episodeId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(episodeId);
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT episode_id, milestone, reached_at, step_index, segment, turns_used, bonus, evidence
            FROM trajectory_milestones
            WHERE episode_id = $episodeId
            ORDER BY reached_at, milestone;
            """;
        command.Parameters.AddWithValue("$episodeId", episodeId);
        var milestones = new List<TrajectoryMilestone>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            milestones.Add(new TrajectoryMilestone(
                reader.GetString(0),
                Enum.Parse<EpisodeMilestone>(reader.GetString(1)),
                ParseTime(reader.GetString(2)),
                reader.GetInt32(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetInt32(5),
                reader.GetDouble(6),
                reader.GetString(7)));
        }
        return milestones;
    }

    private async Task<IReadOnlyList<TrajectoryEpisode>> QueryEpisodesAsync(
        string whereAndOrder,
        Action<SqliteCommand> bind,
        CancellationToken cancellationToken)
    {
        await using var connection = new SqliteConnection(connectionString);
        await connection.OpenAsync(cancellationToken).ConfigureAwait(false);
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT {EpisodeColumns} FROM trajectory_episodes {whereAndOrder};";
        bind(command);
        var episodes = new List<TrajectoryEpisode>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);
        while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
        {
            episodes.Add(new TrajectoryEpisode(
                reader.GetString(0),
                reader.GetString(1),
                reader.GetString(2),
                ParseTime(reader.GetString(3)))
            {
                EndedAt = reader.IsDBNull(4) ? null : ParseTime(reader.GetString(4)),
                EndReason = reader.IsDBNull(5) ? null : reader.GetString(5),
                StepCount = reader.GetInt32(6),
                VerifiedStepCount = reader.GetInt32(7),
                PilotDecisionCount = reader.GetInt32(8),
                PolicyDecisionCount = reader.GetInt32(9),
                TotalReward = reader.GetDouble(10),
                StartNetWorth = reader.IsDBNull(11) ? null : reader.GetInt64(11),
                EndNetWorth = reader.IsDBNull(12) ? null : reader.GetInt64(12),
                StartTurns = reader.IsDBNull(13) ? null : reader.GetInt32(13),
                EndTurns = reader.IsDBNull(14) ? null : reader.GetInt32(14),
                SectorsVisited = reader.GetInt32(15),
                ShipLost = reader.GetInt64(16) != 0,
                ScannerAcquired = reader.GetInt64(17) != 0,
                Status = Enum.Parse<EpisodeStatus>(reader.GetString(18)),
                Goal = Enum.Parse<EpisodeGoal>(reader.GetString(19)),
                Segment = reader.GetInt32(20),
                VisitedSectors = JsonSerializer.Deserialize<List<int>>(reader.GetString(21), JsonOptions) ?? [],
                MilestoneCount = reader.GetInt32(22)
            });
        }
        return episodes;
    }

    private static async Task<bool> ColumnExistsAsync(
        SqliteConnection connection,
        string table,
        string column,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText = $"SELECT COUNT(*) FROM pragma_table_info('{table}') WHERE name = $column;";
        command.Parameters.AddWithValue("$column", column);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false)) > 0;
    }

    private static string FormatTime(DateTimeOffset value) => value.ToUniversalTime().ToString("O");

    private static DateTimeOffset ParseTime(string value) =>
        DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private static object DbValue(object? value) => value ?? DBNull.Value;

    private static string? Serialize<T>(T? value) where T : class =>
        value is null ? null : JsonSerializer.Serialize(value, JsonOptions);

    private static T? Deserialize<T>(SqliteDataReader reader, int ordinal) where T : class =>
        reader.IsDBNull(ordinal) ? null : JsonSerializer.Deserialize<T>(reader.GetString(ordinal), JsonOptions);
}
