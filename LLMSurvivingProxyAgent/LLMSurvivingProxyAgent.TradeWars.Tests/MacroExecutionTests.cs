using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Knowledge;
using LLMSurvivingProxyAgent.Macros;
using LLMSurvivingProxyAgent.Runtime;
using LLMSurvivingProxyAgent.TradeWars;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class MacroExecutionTests
{
    [Fact]
    public async Task FindNearestStardockProducesAndPersistsNavigationPlan()
    {
        await using var fixture = await MacroFixture.CreateAsync();
        await fixture.World.SaveUniverseAsync(new Universe(4, 4, DateTimeOffset.UtcNow));
        await fixture.World.UpsertSectorsAsync(
        [
            CreateSector(1, [2], ExplorationStatus.Explored, true),
            CreateSector(2, [4], ExplorationStatus.Explored, true),
            CreateSector(4, [], ExplorationStatus.Explored, true)
        ]);
        var engine = fixture.CreateEngine(new FindNearestStardockMacro());

        var execution = await engine.ExecuteAsync(
            "meridian",
            "find-nearest-stardock",
            1,
            fixture.World,
            fixture.Navigation);
        var persisted = Assert.Single(await fixture.Telemetry.GetRecentAsync("meridian"));

        Assert.Equal(MacroExecutionStatus.Planned, execution.Status);
        Assert.Equal(new[] { 1, 2, 4 }, execution.PlanSectors);
        Assert.True(execution.EndTime >= execution.StartTime);
        Assert.Equal(execution.ExecutionId, persisted.ExecutionId);
        Assert.Equal(execution.StartTime, persisted.StartTime);
        Assert.Equal(execution.EndTime, persisted.EndTime);
        Assert.Equal(execution.Status, persisted.Status);
        Assert.Equal(execution.Result, persisted.Result);
        Assert.Equal(execution.Notes, persisted.Notes);
        Assert.Equal(execution.PlanSectors, persisted.PlanSectors);
        var experienceDocument = Assert.Single(await fixture.Knowledge.GetAgentDocumentsAsync("meridian"));
        var experienceNode = Assert.Single(experienceDocument.Nodes);
        Assert.Equal(KnowledgeCategory.AgentExperience, experienceNode.Category);
        var experience = Assert.IsType<AgentExperienceEvidence>(
            experienceNode.Evidence[0].Details?.AgentExperience);
        Assert.Equal(execution.ExecutionId, experience.ExperienceId);
        Assert.Equal(nameof(MacroExecutionStatus.Planned), experience.Status);
        Assert.False(experience.GameOutcomeMeasured);
        Assert.Contains("does not establish", experienceNode.Evidence[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task MacroExperienceRecordsSynchronizedSnapshotsAsObservationsOnly()
    {
        await using var fixture = await MacroFixture.CreateAsync();
        await fixture.World.SaveUniverseAsync(new Universe(4, 4, DateTimeOffset.UtcNow));
        await fixture.World.UpsertSectorsAsync(
        [
            CreateSector(1, [2], ExplorationStatus.Explored, true),
            CreateSector(2, [4], ExplorationStatus.Explored, true),
            CreateSector(4, [], ExplorationStatus.Explored, true)
        ]);
        var beforeSnapshot = new GameSnapshot(
            1,
            100_000,
            500,
            null,
            DateTimeOffset.UtcNow.AddSeconds(-1));
        var snapshotProvider = new SequenceSnapshotProvider(
            beforeSnapshot,
            () => new GameSnapshot(1, 100_250, 499, null, DateTimeOffset.UtcNow));
        var engine = fixture.CreateEngine(snapshotProvider, new FindNearestStardockMacro());

        await engine.ExecuteAsync(
            "meridian",
            "find-nearest-stardock",
            1,
            fixture.World,
            fixture.Navigation);

        var experienceNode = Assert.Single(
            Assert.Single(await fixture.Knowledge.GetAgentDocumentsAsync("meridian")).Nodes);
        var experience = Assert.IsType<AgentExperienceEvidence>(
            experienceNode.Evidence[0].Details?.AgentExperience);
        Assert.Equal(beforeSnapshot, experience.BeforeSnapshot);
        Assert.Equal(100_250, experience.AfterSnapshot?.SynchronizedCredits);
        Assert.False(experience.GameOutcomeMeasured);
        Assert.Contains("observations only", experienceNode.Evidence[0].Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task PostSnapshotFailureIsSurfacedAfterMacroTelemetryIsPersisted()
    {
        await using var fixture = await MacroFixture.CreateAsync();
        await fixture.World.SaveUniverseAsync(new Universe(4, 4, DateTimeOffset.UtcNow));
        await fixture.World.UpsertSectorsAsync(
        [
            CreateSector(1, [2], ExplorationStatus.Explored, true),
            CreateSector(2, [4], ExplorationStatus.Explored, true),
            CreateSector(4, [], ExplorationStatus.Explored, true)
        ]);
        var snapshotProvider = new FailingAfterSnapshotProvider(
            new GameSnapshot(1, 100_000, 500, null, DateTimeOffset.UtcNow.AddSeconds(-1)));
        var engine = fixture.CreateEngine(snapshotProvider, new FindNearestStardockMacro());

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.ExecuteAsync(
                "meridian",
                "find-nearest-stardock",
                1,
                fixture.World,
                fixture.Navigation));

        Assert.Contains("post-execution synchronized snapshot", exception.Message, StringComparison.Ordinal);
        var execution = Assert.Single(await fixture.Telemetry.GetRecentAsync("meridian"));
        Assert.Equal(MacroExecutionStatus.Planned, execution.Status);
        Assert.Contains("IOException", execution.Notes, StringComparison.Ordinal);
        var experienceNode = Assert.Single(
            Assert.Single(await fixture.Knowledge.GetAgentDocumentsAsync("meridian")).Nodes);
        var experience = Assert.IsType<AgentExperienceEvidence>(
            experienceNode.Evidence[0].Details?.AgentExperience);
        Assert.NotNull(experience.BeforeSnapshot);
        Assert.Null(experience.AfterSnapshot);
        Assert.False(experience.GameOutcomeMeasured);
    }

    [Fact]
    public async Task TurnExperienceRequiresAndPreservesOneHundredConsecutiveObservations()
    {
        await using var fixture = await MacroFixture.CreateAsync();
        var recorder = new AgentExperienceKnowledgeRecorder(fixture.Knowledge);
        var sessionId = "meridian-alpha-1";
        var firstObservedAt = DateTimeOffset.UtcNow.AddMinutes(-20);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            recorder.RecordTurnObservationAsync(
                "meridian",
                sessionId,
                2,
                new GameSnapshot(12, 20_000, 500, null, firstObservedAt),
                new GameSnapshot(12, 20_000, 499, null, firstObservedAt.AddSeconds(1))));

        for (var turnNumber = 1; turnNumber <= 100; turnNumber++)
        {
            if (turnNumber == 51)
            {
                recorder = new AgentExperienceKnowledgeRecorder(fixture.Knowledge);
            }

            var beforeObservedAt = firstObservedAt.AddSeconds((turnNumber - 1) * 2);
            await recorder.RecordTurnObservationAsync(
                "meridian",
                sessionId,
                turnNumber,
                new GameSnapshot(12, 20_000, 501 - turnNumber, null, beforeObservedAt),
                new GameSnapshot(12, 20_000, 500 - turnNumber, null, beforeObservedAt.AddSeconds(1)));
        }

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            new AgentExperienceKnowledgeRecorder(fixture.Knowledge).RecordTurnObservationAsync(
                "meridian",
                sessionId,
                100,
                new GameSnapshot(12, 20_000, 400, null, firstObservedAt.AddMinutes(10)),
                new GameSnapshot(12, 20_000, 399, null, firstObservedAt.AddMinutes(10).AddSeconds(1))));

        var sessionDocuments = (await fixture.Knowledge.GetAgentDocumentsAsync("meridian"))
            .Where(document => document.RootId == "agent-experience-turns")
            .ToArray();
        var observations = sessionDocuments
            .SelectMany(document => document.Nodes)
            .SelectMany(node => node.Evidence)
            .Select(evidence => Assert.IsType<AgentExperienceEvidence>(
                evidence.Details?.AgentExperience))
            .OrderBy(experience => experience.TurnNumber)
            .ToArray();
        Assert.Equal(100, sessionDocuments.Length);
        Assert.Equal(100, observations.Length);
        Assert.Equal(
            Enumerable.Range(1, 100),
            observations.Select(experience => experience.TurnNumber!.Value));
        Assert.All(observations, experience =>
        {
            Assert.Equal("TurnObservation", experience.EventKind);
            Assert.Equal(sessionId, experience.SessionId);
            Assert.False(experience.GameOutcomeMeasured);
        });
        var recentExperience = await fixture.Knowledge.SearchLayerForAgentAsync(
            "meridian",
            null,
            KnowledgeLayer.Experience,
            limit: 4);
        Assert.Equal(
            new[] { 100, 99, 98, 97 },
            recentExperience.Select(node =>
                node.Evidence[0].Details?.AgentExperience?.TurnNumber ?? 0));
    }

    [Fact]
    public async Task SituationExperienceIsPersistedOnlyForOwningAgentFromTrustedReport()
    {
        await using var fixture = await MacroFixture.CreateAsync();
        var report = CreateTrustedSituationReport();
        var recorder = new AgentExperienceKnowledgeRecorder(fixture.Knowledge);

        await recorder.RecordSituationObservationAsync("meridian", "alpha-1a", report);

        var document = Assert.Single(
            await fixture.Knowledge.GetAgentDocumentsAsync("meridian"),
            item => item.RootId == "agent-experience-situations");
        Assert.Equal("meridian", document.OwnerAgentId);
        Assert.Empty(await fixture.Knowledge.GetAgentDocumentsAsync("another-agent"));
        var experience = Assert.IsType<AgentExperienceEvidence>(
            Assert.Single(Assert.Single(document.Nodes).Evidence).Details?.AgentExperience);
        Assert.Equal("SituationObservation", experience.EventKind);
        Assert.Equal("Observed", experience.Status);
        Assert.Equal("alpha-1a", experience.SessionId);
        Assert.Null(experience.BeforeSnapshot);
        Assert.Equal(report.Snapshot, experience.AfterSnapshot);
        var storedReport = Assert.IsType<GameSituationReport>(experience.SituationReport);
        Assert.Equal(report.CapturedAt, storedReport.CapturedAt);
        Assert.Equal(report.Presence, storedReport.Presence);
        Assert.Equal(report.Authentication, storedReport.Authentication);
        Assert.Equal(report.Synchronization, storedReport.Synchronization);
        Assert.Equal(report.UnknownFields, storedReport.UnknownFields);
        Assert.False(experience.GameOutcomeMeasured);
    }

    [Fact]
    public async Task SituationObservationServiceRejectsUnverifiedStateWithoutRecording()
    {
        var provider = new FixedSituationProvider(new GameSituationReport(
            DateTimeOffset.UtcNow,
            GamePresenceState.Stale,
            GameAuthenticationState.Unverified,
            GameSynchronizationState.Unavailable,
            null,
            null,
            null,
            ["Current sector", "Credits"]));
        var recorder = new CountingExperienceRecorder();
        var service = new GameSituationObservationService(provider, recorder);

        var result = await service.CaptureAndRecordAsync("meridian", "alpha-1a");

        Assert.False(result.Recorded);
        Assert.Contains("Presence is Stale", result.RejectionReason, StringComparison.Ordinal);
        Assert.Equal(0, recorder.SituationObservationCount);
    }

    private static GameSituationReport CreateTrustedSituationReport()
    {
        var observedAt = DateTimeOffset.UtcNow;
        var snapshot = new GameSnapshot(12, 100_000, 500, null, observedAt)
        {
            Authenticated = true,
            CurrentPrompt = "Command",
            CurrentPromptObservedAt = observedAt
        };
        return new GameSituationReport(
            observedAt.AddMilliseconds(10),
            GamePresenceState.Connected,
            GameAuthenticationState.Authenticated,
            GameSynchronizationState.Healthy,
            "Command",
            observedAt,
            snapshot,
            []);
    }

    private sealed class FixedSituationProvider(GameSituationReport report) : IGameSituationProvider
    {
        public Task<GameSituationReport> CaptureSituationAsync(
            string agentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(report);
    }

    private sealed class CountingExperienceRecorder : IAgentExperienceRecorder
    {
        public int SituationObservationCount { get; private set; }

        public Task RecordMacroExecutionAsync(
            MacroExecutionRecord record,
            GameSnapshot? beforeSnapshot = null,
            GameSnapshot? afterSnapshot = null,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RecordTurnObservationAsync(
            string agentId,
            string sessionId,
            int turnNumber,
            GameSnapshot beforeSnapshot,
            GameSnapshot afterSnapshot,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task RecordSituationObservationAsync(
            string agentId,
            string sessionId,
            GameSituationReport report,
            CancellationToken cancellationToken = default)
        {
            SituationObservationCount++;
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PairPortTradePlansOnlyWhenProfilesAndRoutesAreKnown()
    {
        await using var fixture = await MacroFixture.CreateAsync();
        await fixture.World.SaveUniverseAsync(new Universe(2, null, DateTimeOffset.UtcNow));
        await fixture.World.UpsertSectorsAsync(
        [
            CreateSector(1, [2], ExplorationStatus.Explored, true) with
            {
                Port = PortWithProfile(
                    1,
                    (TradeProduct.FuelOre, true),
                    (TradeProduct.Organics, true),
                    (TradeProduct.Equipment, false))
            },
            CreateSector(2, [1], ExplorationStatus.Explored, true) with
            {
                Port = PortWithProfile(
                    2,
                    (TradeProduct.FuelOre, true),
                    (TradeProduct.Organics, false),
                    (TradeProduct.Equipment, true))
            }
        ]);
        var engine = fixture.CreateEngine(new PairPortTradeMacro());

        var execution = await engine.ExecuteAsync(
            "meridian",
            "pair-port-trade",
            1,
            fixture.World,
            fixture.Navigation);

        Assert.Equal(MacroExecutionStatus.Planned, execution.Status);
        Assert.Equal(new[] { 1, 2, 1 }, execution.PlanSectors);
        Assert.Contains("no trade commands were sent", execution.Notes, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PairPortTradePlansSingleCommodityPairAcrossMultipleHops()
    {
        await using var fixture = await MacroFixture.CreateAsync();
        await fixture.World.SaveUniverseAsync(new Universe(3, null, DateTimeOffset.UtcNow));
        await fixture.World.UpsertSectorsAsync(
        [
            CreateSector(1, [2], ExplorationStatus.Explored, true) with
            {
                Port = PortWithProfile(
                    1,
                    (TradeProduct.FuelOre, false),
                    (TradeProduct.Organics, false),
                    (TradeProduct.Equipment, false))
            },
            CreateSector(2, [1, 3], ExplorationStatus.Explored, true),
            CreateSector(3, [2], ExplorationStatus.Explored, true) with
            {
                Port = PortWithProfile(
                    3,
                    (TradeProduct.FuelOre, true),
                    (TradeProduct.Organics, false),
                    (TradeProduct.Equipment, false))
            }
        ]);
        var engine = fixture.CreateEngine(new PairPortTradeMacro());

        var execution = await engine.ExecuteAsync(
            "meridian",
            "pair-port-trade",
            1,
            fixture.World,
            fixture.Navigation);

        Assert.Equal(MacroExecutionStatus.Planned, execution.Status);
        Assert.Equal(new[] { 1, 2, 3, 2, 1 }, execution.PlanSectors);
    }

    [Fact]
    public async Task TradePreflightFindsFreshCompatiblePairBeforeStarDockWithoutExecutingTrades()
    {
        await using var fixture = await MacroFixture.CreateAsync();
        await fixture.World.SaveUniverseAsync(new Universe(4, 4, DateTimeOffset.UtcNow));
        await fixture.World.UpsertSectorsAsync(CreatePreflightSectors());
        var macro = new TradePreflightMacro();

        var execution = await macro.ExecuteAsync(
            new MacroContext("meridian", 1, fixture.World, fixture.Navigation));

        Assert.Equal(MacroExecutionStatus.Planned, execution.Status);
        Assert.Equal(new[] { 1, 2, 3, 4 }, execution.PlanSectors);
        Assert.Contains("sector 2 sells FuelOre", execution.Summary, StringComparison.Ordinal);
        Assert.Contains("sector 3 buys FuelOre", execution.Summary, StringComparison.Ordinal);
        Assert.Contains("possible pre-dock unload stop", execution.Summary, StringComparison.Ordinal);
        Assert.Contains("no live quote", execution.Notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("cargo manifest", execution.Notes, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("No game commands were sent", execution.Notes, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TradePreflightRejectsStalePortProfiles()
    {
        await using var fixture = await MacroFixture.CreateAsync();
        await fixture.World.SaveUniverseAsync(new Universe(4, 4, DateTimeOffset.UtcNow));
        var sectors = CreatePreflightSectors().ToArray();
        sectors[2] = sectors[2] with
        {
            Port = sectors[2].Port! with { ObservedAt = DateTimeOffset.UtcNow.AddMinutes(-16) }
        };
        await fixture.World.UpsertSectorsAsync(sectors);

        var execution = await new TradePreflightMacro().ExecuteAsync(
            new MacroContext("meridian", 1, fixture.World, fixture.Navigation));

        Assert.Equal(MacroExecutionStatus.Blocked, execution.Status);
        Assert.Empty(execution.PlanSectors);
    }

    [Fact]
    public async Task TradePreflightRejectsStaleWarpEvidence()
    {
        await using var fixture = await MacroFixture.CreateAsync();
        await fixture.World.SaveUniverseAsync(new Universe(4, 4, DateTimeOffset.UtcNow));
        var sectors = CreatePreflightSectors().ToArray();
        var staleWarpObservation = DateTimeOffset.UtcNow.AddMinutes(-16);
        sectors[1] = sectors[1] with
        {
            ObservedAt = staleWarpObservation,
            WarpObservedAt = staleWarpObservation
        };
        await fixture.World.UpsertSectorsAsync(sectors);

        var execution = await new TradePreflightMacro().ExecuteAsync(
            new MacroContext("meridian", 1, fixture.World, fixture.Navigation));

        Assert.Equal(MacroExecutionStatus.Blocked, execution.Status);
        Assert.Empty(execution.PlanSectors);
    }

    [Fact]
    public async Task TradePreflightRejectsOneWayPortPairs()
    {
        await using var fixture = await MacroFixture.CreateAsync();
        await fixture.World.SaveUniverseAsync(new Universe(4, 4, DateTimeOffset.UtcNow));
        var sectors = CreatePreflightSectors().ToArray();
        sectors[2] = sectors[2] with { Warps = [4] };
        await fixture.World.UpsertSectorsAsync(sectors);

        var execution = await new TradePreflightMacro().ExecuteAsync(
            new MacroContext("meridian", 1, fixture.World, fixture.Navigation));

        Assert.Equal(MacroExecutionStatus.Blocked, execution.Status);
        Assert.Empty(execution.PlanSectors);
    }

    [Fact]
    public async Task TradePreflightRejectsAProfilePairWithoutSellToBuyDirection()
    {
        await using var fixture = await MacroFixture.CreateAsync();
        await fixture.World.SaveUniverseAsync(new Universe(4, 4, DateTimeOffset.UtcNow));
        var sectors = CreatePreflightSectors().ToArray();
        sectors[1] = sectors[1] with
        {
            Port = PortWithProfile(
                2,
                (TradeProduct.FuelOre, true),
                (TradeProduct.Organics, true),
                (TradeProduct.Equipment, true))
        };
        sectors[2] = sectors[2] with
        {
            Port = PortWithProfile(
                3,
                (TradeProduct.FuelOre, false),
                (TradeProduct.Organics, false),
                (TradeProduct.Equipment, false))
        };
        await fixture.World.UpsertSectorsAsync(sectors);

        var execution = await new TradePreflightMacro().ExecuteAsync(
            new MacroContext("meridian", 1, fixture.World, fixture.Navigation));

        Assert.Equal(MacroExecutionStatus.Blocked, execution.Status);
        Assert.Empty(execution.PlanSectors);
    }

    [Fact]
    public async Task TradePreflightNeverTreatsStarDockAsTheUnloadPort()
    {
        await using var fixture = await MacroFixture.CreateAsync();
        await fixture.World.SaveUniverseAsync(new Universe(3, 3, DateTimeOffset.UtcNow));
        await fixture.World.UpsertSectorsAsync(
        [
            CreateSector(1, [2], ExplorationStatus.Explored, true),
            CreateSector(2, [3], ExplorationStatus.Explored, true) with
            {
                Port = PortWithProfile(
                    2,
                    (TradeProduct.FuelOre, false),
                    (TradeProduct.Organics, true),
                    (TradeProduct.Equipment, true))
            },
            CreateSector(3, [], ExplorationStatus.Explored, true) with
            {
                Port = new Port(
                    3,
                    "StarDock",
                    9,
                    false,
                    0,
                    new Dictionary<TradeProduct, PortProduct>
                    {
                        [TradeProduct.FuelOre] = new(true, 100, 50),
                        [TradeProduct.Organics] = new(false, 100, 50),
                        [TradeProduct.Equipment] = new(false, 100, 50)
                    },
                    DateTimeOffset.UtcNow)
            }
        ]);

        var execution = await new TradePreflightMacro().ExecuteAsync(
            new MacroContext("meridian", 1, fixture.World, fixture.Navigation));

        Assert.Equal(MacroExecutionStatus.Blocked, execution.Status);
        Assert.Empty(execution.PlanSectors);
    }

    [Fact]
    public async Task RefreshCimIsRecordedAsBlockedWithoutLiveSession()
    {
        await using var fixture = await MacroFixture.CreateAsync();
        await fixture.World.SaveUniverseAsync(new Universe(2, null, DateTimeOffset.UtcNow));
        var engine = fixture.CreateEngine(new RefreshCimMacro());

        var execution = await engine.ExecuteAsync(
            "meridian",
            "refresh-cim",
            null,
            fixture.World,
            fixture.Navigation);

        Assert.Equal(MacroExecutionStatus.Blocked, execution.Status);
        Assert.Contains("not configured", execution.Notes, StringComparison.OrdinalIgnoreCase);
        Assert.Single(await fixture.Telemetry.GetRecentAsync());
    }

    [Fact]
    public async Task MacroExceptionIsRethrownAndRecordedAsFailed()
    {
        await using var fixture = await MacroFixture.CreateAsync();
        var engine = fixture.CreateEngine(new FailingMacro());

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            engine.ExecuteAsync(
                "meridian",
                "failing-test",
                null,
                fixture.World,
                fixture.Navigation));

        var record = Assert.Single(await fixture.Telemetry.GetRecentAsync());
        Assert.Equal(MacroExecutionStatus.Failed, record.Status);
        Assert.Equal("controlled failure", record.Result);
        var experienceNode = Assert.Single(
            Assert.Single(await fixture.Knowledge.GetAgentDocumentsAsync("meridian")).Nodes);
        Assert.Equal(KnowledgeCategory.AgentExperience, experienceNode.Category);
        Assert.Equal(
            nameof(MacroExecutionStatus.Failed),
            experienceNode.Evidence[0].Details?.AgentExperience?.Status);
    }

    private static Sector CreateSector(
        int id,
        IReadOnlyList<int> warps,
        ExplorationStatus exploration,
        bool warpDataKnown)
    {
        var observedAt = DateTimeOffset.UtcNow;
        return new Sector(
            id,
            $"Sector {id}",
            exploration,
            warpDataKnown,
            warps,
            null,
            [],
            [],
            [],
            observedAt)
        {
            WarpObservedAt = warpDataKnown ? observedAt : null
        };
    }

    private static Port PortWithProfile(
        int sectorId,
        params (TradeProduct Product, bool Buys)[] profile) =>
        new(
            sectorId,
            $"Port {sectorId}",
            1,
            false,
            0,
            profile.ToDictionary(
                item => item.Product,
                item => new PortProduct(item.Buys, 100, 50)),
            DateTimeOffset.UtcNow);

    private static IReadOnlyList<Sector> CreatePreflightSectors() =>
    [
        CreateSector(1, [2], ExplorationStatus.Explored, true),
        CreateSector(2, [3], ExplorationStatus.Explored, true) with
        {
            Port = PortWithProfile(
                2,
                (TradeProduct.FuelOre, false),
                (TradeProduct.Organics, true),
                (TradeProduct.Equipment, true))
        },
        CreateSector(3, [2, 4], ExplorationStatus.Explored, true) with
        {
            Port = PortWithProfile(
                3,
                (TradeProduct.FuelOre, true),
                (TradeProduct.Organics, false),
                (TradeProduct.Equipment, false))
        },
        CreateSector(4, [], ExplorationStatus.Explored, true) with
        {
            Port = new Port(
                4,
                "StarDock",
                9,
                false,
                0,
                new Dictionary<TradeProduct, PortProduct>(),
                DateTimeOffset.UtcNow)
        }
    ];

    private sealed class MacroFixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private MacroFixture(
            string databasePath,
            SqliteWorldStateStore world,
            SqliteMacroTelemetryStore telemetry,
            SqliteKnowledgeStore knowledge)
        {
            _databasePath = databasePath;
            World = world;
            Navigation = new NavigationService(world);
            Telemetry = telemetry;
            Knowledge = knowledge;
            ExperienceRecorder = new AgentExperienceKnowledgeRecorder(knowledge);
        }

        public SqliteWorldStateStore World { get; }
        public NavigationService Navigation { get; }
        public SqliteMacroTelemetryStore Telemetry { get; }
        public SqliteKnowledgeStore Knowledge { get; }
        private AgentExperienceKnowledgeRecorder ExperienceRecorder { get; }

        public static async Task<MacroFixture> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"macro-{Guid.NewGuid():N}.db");
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false
            }.ToString();
            var world = new SqliteWorldStateStore(connectionString);
            var telemetry = new SqliteMacroTelemetryStore(connectionString);
            var knowledge = new SqliteKnowledgeStore(connectionString);
            await world.InitializeAsync();
            await telemetry.InitializeAsync();
            await knowledge.InitializeAsync();
            return new MacroFixture(path, world, telemetry, knowledge);
        }

        public MacroExecutionEngine CreateEngine(params IMacro[] macros) =>
            new(new MacroRegistry(macros), Telemetry, ExperienceRecorder);

        public MacroExecutionEngine CreateEngine(
            ISynchronizedGameSnapshotProvider snapshotProvider,
            params IMacro[] macros) =>
            new(new MacroRegistry(macros), Telemetry, ExperienceRecorder, snapshotProvider);

        public ValueTask DisposeAsync()
        {
            foreach (var suffix in new[] { "", "-shm", "-wal" })
            {
                var path = _databasePath + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            return ValueTask.CompletedTask;
        }
    }

    private sealed class SequenceSnapshotProvider(
        GameSnapshot beforeSnapshot,
        Func<GameSnapshot> createAfterSnapshot) : ISynchronizedGameSnapshotProvider
    {
        private int _captureCount;

        public Task<GameSnapshot?> CaptureSynchronizedSnapshotAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var snapshot = _captureCount++ == 0 ? beforeSnapshot : createAfterSnapshot();
            return Task.FromResult<GameSnapshot?>(snapshot);
        }
    }

    private sealed class FailingAfterSnapshotProvider(
        GameSnapshot beforeSnapshot) : ISynchronizedGameSnapshotProvider
    {
        private int _captureCount;

        public Task<GameSnapshot?> CaptureSynchronizedSnapshotAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (_captureCount++ == 0)
            {
                return Task.FromResult<GameSnapshot?>(beforeSnapshot);
            }

            throw new IOException("controlled snapshot failure");
        }
    }

    private sealed class FailingMacro : IMacro
    {
        public MacroDescriptor Descriptor { get; } = new(
            "failing-test",
            "Failing test macro",
            "Used to verify failed-execution telemetry.",
            MacroCategory.Recovery,
            [],
            "A failure record.");

        public Task<MacroCheck> CanExecuteAsync(
            MacroContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(new MacroCheck(true, string.Empty));

        public Task<string> EstimateOutcomeAsync(
            MacroContext context,
            CancellationToken cancellationToken = default) =>
            Task.FromResult("test estimate");

        public Task<MacroResult> ExecuteAsync(
            MacroContext context,
            CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("controlled failure");
    }
}
