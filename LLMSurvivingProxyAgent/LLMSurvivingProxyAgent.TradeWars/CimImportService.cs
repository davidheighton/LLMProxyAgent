using System.Globalization;
using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.TradeWars;

public sealed class CimImportService(IWorldStateStore worldStateStore)
{
    public async Task<CimImportReport> ImportPortCimAsync(
        IEnumerable<string> lines,
        DateTimeOffset importedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var universe = await worldStateStore.GetUniverseAsync(cancellationToken)
            ?? throw new InvalidOperationException("Save the universe metadata before importing CIM data.");
        var received = 0;
        var rejected = 0;
        var updated = new List<int>();
        var sectorsToSave = new List<Sector>();

        foreach (var line in lines)
        {
            received++;
            if (!TryParsePortLine(line, importedAt, out var observation) || observation is null)
            {
                rejected++;
                continue;
            }
            if (observation.SectorId > universe.SectorCount)
            {
                rejected++;
                continue;
            }

            var existing = await worldStateStore.GetSectorAsync(observation.SectorId, cancellationToken);
            var oldPort = existing?.Port;
            var port = new Port(
                observation.SectorId,
                oldPort?.Name ?? string.Empty,
                observation.DerivedClassIndex,
                oldPort?.IsDead ?? false,
                oldPort?.BuildTime ?? 0,
                observation.Products,
                observation.ObservedAt);
            var sector = existing is null
                ? NewSector(observation.SectorId, importedAt) with { Port = port }
                : existing with { Port = port, ObservedAt = importedAt };

            sectorsToSave.Add(sector);
            updated.Add(observation.SectorId);
        }
        await worldStateStore.UpsertSectorsAsync(sectorsToSave, cancellationToken);

        return new CimImportReport(
            CimDataKind.Port,
            received,
            updated.Count,
            rejected,
            updated);
    }

    public async Task<CimImportReport> ImportWarpCimAsync(
        IEnumerable<string> lines,
        DateTimeOffset importedAt,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lines);
        var universe = await worldStateStore.GetUniverseAsync(cancellationToken)
            ?? throw new InvalidOperationException("Save the universe metadata before importing CIM data.");
        var received = 0;
        var rejected = 0;
        var updated = new List<int>();
        var sectorsToSave = new List<Sector>();

        foreach (var line in lines)
        {
            received++;
            if (!TryParseWarpLine(line, importedAt, out var observation) || observation is null)
            {
                rejected++;
                continue;
            }
            if (observation.SectorId > universe.SectorCount ||
                observation.Warps.Any(warp => warp > universe.SectorCount))
            {
                rejected++;
                continue;
            }

            var existing = await worldStateStore.GetSectorAsync(observation.SectorId, cancellationToken);
            var exploration = existing?.Exploration ?? ExplorationStatus.Unknown;
            if (exploration == ExplorationStatus.Unknown)
            {
                exploration = ExplorationStatus.WarpOnly;
            }
            var sector = existing is null
                ? NewSector(observation.SectorId, importedAt) with
                {
                    Exploration = exploration,
                    WarpDataKnown = true,
                    Warps = observation.Warps
                }
                : existing with
                {
                    Exploration = exploration,
                    WarpDataKnown = true,
                    Warps = observation.Warps,
                    ObservedAt = importedAt,
                    WarpObservedAt = observation.ObservedAt
                };
            if (existing is null)
            {
                sector = sector with { WarpObservedAt = observation.ObservedAt };
            }

            sectorsToSave.Add(sector);
            updated.Add(observation.SectorId);
        }
        await worldStateStore.UpsertSectorsAsync(sectorsToSave, cancellationToken);

        return new CimImportReport(
            CimDataKind.Warp,
            received,
            updated.Count,
            rejected,
            updated);
    }

    public static bool TryParsePortLine(
        string line,
        DateTimeOffset observedAt,
        out CimPortObservation? observation)
    {
        observation = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length < 7 ||
            !int.TryParse(tokens[0], NumberStyles.None, CultureInfo.InvariantCulture, out var sectorId) ||
            sectorId < 1)
        {
            return false;
        }

        var products = new Dictionary<TradeProduct, PortProduct>();
        var buys = new bool[3];
        var amounts = new int[3];
        var percentages = new int[3];
        var tokenIndex = 1;

        for (var productIndex = 0; productIndex < 3; productIndex++)
        {
            if (tokenIndex >= tokens.Length)
            {
                return false;
            }

            var amountToken = tokens[tokenIndex++];
            if (amountToken == "-")
            {
                buys[productIndex] = true;
                if (tokenIndex >= tokens.Length)
                {
                    return false;
                }
                amountToken = tokens[tokenIndex++];
            }
            else if (amountToken.StartsWith("-", StringComparison.Ordinal))
            {
                buys[productIndex] = true;
                amountToken = amountToken[1..];
            }

            if (tokenIndex >= tokens.Length ||
                !int.TryParse(amountToken, NumberStyles.None, CultureInfo.InvariantCulture, out var amount) ||
                amount < 0)
            {
                return false;
            }

            var percentageToken = tokens[tokenIndex++];
            if (!percentageToken.EndsWith("%", StringComparison.Ordinal) ||
                !int.TryParse(
                    percentageToken[..^1],
                    NumberStyles.None,
                    CultureInfo.InvariantCulture,
                    out var percentage) ||
                percentage is < 0 or > 100)
            {
                return false;
            }

            amounts[productIndex] = Math.Min(amount, ushort.MaxValue);
            percentages[productIndex] = percentage;
        }

        if (tokenIndex != tokens.Length)
        {
            return false;
        }

        var productTypes = Enum.GetValues<TradeProduct>();
        for (var i = 0; i < productTypes.Length; i++)
        {
            products.Add(productTypes[i], new PortProduct(buys[i], amounts[i], percentages[i]));
        }

        observation = new CimPortObservation(
            sectorId,
            products,
            DerivePortClassIndex(buys),
            observedAt);
        return true;
    }

    public static bool TryParseWarpLine(
        string line,
        DateTimeOffset observedAt,
        out CimWarpObservation? observation)
    {
        observation = null;
        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var tokens = line.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
        if (tokens.Length is < 1 or > 7 ||
            !int.TryParse(tokens[0], NumberStyles.None, CultureInfo.InvariantCulture, out var sectorId) ||
            sectorId < 1)
        {
            return false;
        }

        var warps = new List<int>(Math.Max(0, tokens.Length - 1));
        for (var i = 1; i < tokens.Length; i++)
        {
            if (!int.TryParse(tokens[i], NumberStyles.None, CultureInfo.InvariantCulture, out var warp) ||
                warp < 0)
            {
                return false;
            }
            if (warp > 0)
            {
                warps.Add(warp);
            }
        }

        observation = new CimWarpObservation(sectorId, warps, observedAt);
        return true;
    }

    private static Sector NewSector(int sectorId, DateTimeOffset observedAt) =>
        new(
            sectorId,
            string.Empty,
            ExplorationStatus.Unknown,
            false,
            [],
            null,
            [],
            [],
            [],
            observedAt);

    private static int DerivePortClassIndex(IReadOnlyList<bool> buys)
    {
        var (fuel, organics, equipment) = (buys[0], buys[1], buys[2]);
        if (fuel && organics && !equipment) return 1;
        if (fuel && !organics && equipment) return 2;
        if (!fuel && organics && equipment) return 3;
        if (!fuel && !organics && equipment) return 4;
        if (!fuel && organics && !equipment) return 5;
        if (fuel && !organics && !equipment) return 6;
        if (!fuel && !organics && !equipment) return 7;
        if (fuel && organics && equipment) return 8;
        return 0;
    }
}
