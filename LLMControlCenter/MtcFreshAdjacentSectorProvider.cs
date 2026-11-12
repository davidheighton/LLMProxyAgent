using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;
using Core = TWXProxy.Core;

namespace MTC;

internal sealed class MtcFreshAdjacentSectorProvider(
    Func<Core.ModDatabase?> getDatabase) :
    IAutonomousMoveTargetProvider,
    IAutonomousGameWorldProvider,
    IAutonomousPortTradeOpportunityProvider,
    IAutonomousPortCimFreshnessProvider,
    IAutonomousFedSpaceFastStartProvider
{
    private static readonly TimeSpan MaximumSectorDataAge = TimeSpan.FromMinutes(15);
    private const int MaximumPortPairTradeHops = 3;

    public bool RequiresCurrentSectorDisplayBeforeMovement => true;

    public bool IsPortCimFresh()
    {
        Core.ModDatabase? database = getDatabase();
        return database is not null && IsFresh(database.DBHeader.LastPortCIM);
    }

    public Task<int?> GetStarDockSectorAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Core.ModDatabase? database = getDatabase();
        int sector = database?.DBHeader.StarDock ?? 0;
        return Task.FromResult<int?>(
            sector > 0 && database is not null && sector <= database.SectorCount
                ? sector
                : null);
    }

    public Task<int?> GetNextSectorTowardStarDockAsync(
        int currentSector,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Core.ModDatabase? database = getDatabase();
        int starDockSector = database?.DBHeader.StarDock ?? 0;
        return GetNextSectorTowardSectorAsync(currentSector, starDockSector, cancellationToken);
    }

    public Task<int?> GetNextSectorTowardSectorAsync(
        int currentSector,
        int targetSector,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Core.ModDatabase? database = getDatabase();
        if (database is null ||
            currentSector < 1 ||
            currentSector > database.SectorCount ||
            targetSector < 1 ||
            targetSector > database.SectorCount ||
            currentSector == targetSector)
        {
            return Task.FromResult<int?>(null);
        }

        IReadOnlyList<int> path = database.CalculateShortestPath(currentSector, targetSector);
        return Task.FromResult<int?>(path.Count > 1 ? path[1] : null);
    }

    public Task<bool> HasFreshOwnedFighterInSectorAsync(
        int sector,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Core.ModDatabase? database = getDatabase();
        Core.SectorData? sectorData = sector > 0 && database is not null && sector <= database.SectorCount
            ? database.GetSector(sector)
            : null;
        Core.SpaceObject? fighters = sectorData?.Fighters;
        bool hasOwnedFighter =
            IsFresh(sectorData) &&
            fighters is { Quantity: > 0 } &&
            (string.Equals(fighters.Owner, "yours", StringComparison.OrdinalIgnoreCase) ||
             string.Equals(fighters.Owner, "belong to your Corp", StringComparison.OrdinalIgnoreCase));
        return Task.FromResult(hasOwnedFighter);
    }

    public Task<bool?> IsFederationSpaceAsync(
        int sector,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Core.ModDatabase? database = getDatabase();
        Core.SectorData? sectorData =
            sector > 0 && database is not null && sector <= database.SectorCount
                ? database.GetSector(sector)
                : null;
        if (!IsFresh(sectorData))
        {
            return Task.FromResult<bool?>(null);
        }

        string constellation = sectorData!.Constellation.Trim();
        string beacon = sectorData.Beacon.Trim();
        if (string.IsNullOrWhiteSpace(constellation) &&
            string.IsNullOrWhiteSpace(beacon))
        {
            return Task.FromResult<bool?>(null);
        }

        bool isFederationSpace =
            constellation.Contains("Federation", StringComparison.OrdinalIgnoreCase) ||
            beacon.Contains("FedSpace", StringComparison.OrdinalIgnoreCase) ||
            beacon.Contains("FedLaw Enforced", StringComparison.OrdinalIgnoreCase);
        return Task.FromResult<bool?>(isFederationSpace);
    }

    public Task<int?> GetCurrentRefurbPortClassAsync(
        int sector,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Core.ModDatabase? database = getDatabase();
        if (database is null || sector < 1 || sector > database.SectorCount)
        {
            return Task.FromResult<int?>(null);
        }

        if (database.DBHeader.StarDock == sector)
        {
            return Task.FromResult<int?>(9);
        }

        Core.SectorData? sectorData = database.GetSector(sector);
        return Task.FromResult<int?>(
            IsFresh(sectorData) &&
            sectorData?.SectorPort is { Dead: false, ClassIndex: 0 }
                ? 0
                : null);
    }

    public Task<int?> GetNextSectorTowardNearestRefurbPortAsync(
        int currentSector,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Core.ModDatabase? database = getDatabase();
        if (database is null ||
            currentSector < 1 ||
            currentSector > database.SectorCount)
        {
            return Task.FromResult<int?>(null);
        }

        int starDockSector = database.DBHeader.StarDock;
        int? bestTarget = starDockSector > 0 && starDockSector <= database.SectorCount
            ? starDockSector
            : null;
        int bestDistance = int.MaxValue;
        if (bestTarget is { } knownDock && knownDock != currentSector)
        {
            IReadOnlyList<int> dockPath = database.CalculateShortestPath(currentSector, knownDock);
            if (dockPath.Count > 1)
            {
                bestDistance = dockPath.Count - 1;
            }
            else
            {
                bestTarget = null;
            }
        }

        for (int candidate = 1; candidate <= database.SectorCount; candidate++)
        {
            if (candidate == currentSector ||
                candidate == starDockSector)
            {
                continue;
            }

            Core.SectorData? sectorData = database.GetSector(candidate);
            if (!IsFresh(sectorData) ||
                sectorData?.SectorPort is not { Dead: false, ClassIndex: 0 })
            {
                continue;
            }

            IReadOnlyList<int> path = database.CalculateShortestPath(currentSector, candidate);
            if (path.Count <= 1)
            {
                continue;
            }

            int distance = path.Count - 1;
            if (distance < bestDistance ||
                (distance == bestDistance &&
                 (bestTarget is null || candidate < bestTarget.Value)))
            {
                bestTarget = candidate;
                bestDistance = distance;
            }
        }

        if (bestTarget is not { } target || target == currentSector)
        {
            return Task.FromResult<int?>(null);
        }

        IReadOnlyList<int> shortestPath = database.CalculateShortestPath(currentSector, target);
        return Task.FromResult<int?>(shortestPath.Count > 1 ? shortestPath[1] : null);
    }

    public Task<AutonomousPortTradeOpportunity?> GetOpportunityAsync(
        int currentSector,
        int? previousSector,
        int? nextSector,
        GameSnapshot snapshot,
        CancellationToken cancellationToken = default,
        IReadOnlySet<int>? excludedTradeSectors = null,
        TradeProduct? preferredProduct = null)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        cancellationToken.ThrowIfCancellationRequested();
        if (!GameSnapshotValidation.IsValid(snapshot) ||
            snapshot.CurrentSector != currentSector)
        {
            return Task.FromResult<AutonomousPortTradeOpportunity?>(null);
        }

        Core.ModDatabase? database = getDatabase();
        if (database is null)
        {
            return Task.FromResult<AutonomousPortTradeOpportunity?>(null);
        }

        // Nearly broke: Fuel Ore is the cheapest cargo, so it can still be bought (one unit at a time if need be) to
        // rebuild credits.
        if (preferredProduct is null &&
            snapshot.SynchronizedCredits is { } lowCredits &&
            lowCredits < AutonomousTradePolicy.LowCreditFuelOreThreshold)
        {
            preferredProduct = TradeProduct.FuelOre;
        }

        Core.SectorData? current = database.GetSector(currentSector);
        Core.Port? currentPort = current?.SectorPort;
        if (!IsFresh(current))
        {
            return Task.FromResult<AutonomousPortTradeOpportunity?>(null);
        }

        int sectorCount = database.SectorCount;
        if (IsRegularPort(currentPort) &&
            excludedTradeSectors?.Contains(currentSector) != true &&
            PortTradeCompatibility.FindSellableCargo(currentPort!.ClassIndex, snapshot) is { } carriedProduct)
        {
            (TradeProduct? followUpProduct, int? followUpPartnerSector) =
                FindFollowUpBuyPlan(
                    database,
                    current!,
                    nextSector,
                    excludedTradeSectors,
                    preferredProduct,
                    snapshot,
                    carriedProduct);
            return Task.FromResult<AutonomousPortTradeOpportunity?>(
                new AutonomousPortTradeOpportunity(
                    currentSector,
                    carriedProduct,
                    SellingExistingCargo: true)
                {
                    FollowUpBuyProduct = followUpProduct,
                    FollowUpPartnerSector = followUpPartnerSector
                });
        }

        if (HasCarriedCargo(snapshot))
        {
            if (previousSector is > 0)
            {
                Core.SectorData? previous = database.GetSector(previousSector.Value);
                Core.Port? previousPort = previous?.SectorPort;
                // Only cargo actually aboard can be sold here; a product merely compatible with the port pair is not.
                TradeProduct? product =
                    excludedTradeSectors?.Contains(previousSector.Value) != true &&
                    IsFresh(previous) &&
                    IsRegularPort(previousPort) &&
                    IsRegularPort(currentPort)
                    ? FindCarriedPairProduct(snapshot, previousPort!.ClassIndex, currentPort!.ClassIndex)
                    : null;
                if (product is { } sellProduct)
                {
                    return Task.FromResult<AutonomousPortTradeOpportunity?>(
                        new AutonomousPortTradeOpportunity(
                            previousSector.Value,
                            sellProduct,
                            SellingExistingCargo: true));
                }
            }

            return Task.FromResult(
                FindNearestCargoBuyerPlan(
                    database,
                    currentSector,
                    nextSector,
                    snapshot,
                    excludedTradeSectors));
        }

        if (snapshot.HoldsEmpty is > 0 &&
            snapshot.SynchronizedCredits is > 0)
        {
            // The Dock Shopper needs holds full of Fuel Ore at StarDock, so any port selling it will do; StarDock is
            // the "partner" the cargo is carried to.
            if (preferredProduct == TradeProduct.FuelOre &&
                snapshot.HoldsEmpty == snapshot.HoldsTotal &&
                IsRegularPort(currentPort) &&
                excludedTradeSectors?.Contains(currentSector) != true &&
                PortTradeCompatibility.SellsProduct(currentPort!.ClassIndex, TradeProduct.FuelOre) &&
                database.DBHeader.StarDock is > 0 and var dockShopperSector &&
                dockShopperSector != currentSector)
            {
                return Task.FromResult<AutonomousPortTradeOpportunity?>(
                    new AutonomousPortTradeOpportunity(
                        dockShopperSector,
                        TradeProduct.FuelOre,
                        SellingExistingCargo: false));
            }

            IReadOnlyList<int> adjacentSectors = current!.Warp
                .Where(destination =>
                    destination > 0 &&
                    destination <= sectorCount &&
                    destination != currentSector)
                .Select(destination => (int)destination)
                .Distinct()
                .OrderBy(destination => destination == nextSector ? 0 : 1)
                .ThenBy(destination => destination)
                .ToArray();

            if (IsRegularPort(currentPort))
            {
                foreach (TradeProduct productPriority in GetTradeProductPriority(preferredProduct, snapshot.SynchronizedCredits, snapshot.HoldsEmpty))
                {
                    foreach (int buyerSector in adjacentSectors)
                    {
                        Core.Port? buyerPort = database.GetSector(buyerSector)?.SectorPort;
                        TradeProduct? product = IsRegularPort(buyerPort)
                            ? PortTradeCompatibility.FindTradeProduct(
                                currentPort!.ClassIndex,
                                buyerPort!.ClassIndex,
                                productPriority)
                            : null;
                        if (product == productPriority)
                        {
                            return Task.FromResult<AutonomousPortTradeOpportunity?>(
                                new AutonomousPortTradeOpportunity(
                                    buyerSector,
                                    productPriority,
                                    SellingExistingCargo: false));
                        }
                    }
                }
            }

            foreach (int sellerSector in adjacentSectors)
            {
                if (excludedTradeSectors?.Contains(sellerSector) == true)
                {
                    continue;
                }

                Core.Port? sellerPort = database.GetSector(sellerSector)?.SectorPort;
                TradeProduct? product = null;
                if (IsRegularPort(sellerPort) && IsRegularPort(currentPort))
                {
                    foreach (TradeProduct productPriority in GetTradeProductPriority(preferredProduct, snapshot.SynchronizedCredits, snapshot.HoldsEmpty))
                    {
                        if (PortTradeCompatibility.FindTradeProduct(
                                sellerPort!.ClassIndex,
                                currentPort!.ClassIndex,
                                productPriority) == productPriority)
                        {
                            product = productPriority;
                            break;
                        }
                    }
                }
                if (product is { } tradeProduct)
                {
                    return Task.FromResult<AutonomousPortTradeOpportunity?>(
                        new AutonomousPortTradeOpportunity(
                            currentSector,
                            tradeProduct,
                            SellingExistingCargo: false)
                        {
                            TradeSector = sellerSector,
                            RouteSector = sellerSector
                        });
                }
            }

            // Before the scanner, a funded agent heads to StarDock instead of starting distant trades; once a scanner is
            // installed, multi-hop pairs are how it keeps earning (adjacent pairs alone are rare).
            bool scannerInstalled = snapshot.DensityScannerInstalled is true || snapshot.HoloScannerInstalled is true;
            if (snapshot.SynchronizedCredits is { } credits &&
                (credits < AutonomousTradePolicy.MinimumScannerUpgradeCredits ||
                 preferredProduct is not null ||
                 scannerInstalled))
            {
                AutonomousPortTradeOpportunity? knownPair = FindNearestTradePlan(
                    database,
                    currentSector,
                    nextSector,
                    excludedTradeSectors,
                    preferredProduct,
                    scannerless: !scannerInstalled,
                    availableCredits: credits,
                    emptyHolds: snapshot.HoldsEmpty);
                if (knownPair is not null)
                {
                    return Task.FromResult<AutonomousPortTradeOpportunity?>(knownPair);
                }
            }
        }

        return Task.FromResult<AutonomousPortTradeOpportunity?>(null);
    }

    public Task<int?> GetNextFedSpaceSweepSectorAsync(
        int currentSector,
        IReadOnlySet<int> visitedSectors,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(visitedSectors);
        cancellationToken.ThrowIfCancellationRequested();
        Core.ModDatabase? database = getDatabase();
        if (database is null || currentSector is < 1 or > 10)
        {
            return Task.FromResult<int?>(null);
        }

        var candidates = Enumerable.Range(1, 10)
            .Where(sector => !visitedSectors.Contains(sector))
            .Select(sector => (Sector: sector, Path: FindFedSpacePath(database, currentSector, sector, 1)))
            .Where(candidate => candidate.Path.Count > 1)
            .OrderBy(candidate => candidate.Path.Count)
            .ThenBy(candidate => candidate.Sector)
            .FirstOrDefault();
        return Task.FromResult<int?>(
            candidates.Path is { Count: > 1 } ? candidates.Path[1] : null);
    }

    public Task<AutonomousPortTradeOpportunity?> GetFedSpaceTradeOpportunityAsync(
        int currentSector,
        int? nextSector,
        GameSnapshot snapshot,
        IReadOnlySet<int> excludedTradeSectors,
        TradeProduct? preferredProduct = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        ArgumentNullException.ThrowIfNull(excludedTradeSectors);
        cancellationToken.ThrowIfCancellationRequested();
        if (currentSector is < 1 or > 10 ||
            snapshot.CurrentSector != currentSector ||
            !GameSnapshotValidation.IsValid(snapshot))
        {
            return Task.FromResult<AutonomousPortTradeOpportunity?>(null);
        }

        Core.ModDatabase? database = getDatabase();
        if (database is null || !IsPortCimFresh())
        {
            return Task.FromResult<AutonomousPortTradeOpportunity?>(null);
        }

        Core.SectorData? current = database.GetSector(currentSector);
        if (!IsFresh(current))
        {
            return Task.FromResult<AutonomousPortTradeOpportunity?>(null);
        }

        if (HasCarriedCargo(snapshot))
        {
            if (excludedTradeSectors.Contains(currentSector) != true &&
                IsRegularPort(current!.SectorPort) &&
                PortTradeCompatibility.FindSellableCargo(current.SectorPort!.ClassIndex, snapshot)
                    is { } carriedProduct)
            {
                (TradeProduct? followUpProduct, int? followUpPartnerSector) =
                    FindFedSpaceFollowUpBuyPlan(
                        database,
                        current,
                        excludedTradeSectors,
                        preferredProduct);
                return Task.FromResult<AutonomousPortTradeOpportunity?>(
                    new AutonomousPortTradeOpportunity(
                        currentSector,
                        carriedProduct,
                        SellingExistingCargo: true)
                    {
                        FollowUpBuyProduct = followUpProduct,
                        FollowUpPartnerSector = followUpPartnerSector
                    });
            }

            AutonomousPortTradeOpportunity? cargoPlan = FindFedSpaceCargoBuyerPlan(
                database,
                currentSector,
                nextSector,
                snapshot,
                excludedTradeSectors);
            return Task.FromResult(cargoPlan);
        }

        AutonomousPortTradeOpportunity? bestPlan = null;
        int bestRouteLength = int.MaxValue;
        int bestPairLength = int.MaxValue;
        int bestProductPriority = int.MaxValue;
        int bestRoutePriority = int.MaxValue;
        int bestSellerSector = int.MaxValue;
        int bestBuyerSector = int.MaxValue;
        for (int sellerSector = 2; sellerSector <= 10; sellerSector++)
        {
            if (excludedTradeSectors.Contains(sellerSector))
            {
                continue;
            }

            Core.SectorData? seller = database.GetSector(sellerSector);
            if (!IsFresh(seller) || !IsRegularPort(seller?.SectorPort))
            {
                continue;
            }

            IReadOnlyList<int> route = FindFedSpacePath(database, currentSector, sellerSector, 1);
            if (route.Count == 0)
            {
                continue;
            }

            for (int buyerSector = 2; buyerSector <= 10; buyerSector++)
            {
                if (buyerSector == sellerSector || excludedTradeSectors.Contains(buyerSector))
                {
                    continue;
                }

                Core.SectorData? buyer = database.GetSector(buyerSector);
                if (!IsFresh(buyer) || !IsRegularPort(buyer?.SectorPort))
                {
                    continue;
                }

                IReadOnlyList<int> pairPath = FindFedSpacePath(database, sellerSector, buyerSector, 2);
                if (pairPath.Count is < 2 or > 4)
                {
                    continue;
                }

                TradeProduct? product = PortTradeCompatibility.FindMostProfitableProduct(
                    seller!.SectorPort!.ClassIndex,
                    buyer!.SectorPort!.ClassIndex,
                    preferredProduct,
                    snapshot.SynchronizedCredits,
                    snapshot.HoldsEmpty);
                if (product is not { } tradeProduct)
                {
                    continue;
                }

                int routeLength = route.Count - 1;
                int pairLength = pairPath.Count - 1;
                int productPriority =
                    preferredProduct is { } preferred && tradeProduct != preferred ? 1 : 0;
                int routePriority = route.Count > 1 && route[1] == nextSector ? 0 : 1;
                if (productPriority > bestProductPriority ||
                    productPriority == bestProductPriority &&
                    (routeLength > bestRouteLength ||
                     routeLength == bestRouteLength &&
                     (pairLength > bestPairLength ||
                      pairLength == bestPairLength &&
                      (routePriority > bestRoutePriority ||
                       routePriority == bestRoutePriority &&
                       (sellerSector > bestSellerSector ||
                        sellerSector == bestSellerSector &&
                        buyerSector >= bestBuyerSector)))))
                {
                    continue;
                }

                bestProductPriority = productPriority;
                bestRouteLength = routeLength;
                bestPairLength = pairLength;
                bestRoutePriority = routePriority;
                bestSellerSector = sellerSector;
                bestBuyerSector = buyerSector;
                bestPlan = new AutonomousPortTradeOpportunity(
                    buyerSector,
                    tradeProduct,
                    SellingExistingCargo: false)
                {
                    TradeSector = sellerSector == currentSector ? null : sellerSector,
                    RouteSector = sellerSector == currentSector ? null : route[1]
                };
            }
        }

        return Task.FromResult(bestPlan);
    }

    public Task<int?> GetNextSectorTowardFedSpaceTradeTargetAsync(
        int currentSector,
        int targetSector,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Core.ModDatabase? database = getDatabase();
        if (database is null ||
            currentSector is < 1 or > 10 ||
            targetSector is < 2 or > 10)
        {
            return Task.FromResult<int?>(null);
        }

        IReadOnlyList<int> path = FindFedSpacePath(database, currentSector, targetSector, 1);
        return Task.FromResult<int?>(path.Count > 1 ? path[1] : null);
    }

    public Task<IReadOnlyList<int>> GetFreshAdjacentSectorIdsAsync(
        int? currentSector,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Core.ModDatabase? database = getDatabase();
        if (currentSector is not > 0 ||
            database is null ||
            currentSector > database.SectorCount)
        {
            return Task.FromResult<IReadOnlyList<int>>([]);
        }

        Core.SectorData? sector = database.GetSector(currentSector.Value);
        if (!IsFresh(sector))
        {
            return Task.FromResult<IReadOnlyList<int>>([]);
        }

        IReadOnlyList<int> adjacentSectors = sector!.Warp
            .Where(destination =>
                destination > 0 &&
                destination <= database.SectorCount &&
                destination != currentSector.Value)
            .Select(destination => (int)destination)
            .Distinct()
            .Order()
            .ToArray();
        return Task.FromResult(adjacentSectors);
    }

    public Task<IReadOnlyList<AdjacentSectorKnowledge>> GetSectorKnowledgeAsync(
        IReadOnlyList<int> sectorIds,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sectorIds);
        cancellationToken.ThrowIfCancellationRequested();
        Core.ModDatabase? database = getDatabase();

        IReadOnlyList<AdjacentSectorKnowledge> knowledge = sectorIds
            .Distinct()
            .Select(sectorId =>
            {
                Core.SectorData? sector = database?.GetSector(sectorId);
                if (!IsFresh(sector))
                {
                    return new AdjacentSectorKnowledge(
                        sectorId,
                        ExplorationStatus.Unknown,
                        false,
                        false);
                }

                ExplorationStatus exploration = sector!.Explored switch
                {
                    Core.ExploreType.Yes => ExplorationStatus.Explored,
                    Core.ExploreType.Calc or Core.ExploreType.Density => ExplorationStatus.WarpOnly,
                    _ => ExplorationStatus.Unknown
                };
                return new AdjacentSectorKnowledge(
                    sector.Number,
                    exploration,
                    sector.Warp.Any(destination => destination > 0),
                    sector.SectorPort is not null);
            })
            .ToArray();
        return Task.FromResult(knowledge);
    }

    private static bool IsFresh(Core.SectorData? sector)
    {
        return sector is not null && IsFresh(sector.Update);
    }

    private static bool IsRegularPort(Core.Port? port) =>
        port is { Dead: false, ClassIndex: >= 1 and <= 8 };

    private static IReadOnlyList<int> FindFedSpacePath(
        Core.ModDatabase database,
        int fromSector,
        int toSector,
        int minimumSector)
    {
        if (fromSector < minimumSector || fromSector > 10 ||
            toSector < minimumSector || toSector > 10)
        {
            return [];
        }
        if (fromSector == toSector)
        {
            return [fromSector];
        }

        var queue = new Queue<int>();
        var previous = new Dictionary<int, int>();
        var visited = new HashSet<int> { fromSector };
        queue.Enqueue(fromSector);
        while (queue.TryDequeue(out int current))
        {
            Core.SectorData? sector = database.GetSector(current);
            if (!IsFresh(sector))
            {
                continue;
            }

            foreach (ushort destinationValue in sector!.Warp.Order())
            {
                int destination = destinationValue;
                if (destination < minimumSector ||
                    destination > 10 ||
                    !visited.Add(destination))
                {
                    continue;
                }

                previous[destination] = current;
                if (destination == toSector)
                {
                    var path = new List<int> { toSector };
                    int cursor = toSector;
                    while (cursor != fromSector)
                    {
                        cursor = previous[cursor];
                        path.Add(cursor);
                    }
                    path.Reverse();
                    return path;
                }

                if (!IsFresh(database.GetSector(destination)))
                {
                    continue;
                }

                queue.Enqueue(destination);
            }
        }

        return [];
    }

    private static AutonomousPortTradeOpportunity? FindFedSpaceCargoBuyerPlan(
        Core.ModDatabase database,
        int currentSector,
        int? preferredRouteSector,
        GameSnapshot snapshot,
        IReadOnlySet<int> excludedTradeSectors)
    {
        AutonomousPortTradeOpportunity? bestPlan = null;
        int bestDistance = int.MaxValue;
        int bestRoutePriority = int.MaxValue;
        for (int buyerSector = 2; buyerSector <= 10; buyerSector++)
        {
            if (excludedTradeSectors.Contains(buyerSector))
            {
                continue;
            }

            Core.SectorData? buyer = database.GetSector(buyerSector);
            TradeProduct? product = IsFresh(buyer) && IsRegularPort(buyer?.SectorPort)
                ? PortTradeCompatibility.FindSellableCargo(buyer!.SectorPort!.ClassIndex, snapshot)
                : null;
            if (product is not { } carriedProduct)
            {
                continue;
            }

            IReadOnlyList<int> path = FindFedSpacePath(database, currentSector, buyerSector, 1);
            if (path.Count <= 1)
            {
                continue;
            }

            int distance = path.Count - 1;
            int routePriority = path[1] == preferredRouteSector ? 0 : 1;
            if (distance > bestDistance ||
                distance == bestDistance && routePriority >= bestRoutePriority)
            {
                continue;
            }

            bestDistance = distance;
            bestRoutePriority = routePriority;
            bestPlan = new AutonomousPortTradeOpportunity(
                buyerSector,
                carriedProduct,
                SellingExistingCargo: true)
            {
                TradeSector = buyerSector,
                RouteSector = path[1]
            };
        }

        return bestPlan;
    }

    private static (TradeProduct? Product, int? PartnerSector) FindFedSpaceFollowUpBuyPlan(
        Core.ModDatabase database,
        Core.SectorData seller,
        IReadOnlySet<int> excludedTradeSectors,
        TradeProduct? preferredProduct)
    {
        if (!IsRegularPort(seller.SectorPort))
        {
            return (null, null);
        }

        for (int buyerSector = 2; buyerSector <= 10; buyerSector++)
        {
            if (buyerSector == seller.Number || excludedTradeSectors.Contains(buyerSector))
            {
                continue;
            }

            Core.SectorData? buyer = database.GetSector(buyerSector);
            if (!IsFresh(buyer) || !IsRegularPort(buyer?.SectorPort))
            {
                continue;
            }

            IReadOnlyList<int> pairPath = FindFedSpacePath(database, seller.Number, buyerSector, 2);
            if (pairPath.Count is < 2 or > 4)
            {
                continue;
            }

            TradeProduct? product = PortTradeCompatibility.FindTradeProduct(
                seller.SectorPort!.ClassIndex,
                buyer!.SectorPort!.ClassIndex,
                preferredProduct);
            if (product is { } followUpProduct)
            {
                return (followUpProduct, buyerSector);
            }
        }

        return (null, null);
    }

    private static TradeProduct? FindCarriedPairProduct(GameSnapshot snapshot, int sellerClass, int buyerClass) =>
        PortTradeCompatibility.ValueOrder
            .Where(product =>
                CarriedUnits(snapshot, product) > 0 &&
                PortTradeCompatibility.SellsProduct(sellerClass, product) &&
                PortTradeCompatibility.BuysProduct(buyerClass, product))
            .Cast<TradeProduct?>()
            .FirstOrDefault();

    private static int CarriedUnits(GameSnapshot snapshot, TradeProduct product) => product switch
    {
        TradeProduct.FuelOre => snapshot.FuelOre ?? 0,
        TradeProduct.Organics => snapshot.Organics ?? 0,
        _ => snapshot.Equipment ?? 0
    };

    private static bool HasCarriedCargo(GameSnapshot snapshot) =>
        snapshot.FuelOre is > 0 ||
        snapshot.Organics is > 0 ||
        snapshot.Equipment is > 0;

    private static AutonomousPortTradeOpportunity? FindNearestTradePlan(
        Core.ModDatabase database,
        int currentSector,
        int? preferredRouteSector,
        IReadOnlySet<int>? excludedTradeSectors,
        TradeProduct? preferredProduct,
        bool scannerless,
        long? availableCredits = null,
        int? emptyHolds = null)
    {
        int starDockSector = database.DBHeader.StarDock;
        var ports = new Dictionary<int, Core.Port>();
        for (int sector = 1; sector <= database.SectorCount; sector++)
        {
            Core.SectorData? sectorData = database.GetSector(sector);
            if (IsFresh(sectorData) &&
                IsRegularPort(sectorData?.SectorPort) &&
                (!scannerless || IsScannerlessSectorAllowed(sector, sectorData, starDockSector)))
            {
                ports[sector] = sectorData!.SectorPort!;
            }
        }

        AutonomousPortTradeOpportunity? bestPlan = null;
        int bestProductPriority = int.MaxValue;
        double bestProfitPerHop = double.MinValue;
        int bestDistance = int.MaxValue;
        int bestPairDistance = int.MaxValue;
        int bestRoutePriority = int.MaxValue;
        foreach ((int sellerSector, Core.Port sellerPort) in ports)
        {
            if (excludedTradeSectors?.Contains(sellerSector) == true)
            {
                continue;
            }

            IReadOnlyDictionary<int, int> reachableBuyers = FindReachablePortDistances(
                database,
                sellerSector,
                ports,
                MaximumPortPairTradeHops,
                scannerless,
                starDockSector);
            if (reachableBuyers.Count == 0)
            {
                continue;
            }

            IReadOnlyList<int> path = database.CalculateShortestPath(currentSector, sellerSector);
            if (path.Count == 0 ||
                (sellerSector != currentSector && path.Count <= 1) ||
                (scannerless && !IsScannerlessRouteAllowed(database, path, starDockSector)))
            {
                continue;
            }
            int? routeSector = sellerSector == currentSector ? null : path[1];
            int distance = path.Count - 1;
            int routePriority = routeSector == preferredRouteSector ? 0 : 1;

            foreach ((int buyerSector, int pairDistance) in reachableBuyers)
            {
                // The attempted set only stops another trade *at* a port; the port just traded at is often the best
                // partner (the other half of a bidirectional pair), so it is not excluded as a buyer.
                if (buyerSector == sellerSector)
                {
                    continue;
                }

                Core.Port buyerPort = ports[buyerSector];
                IReadOnlyList<int> pairPath = database.CalculateShortestPath(sellerSector, buyerSector);
                if (pairPath.Count is < 2 or > MaximumPortPairTradeHops + 1 ||
                    (scannerless &&
                     !IsScannerlessRouteAllowed(database, pairPath, starDockSector)))
                {
                    continue;
                }
                TradeProduct? product = PortTradeCompatibility.FindMostProfitableProduct(
                    sellerPort.ClassIndex,
                    buyerPort.ClassIndex,
                    preferredProduct,
                    availableCredits,
                    emptyHolds);
                if (product is not { } tradeProduct)
                {
                    continue;
                }
                int productPriority =
                    preferredProduct is { } preferred && tradeProduct != preferred ? 1 : 0;
                // Round-trip profit per hop: the cargo carried to the buyer plus the best cargo carried back. A pair
                // that trades Organics one way and Equipment the other earns on both legs and ranks highest.
                long returnProfit = PortTradeCompatibility.FindMostProfitableProduct(
                        buyerPort.ClassIndex,
                        sellerPort.ClassIndex,
                        null,
                        availableCredits,
                        emptyHolds) is { } returnProduct
                    ? PortTradeCompatibility.EstimateTripProfit(returnProduct, availableCredits, emptyHolds)
                    : 0;
                double profitPerHop =
                    (PortTradeCompatibility.EstimateTripProfit(tradeProduct, availableCredits, emptyHolds) + returnProfit) /
                    (double)Math.Max(1, distance + 2 * pairDistance);

                if (productPriority > bestProductPriority ||
                    (productPriority == bestProductPriority &&
                     (profitPerHop < bestProfitPerHop ||
                      (profitPerHop == bestProfitPerHop &&
                       (distance > bestDistance ||
                        (distance == bestDistance &&
                         (pairDistance > bestPairDistance ||
                          (pairDistance == bestPairDistance && routePriority >= bestRoutePriority))))))))
                {
                    continue;
                }

                bestProductPriority = productPriority;
                bestProfitPerHop = profitPerHop;
                bestDistance = distance;
                bestPairDistance = pairDistance;
                bestRoutePriority = routePriority;
                bestPlan = new AutonomousPortTradeOpportunity(
                    buyerSector,
                    tradeProduct,
                    SellingExistingCargo: false)
                {
                    TradeSector = sellerSector,
                    RouteSector = routeSector
                };
            }
        }

        return bestPlan;
    }

    private static IReadOnlyDictionary<int, int> FindReachablePortDistances(
        Core.ModDatabase database,
        int sellerSector,
        IReadOnlyDictionary<int, Core.Port> ports,
        int maximumHops,
        bool scannerless,
        int starDockSector)
    {
        var distances = new Dictionary<int, int>();
        var visited = new HashSet<int> { sellerSector };
        var pending = new Queue<(int Sector, int Distance)>();
        pending.Enqueue((sellerSector, 0));

        while (pending.TryDequeue(out (int Sector, int Distance) current) &&
               current.Distance < maximumHops)
        {
            Core.SectorData? currentData = database.GetSector(current.Sector);
            if (!IsFresh(currentData))
            {
                continue;
            }

            foreach (ushort destination in currentData!.Warp)
            {
                int nextSector = destination;
                if (nextSector < 1 ||
                    nextSector > database.SectorCount ||
                    !visited.Add(nextSector))
                {
                    continue;
                }
                Core.SectorData? nextData = database.GetSector(nextSector);
                if (scannerless && !IsScannerlessSectorAllowed(nextSector, nextData, starDockSector))
                {
                    continue;
                }

                int nextDistance = current.Distance + 1;
                if (ports.ContainsKey(nextSector))
                {
                    distances[nextSector] = nextDistance;
                }

                if (nextDistance < maximumHops && IsFresh(nextData))
                {
                    pending.Enqueue((nextSector, nextDistance));
                }
            }
        }

        return distances;
    }

    private static bool IsScannerlessRouteAllowed(
        Core.ModDatabase database,
        IReadOnlyList<int> route,
        int starDockSector) =>
        route.All(sector =>
            IsScannerlessSectorAllowed(sector, database.GetSector(sector), starDockSector));

    private static bool IsScannerlessSectorAllowed(
        int sector,
        Core.SectorData? sectorData,
        int starDockSector) =>
        sector != 99 &&
        (sector is >= 1 and <= 10 ||
         sector == starDockSector ||
         IsFresh(sectorData) && sectorData!.Explored == Core.ExploreType.Yes);

    private static AutonomousPortTradeOpportunity? FindNearestCargoBuyerPlan(
        Core.ModDatabase database,
        int currentSector,
        int? preferredRouteSector,
        GameSnapshot snapshot,
        IReadOnlySet<int>? excludedTradeSectors)
    {
        AutonomousPortTradeOpportunity? bestPlan = null;
        int bestDistance = int.MaxValue;
        int bestRoutePriority = int.MaxValue;
        for (int buyerSector = 1; buyerSector <= database.SectorCount; buyerSector++)
        {
            if (buyerSector == currentSector ||
                excludedTradeSectors?.Contains(buyerSector) == true)
            {
                continue;
            }

            Core.SectorData? buyerData = database.GetSector(buyerSector);
            Core.Port? buyerPort = buyerData?.SectorPort;
            TradeProduct? product = IsFresh(buyerData) && IsRegularPort(buyerPort)
                ? PortTradeCompatibility.FindSellableCargo(buyerPort!.ClassIndex, snapshot)
                : null;
            if (product is not { } carriedProduct)
            {
                continue;
            }

            IReadOnlyList<int> path = database.CalculateShortestPath(currentSector, buyerSector);
            if (path.Count <= 1)
            {
                continue;
            }

            int routeSector = path[1];
            int distance = path.Count - 1;
            int routePriority = routeSector == preferredRouteSector ? 0 : 1;
            if (distance > bestDistance ||
                (distance == bestDistance && routePriority >= bestRoutePriority))
            {
                continue;
            }

            bestDistance = distance;
            bestRoutePriority = routePriority;
            bestPlan = new AutonomousPortTradeOpportunity(
                buyerSector,
                carriedProduct,
                SellingExistingCargo: true)
            {
                TradeSector = buyerSector,
                RouteSector = routeSector
            };
        }

        return bestPlan;
    }

    private static (TradeProduct? Product, int? PartnerSector) FindFollowUpBuyPlan(
        Core.ModDatabase database,
        Core.SectorData currentSector,
        int? preferredPartnerSector,
        IReadOnlySet<int>? excludedTradeSectors,
        TradeProduct? preferredProduct,
        GameSnapshot snapshot,
        TradeProduct soldProduct)
    {
        Core.Port? sellerPort = currentSector.SectorPort;
        if (!IsRegularPort(sellerPort))
        {
            return (null, null);
        }

        // Credits and holds once the carried cargo is sold, so the follow-up product is chosen for what the ship can
        // actually buy here (a sale usually makes Equipment or Organics affordable).
        int soldUnits = soldProduct switch
        {
            TradeProduct.FuelOre => snapshot.FuelOre ?? 0,
            TradeProduct.Organics => snapshot.Organics ?? 0,
            _ => snapshot.Equipment ?? 0
        };
        (int soldUnitCost, int soldUnitMargin) = PortTradeCompatibility.TypicalEconomics(soldProduct);
        long? creditsAfterSale = snapshot.SynchronizedCredits is { } credits
            ? credits + (long)soldUnits * (soldUnitCost + soldUnitMargin)
            : null;
        int? emptyHoldsAfterSale = snapshot.HoldsEmpty is { } empty ? empty + soldUnits : null;

        bool scannerless = snapshot.DensityScannerInstalled is not true && snapshot.HoloScannerInstalled is not true;
        int starDockSector = database.DBHeader.StarDock;
        var ports = new Dictionary<int, Core.Port>();
        for (int sector = 1; sector <= database.SectorCount; sector++)
        {
            Core.SectorData? sectorData = database.GetSector(sector);
            if (sector != currentSector.Number &&
                IsFresh(sectorData) &&
                IsRegularPort(sectorData?.SectorPort) &&
                (!scannerless || IsScannerlessSectorAllowed(sector, sectorData, starDockSector)))
            {
                ports[sector] = sectorData!.SectorPort!;
            }
        }

        IReadOnlyDictionary<int, int> partners = FindReachablePortDistances(
            database,
            currentSector.Number,
            ports,
            MaximumPortPairTradeHops,
            scannerless,
            starDockSector);
        TradeProduct? bestProduct = null;
        int? bestPartner = null;
        int bestProductPriority = int.MaxValue;
        double bestProfitPerHop = double.MinValue;
        int bestPartnerPriority = int.MaxValue;
        int bestDistance = int.MaxValue;
        foreach ((int partnerSector, int distance) in partners.OrderBy(pair => pair.Key))
        {
            TradeProduct? product = PortTradeCompatibility.FindMostProfitableProduct(
                sellerPort!.ClassIndex,
                ports[partnerSector].ClassIndex,
                preferredProduct,
                creditsAfterSale,
                emptyHoldsAfterSale);
            if (product is not { } tradeProduct)
            {
                continue;
            }

            int productPriority = preferredProduct is { } preferred && tradeProduct != preferred ? 1 : 0;
            // Round trip: also count what the partner sells that this port buys, so a two-way pair wins.
            long returnProfit = PortTradeCompatibility.FindMostProfitableProduct(
                    ports[partnerSector].ClassIndex,
                    sellerPort.ClassIndex,
                    null,
                    creditsAfterSale,
                    emptyHoldsAfterSale) is { } returnProduct
                ? PortTradeCompatibility.EstimateTripProfit(returnProduct, creditsAfterSale, emptyHoldsAfterSale)
                : 0;
            double profitPerHop =
                (PortTradeCompatibility.EstimateTripProfit(tradeProduct, creditsAfterSale, emptyHoldsAfterSale) + returnProfit) /
                (double)Math.Max(1, 2 * distance);
            int partnerPriority = partnerSector == preferredPartnerSector ? 0 : 1;
            if (productPriority > bestProductPriority ||
                productPriority == bestProductPriority &&
                (profitPerHop < bestProfitPerHop ||
                 profitPerHop == bestProfitPerHop &&
                 (partnerPriority > bestPartnerPriority ||
                  partnerPriority == bestPartnerPriority && distance >= bestDistance)))
            {
                continue;
            }

            bestProductPriority = productPriority;
            bestProfitPerHop = profitPerHop;
            bestPartnerPriority = partnerPriority;
            bestDistance = distance;
            bestProduct = tradeProduct;
            bestPartner = partnerSector;
        }

        return (bestProduct, bestPartner);
    }

    private static IReadOnlyList<TradeProduct> GetTradeProductPriority(
        TradeProduct? preferredProduct,
        long? availableCredits = null,
        int? emptyHolds = null) =>
        PortTradeCompatibility.RankByProfit(preferredProduct, availableCredits, emptyHolds);

    private static bool IsFresh(DateTime observedAt)
    {
        if (observedAt == DateTime.MinValue)
        {
            return false;
        }

        DateTimeOffset observedAtUtc = new(observedAt.ToUniversalTime());
        TimeSpan age = DateTimeOffset.UtcNow - observedAtUtc;
        return age >= TimeSpan.Zero && age <= MaximumSectorDataAge;
    }

}
