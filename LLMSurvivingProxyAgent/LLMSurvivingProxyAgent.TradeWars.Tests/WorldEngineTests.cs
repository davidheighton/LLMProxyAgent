using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;
using LLMSurvivingProxyAgent.TradeWars;
using Microsoft.Data.Sqlite;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class WorldEngineTests
{
    [Fact]
    public async Task NavigationFindsShortestDirectedPathAndHonorsAvoidList()
    {
        await using var fixture = await WorldFixture.CreateAsync();
        await fixture.Store.SaveUniverseAsync(new Universe(8, null, DateTimeOffset.UtcNow));
        await fixture.Store.UpsertSectorsAsync(
        [
            CreateSector(1, [2, 3], ExplorationStatus.Explored, true),
            CreateSector(2, [4], ExplorationStatus.Explored, true),
            CreateSector(3, [4], ExplorationStatus.Explored, true),
            CreateSector(4, [5], ExplorationStatus.Explored, true),
            CreateSector(5, [], ExplorationStatus.Explored, true)
        ]);

        var shortest = await fixture.Navigation.FindShortestPathAsync(1, 5);
        var avoidingTwo = await fixture.Navigation.FindShortestPathAsync(1, 5, new HashSet<int> { 2 });
        var reverse = await fixture.Navigation.FindShortestPathAsync(5, 1);

        Assert.Equal(new[] { 1, 2, 4, 5 }, shortest?.Sectors);
        Assert.Equal(new[] { 1, 3, 4, 5 }, avoidingTwo?.Sectors);
        Assert.Null(reverse);
    }

    [Fact]
    public async Task UniverseLoadSeedsAllSectorsAndRollsBackInvalidObservations()
    {
        await using var fixture = await WorldFixture.CreateAsync();

        await fixture.Store.LoadUniverseAsync(
            new Universe(3, null, DateTimeOffset.UtcNow),
            [CreateSector(1, [2], ExplorationStatus.WarpOnly, true)]);
        var sectors = await fixture.Store.GetSectorsAsync(limit: 10);

        Assert.Equal(3, sectors.Count);
        Assert.Equal(new[] { 2 }, (await fixture.Store.GetSectorAsync(1))?.Warps);
        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            fixture.Store.LoadUniverseAsync(
                new Universe(2, null, DateTimeOffset.UtcNow),
                [CreateSector(3, [], ExplorationStatus.Explored, true)]));

        Assert.Equal(3, (await fixture.Store.GetUniverseAsync())?.SectorCount);
        Assert.NotNull(await fixture.Store.GetSectorAsync(3));
    }

    [Fact]
    public async Task NearestStarDockUsesPortDataWhenUniverseHeaderIsUnknown()
    {
        await using var fixture = await WorldFixture.CreateAsync();
        await fixture.Store.SaveUniverseAsync(new Universe(8, null, DateTimeOffset.UtcNow));
        await fixture.Store.UpsertSectorsAsync(
        [
            CreateSector(1, [2, 3], ExplorationStatus.Explored, true),
            CreateSector(2, [5], ExplorationStatus.Explored, true),
            CreateSector(3, [6], ExplorationStatus.Explored, true),
            CreateSector(5, [6], ExplorationStatus.Explored, true),
            CreateSector(6, [], ExplorationStatus.Explored, true) with
            {
                Port = new Port(6, "StarDock", 9, false, 0, new Dictionary<TradeProduct, PortProduct>(), DateTimeOffset.UtcNow)
            }
        ]);

        var path = await fixture.Navigation.FindNearestStarDockAsync(1);

        Assert.Equal(new[] { 1, 3, 6 }, path?.Sectors);
    }

    [Fact]
    public async Task AdjacentQueryReturnsUnknownAndWarpOnlyButNotExploredSectors()
    {
        await using var fixture = await WorldFixture.CreateAsync();
        await fixture.Store.SaveUniverseAsync(new Universe(4, null, DateTimeOffset.UtcNow));
        await fixture.Store.UpsertSectorsAsync(
        [
            CreateSector(1, [2, 3, 4], ExplorationStatus.Explored, true),
            CreateSector(2, [], ExplorationStatus.Unknown, false),
            CreateSector(3, [], ExplorationStatus.WarpOnly, false),
            CreateSector(4, [], ExplorationStatus.Explored, false)
        ]);

        var adjacent = await fixture.Navigation.FindUnexploredAdjacentSectorsAsync(1);

        Assert.Equal(new[] { 2, 3 }, adjacent.Select(sector => sector.Id));
    }

    [Fact]
    public async Task AutonomousMovementCandidatesRequireFreshKnownOutgoingWarps()
    {
        await using var fixture = await WorldFixture.CreateAsync();
        await fixture.Store.SaveUniverseAsync(new Universe(4, null, DateTimeOffset.UtcNow));
        await fixture.Store.UpsertSectorAsync(
            CreateSector(1, [3, 2], ExplorationStatus.Explored, true));
        var provider = new FreshAdjacentSectorProvider(fixture.Store);

        Assert.Equal([2, 3], await provider.GetFreshAdjacentSectorIdsAsync(1));

        await fixture.Store.UpsertSectorAsync(
            CreateSector(1, [2, 3], ExplorationStatus.Explored, true) with
            {
                ObservedAt = DateTimeOffset.UtcNow.AddMinutes(-16)
            });

        Assert.Empty(await provider.GetFreshAdjacentSectorIdsAsync(1));
        Assert.Empty(await provider.GetFreshAdjacentSectorIdsAsync(null));
        Assert.Empty(await provider.GetFreshAdjacentSectorIdsAsync(0));
    }

    [Fact]
    public async Task SectorDetailsAndCorporationsSurviveDatabaseReopen()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"world-{Guid.NewGuid():N}.db");
        try
        {
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = filePath,
                Pooling = false
            }.ToString();
            var store = new SqliteWorldStateStore(connectionString);
            await store.InitializeAsync();
            await store.SaveUniverseAsync(new Universe(2, null, DateTimeOffset.UtcNow));
            var port = new Port(
                1,
                "Trade Port",
                1,
                false,
                10,
                new Dictionary<TradeProduct, PortProduct>
                {
                    [TradeProduct.FuelOre] = new(true, 1200, 55)
                },
                DateTimeOffset.UtcNow);
            await store.UpsertSectorAsync(CreateSector(1, [2], ExplorationStatus.Explored, true) with
            {
                Port = port,
                Planets =
                [
                    new Planet(42, 1, "Outpost", "Meridian", 3, true, 150, 700, 200, 50, DateTimeOffset.UtcNow)
                ],
                Traders =
                [
                    new Trader("Meridian", 1, "Merchant Cruiser", "Wayfarer", 30, 7, DateTimeOffset.UtcNow)
                ]
            });
            await store.UpsertCorporationAsync(
                new Corporation(7, "Explorers", ["Meridian"], DateTimeOffset.UtcNow));

            var reopenedStore = new SqliteWorldStateStore(connectionString);
            await reopenedStore.InitializeAsync();
            var sector = await reopenedStore.GetSectorAsync(1);
            var corporation = await reopenedStore.GetCorporationAsync(7);

            Assert.Equal(1200, sector?.Port?.Products[TradeProduct.FuelOre].Amount);
            Assert.NotNull(sector?.WarpObservedAt);
            Assert.Equal("Outpost", Assert.Single(sector!.Planets).Name);
            Assert.Equal("Wayfarer", Assert.Single(sector.Traders).ShipName);
            Assert.Equal("Meridian", Assert.Single(corporation!.Members));
        }
        finally
        {
            foreach (var suffix in new[] { "", "-shm", "-wal" })
            {
                var path = filePath + suffix;
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [Fact]
    public async Task CimImportValidatesRowsAndPreservesExploredState()
    {
        await using var fixture = await WorldFixture.CreateAsync();
        await fixture.Store.SaveUniverseAsync(new Universe(8, null, DateTimeOffset.UtcNow));
        var priorPort = new Port(
            3,
            "Trade Port",
            1,
            true,
            12,
            new Dictionary<TradeProduct, PortProduct>
            {
                [TradeProduct.FuelOre] = new(false, 99, 10)
            },
            DateTimeOffset.UtcNow.AddMinutes(-1));
        await fixture.Store.UpsertSectorAsync(
            CreateSector(3, [], ExplorationStatus.Explored, true) with { Port = priorPort });
        var cim = new CimImportService(fixture.Store);

        var warpReport = await cim.ImportWarpCimAsync(
            ["3 1 2 0 0 0 0", "999 1", "not a sector"],
            DateTimeOffset.UtcNow);
        var portReport = await cim.ImportPortCimAsync(
            [
                "3 - 450 51% 200 42% -65530 30%",
                "4 - 450 51% 200 42% -65530 30%",
                "5 1 101% 2 4% 3 5%"
            ],
            DateTimeOffset.UtcNow);

        Assert.Equal((3, 1, 2), (
            warpReport.LinesReceived,
            warpReport.LinesAccepted,
            warpReport.LinesRejected));
        Assert.Equal((3, 2, 1), (
            portReport.LinesReceived,
            portReport.LinesAccepted,
            portReport.LinesRejected));
        var updatedExploredSector = await fixture.Store.GetSectorAsync(3);
        Assert.Equal(ExplorationStatus.Explored, updatedExploredSector?.Exploration);
        Assert.Equal(new[] { 1, 2 }, updatedExploredSector?.Warps);
        Assert.Equal("Trade Port", updatedExploredSector?.Port?.Name);
        Assert.True(updatedExploredSector?.Port?.IsDead);
        Assert.Equal(12, updatedExploredSector?.Port?.BuildTime);
        Assert.Equal(
            new PortProduct(true, 450, 51),
            updatedExploredSector?.Port?.Products[TradeProduct.FuelOre]);
        Assert.Equal(
            new PortProduct(false, 200, 42),
            updatedExploredSector?.Port?.Products[TradeProduct.Organics]);
        Assert.Equal(
            new PortProduct(true, 65530, 30),
            updatedExploredSector?.Port?.Products[TradeProduct.Equipment]);
        Assert.Equal(ExplorationStatus.Unknown, (await fixture.Store.GetSectorAsync(4))?.Exploration);
        Assert.Equal(2, (await fixture.Store.GetSectorAsync(4))?.Port?.ClassIndex);
    }

    [Fact]
    public async Task PortCimRefreshDoesNotMakeOldWarpEvidenceFresh()
    {
        await using var fixture = await WorldFixture.CreateAsync();
        await fixture.Store.SaveUniverseAsync(new Universe(4, null, DateTimeOffset.UtcNow));
        var cim = new CimImportService(fixture.Store);
        var warpObservedAt = DateTimeOffset.UtcNow.AddMinutes(-20);
        var portObservedAt = DateTimeOffset.UtcNow;

        await cim.ImportWarpCimAsync(["2 3 0 0 0 0 0"], warpObservedAt);
        await cim.ImportPortCimAsync(["2 - 100 50% 100 50% 100 50%"], portObservedAt);
        var sector = await fixture.Store.GetSectorAsync(2);

        Assert.Equal(warpObservedAt, sector?.WarpObservedAt);
        Assert.Equal(portObservedAt, sector?.Port?.ObservedAt);
        Assert.Equal(portObservedAt, sector?.ObservedAt);
    }

    [Fact]
    public void CimPortParserRejectsUnexpectedTrailingTokens()
    {
        var parsed = CimImportService.TryParsePortLine(
            "4 450 51% 200 42% 300 30% unexpected",
            DateTimeOffset.UtcNow,
            out var observation);

        Assert.False(parsed);
        Assert.Null(observation);
    }

    [Fact]
    public async Task CimWarpImportMarksNewSectorsWarpOnly()
    {
        await using var fixture = await WorldFixture.CreateAsync();
        await fixture.Store.SaveUniverseAsync(new Universe(8, null, DateTimeOffset.UtcNow));
        var cim = new CimImportService(fixture.Store);

        var report = await cim.ImportWarpCimAsync(["4 0 2 0 0 0 0"], DateTimeOffset.UtcNow);
        var sector = await fixture.Store.GetSectorAsync(4);

        Assert.Equal(1, report.LinesAccepted);
        Assert.Equal(ExplorationStatus.WarpOnly, sector?.Exploration);
        Assert.True(sector?.WarpDataKnown);
        Assert.Equal(new[] { 2 }, sector?.Warps);
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

    private sealed class WorldFixture : IAsyncDisposable
    {
        private readonly string _databasePath;

        private WorldFixture(string databasePath, SqliteWorldStateStore store)
        {
            _databasePath = databasePath;
            Store = store;
            Navigation = new NavigationService(store);
        }

        public SqliteWorldStateStore Store { get; }
        public NavigationService Navigation { get; }

        public static async Task<WorldFixture> CreateAsync()
        {
            var path = Path.Combine(Path.GetTempPath(), $"world-{Guid.NewGuid():N}.db");
            var connectionString = new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Pooling = false
            }.ToString();
            var store = new SqliteWorldStateStore(connectionString);
            await store.InitializeAsync();
            return new WorldFixture(path, store);
        }

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
}
