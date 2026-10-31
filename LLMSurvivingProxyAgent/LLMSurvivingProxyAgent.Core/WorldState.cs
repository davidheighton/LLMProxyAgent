namespace LLMSurvivingProxyAgent.Core;

public sealed record Universe(
    int SectorCount,
    int? StarDockSector,
    DateTimeOffset? LastUpdated);

public enum ExplorationStatus
{
    Unknown,
    WarpOnly,
    Explored
}

public enum TradeProduct
{
    FuelOre,
    Organics,
    Equipment
}

public sealed record PortProduct(
    bool BuysProduct,
    int Amount,
    int Percent);

public sealed record Port(
    int SectorId,
    string Name,
    int ClassIndex,
    bool IsDead,
    int BuildTime,
    IReadOnlyDictionary<TradeProduct, PortProduct> Products,
    DateTimeOffset? ObservedAt);

public sealed record Sector(
    int Id,
    string Name,
    ExplorationStatus Exploration,
    bool WarpDataKnown,
    IReadOnlyList<int> Warps,
    Port? Port,
    IReadOnlyList<Planet> Planets,
    IReadOnlyList<Trader> Traders,
    IReadOnlyList<Ship> Ships,
    DateTimeOffset? ObservedAt)
{
    public DateTimeOffset? WarpObservedAt { get; init; }
}

public sealed record AdjacentSectorKnowledge(
    int SectorId,
    ExplorationStatus Exploration,
    bool WarpDataKnown,
    bool PortKnown);

public sealed record Planet(
    int Id,
    int SectorId,
    string Name,
    string Owner,
    int Level,
    bool? Shielded,
    int Fighters,
    int FuelOre,
    int Organics,
    int Equipment,
    DateTimeOffset? ObservedAt);

public sealed record Trader(
    string Name,
    int SectorId,
    string ShipType,
    string ShipName,
    int Fighters,
    int? CorporationId,
    DateTimeOffset? ObservedAt);

public sealed record Corporation(
    int Id,
    string Name,
    IReadOnlyList<string> Members,
    DateTimeOffset? ObservedAt);

public enum CimDataKind
{
    Port,
    Warp
}

public sealed record CimPortObservation(
    int SectorId,
    IReadOnlyDictionary<TradeProduct, PortProduct> Products,
    int DerivedClassIndex,
    DateTimeOffset ObservedAt);

public sealed record CimWarpObservation(
    int SectorId,
    IReadOnlyList<int> Warps,
    DateTimeOffset ObservedAt);

public sealed record CimImportReport(
    CimDataKind Kind,
    int LinesReceived,
    int LinesAccepted,
    int LinesRejected,
    IReadOnlyList<int> UpdatedSectors);

public sealed record Ship(
    string Name,
    string Owner,
    string ShipType,
    int SectorId,
    int Fighters,
    DateTimeOffset? ObservedAt);

public sealed record NavigationPath(IReadOnlyList<int> Sectors)
{
    public int HopCount => Math.Max(0, Sectors.Count - 1);
}

public sealed record GameSnapshot(
    int? CurrentSector,
    long? SynchronizedCredits,
    int? Turns,
    Ship? Ship,
    DateTimeOffset ObservedAt)
{
    public int? HoldsEmpty { get; init; }
    public int? HoldsTotal { get; init; }
    public int? FuelOre { get; init; }
    public int? Organics { get; init; }
    public int? Equipment { get; init; }
    public int? Shields { get; init; }
    public long? Experience { get; init; }
    public long? Alignment { get; init; }
    public int? CorporationId { get; init; }
    public bool? HoloScannerInstalled { get; init; }
    public bool? DensityScannerInstalled { get; init; }
    public string? CurrentPrompt { get; init; }
    public DateTimeOffset? CurrentPromptObservedAt { get; init; }
    public bool? Authenticated { get; init; }
}

public enum GamePresenceState
{
    Unknown,
    Connected,
    Disconnected,
    Stale
}

public enum GameAuthenticationState
{
    Unverified,
    Authenticated
}

public enum GameSynchronizationState
{
    Unavailable,
    Healthy,
    Stale
}

public sealed record GameSituationReport(
    DateTimeOffset CapturedAt,
    GamePresenceState Presence,
    GameAuthenticationState Authentication,
    GameSynchronizationState Synchronization,
    string? CurrentPrompt,
    DateTimeOffset? CurrentPromptObservedAt,
    GameSnapshot? Snapshot,
    IReadOnlyList<string> UnknownFields)
{
    public IReadOnlyList<ObservedGameText> RecentServerText { get; init; } = [];
    public GameScriptInputPrompt? ScriptInputPrompt { get; init; }
    public IntegratedGameClientSnapshot? IntegratedGameClient { get; init; }
}

public sealed record IntegratedGameSectorSnapshot(
    int Number,
    string Explored,
    string Constellation,
    string Beacon,
    int Density,
    IReadOnlyList<int> WarpsOut,
    string Port,
    IReadOnlyList<string> Planets,
    IReadOnlyList<string> Traders,
    IReadOnlyList<string> Ships,
    string Fighters,
    string ArmidMines,
    string LimpetMines);

public sealed record IntegratedGameScriptSnapshot(
    string Name,
    string Reference,
    bool Paused,
    bool IsSystemScript,
    bool IsBot);

public sealed record IntegratedGameScriptCapability(
    string Id,
    string Reference,
    string Purpose,
    bool MayStart);

public sealed record IntegratedGameScriptCatalogEntry(
    string File,
    string Category);

public sealed record IntegratedGameClientSnapshot(
    DateTimeOffset CapturedAt,
    bool TerminalConnected,
    bool TwxDatabaseAvailable,
    bool ScriptServiceAvailable,
    int? SectorCount,
    int? CurrentSector,
    int? StarDockSector,
    DateTimeOffset? LastPortCimAt,
    IReadOnlyList<IntegratedGameSectorSnapshot> RelevantSectors,
    IReadOnlyList<IntegratedGameScriptSnapshot> RunningScripts,
    IReadOnlyList<IntegratedGameScriptCapability> ScriptCapabilities)
{
    public string ScriptCatalogStatus { get; init; } = "Script catalog was not queried.";
    public int ScriptCatalogTotalEntries { get; init; }
    public IReadOnlyList<IntegratedGameScriptCatalogEntry> ScriptCatalogEntries { get; init; } = [];
}

public static class IntegratedGameClientLocationPolicy
{
    public static IntegratedGameClientSnapshot? UseSynchronizedShipLocation(
        GameSnapshot snapshot,
        IntegratedGameClientSnapshot? integratedGameClient)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        if (integratedGameClient?.CurrentSector is not { } integratedSector ||
            snapshot.CurrentSector is not { } synchronizedSector ||
            integratedSector == synchronizedSector)
        {
            return integratedGameClient;
        }

        return integratedGameClient with
        {
            CurrentSector = synchronizedSector,
            RelevantSectors = []
        };
    }
}

public sealed record ObservedGameText(DateTimeOffset ObservedAt, string Text);

public sealed record GameScriptInputPrompt(
    string ScriptName,
    string Text,
    bool SingleKey,
    DateTimeOffset ObservedAt);

public sealed record GameSituationObservationResult(
    GameSituationReport Report,
    bool Recorded,
    string? RejectionReason);

public sealed record GameSituationObservationRequest(string SessionId);

public static class GameSnapshotValidation
{
    public static bool IsValid(GameSnapshot? snapshot)
    {
        if (snapshot is null ||
            snapshot.ObservedAt == default ||
            snapshot.CurrentSector is <= 0 ||
            snapshot.SynchronizedCredits is < 0 ||
            snapshot.Turns is < 0 ||
            snapshot.HoldsEmpty is < 0 ||
            snapshot.HoldsTotal is < 0 ||
            snapshot.FuelOre is < 0 ||
            snapshot.Organics is < 0 ||
            snapshot.Equipment is < 0 ||
            snapshot.Shields is < 0 ||
            snapshot.Experience is < 0 ||
            snapshot.CorporationId is < 0 ||
            snapshot.HoldsEmpty is { } emptyHolds &&
            snapshot.HoldsTotal is { } totalHolds &&
            emptyHolds > totalHolds ||
            snapshot.CurrentPromptObservedAt is { } promptObservedAt &&
            promptObservedAt > DateTimeOffset.UtcNow.AddMinutes(5) ||
            snapshot.ObservedAt > DateTimeOffset.UtcNow.AddMinutes(5))
        {
            return false;
        }

        return snapshot.Ship is not { } ship ||
            (!string.IsNullOrWhiteSpace(ship.ShipType) &&
             ship.SectorId > 0 &&
             ship.Fighters >= 0 &&
             (snapshot.CurrentSector is not { } sector || ship.SectorId == sector) &&
             (ship.ObservedAt is not { } observedAt || observedAt <= snapshot.ObservedAt));
    }
}
