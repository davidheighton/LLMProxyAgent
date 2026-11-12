using System;
using System.IO;
using LLMSurvivingProxyAgent.Core;

namespace MTC;

internal static class WorldTradeScriptStartPolicy
{
    internal const string ScriptReference = "trading-cashing/2-worldtrade.ts";

    public static bool IsWorldTradeScriptReference(string? scriptReference) =>
        string.Equals(
            (scriptReference ?? string.Empty).Trim().Replace('\\', '/').TrimStart('/'),
            ScriptReference,
            StringComparison.OrdinalIgnoreCase);

    public static string? GetBlockReason(
        string scriptReference,
        GameAgentContextSnapshot context,
        int? starDockSector,
        DateTimeOffset now)
    {
        ArgumentNullException.ThrowIfNull(context);

        string fileName = Path.GetFileName(scriptReference.Replace('\\', '/'));
        if (!fileName.Contains("worldtrade", StringComparison.OrdinalIgnoreCase) &&
            !string.Equals(fileName, "1_Trade.ts", StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (!context.Connected ||
            !string.Equals(context.CurrentPrompt, "Command", StringComparison.OrdinalIgnoreCase))
        {
            return "World Trade requires an authenticated TradeWars Command prompt.";
        }

        if (starDockSector is not > 0)
            return "World Trade is blocked until the StarDock sector is verified.";

        if (context.CurrentSector != starDockSector)
            return $"World Trade can only start at verified StarDock sector {starDockSector}.";

        string? scannerType = GameAgentScanPolicy.GetFreshScannerType(context, now);
        if (scannerType is null)
        {
            return "World Trade is blocked until a fresh synchronized ship status is available at StarDock.";
        }

        if (!GameScannerType.HasLongRangeScanner(scannerType))
        {
            return "World Trade requires verified Density or Holo Scanner ownership.";
        }

        return null;
    }

    public static bool IsDensityOnlyScanner(
        GameAgentContextSnapshot context,
        DateTimeOffset now)
    {
        string? scannerType = GameAgentScanPolicy.GetFreshScannerType(context, now);
        return scannerType is not null &&
               GameScannerType.IsDensityScanner(scannerType) &&
               !GameScannerType.IsHoloScanner(scannerType);
    }
}
