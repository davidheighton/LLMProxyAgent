using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class LearningTrajectoryTests
{
    private static readonly DateTimeOffset BaseTime = DateTimeOffset.UtcNow.AddMinutes(-10);

    [Fact]
    public void RewardCountsVerifiedCreditGainMinusTurnCost()
    {
        GameSnapshot before = Snapshot(551, credits: 5_555, turns: 1_000, at: BaseTime);
        GameSnapshot after = Snapshot(551, credits: 6_521, turns: 990, at: BaseTime.AddSeconds(30));

        GameplayReward reward = GameplayRewardCalculator.Compute(before, after, newSector: false);

        Assert.True(reward.Verified);
        Assert.Equal(966, reward.CreditsDelta);
        Assert.Equal(966, reward.NetWorthDelta);
        Assert.Equal(10, reward.TurnsUsed);
        Assert.Equal(0.966 - 10 * 0.02, reward.Total, precision: 4);
    }

    [Fact]
    public void RewardTreatsAssetPurchasesAsNetWorthNotPureLoss()
    {
        GameSnapshot before = Snapshot(551, credits: 10_000, turns: 100, at: BaseTime) with
        {
            DensityScannerInstalled = false,
            HoloScannerInstalled = false
        };
        GameSnapshot after = Snapshot(551, credits: 8_000, turns: 100, at: BaseTime.AddSeconds(5)) with
        {
            DensityScannerInstalled = true,
            HoloScannerInstalled = false
        };

        GameplayReward reward = GameplayRewardCalculator.Compute(before, after, newSector: false);

        Assert.True(reward.Verified);
        Assert.True(reward.ScannerAcquired);
        Assert.Equal(-2_000, reward.CreditsDelta);
        Assert.Equal(0, reward.NetWorthDelta);
        Assert.Equal(GameplayRewardWeights.Default.ScannerMilestoneBonus, reward.Total, precision: 4);
    }

    [Fact]
    public void RewardPenalizesShipLossAndRewardsExploration()
    {
        GameSnapshot before = Snapshot(369, credits: 21_856, turns: 500, at: BaseTime);
        GameSnapshot after = Snapshot(99, credits: 21_856, turns: 499, at: BaseTime.AddSeconds(5), shipType: "Escape Pod");

        GameplayReward reward = GameplayRewardCalculator.Compute(before, after, newSector: true);

        Assert.True(reward.ShipLost);
        Assert.True(reward.NewSector);
        Assert.True(reward.Total < -GameplayRewardWeights.Default.ShipLossPenalty + 1);
    }

    [Fact]
    public void RewardIsUnverifiedWithoutNewerValidSnapshot()
    {
        GameSnapshot before = Snapshot(10, credits: 100, turns: 10, at: BaseTime);

        Assert.False(GameplayRewardCalculator.Compute(before, null, false).Verified);
        Assert.False(GameplayRewardCalculator.Compute(before, before, false).Verified);
        Assert.False(GameplayRewardCalculator.Compute(
            before,
            Snapshot(10, credits: 100, turns: 10, at: BaseTime.AddSeconds(1)) with { HoldsEmpty = 50, HoldsTotal = 20 },
            false).Verified);
    }

    [Fact]
    public async Task RecorderPersistsTransitionsAndEpisodeSummary()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteTrajectoryStore(database.ConnectionString);
        var recorder = new TrajectoryRecorder(store, NullLogger<TrajectoryRecorder>.Instance);

        await recorder.RecordDecisionAsync(
            "meridian",
            "qwen3:8b",
            Situation(Snapshot(172, credits: 5_752, turns: 1_000, at: BaseTime)),
            new AutonomousTestActionDecision(AutonomousTestAction.MoveToAdjacentSector, "Head to StarDock.", 551)
            {
                InteractionId = "interaction-1"
            },
            TrajectoryDecisionSource.Pilot,
            "Buy a Density Scanner");
        recorder.ObserveSnapshot("meridian", Snapshot(551, credits: 5_555, turns: 999, at: BaseTime.AddSeconds(10)));
        await recorder.RecordDecisionAsync(
            "meridian",
            "qwen3:8b",
            Situation(Snapshot(551, credits: 5_555, turns: 999, at: BaseTime.AddSeconds(10))),
            new AutonomousTestActionDecision(AutonomousTestAction.TradeAtCurrentPort, "Sell Fuel Ore."),
            TrajectoryDecisionSource.Policy,
            "Buy a Density Scanner");
        recorder.ObserveSnapshot("meridian", Snapshot(551, credits: 6_521, turns: 998, at: BaseTime.AddSeconds(20)));
        await recorder.EndEpisodeAsync("meridian", EpisodeStatus.Stopped, "Test complete");

        TrajectoryEpisode episode = Assert.Single(await store.GetRecentEpisodesAsync("meridian"));
        Assert.Equal("Test complete", episode.EndReason);
        Assert.Equal(2, episode.StepCount);
        Assert.Equal(2, episode.VerifiedStepCount);
        Assert.Equal(1, episode.PilotDecisionCount);
        Assert.Equal(1, episode.PolicyDecisionCount);
        Assert.Equal(2, episode.SectorsVisited);
        Assert.Equal(769, episode.NetWorthDelta);
        Assert.Equal(2, episode.TurnsUsed);
        Assert.False(episode.ShipLost);

        IReadOnlyList<TrajectoryStep> steps = await store.GetStepsAsync(episode.EpisodeId);
        Assert.Equal(2, steps.Count);
        Assert.Equal("interaction-1", steps[0].InteractionId);
        Assert.Equal(TrajectoryDecisionSource.Pilot, steps[0].Source);
        Assert.Equal(551, steps[0].TargetSector);
        Assert.True(steps[0].Reward!.NewSector);
        Assert.Equal(-197, steps[0].Reward!.CreditsDelta);
        Assert.Equal(966, steps[1].Reward!.CreditsDelta);
        Assert.Equal(551, steps[1].After!.CurrentSector);
        Assert.Equal(episode.TotalReward, steps.Sum(step => step.Reward!.Total), precision: 4);
    }

    [Fact]
    public async Task RecorderLeavesStepUnverifiedWhenNoFreshSnapshotFollows()
    {
        using var database = new TemporaryDatabase();
        var store = new SqliteTrajectoryStore(database.ConnectionString);
        var recorder = new TrajectoryRecorder(store, NullLogger<TrajectoryRecorder>.Instance);

        await recorder.RecordDecisionAsync(
            "meridian",
            "qwen3:8b",
            Situation(Snapshot(551, credits: 5_555, turns: 999, at: BaseTime)),
            new AutonomousTestActionDecision(AutonomousTestAction.None, "Hold."),
            TrajectoryDecisionSource.Policy,
            null);
        await recorder.EndEpisodeAsync("meridian", EpisodeStatus.Stopped, "Stopped");

        TrajectoryEpisode episode = Assert.Single(await store.GetRecentEpisodesAsync("meridian"));
        TrajectoryStep step = Assert.Single(await store.GetStepsAsync(episode.EpisodeId));
        Assert.False(step.Reward!.Verified);
        Assert.Equal(0, episode.VerifiedStepCount);
        Assert.Equal(0, episode.TotalReward);
    }

    [Fact]
    public async Task RecorderDoesNotThrowWhenPersistenceFails()
    {
        var recorder = new TrajectoryRecorder(new FailingTrajectoryStore(), NullLogger<TrajectoryRecorder>.Instance);

        await recorder.RecordDecisionAsync(
            "meridian",
            "qwen3:8b",
            Situation(Snapshot(1, credits: 100, turns: 10, at: BaseTime)),
            new AutonomousTestActionDecision(AutonomousTestAction.None, "Hold."),
            TrajectoryDecisionSource.Policy,
            null);
        await recorder.EndEpisodeAsync("meridian", EpisodeStatus.Stopped, "Stopped");
    }

    [Fact]
    public void InteractionHistorySurvivesRestartAndMarksInterruptedCalls()
    {
        using var database = new TemporaryDatabase();
        DateTimeOffset startedAt = BaseTime;
        var first = new SqliteLlmInteractionHistoryStore(database.ConnectionString);
        var completed = new LlmInteractionRecord(
            "interaction-1", "meridian", "Pilot", "Ollama", "qwen3:8b", 1,
            startedAt, startedAt.AddSeconds(2), "[system]\nDecide.", """{"action":"None"}""", true, null);
        first.Record(completed with { IsInProgress = true, Response = string.Empty, Succeeded = false });
        first.Record(completed);
        first.RecordAppliedOutcome("interaction-1", "Held at StarDock.");
        first.Record(new LlmInteractionRecord(
            "interaction-2", "meridian", "Pilot", "Ollama", "qwen3:8b", 1,
            startedAt.AddSeconds(5), startedAt.AddSeconds(5), "[system]\nDecide again.", string.Empty, false, null)
        {
            IsInProgress = true
        });

        var restarted = new SqliteLlmInteractionHistoryStore(database.ConnectionString);
        IReadOnlyList<LlmInteractionRecord> history = restarted.GetRecentByRole("MERIDIAN", "pilot");

        Assert.Equal(2, history.Count);
        Assert.Equal("interaction-2", history[0].InteractionId);
        Assert.False(history[0].IsInProgress);
        Assert.Equal("Interrupted before completion.", history[0].Error);
        Assert.Equal("""{"action":"None"}""", history[1].Response);
        Assert.Equal("Held at StarDock.", history[1].AppliedOutcome);
        Assert.True(history[1].Succeeded);
    }

    private static GameSituationReport Situation(GameSnapshot snapshot) =>
        new(
            snapshot.ObservedAt,
            GamePresenceState.Connected,
            GameAuthenticationState.Authenticated,
            GameSynchronizationState.Healthy,
            "Command",
            snapshot.ObservedAt,
            snapshot,
            []);

    private static GameSnapshot Snapshot(
        int sector,
        long credits,
        int turns,
        DateTimeOffset at,
        string shipType = "Merchant Cruiser") =>
        new(sector, credits, turns, new Ship("Meridian One", "Meridian", shipType, sector, 0, at), at)
        {
            HoldsTotal = 20,
            HoldsEmpty = 20,
            FuelOre = 0,
            Organics = 0,
            Equipment = 0
        };

    private sealed class FailingTrajectoryStore : ITrajectoryStore
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            throw new IOException("Disk unavailable.");

        public Task UpsertEpisodeAsync(TrajectoryEpisode episode, CancellationToken cancellationToken = default) =>
            throw new IOException("Disk unavailable.");

        public Task UpsertStepAsync(TrajectoryStep step, CancellationToken cancellationToken = default) =>
            throw new IOException("Disk unavailable.");

        public Task UpsertMilestoneAsync(TrajectoryMilestone milestone, CancellationToken cancellationToken = default) =>
            throw new IOException("Disk unavailable.");

        public Task<TrajectoryEpisode?> GetEpisodeAsync(string episodeId, CancellationToken cancellationToken = default) =>
            throw new IOException("Disk unavailable.");

        public Task<IReadOnlyList<TrajectoryMilestone>> GetMilestonesAsync(
            string episodeId,
            CancellationToken cancellationToken = default) =>
            throw new IOException("Disk unavailable.");

        public Task<IReadOnlyList<TrajectoryEpisode>> GetRecentEpisodesAsync(
            string? agentId = null,
            int limit = 50,
            CancellationToken cancellationToken = default) =>
            throw new IOException("Disk unavailable.");

        public Task<IReadOnlyList<TrajectoryStep>> GetStepsAsync(
            string episodeId,
            CancellationToken cancellationToken = default) =>
            throw new IOException("Disk unavailable.");
    }

    private sealed class TemporaryDatabase : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), $"twbot-trajectory-{Guid.NewGuid():N}.db");

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
