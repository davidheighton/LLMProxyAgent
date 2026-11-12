namespace LLMSurvivingProxyAgent.Core;

public static class PortTradeCompatibility
{
    private const int UnknownHoldCount = 20;

    /// <summary>Products from most to least valuable per hold, used when credits and holds are unknown.</summary>
    public static IReadOnlyList<TradeProduct> ValueOrder { get; } =
        [TradeProduct.Equipment, TradeProduct.Organics, TradeProduct.FuelOre];

    /// <summary>
    /// Typical per-unit purchase price and per-unit resale margin observed on TW2002 ports. Only the relative values
    /// matter: they rank products by expected trip profit (Equipment ~50/unit, Organics ~30, Fuel Ore ~10).
    /// </summary>
    public static (int UnitCost, int UnitMargin) TypicalEconomics(TradeProduct product) => product switch
    {
        TradeProduct.Equipment => (75, 50),
        TradeProduct.Organics => (37, 30),
        _ => (18, 10)
    };

    /// <summary>Expected profit of one full trip: the units that fit in the holds and are affordable, times the margin.</summary>
    public static long EstimateTripProfit(TradeProduct product, long? availableCredits, int? emptyHolds)
    {
        (int unitCost, int unitMargin) = TypicalEconomics(product);
        long units = Math.Max(0, emptyHolds ?? UnknownHoldCount);
        if (availableCredits is { } credits)
        {
            units = Math.Min(units, Math.Max(0, credits) / unitCost);
        }

        return units * unitMargin;
    }

    /// <summary>Products ordered by expected trip profit (ties keep <see cref="ValueOrder"/>), preferred product first.</summary>
    public static IReadOnlyList<TradeProduct> RankByProfit(
        TradeProduct? preferredProduct,
        long? availableCredits,
        int? emptyHolds)
    {
        IEnumerable<TradeProduct> ranked = ValueOrder
            .Select((product, index) => (product, index))
            .OrderByDescending(item => EstimateTripProfit(item.product, availableCredits, emptyHolds))
            .ThenBy(item => item.index)
            .Select(item => item.product);
        return preferredProduct is { } preferred
            ? [preferred, .. ranked.Where(product => product != preferred)]
            : [.. ranked];
    }

    public static TradeProduct? FindTradeProduct(
        int sellerClass,
        int buyerClass,
        TradeProduct? preferredProduct = null) =>
        FindMostProfitableProduct(sellerClass, buyerClass, preferredProduct, null, null);

    /// <summary>
    /// The compatible product with the best expected trip profit for the given credits and holds; an explicitly
    /// preferred product (e.g. Fuel Ore for the Dock Shopper) wins whenever it is compatible.
    /// </summary>
    public static TradeProduct? FindMostProfitableProduct(
        int sellerClass,
        int buyerClass,
        TradeProduct? preferredProduct,
        long? availableCredits,
        int? emptyHolds)
    {
        foreach (TradeProduct product in RankByProfit(preferredProduct, availableCredits, emptyHolds))
        {
            if (Sells(sellerClass, product) && Buys(buyerClass, product))
            {
                return product;
            }
        }

        return null;
    }

    public static TradeProduct? FindSellableCargo(int buyerClass, GameSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);

        foreach (TradeProduct product in Enum.GetValues<TradeProduct>())
        {
            int? quantity = product switch
            {
                TradeProduct.FuelOre => snapshot.FuelOre,
                TradeProduct.Organics => snapshot.Organics,
                TradeProduct.Equipment => snapshot.Equipment,
                _ => null
            };
            if (quantity is > 0 && Buys(buyerClass, product))
            {
                return product;
            }
        }

        return null;
    }

    public static bool SellsProduct(int portClass, TradeProduct product) => Sells(portClass, product);

    public static bool BuysProduct(int portClass, TradeProduct product) => Buys(portClass, product);

    private static bool Sells(int portClass, TradeProduct product) =>
        portClass is >= 1 and <= 8 && !Buys(portClass, product);

    private static bool Buys(int portClass, TradeProduct product) =>
        portClass switch
        {
            1 => product is TradeProduct.FuelOre or TradeProduct.Organics,
            2 => product is TradeProduct.FuelOre or TradeProduct.Equipment,
            3 => product is TradeProduct.Organics or TradeProduct.Equipment,
            4 => product is TradeProduct.Equipment,
            5 => product is TradeProduct.Organics,
            6 => product is TradeProduct.FuelOre,
            7 => false,
            8 => true,
            9 => true,
            _ => false
        };
}
