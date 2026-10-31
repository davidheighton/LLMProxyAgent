namespace LLMSurvivingProxyAgent.Core;

public static class PortTradeCompatibility
{
    public static TradeProduct? FindTradeProduct(
        int sellerClass,
        int buyerClass,
        TradeProduct? preferredProduct = null)
    {
        if (preferredProduct is { } preferred &&
            Sells(sellerClass, preferred) &&
            Buys(buyerClass, preferred))
        {
            return preferred;
        }

        foreach (TradeProduct product in Enum.GetValues<TradeProduct>())
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
