using System;

namespace LLMSurvivingProxyAgent.Core;

public static class GameScannerType
{
    public static bool IsDensityScanner(string? scannerType) =>
        scannerType?.Contains("Dens", StringComparison.OrdinalIgnoreCase) == true;

    public static bool IsHoloScanner(string? scannerType) =>
        scannerType?.Contains("Holo", StringComparison.OrdinalIgnoreCase) == true;

    public static bool HasLongRangeScanner(string? scannerType) =>
        IsDensityScanner(scannerType) || IsHoloScanner(scannerType);
}
