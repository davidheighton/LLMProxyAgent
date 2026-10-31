using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Macros;

/// <summary>
/// Builds an offline-only candidate port-pair itinerary on a fresh directed
/// route to StarDock. It does not inspect live trade prompts or issue commands.
/// </summary>
public sealed class TradePreflightMacro : IMacro
{
    private static readonly TimeSpan MaximumObservationAge = TimeSpan.FromMinutes(15);

    private const string Limitations =
        "Candidate compatibility only. Stored port amounts are not live inventory; no live quote, " +
        "cargo manifest, ship holds, synchronized affordability, or profit data is available. " +
        "StarDock is excluded as a commodity port. No game commands were sent.";

    public MacroDescriptor Descriptor { get; } = new(
        "trade-preflight-to-stardock",
        "Trade Preflight Toward StarDock",
        "Finds a fresh, compatible regular-port pair on a directed route to StarDock and identifies a possible pre-dock unload port.",
        MacroCategory.Trading,
        [
            "Current sector and StarDock sector are known",
            "Fresh complete port profiles and fresh directed warp observations are available",
            "A regular port pair has compatible buy/sell products and a directed return path"
        ],
        "A read-only candidate itinerary; compatibility is not a live trade, unload, or profit guarantee.");

    public async Task<MacroCheck> CanExecuteAsync(
        MacroContext context,
        CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(context, cancellationToken).ConfigureAwait(false);
        return plan is null
            ? new MacroCheck(false, "No fresh, complete trade-preflight candidate is available.")
            : new MacroCheck(true, plan.Description);
    }

    public async Task<string> EstimateOutcomeAsync(
        MacroContext context,
        CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(context, cancellationToken).ConfigureAwait(false);
        return plan is null
            ? "No candidate can be estimated without fresh port profiles and directed warp routes."
            : $"{plan.Description} {Limitations}";
    }

    public async Task<MacroResult> ExecuteAsync(
        MacroContext context,
        CancellationToken cancellationToken = default)
    {
        var plan = await BuildPlanAsync(context, cancellationToken).ConfigureAwait(false);
        return plan is null
            ? new MacroResult(
                MacroExecutionStatus.Blocked,
                "No fresh compatible regular-port pair was found on a complete directed route to StarDock.",
                Limitations,
                [])
            : new MacroResult(
                MacroExecutionStatus.Planned,
                $"Candidate itinerary: {string.Join(" -> ", plan.Route)}. {plan.Description}",
                Limitations,
                plan.Route);
    }

    private static async Task<PreflightPlan?> BuildPlanAsync(
        MacroContext context,
        CancellationToken cancellationToken)
    {
        if (context.CurrentSector is not > 0)
        {
            return null;
        }

        var now = DateTimeOffset.UtcNow;
        var universe = await context.WorldStateStore
            .GetUniverseAsync(cancellationToken)
            .ConfigureAwait(false);
        if (universe is null ||
            context.CurrentSector.Value > universe.SectorCount ||
            universe.StarDockSector is not { } dockSector ||
            dockSector < 1 ||
            dockSector > universe.SectorCount ||
            context.CurrentSector.Value == dockSector)
        {
            return null;
        }

        var sectors = await GetSectorsAsync(context.WorldStateStore, cancellationToken)
            .ConfigureAwait(false);
        var byId = sectors.ToDictionary(sector => sector.Id);

        if (!byId.TryGetValue(dockSector, out var dockObservation) ||
            dockObservation.Port is { ClassIndex: >= 1 and <= 8 })
        {
            // The destination may be identified by universe metadata or by a
            // class-9 observation, but it is never eligible as a trade port.
            return null;
        }

        var routeToDock = FindFreshDirectedPath(
            context.CurrentSector.Value,
            dockSector,
            byId,
            universe.SectorCount,
            now);
        if (routeToDock is null || routeToDock.Count < 2)
        {
            return null;
        }

        var portsOnRoute = routeToDock
            .Take(routeToDock.Count - 1)
            .Select((sectorId, index) => (SectorId: sectorId, Index: index))
            .Where(item => byId.TryGetValue(item.SectorId, out var sector) &&
                IsFreshRegularPort(sector, now))
            .ToArray();

        for (var sourcePosition = 0; sourcePosition < portsOnRoute.Length; sourcePosition++)
        {
            var sourceEntry = portsOnRoute[sourcePosition];
            var sourceSector = byId[sourceEntry.SectorId];
            var sourcePort = sourceSector.Port!;

            for (var buyerPosition = sourcePosition + 1; buyerPosition < portsOnRoute.Length; buyerPosition++)
            {
                var buyerEntry = portsOnRoute[buyerPosition];
                if (buyerEntry.Index <= sourceEntry.Index)
                {
                    continue;
                }

                var buyerSector = byId[buyerEntry.SectorId];
                var buyerPort = buyerSector.Port!;
                if (sourceSector.Id == buyerSector.Id ||
                    !PairPortTradeMacro.HasComplementaryProfiles(sourcePort, buyerPort))
                {
                    continue;
                }

                var compatibleProduct = Enum.GetValues<TradeProduct>()
                    .FirstOrDefault(product =>
                        sourcePort.Products.TryGetValue(product, out var sourceProduct) &&
                        !sourceProduct.BuysProduct &&
                        buyerPort.Products.TryGetValue(product, out var buyerProduct) &&
                        buyerProduct.BuysProduct);
                if (!sourcePort.Products.TryGetValue(compatibleProduct, out var sourceItem) ||
                    sourceItem.BuysProduct ||
                    !buyerPort.Products.TryGetValue(compatibleProduct, out var buyerItem) ||
                    !buyerItem.BuysProduct)
                {
                    continue;
                }

                // A pair is only considered compatible when the directed
                // return route is also known and fresh.
                var returnPath = FindFreshDirectedPath(
                    buyerSector.Id,
                    sourceSector.Id,
                    byId,
                    universe.SectorCount,
                    now);
                if (returnPath is null)
                {
                    continue;
                }

                return new PreflightPlan(
                    routeToDock,
                    $"Compatible regular-port candidate: sector {sourceSector.Id} sells {compatibleProduct} " +
                    $"to the ship; sector {buyerSector.Id} buys {compatibleProduct} and is a possible " +
                    "pre-dock unload stop if the ship actually carries it. A fresh directed return " +
                    "route between the pair is also known.");
            }
        }

        return null;
    }

    private static async Task<IReadOnlyList<Sector>> GetSectorsAsync(
        IWorldStateStore worldStateStore,
        CancellationToken cancellationToken)
    {
        const int pageSize = 1000;
        var sectors = new List<Sector>();
        for (var offset = 0; ; offset += pageSize)
        {
            var page = await worldStateStore
                .GetSectorsAsync(offset, pageSize, cancellationToken)
                .ConfigureAwait(false);
            sectors.AddRange(page);
            if (page.Count < pageSize)
            {
                return sectors;
            }
        }
    }

    private static bool IsFreshRegularPort(Sector sector, DateTimeOffset now)
    {
        var port = sector.Port;
        if (port is null ||
            port.IsDead ||
            port.SectorId != sector.Id ||
            port.ClassIndex is < 1 or > 8 ||
            !IsFresh(port.ObservedAt, now))
        {
            return false;
        }

        return Enum.GetValues<TradeProduct>().All(product =>
            port.Products.TryGetValue(product, out var item) &&
            item.Amount >= 0 &&
            item.Percent is >= 0 and <= 100);
    }

    private static bool IsFresh(DateTimeOffset? observedAt, DateTimeOffset now) =>
        observedAt is { } value &&
        value <= now &&
        now - value <= MaximumObservationAge;

    private static IReadOnlyList<int>? FindFreshDirectedPath(
        int fromSector,
        int toSector,
        IReadOnlyDictionary<int, Sector> sectors,
        int sectorCount,
        DateTimeOffset now)
    {
        if (!sectors.ContainsKey(fromSector) || !sectors.ContainsKey(toSector))
        {
            return null;
        }
        if (fromSector == toSector)
        {
            return [fromSector];
        }

        var queue = new Queue<int>();
        var previous = new Dictionary<int, int>();
        var visited = new HashSet<int> { fromSector };
        queue.Enqueue(fromSector);

        while (queue.Count > 0)
        {
            var current = queue.Dequeue();
            if (!sectors.TryGetValue(current, out var source) ||
                !source.WarpDataKnown ||
                !IsFresh(source.WarpObservedAt, now))
            {
                continue;
            }

            foreach (var next in source.Warps
                .Where(destination => destination >= 1 && destination <= sectorCount)
                .Distinct()
                .Order())
            {
                if (!sectors.ContainsKey(next) || !visited.Add(next))
                {
                    continue;
                }

                previous[next] = current;
                if (next == toSector)
                {
                    var path = new List<int> { toSector };
                    var cursor = toSector;
                    while (cursor != fromSector)
                    {
                        cursor = previous[cursor];
                        path.Add(cursor);
                    }
                    path.Reverse();
                    return path;
                }
                queue.Enqueue(next);
            }
        }

        return null;
    }

    private sealed record PreflightPlan(
        IReadOnlyList<int> Route,
        string Description);
}
