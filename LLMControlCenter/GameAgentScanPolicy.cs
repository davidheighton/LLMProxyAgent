using System;
using System.Linq;
using LLMSurvivingProxyAgent.Core;

namespace MTC;

internal static class GameAgentScanPolicy
{
    private static readonly TimeSpan MaximumShipStatusAge = TimeSpan.FromSeconds(30);

    public static string? GetFreshScannerType(
        GameAgentContextSnapshot context,
        DateTimeOffset now)
    {
        GameAgentEvent? shipStatus = context.RecentEvents
            .Where(gameEvent => gameEvent.Kind == GameAgentEventKind.SynchronizedShipStatus)
            .OrderByDescending(gameEvent => gameEvent.Timestamp)
            .FirstOrDefault();

        return shipStatus is not null &&
               now >= shipStatus.Timestamp &&
               now - shipStatus.Timestamp <= MaximumShipStatusAge &&
               shipStatus.CurrentSector == context.CurrentSector &&
               shipStatus.Metadata.TryGetValue("scannerType", out string? scannerType) &&
               !string.IsNullOrWhiteSpace(scannerType)
            ? scannerType
            : null;
    }

    public static bool TrySelectScanType(
        string requestedScanType,
        string? scannerType,
        out string selectedScanType,
        out bool requiresScanChoice)
    {
        selectedScanType = string.Empty;
        requiresScanChoice = GameScannerType.IsHoloScanner(scannerType);
        if (string.Equals(requestedScanType, "H", StringComparison.OrdinalIgnoreCase))
        {
            if (requiresScanChoice)
                selectedScanType = "H";
            else if (GameScannerType.IsDensityScanner(scannerType))
                selectedScanType = "D";
        }
        else if (string.Equals(requestedScanType, "D", StringComparison.OrdinalIgnoreCase))
        {
            if (requiresScanChoice)
                selectedScanType = "D";
            else if (GameScannerType.IsDensityScanner(scannerType))
                selectedScanType = "D";
        }

        return selectedScanType.Length > 0;
    }
}
