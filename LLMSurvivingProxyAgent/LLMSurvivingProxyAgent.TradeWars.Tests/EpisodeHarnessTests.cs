using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Harness;
using LLMSurvivingProxyAgent.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class EpisodeHarnessTests
{
    private static readonly ShipCapacity Cruiser = new("Merchant Cruiser", 75, 2_500, 400, "test");

    [Fact]
    public void ShipCatalogParsesMombotLinesWithMultiWordNames()
    {
        IReadOnlyList<ShipCapacity> ships = ShipCatalogCapacityProvider.Parse(
            [
                "400 2 3 41300 75 2500 20 3 0 Merchant Cruiser",
                "garbage line",
                "750 4 4 182000 150 10000 50 4 1 Imperial StarShip (Fed)"
            ],
            "test");

        Assert.Equal(2, ships.Count);
        Assert.Equal(new ShipCapacity("Merchant Cruiser", 75, 2_500, 400, "test"), ships[0]);
        Assert.Equal("Imperial StarShip (Fed)", ships[1].ShipClass);
    }

    [Fact]
    public void ShipCatalogProviderMatchesShipClassCaseInsensitively()
    {
        string path = Path.Combine(Path.GetTempPath(), $"ships-{Guid.NewGuid():N}.cfg");
        File.WriteAllLines(path, ["400 2 3 41300 75 2500 20 3 0 Merchant Cruiser"]);
        try
        {
            var provider = new ShipCatalogCapacityProvider(() => path);
            Assert.Equal(75, provider.GetCapacity("merchant cruiser")?.MaxHolds);
            Assert.Null(provider.GetCapacity("Scout Marauder"));
            Assert.Null(new ShipCatalogCapacityProvider(() => null).GetCapacity("Merchant Cruiser"));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void EvaluatorAwardsEvidencedMilestonesOnce()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        GameSnapshot snapshot = Snapshot(551, now) with { DensityScannerInstalled = true, HoloScannerInstalled = false };
        TrajectoryStep profitableTrade = Step(nameof(AutonomousTestAction.TradeAtCurrentPort), creditsDelta: 966);

        IReadOnlyList<EpisodeMilestoneObservation> first = EpisodeGoalEvaluator.Evaluate(
            new EpisodeGoalContext(snapshot, 551, Cruiser, null, profitableTrade),
            new HashSet<EpisodeMilestone>(),
            now);
        IReadOnlyList<EpisodeMilestoneObservation> again = EpisodeGoalEvaluator.Evaluate(
            new EpisodeGoalContext(snapshot, 551, Cruiser, null, profitableTrade),
            first.Select(item => item.Milestone).ToHashSet(),
            now);

        Assert.Equal(
            [EpisodeMilestone.ReachedStarDock, EpisodeMilestone.DensityScanner, EpisodeMilestone.FirstProfitableTrade],
            first.Select(item => item.Milestone));
        Assert.Empty(again);
    }

    [Fact]
    public void EvaluatorRequiresProfitAndFreshSnapshot()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        Assert.DoesNotContain(
            EpisodeGoalEvaluator.Evaluate(
                new EpisodeGoalContext(Snapshot(10, now), null, null, null,
                    Step(nameof(AutonomousTestAction.TradeAtCurrentPort), creditsDelta: -50)),
                new HashSet<EpisodeMilestone>(),
                now),
            item => item.Milestone == EpisodeMilestone.FirstProfitableTrade);
        Assert.Empty(EpisodeGoalEvaluator.Evaluate(
            new EpisodeGoalContext(
                Snapshot(551, now.AddMinutes(-5)) with { HoloScannerInstalled = true },
                551, Cruiser, null, null),
            new HashSet<EpisodeMilestone>(),
            now));
    }

    [Fact]
    public void EvaluatorDetectsFullUpgradeOnlyWithKnownLimits()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        GameSnapshot maxed = Snapshot(551, now, holds: 75, fighters: 2_500, shields: 400) with
        {
            HoloScannerInstalled = true,
            DensityScannerInstalled = true
        };

        IReadOnlyList<EpisodeMilestone> known = EpisodeGoalEvaluator
            .Evaluate(new EpisodeGoalContext(maxed, null, Cruiser, null, null), new HashSet<EpisodeMilestone>(), now)
            .Select(item => item.Milestone)
            .ToList();
        IReadOnlyList<EpisodeMilestone> unknown = EpisodeGoalEvaluator
            .Evaluate(new EpisodeGoalContext(maxed, null, null, null, null), new HashSet<EpisodeMilestone>(), now)
            .Select(item => item.Milestone)
            .ToList();

        Assert.Contains(EpisodeMilestone.FullyUpgraded, known);
        Assert.Contains(EpisodeMilestone.MaxShields, known);
        Assert.DoesNotContain(EpisodeMilestone.MaxHolds, unknown);
        Assert.DoesNotContain(EpisodeMilestone.FullyUpgraded, unknown);
    }

    [Fact]
    public void EvaluatorAcceptsZeroAvailableRefurbQuoteOnlyWhenCreditsWereNotTheLimit()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var quote = new AutonomousRefurbQuote(551, 0, 900, 0, 120, 5, 200, 3, now);

        bool Maxed(long credits) => EpisodeGoalEvaluator.Evaluate(
                new EpisodeGoalContext(Snapshot(551, now, credits: credits), null, null, quote, null),
                new HashSet<EpisodeMilestone>(),
                now)
            .Any(item => item.Milestone == EpisodeMilestone.MaxHolds);

        Assert.True(Maxed(50_000));
        Assert.False(Maxed(500));
    }

    [Fact]
    public async Task SupervisorEndsEpisodeForEachCondition()
    {
        using var database = new TemporaryDatabase();
        var time = new ManualTime(DateTimeOffset.UtcNow);
        var recorder = new TrajectoryRecorder(new SqliteTrajectoryStore(database.ConnectionString), NullLogger<TrajectoryRecorder>.Instance, timeProvider: time);
        await recorder.BeginEpisodeAsync("meridian", "qwen3:8b", "episode-1", EpisodeGoal.FullyUpgradedShip);
        var options = new EpisodeOptions("episode-1", EpisodeGoal.FullyUpgradedShip, TimeSpan.FromMinutes(30));
        var capacity = new FixedCapacity(Cruiser);
        EpisodeSupervisor Create() => new(options, recorder, capacity, NullLogger<EpisodeSupervisor>.Instance, time);

        EpisodeSupervisor normal = Create();
        Assert.Null(await normal.EvaluateAsync("meridian", Situation(Snapshot(10, time.GetUtcNow())), null, null));

        Assert.Equal(
            EpisodeStatus.ShipLost,
            (await Create().EvaluateAsync("meridian", Situation(Snapshot(10, time.GetUtcNow(), shipType: "Escape Pod")), null, null))?.Status);
        Assert.Equal(
            EpisodeStatus.PausedOutOfTurns,
            (await Create().EvaluateAsync("meridian", Situation(Snapshot(10, time.GetUtcNow(), turns: 2)), null, null))?.Status);

        await recorder.BeginEpisodeAsync("finisher", "qwen3:8b", "episode-2", EpisodeGoal.FullyUpgradedShip);
        GameSnapshot maxed = Snapshot(551, time.GetUtcNow(), holds: 75, fighters: 2_500, shields: 400) with { HoloScannerInstalled = true };
        Assert.Equal(
            EpisodeStatus.Completed,
            (await Create().EvaluateAsync("finisher", Situation(maxed), 551, null))?.Status);
        Assert.Contains(EpisodeMilestone.FullyUpgraded, recorder.GetReachedMilestones("finisher"));
        Assert.DoesNotContain(EpisodeMilestone.FullyUpgraded, recorder.GetReachedMilestones("meridian"));

        time.Advance(TimeSpan.FromMinutes(31));
        Assert.Equal(
            EpisodeStatus.TimedOut,
            (await normal.EvaluateAsync("meridian", Situation(Snapshot(10, DateTimeOffset.UtcNow)), null, null))?.Status);
    }

    [Fact]
    public async Task PausedEpisodeResumesAsNextSegmentAndTerminalEpisodeDoesNot()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteTrajectoryStore(database.ConnectionString);
        var recorder = new TrajectoryRecorder(store, NullLogger<TrajectoryRecorder>.Instance);
        DateTimeOffset now = DateTimeOffset.UtcNow;

        await recorder.BeginEpisodeAsync("learner-1", "qwen3:8b", "episode-1", EpisodeGoal.FullyUpgradedShip);
        await recorder.RecordDecisionAsync("learner-1", "qwen3:8b", Situation(Snapshot(1, now.AddSeconds(-20))),
            new AutonomousTestActionDecision(AutonomousTestAction.MoveToAdjacentSector, "Go.", 2), TrajectoryDecisionSource.Pilot, null);
        recorder.ObserveSnapshot("learner-1", Snapshot(2, now.AddSeconds(-10), turns: 997));
        await recorder.RecordMilestonesAsync("learner-1", [new EpisodeMilestoneObservation(EpisodeMilestone.ReachedStarDock, "test")]);
        TrajectoryEpisode? paused = await recorder.EndEpisodeAsync("learner-1", EpisodeStatus.PausedOutOfTurns, "Out of turns");

        TrajectoryEpisode? resumed = await recorder.BeginEpisodeAsync("learner-1", "qwen3:8b", "episode-1", EpisodeGoal.FullyUpgradedShip);
        await recorder.RecordDecisionAsync("learner-1", "qwen3:8b", Situation(Snapshot(2, now)),
            new AutonomousTestActionDecision(AutonomousTestAction.None, "Hold."), TrajectoryDecisionSource.Policy, null);
        await recorder.EndEpisodeAsync("learner-1", EpisodeStatus.Completed, "Done");

        Assert.Equal(EpisodeStatus.PausedOutOfTurns, paused?.Status);
        Assert.Equal(2, resumed?.Segment);
        Assert.Contains(EpisodeMilestone.ReachedStarDock, (await store.GetMilestonesAsync("episode-1")).Select(item => item.Milestone));
        TrajectoryEpisode final = (await store.GetEpisodeAsync("episode-1"))!;
        Assert.Equal(EpisodeStatus.Completed, final.Status);
        Assert.Equal(2, final.StepCount);
        Assert.Equal(1, final.MilestoneCount);
        Assert.Equal([1, 2], (await store.GetStepsAsync("episode-1")).Select(step => step.Segment));
        Assert.Equal(GameplayRewardWeights.Default.ReachedStarDockBonus, final.TotalReward - (await store.GetStepsAsync("episode-1")).Sum(step => step.Reward?.Verified == true ? step.Reward.Total : 0), precision: 4);
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            recorder.BeginEpisodeAsync("learner-1", "qwen3:8b", "episode-1", EpisodeGoal.FullyUpgradedShip));
    }

    [Fact]
    public async Task StoreMigratesStepOneSchema()
    {
        using var database = new TemporaryDatabase();
        using (var connection = new SqliteConnection(database.ConnectionString))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE trajectory_episodes (episode_id TEXT PRIMARY KEY, agent_id TEXT NOT NULL, model TEXT NOT NULL,
                    started_at TEXT NOT NULL, ended_at TEXT NULL, end_reason TEXT NULL, step_count INTEGER NOT NULL,
                    verified_step_count INTEGER NOT NULL, pilot_decision_count INTEGER NOT NULL,
                    policy_decision_count INTEGER NOT NULL, total_reward REAL NOT NULL, start_net_worth INTEGER NULL,
                    end_net_worth INTEGER NULL, start_turns INTEGER NULL, end_turns INTEGER NULL,
                    sectors_visited INTEGER NOT NULL, ship_lost INTEGER NOT NULL, scanner_acquired INTEGER NOT NULL);
                INSERT INTO trajectory_episodes VALUES ('old', 'meridian', 'm', '2026-11-01T00:00:00.0000000+00:00', NULL, NULL,
                    3, 2, 1, 2, 1.5, 100, 200, 10, 8, 2, 0, 0);
                """;
            command.ExecuteNonQuery();
        }

        var store = new SqliteTrajectoryStore(database.ConnectionString);
        await store.InitializeAsync();
        await store.InitializeAsync();

        TrajectoryEpisode old = (await store.GetEpisodeAsync("old"))!;
        Assert.Equal(EpisodeStatus.Running, old.Status);
        Assert.Equal(1, old.Segment);
        Assert.Empty(old.VisitedSectors);
        Assert.Equal(3, old.StepCount);
    }

    [Fact]
    public void EpisodeCommandLineRoundTripsAndValidates()
    {
        var options = new EpisodeOptions("b1-01", EpisodeGoal.FullyUpgradedShip, TimeSpan.FromMinutes(480))
        {
            ResultPath = Path.Combine(Path.GetTempPath(), "result.json"),
            ExitOnEnd = true,
            PilotModel = "qwen3:1.7b"
        };
        string[] args = ["--agent-id", "learner-1", .. EpisodeCommandLine.ToArguments(options)];

        Assert.True(EpisodeCommandLine.TryExtract(args, out string[] remaining, out IReadOnlyDictionary<string, string> values, out string? error), error);
        Assert.Equal(["--agent-id", "learner-1"], remaining);
        Assert.Equal("true", values["ExitOnEnd"]);
        Assert.Equal("qwen3:1.7b", values["PilotModel"]);
        Assert.False(EpisodeCommandLine.TryExtract(["--episode-id", "x", "--episode-pilot-model", "bad model;rm"], out _, out _, out _));

        Assert.False(EpisodeCommandLine.TryExtract(["--episode-goal", "FullyUpgradedShip"], out _, out _, out error));
        Assert.Contains("--episode-id", error);
        Assert.False(EpisodeCommandLine.TryExtract(["--episode-id", "x", "--episode-wall-clock-minutes", "-1"], out _, out _, out _));
        Assert.False(EpisodeCommandLine.TryExtract(["--episode-id", "x", "--episode-goal", "Unknown"], out _, out _, out _));
        Assert.True(EpisodeCommandLine.TryExtract(["--game-profile", "a.json"], out remaining, out values, out _));
        Assert.Empty(values);
        Assert.Equal(["--game-profile", "a.json"], remaining);
    }

    [Fact]
    public void EpisodeOptionsRoundTripThroughEnvironment()
    {
        try
        {
            Assert.True(EpisodeCommandLine.TryExtract(
                ["--episode-id", "env-01", "--episode-wall-clock-minutes", "10", "--exit-on-episode-end"],
                out _,
                out IReadOnlyDictionary<string, string> values,
                out string? error), error);
            EpisodeCommandLine.SetEnvironment(values);

            EpisodeOptions? options = EpisodeCommandLine.ReadEnvironment();

            Assert.Equal("env-01", options?.EpisodeId);
            Assert.Equal(TimeSpan.FromMinutes(10), options?.WallClockCap);
            Assert.True(options?.ExitOnEnd);
        }
        finally
        {
            EpisodeCommandLine.SetEnvironment(new Dictionary<string, string>());
        }
        Assert.Null(EpisodeCommandLine.ReadEnvironment());
    }

    [Fact]
    public void ProfileClonerCreatesIsolatedEpisodeProfile()
    {
        using var games = new TemporaryDirectory();
        string baseDb = Path.Combine(games.Path, "Server - Game E.xdb");
        File.WriteAllText(baseDb, "map");
        Directory.CreateDirectory(Path.Combine(games.Path, "Server - Game E"));
        File.WriteAllText(Path.Combine(games.Path, "Server - Game E", "ships.cfg"), "400 2 3 41300 75 2500 20 3 0 Merchant Cruiser");
        string baseProfile = Path.Combine(games.Path, "Server - Game E.json");
        File.WriteAllText(baseProfile, $$"""
            { "Id": "base", "Name": "Server - Game E", "DatabasePath": {{System.Text.Json.JsonSerializer.Serialize(baseDb)}},
              "Mtc": { "State": { "Credits": 39727 } }, "mombot": { "Enabled": true } }
            """);

        ClonedProfile clone = ProfileCloner.Clone(baseProfile, "learner-abc-01", MapSeed.CopyBase);

        var json = System.Text.Json.Nodes.JsonNode.Parse(File.ReadAllText(clone.ProfilePath))!;
        Assert.Equal("Server - Game E - learner-abc-01", json["Name"]!.GetValue<string>());
        Assert.NotEqual("base", json["Id"]!.GetValue<string>());
        Assert.Equal(clone.DatabasePath, json["DatabasePath"]!.GetValue<string>());
        Assert.Null(json["Mtc"]!["State"]);
        Assert.False(json["mombot"]!["Enabled"]!.GetValue<bool>());
        Assert.Equal("map", File.ReadAllText(clone.DatabasePath));
        Assert.True(clone.ShipCatalogCopied);
        Assert.True(File.Exists(Path.Combine(games.Path, clone.GameName, "ships.cfg")));
        Assert.Throws<InvalidOperationException>(() => ProfileCloner.Clone(baseProfile, "learner-abc-01", MapSeed.CopyBase));

        using (new FileStream(baseProfile + ".lock", FileMode.Create, FileAccess.ReadWrite, FileShare.None))
        {
            Assert.Throws<InvalidOperationException>(() => ProfileCloner.Clone(baseProfile, "learner-abc-02", MapSeed.CopyBase));
            ClonedProfile empty = ProfileCloner.Clone(baseProfile, "learner-abc-03", MapSeed.Empty);
            Assert.False(empty.MapCopied);
        }
    }

    [Fact]
    public async Task SchedulerRespectsParallelismAndMapsOutcomes()
    {
        using var database = new TemporaryDatabase();
        using var results = new TemporaryDirectory();
        (HarnessStore harness, SqliteTrajectoryStore trajectories, HarnessBatch batch) = await SeedBatchAsync(database);
        IReadOnlyList<HarnessEpisode> episodes = await SeedEpisodesAsync(harness, batch, 5);
        await trajectories.UpsertEpisodeAsync(new TrajectoryEpisode(episodes[3].EpisodeId, episodes[3].AgentId, "m", DateTimeOffset.UtcNow));
        var launcher = new FakeLauncher(new Dictionary<int, EpisodeProcessOutcome>
        {
            [1] = new(EpisodeExitCodes.Completed, false, null),
            [2] = new(EpisodeExitCodes.PausedOutOfTurns, false, null),
            [3] = new(EpisodeExitCodes.ShipLost, false, null),
            [4] = new(null, true, "MTC did not exit."),
            [5] = new(0, false, null)
        });
        var scheduler = new HarnessScheduler(
            harness, trajectories, launcher,
            new SchedulerSettings(2, EpisodeGoal.FullyUpgradedShip, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), TimeOnly.MinValue, results.Path, PilotModel: "qwen3:1.7b"),
            TextWriter.Null);

        await scheduler.RunDueAsync(episodes, CancellationToken.None);

        IReadOnlyList<HarnessEpisode> after = await harness.GetEpisodesAsync(batch.BatchId);
        Assert.Equal(2, launcher.MaximumConcurrent);
        Assert.Equal(
            [HarnessEpisodeState.Completed, HarnessEpisodeState.Paused, HarnessEpisodeState.ShipLost, HarnessEpisodeState.TimedOut, HarnessEpisodeState.Stopped],
            after.Select(episode => episode.State));
        Assert.All(after, episode => Assert.Equal(1, episode.Segments));
        Assert.True(after[1].ResumeAfter > DateTimeOffset.UtcNow);
        Assert.Equal(EpisodeStatus.TimedOut, (await trajectories.GetEpisodeAsync(episodes[3].EpisodeId))!.Status);
        Assert.Contains("--create-account-if-needed", launcher.Launches[0].Arguments);
        Assert.Contains("--exit-on-episode-end", launcher.Launches[0].Arguments);
        Assert.Equal("qwen3:1.7b", launcher.Launches[0].Arguments[launcher.Launches[0].Arguments.ToList().IndexOf("--episode-pilot-model") + 1]);
        Assert.False(scheduler.IsDue(after[1]));
        Assert.True(scheduler.IsDue(after[1] with { ResumeAfter = DateTimeOffset.UtcNow.AddMinutes(-1) }));
    }

    [Fact]
    public async Task RollingRunKeepsSlotsFullAndStopsAtTheEpisodeLimit()
    {
        using var database = new TemporaryDatabase();
        using var results = new TemporaryDirectory();
        (HarnessStore harness, SqliteTrajectoryStore trajectories, HarnessBatch batch) = await SeedBatchAsync(database);
        var launcher = new FakeLauncher(Enumerable.Range(1, 4)
            .ToDictionary(ordinal => ordinal, _ => new EpisodeProcessOutcome(EpisodeExitCodes.TimedOut, false, null)));
        var scheduler = new HarnessScheduler(
            harness, trajectories, launcher,
            new SchedulerSettings(2, EpisodeGoal.FullyUpgradedShip, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), TimeOnly.MinValue, results.Path),
            TextWriter.Null);
        int reports = 0;

        int started = await scheduler.RunRollingAsync(
            (ordinal, token) => CreateRollingEpisodeAsync(harness, batch, ordinal, token),
            new RollingLimits(MaxEpisodes: 4) { PollInterval = TimeSpan.FromMilliseconds(20) },
            _ =>
            {
                Interlocked.Increment(ref reports);
                return Task.CompletedTask;
            },
            CancellationToken.None);

        Assert.Equal(4, started);
        Assert.Equal(4, launcher.Launches.Count);
        Assert.Equal(2, launcher.MaximumConcurrent);
        Assert.Equal(4, reports);
        Assert.All(await harness.GetEpisodesAsync(batch.BatchId), episode => Assert.Equal(HarnessEpisodeState.TimedOut, episode.State));
    }

    [Fact]
    public async Task RollingRunStopsAfterConsecutiveErrorsOrAStopFile()
    {
        using var database = new TemporaryDatabase();
        using var results = new TemporaryDirectory();
        (HarnessStore harness, SqliteTrajectoryStore trajectories, HarnessBatch batch) = await SeedBatchAsync(database);
        var launcher = new ImmediateLauncher(new EpisodeProcessOutcome(null, false, "MTC crashed."));
        var scheduler = new HarnessScheduler(
            harness, trajectories, launcher,
            new SchedulerSettings(1, EpisodeGoal.FullyUpgradedShip, TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1), TimeOnly.MinValue, results.Path),
            TextWriter.Null);
        var fastLimits = new RollingLimits(MaxConsecutiveErrors: 3)
        {
            ErrorBackoff = TimeSpan.Zero,
            PollInterval = TimeSpan.FromMilliseconds(20)
        };

        int started = await scheduler.RunRollingAsync(
            (ordinal, token) => CreateRollingEpisodeAsync(harness, batch, ordinal, token),
            fastLimits,
            null,
            CancellationToken.None);

        Assert.Equal(3, started);
        Assert.All(await harness.GetEpisodesAsync(batch.BatchId), episode => Assert.Equal(HarnessEpisodeState.Error, episode.State));

        string stopFile = Path.Combine(results.Path, "harness.stop");
        await File.WriteAllTextAsync(stopFile, string.Empty);
        int startedAfterStop = await scheduler.RunRollingAsync(
            (ordinal, token) => CreateRollingEpisodeAsync(harness, batch, 100 + ordinal, token),
            fastLimits with { StopFilePath = stopFile },
            null,
            CancellationToken.None);
        Assert.Equal(0, startedAfterStop);
    }

    private static async Task<HarnessEpisode> CreateRollingEpisodeAsync(
        HarnessStore harness,
        HarnessBatch batch,
        int ordinal,
        CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var episode = new HarnessEpisode(
            $"{batch.BatchId}-{ordinal:D2}", batch.BatchId, ordinal, $"learner-roll-{ordinal:D2}", "profile.json",
            HarnessEpisodeState.Pending, 0, null, null, null, now, now);
        await harness.UpsertEpisodeAsync(episode, cancellationToken);
        return episode;
    }

    [Fact]
    public async Task ReportAggregatesFunnelAndDecisionSources()
    {
        using var database = new TemporaryDatabase();
        (HarnessStore harness, SqliteTrajectoryStore trajectories, HarnessBatch batch) = await SeedBatchAsync(database);
        IReadOnlyList<HarnessEpisode> episodes = await SeedEpisodesAsync(harness, batch, 2);
        DateTimeOffset start = DateTimeOffset.UtcNow.AddHours(-1);
        await trajectories.UpsertEpisodeAsync(new TrajectoryEpisode(episodes[0].EpisodeId, episodes[0].AgentId, "m", start)
        {
            Status = EpisodeStatus.Completed, StartNetWorth = 1_000, EndNetWorth = 11_000, StartTurns = 1_000, EndTurns = 900, TotalReward = 40
        });
        await trajectories.UpsertStepAsync(new TrajectoryStep("s1", episodes[0].EpisodeId, episodes[0].AgentId, 0, start, TrajectoryDecisionSource.Pilot, "None", 0, "r", null, null, null));
        await trajectories.UpsertStepAsync(new TrajectoryStep("s2", episodes[0].EpisodeId, episodes[0].AgentId, 1, start, TrajectoryDecisionSource.PilotOverriddenByPolicy, "None", 0, "r", null, null, null));
        await trajectories.UpsertMilestoneAsync(new TrajectoryMilestone(episodes[0].EpisodeId, EpisodeMilestone.ReachedStarDock, start.AddMinutes(30), 1, 1, 40, 2, "e"));
        await harness.UpsertEpisodeAsync(episodes[0] with { State = HarnessEpisodeState.Completed, Segments = 1 });
        await harness.UpsertEpisodeAsync(episodes[1] with { State = HarnessEpisodeState.ShipLost, Segments = 1 });

        BatchReport report = await BatchReportBuilder.BuildAsync(
            batch, await harness.GetEpisodesAsync(batch.BatchId), trajectories, DateTimeOffset.UtcNow);
        string markdown = BatchReportBuilder.ToMarkdown(report);

        Assert.Equal(0.5, report.CompletionRate);
        Assert.Equal(0.5, report.ShipLossRate);
        Assert.Equal(100, report.MedianNetWorthPerTurn);
        Assert.Equal(1, report.DecisionSources["Pilot"]);
        Assert.Equal(1, report.DecisionSources["PilotOverriddenByPolicy"]);
        MilestoneFunnelRow dock = report.Funnel.Single(row => row.Milestone == EpisodeMilestone.ReachedStarDock);
        Assert.Equal(1, dock.EpisodesReached);
        Assert.Equal(40, dock.MedianTurnsUsed);
        Assert.Equal(30, dock.MedianMinutes);
        Assert.Contains("| ReachedStarDock | 1 | 50% | 40.00 | 30.00 |", markdown);
        Assert.Contains("learner-t-02", markdown);
    }

    [Fact]
    public void NextGameDayResetIsAlwaysAFutureLocalReset()
    {
        var scheduler = new HarnessScheduler(
            null!, null!, null!,
            new SchedulerSettings(1, EpisodeGoal.FullyUpgradedShip, TimeSpan.FromMinutes(1), TimeSpan.Zero, new TimeOnly(0, 0), "."),
            TextWriter.Null);
        DateTimeOffset start = new(2026, 10, 30, 0, 0, 0, TimeSpan.Zero);
        for (int hour = 0; hour < 24 * 5; hour += 1)
        {
            DateTimeOffset now = start.AddHours(hour).AddMinutes(17);
            DateTimeOffset reset = scheduler.NextGameDayReset(now);
            DateTimeOffset local = TimeZoneInfo.ConvertTime(reset, TimeZoneInfo.Local);

            Assert.True(reset > now, $"{reset:O} is not after {now:O}");
            Assert.True(reset - now <= TimeSpan.FromHours(25), $"{reset:O} is too far after {now:O}");
            Assert.Equal(TimeSpan.Zero, local.TimeOfDay);
        }
    }

    [Theory]
    [InlineData("Meridian")]
    [InlineData("Ab")]
    [InlineData("ABCDEFGHIJKLMNOPrime")]
    [InlineData("VeryLongCallsignName")]
    public void DerivedTraderAliasIsValidAndDistinct(string loginName)
    {
        string alias = AutonomousTestWorker.DeriveDistinctTraderAlias(loginName);

        Assert.NotEqual(loginName, alias, StringComparer.OrdinalIgnoreCase);
        Assert.True(AutonomousTestWorker.IsAliasDistinctFromLogin(alias, loginName));
        Assert.InRange(alias.Length, 3, 20);
        Assert.All(alias, character => Assert.True(char.IsAsciiLetter(character)));
    }

    [Theory]
    [InlineData("EclipticExile - Learner-513ec7-0", "EclipticExile")]
    [InlineData("EONEX-TRADER-E", "EONEX")]
    [InlineData("  NyxSiren ", "NyxSiren")]
    [InlineData("E", "E")]
    [InlineData("Averyveryverylongcallsignname", "Averyveryverylongcal")]
    public void GeneratedLoginNamesKeepLeadingLetters(string generated, string expected) =>
        Assert.Equal(expected, AutonomousTestWorker.NormalizeGeneratedLoginName(generated));

    [Fact]
    public void UniqueLoginNameIsValidStableAndAgentSpecific()
    {
        string first = AutonomousTestWorker.MakeUniqueLoginName("EchelonX", "learner-abc-01");
        string again = AutonomousTestWorker.MakeUniqueLoginName("EchelonX", "learner-abc-01");
        string other = AutonomousTestWorker.MakeUniqueLoginName("EchelonX", "learner-abc-02");
        string longName = AutonomousTestWorker.MakeUniqueLoginName("AveryveryverylongCallsign", "learner-abc-01");

        Assert.StartsWith("EchelonX", first, StringComparison.Ordinal);
        Assert.Equal(first, again);
        Assert.NotEqual(first, other);
        Assert.InRange(longName.Length, 3, 20);
        Assert.All(first + longName, character => Assert.True(char.IsAsciiLetter(character)));
        Assert.InRange(AutonomousTestWorker.MakeUniqueLoginName("E", "learner-abc-01").Length, 9, 20);
    }

    [Fact]
    public void FallbackCallsignIsValidLoginName()
    {
        for (int index = 0; index < 50; index++)
        {
            string callsign = AutonomousTestWorker.CreateFallbackCallsign();
            Assert.InRange(callsign.Length, 3, 20);
            Assert.All(callsign, character => Assert.True(char.IsAsciiLetter(character)));
        }
    }

    [Fact]
    public void DerivedCharacterNameFitsShipAndPlanetRules()
    {
        Assert.Equal("NyxSiren Voyager", AutonomousTestWorker.DeriveCharacterName("NyxSiren", "Voyager"));
        string longName = AutonomousTestWorker.DeriveCharacterName("VeryLongCallsignName", "Voyager");
        Assert.InRange(longName.Length, 3, 30);
        Assert.Equal(longName, longName.Trim());
        Assert.All(longName, character => Assert.True(char.IsAsciiLetter(character) || character == ' '));
    }

    [Fact]
    public async Task LearnerCleanupRemovesRegistryEntryAndProfileButKeepsLogs()
    {
        using var root = new TemporaryDirectory();
        string games = Path.Combine(root.Path, "games");
        Directory.CreateDirectory(Path.Combine(games, "Server - Game E - learner-abc-01", "agent"));
        Directory.CreateDirectory(Path.Combine(root.Path, "logs"));
        File.WriteAllText(Path.Combine(games, "Server - Game E - learner-abc-01", "agent", "events-1.jsonl"), "{}");
        File.WriteAllText(Path.Combine(root.Path, "logs", "Server - Game E - learner-abc-01_debug.log"), "log");
        string database = Path.Combine(games, "Server - Game E - learner-abc-01.xdb");
        File.WriteAllText(database, "db");
        File.WriteAllText(Path.Combine(games, "Server - Game E - learner-abc-01.json"),
            $$"""{ "Name": "Server - Game E - learner-abc-01", "DatabasePath": {{System.Text.Json.JsonSerializer.Serialize(database)}} }""");
        string agentsDb = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(root.Path, "agents.db"),
            ForeignKeys = true,
            Pooling = false
        }.ToString();
        using (var connection = new SqliteConnection(agentsDb))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                CREATE TABLE agents (agent_id TEXT PRIMARY KEY);
                CREATE TABLE agent_journal (agent_id TEXT NOT NULL REFERENCES agents(agent_id) ON DELETE CASCADE, text TEXT);
                CREATE TABLE agent_team_status (agent_id TEXT NOT NULL);
                INSERT INTO agents VALUES ('learner-abc-01'), ('meridian');
                INSERT INTO agent_journal VALUES ('learner-abc-01', 'x'), ('meridian', 'y');
                INSERT INTO agent_team_status VALUES ('learner-abc-01');
                """;
            command.ExecuteNonQuery();
        }
        var cleanup = new LearnerCleanup(agentsDb, Path.Combine(root.Path, "archive"), TextWriter.Null);
        var episode = new HarnessEpisode("batch-01", "batch", 1, "learner-abc-01", "Server - Game E - learner-abc-01.json",
            HarnessEpisodeState.Error, 1, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);

        Assert.Equal(LearnerCleanupResult.Skipped, await cleanup.CleanAsync(episode with { State = HarnessEpisodeState.Paused }, games));
        Assert.Equal(LearnerCleanupResult.Cleaned, await cleanup.CleanAsync(episode, games));
        Assert.Equal(LearnerCleanupResult.AlreadyClean, await cleanup.CleanAsync(episode, games));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            cleanup.CleanAsync(episode with { AgentId = "meridian" }, games));

        Assert.False(File.Exists(Path.Combine(games, "Server - Game E - learner-abc-01.json")));
        Assert.False(File.Exists(database));
        Assert.False(Directory.Exists(Path.Combine(games, "Server - Game E - learner-abc-01")));
        Assert.True(File.Exists(Path.Combine(root.Path, "archive", "batch", "batch-01", "events-1.jsonl")));
        Assert.True(File.Exists(Path.Combine(root.Path, "archive", "batch", "batch-01", "Server - Game E - learner-abc-01_debug.log")));
        using (var connection = new SqliteConnection(agentsDb))
        {
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = """
                SELECT (SELECT group_concat(agent_id) FROM agents),
                       (SELECT COUNT(*) FROM agent_journal),
                       (SELECT COUNT(*) FROM agent_team_status);
                """;
            using var reader = command.ExecuteReader();
            reader.Read();
            Assert.Equal("meridian", reader.GetString(0));
            Assert.Equal(1, reader.GetInt64(1));
            Assert.Equal(0, reader.GetInt64(2));
        }
    }

    private static async Task<(HarnessStore, SqliteTrajectoryStore, HarnessBatch)> SeedBatchAsync(TemporaryDatabase database)
    {
        var harness = new HarnessStore(database.ConnectionString);
        var trajectories = new SqliteTrajectoryStore(database.ConnectionString);
        await trajectories.InitializeAsync();
        await harness.InitializeAsync();
        var batch = new HarnessBatch("batch-t", DateTimeOffset.UtcNow, "base.json", "games", 2, nameof(EpisodeGoal.FullyUpgradedShip), 1);
        await harness.InsertBatchAsync(batch);
        return (harness, trajectories, batch);
    }

    private static async Task<IReadOnlyList<HarnessEpisode>> SeedEpisodesAsync(HarnessStore harness, HarnessBatch batch, int count)
    {
        var episodes = new List<HarnessEpisode>();
        for (int ordinal = 1; ordinal <= count; ordinal++)
        {
            var episode = new HarnessEpisode($"{batch.BatchId}-{ordinal:D2}", batch.BatchId, ordinal, $"learner-t-{ordinal:D2}",
                $"profile-{ordinal}.json", HarnessEpisodeState.Pending, 0, null, null, null, DateTimeOffset.UtcNow, DateTimeOffset.UtcNow);
            await harness.UpsertEpisodeAsync(episode);
            episodes.Add(episode);
        }
        return episodes;
    }

    private static TrajectoryStep Step(string action, long creditsDelta) =>
        new("s", "e", "a", 0, DateTimeOffset.UtcNow, TrajectoryDecisionSource.Pilot, action, 0, "r", null, null, null)
        {
            Reward = new GameplayReward(true, creditsDelta / 1000d, creditsDelta, creditsDelta, 1, false, false, false, null)
        };

    [Fact]
    public async Task HeartbeatEndsAnOverCapEpisodeWhileAScriptBlocksDecisions()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteTrajectoryStore(database.ConnectionString);
        await store.InitializeAsync();
        var recorder = new TrajectoryRecorder(store, NullLogger<TrajectoryRecorder>.Instance);
        var options = new EpisodeOptions("episode-heartbeat", EpisodeGoal.FullyUpgradedShip, TimeSpan.FromMilliseconds(1));
        var supervisor = new EpisodeSupervisor(options, recorder, null, NullLogger<EpisodeSupervisor>.Instance);
        var completion = new RecordingCompletionHandler();
        var eventHub = new ProxyGameEventHub();
        using var worker = new AutonomousTestWorker(
            "learner-hb",
            "qwen3:1.7b",
            new Uri("http://localhost:11434/"),
            eventHub,
            new FixedSituationProvider(Situation(Snapshot(20, DateTimeOffset.UtcNow))),
            new NoDecisionActionProvider(),
            new NoCommandSender(),
            NullLogger<AutonomousTestWorker>.Instance,
            trajectoryRecorder: recorder,
            episodeSupervisor: supervisor,
            episodeCompletionHandler: completion);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            await Task.Delay(50);
            eventHub.TryPublish(new ProxySessionEvent("learner-hb", new ProxyGameEvent(DateTimeOffset.UtcNow, "PresenceHeartbeat")));
            TrajectoryEpisode ended = await completion.Ended.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(EpisodeStatus.TimedOut, ended.Status);
            Assert.Equal(EpisodeStatus.TimedOut, (await store.GetEpisodeAsync("episode-heartbeat"))!.Status);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task PausingAManagedWorkerKeepsTheEpisodeOpenAndResumesIt()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteTrajectoryStore(database.ConnectionString);
        await store.InitializeAsync();
        var recorder = new TrajectoryRecorder(store, NullLogger<TrajectoryRecorder>.Instance);
        var options = new EpisodeOptions("episode-takeover", EpisodeGoal.FullyUpgradedShip, TimeSpan.FromHours(1));
        var completion = new RecordingCompletionHandler();
        AutonomousTestWorker CreateWorker() => new(
            "learner-pause",
            "qwen3:1.7b",
            new Uri("http://localhost:11434/"),
            new ProxyGameEventHub(),
            new FixedSituationProvider(Situation(Snapshot(20, DateTimeOffset.UtcNow))),
            new NoDecisionActionProvider(),
            new NoCommandSender(),
            NullLogger<AutonomousTestWorker>.Instance,
            trajectoryRecorder: recorder,
            episodeSupervisor: new EpisodeSupervisor(options, recorder, null, NullLogger<EpisodeSupervisor>.Instance),
            episodeCompletionHandler: completion);

        using (AutonomousTestWorker first = CreateWorker())
        {
            await first.StartAsync(CancellationToken.None);
            await Task.Delay(100);
            await first.StopAsync(CancellationToken.None);
        }

        Assert.False(completion.Ended.Task.IsCompleted);
        Assert.Equal(EpisodeStatus.Running, (await store.GetEpisodeAsync("episode-takeover"))!.Status);

        using (AutonomousTestWorker resumed = CreateWorker())
        {
            await resumed.StartAsync(CancellationToken.None);
            await Task.Delay(100);
            await resumed.StopAsync(CancellationToken.None);
        }

        TrajectoryEpisode episode = (await store.GetEpisodeAsync("episode-takeover"))!;
        Assert.Equal(EpisodeStatus.Running, episode.Status);
        Assert.Equal(1, episode.Segment);
        Assert.False(completion.Ended.Task.IsCompleted);
    }

    private sealed class FixedSituationProvider(GameSituationReport situation) : IGameSituationProvider
    {
        public Task<GameSituationReport> CaptureSituationAsync(string agentId, CancellationToken cancellationToken = default) =>
            Task.FromResult(situation);
    }

    private sealed class NoDecisionActionProvider : IAutonomousTestActionProvider
    {
        public string Id => "none";

        public Task<AutonomousTestActionDecision> DecideAsync(
            ModelConfiguration configuration,
            AutonomousTestActionContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No decision is expected while a script runs.");
    }

    private sealed class NoCommandSender : IGameCommandSender
    {
        public Task SendAsync(string agentId, GameCommandRequest request, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("No command is expected while a script runs.");
    }

    private sealed class RecordingCompletionHandler : IEpisodeCompletionHandler
    {
        public TaskCompletionSource<TrajectoryEpisode> Ended { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task OnEpisodeEndedAsync(TrajectoryEpisode episode, CancellationToken cancellationToken = default)
        {
            Ended.TrySetResult(episode);
            return Task.CompletedTask;
        }
    }

    private static GameSituationReport Situation(GameSnapshot snapshot) =>
        new(snapshot.ObservedAt, GamePresenceState.Connected, GameAuthenticationState.Authenticated,
            GameSynchronizationState.Healthy, "Command", snapshot.ObservedAt, snapshot, []);

    private static GameSnapshot Snapshot(
        int sector,
        DateTimeOffset at,
        long credits = 5_000,
        int turns = 1_000,
        int holds = 20,
        int fighters = 30,
        int shields = 0,
        string shipType = "Merchant Cruiser") =>
        new(sector, credits, turns, new Ship("Ship", "Learner", shipType, sector, fighters, at), at)
        {
            HoldsTotal = holds,
            HoldsEmpty = holds,
            Shields = shields
        };

    private sealed class FixedCapacity(ShipCapacity capacity) : IShipCapacityProvider
    {
        public ShipCapacity? GetCapacity(string? shipClass) =>
            string.Equals(shipClass, capacity.ShipClass, StringComparison.OrdinalIgnoreCase) ? capacity : null;
    }

    private sealed class ManualTime(DateTimeOffset now) : TimeProvider
    {
        private DateTimeOffset _now = now;

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private sealed class ImmediateLauncher(EpisodeProcessOutcome outcome) : IEpisodeProcessLauncher
    {
        public async Task<EpisodeProcessOutcome> RunAsync(EpisodeLaunch launch, TimeSpan timeout, CancellationToken cancellationToken)
        {
            await Task.Yield();
            return outcome;
        }
    }

    private sealed class FakeLauncher(IReadOnlyDictionary<int, EpisodeProcessOutcome> outcomes) : IEpisodeProcessLauncher
    {
        private readonly TaskCompletionSource _twoRunning = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int _concurrent;

        public int MaximumConcurrent { get; private set; }

        public List<EpisodeLaunch> Launches { get; } = [];

        public async Task<EpisodeProcessOutcome> RunAsync(EpisodeLaunch launch, TimeSpan timeout, CancellationToken cancellationToken)
        {
            lock (Launches)
            {
                Launches.Add(launch);
                MaximumConcurrent = Math.Max(MaximumConcurrent, ++_concurrent);
                if (_concurrent >= 2)
                {
                    _twoRunning.TrySetResult();
                }
            }
            // Hold the first launches until two overlap (or a generous timeout) so slow CI cannot hide concurrency.
            await Task.WhenAny(_twoRunning.Task, Task.Delay(TimeSpan.FromSeconds(5), cancellationToken));
            await Task.Delay(20, cancellationToken);
            lock (Launches)
            {
                _concurrent--;
            }
            return outcomes[launch.Episode.Ordinal];
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        public string Path { get; } = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"twbot-harness-{Guid.NewGuid():N}");

        public TemporaryDirectory() => Directory.CreateDirectory(Path);

        public void Dispose()
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, recursive: true);
            }
        }
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string _path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), $"twbot-harness-{Guid.NewGuid():N}.db");

        public string ConnectionString => new SqliteConnectionStringBuilder
        {
            DataSource = _path,
            ForeignKeys = true,
            Pooling = false
        }.ToString();

        public void Dispose()
        {
            if (File.Exists(_path))
            {
                File.Delete(_path);
            }
        }
    }
}
