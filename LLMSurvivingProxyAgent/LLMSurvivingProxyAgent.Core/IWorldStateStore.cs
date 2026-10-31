namespace LLMSurvivingProxyAgent.Core;

public interface IWorldStateStore
{
    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task LoadUniverseAsync(
        Universe universe,
        IEnumerable<Sector> sectorObservations,
        CancellationToken cancellationToken = default);
    Task SaveUniverseAsync(Universe universe, CancellationToken cancellationToken = default);
    Task<Universe?> GetUniverseAsync(CancellationToken cancellationToken = default);
    Task<Sector?> GetSectorAsync(int sectorId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<Sector>> GetSectorsAsync(
        int offset = 0,
        int limit = 500,
        CancellationToken cancellationToken = default);
    Task UpsertSectorAsync(Sector sector, CancellationToken cancellationToken = default);
    Task UpsertSectorsAsync(
        IEnumerable<Sector> sectors,
        CancellationToken cancellationToken = default);
    Task<IReadOnlyDictionary<int, IReadOnlyList<int>>> GetWarpGraphAsync(
        CancellationToken cancellationToken = default);
    Task<IReadOnlyList<int>> GetStarDockSectorIdsAsync(
        CancellationToken cancellationToken = default);
    Task UpsertCorporationAsync(
        Corporation corporation,
        CancellationToken cancellationToken = default);
    Task<Corporation?> GetCorporationAsync(
        int corporationId,
        CancellationToken cancellationToken = default);
}
