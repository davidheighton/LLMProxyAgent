using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class FreshAdjacentSectorKnowledgeTests
{
    [Fact]
    public async Task ReturnsStoredExplorationAndIntelWithoutPromotingUnknownSectors()
    {
        var observedAt = DateTimeOffset.UtcNow;
        var store = new TestWorldStateStore(
            new Dictionary<int, Sector>
            {
                [2] = new Sector(
                    2,
                    string.Empty,
                    ExplorationStatus.WarpOnly,
                    true,
                    [4],
                    null,
                    [],
                    [],
                    [],
                    observedAt)
            });
        var provider = new FreshAdjacentSectorProvider(store);

        var result = await provider.GetSectorKnowledgeAsync([2, 99, 2]);

        Assert.Equal(
            [
                new AdjacentSectorKnowledge(2, ExplorationStatus.WarpOnly, true, false),
                new AdjacentSectorKnowledge(99, ExplorationStatus.Unknown, false, false)
            ],
            result);
    }

    private sealed class TestWorldStateStore(IReadOnlyDictionary<int, Sector> sectors)
        : IWorldStateStore
    {
        public Task<Sector?> GetSectorAsync(
            int sectorId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            sectors.TryGetValue(sectorId, out var sector);
            return Task.FromResult(sector);
        }

        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task LoadUniverseAsync(
            Universe universe,
            IEnumerable<Sector> sectorObservations,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task SaveUniverseAsync(Universe universe, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Universe?> GetUniverseAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<Sector>> GetSectorsAsync(
            int offset = 0,
            int limit = 500,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpsertSectorAsync(Sector sector, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpsertSectorsAsync(
            IEnumerable<Sector> sectors,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyDictionary<int, IReadOnlyList<int>>> GetWarpGraphAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<IReadOnlyList<int>> GetStarDockSectorIdsAsync(
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task UpsertCorporationAsync(
            Corporation corporation,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<Corporation?> GetCorporationAsync(
            int corporationId,
            CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }
}
