using LLMSurvivingProxyAgent.Core;
using System.Numerics;

namespace LLMSurvivingProxyAgent.Macros;

public sealed class PairPortTradeMacro : IMacro
{
    public MacroDescriptor Descriptor { get; } = new(
        "pair-port-trade",
        "Pair Port Trade",
        "Find a known pair of ports with complementary buy/sell profiles and plan routes between them.",
        MacroCategory.Trading,
        ["At least two known ports", "Port product profiles and directed warps are known"],
        "A candidate pair and round-trip navigation plan; no profit or trade is guaranteed.");

    public async Task<MacroCheck> CanExecuteAsync(
        MacroContext context,
        CancellationToken cancellationToken = default)
    {
        var ports = await GetPortsAsync(context.WorldStateStore, cancellationToken);
        return FindComplementaryPair(ports) is not null
            ? new MacroCheck(true, string.Empty)
            : new MacroCheck(false, "No complementary two-port buy/sell profile is known.");
    }

    public async Task<string> EstimateOutcomeAsync(
        MacroContext context,
        CancellationToken cancellationToken = default)
    {
        var pair = FindComplementaryPair(
            await GetPortsAsync(context.WorldStateStore, cancellationToken));
        return pair is null
            ? "No candidate pair can be estimated from the current port observations."
            : $"Potential pair: sectors {pair.Value.First.Id} and {pair.Value.Second.Id}; " +
              "server prices, inventory, holds, and profitability are not confirmed.";
    }

    public async Task<MacroResult> ExecuteAsync(
        MacroContext context,
        CancellationToken cancellationToken = default)
    {
        var pair = FindComplementaryPair(
            await GetPortsAsync(context.WorldStateStore, cancellationToken));
        if (pair is null)
        {
            return new MacroResult(
                MacroExecutionStatus.Blocked,
                "No complementary port pair was found.",
                "No trade commands were sent.",
                []);
        }

        var outbound = await context.NavigationService.FindShortestPathAsync(
            pair.Value.First.Id,
            pair.Value.Second.Id,
            cancellationToken: cancellationToken);
        var returning = await context.NavigationService.FindShortestPathAsync(
            pair.Value.Second.Id,
            pair.Value.First.Id,
            cancellationToken: cancellationToken);
        if (outbound is null || returning is null)
        {
            return new MacroResult(
                MacroExecutionStatus.Blocked,
                $"Complementary ports at sectors {pair.Value.First.Id} and {pair.Value.Second.Id} " +
                "do not have known routes in both directions.",
                "No trade commands were sent.",
                []);
        }

        var route = outbound.Sectors.Concat(returning.Sectors.Skip(1)).ToArray();
        return new MacroResult(
            MacroExecutionStatus.Planned,
            $"Candidate round trip: {string.Join(" -> ", route)}",
            "Planning only. Live stock, current prices, ship holds, reserves, and net profit have not been verified; no trade commands were sent.",
            route);
    }

    private static async Task<IReadOnlyList<Sector>> GetPortsAsync(
        IWorldStateStore worldStateStore,
        CancellationToken cancellationToken)
    {
        var ports = new List<Sector>();
        const int pageSize = 1000;
        for (var offset = 0; ; offset += pageSize)
        {
            var page = await worldStateStore.GetSectorsAsync(offset, pageSize, cancellationToken);
            ports.AddRange(page.Where(sector => sector.Port is { IsDead: false }));
            if (page.Count < pageSize)
            {
                break;
            }
        }
        return ports;
    }

    private static (Sector First, Sector Second)? FindComplementaryPair(
        IReadOnlyList<Sector> sectors)
    {
        var groups = sectors
            .Select(sector => (Sector: sector, Profile: sector.Port is null ? null : GetBuyProfile(sector.Port)))
            .Where(item => item.Profile.HasValue)
            .GroupBy(item => item.Profile!.Value, item => item.Sector)
            .ToDictionary(
                group => group.Key,
                group => group.OrderBy(sector => sector.Id).ToArray());

        foreach (var firstProfile in groups.Keys.Order())
        {
            foreach (var secondProfile in groups.Keys.Where(profile => profile > firstProfile).Order())
            {
                if (BitOperations.PopCount((uint)(firstProfile ^ secondProfile)) >= 1)
                {
                    return (groups[firstProfile][0], groups[secondProfile][0]);
                }
            }
        }
        return null;
    }

    internal static bool HasComplementaryProfiles(Port first, Port second)
    {
        var firstProfile = GetBuyProfile(first);
        var secondProfile = GetBuyProfile(second);
        return firstProfile.HasValue &&
            secondProfile.HasValue &&
            BitOperations.PopCount((uint)(firstProfile.Value ^ secondProfile.Value)) >= 1;
    }

    private static int? GetBuyProfile(Port port)
    {
        var profile = 0;
        var bit = 1;
        foreach (var product in Enum.GetValues<TradeProduct>())
        {
            if (!port.Products.TryGetValue(product, out var item))
            {
                return null;
            }
            if (item.BuysProduct)
            {
                profile |= bit;
            }
            bit <<= 1;
        }
        return profile;
    }
}
