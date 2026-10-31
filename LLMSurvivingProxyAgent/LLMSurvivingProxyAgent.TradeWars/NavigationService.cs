using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.TradeWars;

public sealed class NavigationService(IWorldStateStore worldStateStore)
{
    public async Task<NavigationPath?> FindShortestPathAsync(
        int fromSector,
        int toSector,
        IReadOnlySet<int>? avoidSectors = null,
        CancellationToken cancellationToken = default)
    {
        var universe = await worldStateStore.GetUniverseAsync(cancellationToken);
        if (universe is null ||
            !IsWithinUniverse(fromSector, universe.SectorCount) ||
            !IsWithinUniverse(toSector, universe.SectorCount) ||
            avoidSectors?.Contains(fromSector) == true ||
            avoidSectors?.Contains(toSector) == true)
        {
            return null;
        }
        if (fromSector == toSector)
        {
            return new NavigationPath([fromSector]);
        }

        var graph = await worldStateStore.GetWarpGraphAsync(cancellationToken);
        return FindPath(graph, fromSector, toSector, avoidSectors);
    }

    public async Task<NavigationPath?> FindNearestStarDockAsync(
        int fromSector,
        IReadOnlySet<int>? avoidSectors = null,
        CancellationToken cancellationToken = default)
    {
        var universe = await worldStateStore.GetUniverseAsync(cancellationToken);
        if (universe is null || !IsWithinUniverse(fromSector, universe.SectorCount))
        {
            return null;
        }

        var docks = (await worldStateStore.GetStarDockSectorIdsAsync(cancellationToken)).ToHashSet();
        if (universe.StarDockSector is int headerDock)
        {
            docks.Add(headerDock);
        }
        if (docks.Count == 0)
        {
            return null;
        }
        if (avoidSectors?.Contains(fromSector) == true)
        {
            return null;
        }

        var graph = await worldStateStore.GetWarpGraphAsync(cancellationToken);
        var queue = new Queue<int>();
        var previous = new Dictionary<int, int>();
        var visited = new HashSet<int> { fromSector };
        queue.Enqueue(fromSector);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (docks.Contains(current))
            {
                return new NavigationPath(ReconstructPath(previous, fromSector, current));
            }

            if (!graph.TryGetValue(current, out var warps))
            {
                continue;
            }

            foreach (var next in warps.Order())
            {
                if (avoidSectors?.Contains(next) == true || !visited.Add(next))
                {
                    continue;
                }
                previous[next] = current;
                queue.Enqueue(next);
            }
        }

        return null;
    }

    public async Task<IReadOnlyList<Sector>> FindUnexploredAdjacentSectorsAsync(
        int sectorId,
        CancellationToken cancellationToken = default)
    {
        var sector = await worldStateStore.GetSectorAsync(sectorId, cancellationToken);
        if (sector is null || !sector.WarpDataKnown)
        {
            return [];
        }

        var unexplored = new List<Sector>();
        foreach (var neighborId in sector.Warps.Order())
        {
            var neighbor = await worldStateStore.GetSectorAsync(neighborId, cancellationToken);
            if (neighbor is not null && neighbor.Exploration != ExplorationStatus.Explored)
            {
                unexplored.Add(neighbor);
            }
        }
        return unexplored;
    }

    private static NavigationPath? FindPath(
        IReadOnlyDictionary<int, IReadOnlyList<int>> graph,
        int fromSector,
        int toSector,
        IReadOnlySet<int>? avoidSectors)
    {
        var queue = new Queue<int>();
        var previous = new Dictionary<int, int>();
        var visited = new HashSet<int> { fromSector };
        queue.Enqueue(fromSector);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!graph.TryGetValue(current, out var warps))
            {
                continue;
            }

            foreach (var next in warps.Order())
            {
                if (avoidSectors?.Contains(next) == true || !visited.Add(next))
                {
                    continue;
                }
                previous[next] = current;
                if (next == toSector)
                {
                    return new NavigationPath(ReconstructPath(previous, fromSector, toSector));
                }
                queue.Enqueue(next);
            }
        }

        return null;
    }

    private static IReadOnlyList<int> ReconstructPath(
        IReadOnlyDictionary<int, int> previous,
        int fromSector,
        int toSector)
    {
        var path = new List<int> { toSector };
        var current = toSector;
        while (current != fromSector)
        {
            current = previous[current];
            path.Add(current);
        }
        path.Reverse();
        return path;
    }

    private static bool IsWithinUniverse(int sectorId, int sectorCount) =>
        sectorId >= 1 && sectorId <= sectorCount;
}
