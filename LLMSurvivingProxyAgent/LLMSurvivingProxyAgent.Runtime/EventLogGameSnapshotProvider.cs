using System.Globalization;
using System.Text.Json;
using LLMSurvivingProxyAgent.Core;

namespace LLMSurvivingProxyAgent.Runtime;

public class EventLogGameSnapshotProvider :
    ISynchronizedGameSnapshotProvider,
    IGameSituationProvider
{
    private static readonly TimeSpan MaximumSnapshotAge = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MaximumPresenceHeartbeatAge = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaximumPromptAge = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MaximumMenuTextAge = TimeSpan.FromMinutes(2);
    private const int MaximumRecentServerTextLines = 32;
    private const int MaximumServerTextLineLength = 500;
    private readonly IReadOnlyDictionary<string, string> _eventLogPaths;

    public EventLogGameSnapshotProvider(IReadOnlyDictionary<string, string> eventLogPaths)
    {
        ArgumentNullException.ThrowIfNull(eventLogPaths);
        _eventLogPaths = eventLogPaths.ToDictionary(
            entry =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(entry.Key);
                return entry.Key;
            },
            entry =>
            {
                ArgumentException.ThrowIfNullOrWhiteSpace(entry.Value);
                return Path.GetFullPath(entry.Value);
            },
            StringComparer.OrdinalIgnoreCase);
    }

    public sealed class MtcEventLogGameSnapshotProvider : EventLogGameSnapshotProvider
    {
        public MtcEventLogGameSnapshotProvider(IReadOnlyDictionary<string, string> eventLogPaths)
            : base(eventLogPaths)
        {
        }
    }

    public async Task<GameSnapshot?> CaptureSynchronizedSnapshotAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        var situation = await CaptureSituationAsync(agentId, cancellationToken);
        return situation.Synchronization == GameSynchronizationState.Healthy
            ? situation.Snapshot
            : null;
    }

    public async Task<GameSituationReport> CaptureSituationAsync(
        string agentId,
        CancellationToken cancellationToken = default) =>
        await CaptureSituationAsync(agentId, cancellationToken, retryAttempt: 0);

    private async Task<GameSituationReport> CaptureSituationAsync(
        string agentId,
        CancellationToken cancellationToken,
        int retryAttempt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        cancellationToken.ThrowIfCancellationRequested();

        var unknownAgent = !_eventLogPaths.TryGetValue(agentId, out var configuredPath);
        IReadOnlyList<string> eventFiles = unknownAgent
            ? []
            : ResolveEventFiles(configuredPath!);
        var presence = GamePresenceState.Unknown;
        var authentication = GameAuthenticationState.Unverified;
        var connectionEventIndex = -1;
        var lastHeartbeatIndex = -1;
        DateTimeOffset? lastHeartbeatAt = null;
        var lastSectorChangeIndex = -1;
        var lastShipStatusIndex = -1;
        DateTimeOffset? lastShipStatusObservedAt = null;
        GameSnapshot? latestSnapshot = null;
        bool? observedHoloScannerInstalled = null;
        bool? observedDensityScannerInstalled = null;
        string? scannerShipName = null;
        string? scannerShipType = null;
        string? currentPrompt = null;
        DateTimeOffset? currentPromptObservedAt = null;
        var recentServerText = new List<ObservedGameText>();
        var eventIndex = 0;

        foreach (var eventFile in eventFiles)
        {
            EventLogFileSnapshot eventLogSnapshot =
                await ReadCompleteEventLinesAsync(eventFile, cancellationToken);
            for (int lineIndex = 0; lineIndex < eventLogSnapshot.Lines.Count; lineIndex++)
            {
                string line = eventLogSnapshot.Lines[lineIndex];
                cancellationToken.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(line))
                {
                    continue;
                }

                JsonDocument document;
                try
                {
                    document = ParseEvent(line, eventFile, lineIndex + 1);
                }
                catch (InvalidDataException) when (
                    retryAttempt < 2 &&
                    HasEventLogGrown(eventFile, eventLogSnapshot.Length))
                {
                    await Task.Delay(
                        TimeSpan.FromMilliseconds(25 * (retryAttempt + 1)),
                        cancellationToken);
                    return await CaptureSituationAsync(
                        agentId,
                        cancellationToken,
                        retryAttempt + 1);
                }

                using (document)
                {
                var root = document.RootElement;
                var kind = GetEventKind(root);
                switch (kind)
                {
                    case "Connected":
                        presence = GamePresenceState.Connected;
                        authentication = GameAuthenticationState.Unverified;
                        connectionEventIndex = eventIndex;
                        lastHeartbeatIndex = -1;
                        lastHeartbeatAt = null;
                        currentPrompt = null;
                        currentPromptObservedAt = null;
                        recentServerText.Clear();
                        break;
                    case "Disconnected":
                        presence = GamePresenceState.Disconnected;
                        authentication = GameAuthenticationState.Unverified;
                        connectionEventIndex = eventIndex;
                        lastHeartbeatIndex = -1;
                        lastHeartbeatAt = null;
                        currentPrompt = null;
                        currentPromptObservedAt = null;
                        recentServerText.Clear();
                        break;
                    case "ServerLine":
                        if (presence == GamePresenceState.Connected &&
                            eventIndex > connectionEventIndex &&
                            authentication == GameAuthenticationState.Authenticated &&
                            GetOptionalTimestamp(root, "Timestamp") is { } lineObservedAt &&
                            lineObservedAt <= DateTimeOffset.UtcNow &&
                            DateTimeOffset.UtcNow - lineObservedAt <= MaximumMenuTextAge &&
                            GetOptionalString(root, "PlainText") is { } serverText &&
                            !string.IsNullOrWhiteSpace(serverText))
                        {
                            string normalizedText = serverText.Trim();
                            if (normalizedText.Length > MaximumServerTextLineLength)
                            {
                                normalizedText = normalizedText[..MaximumServerTextLineLength];
                            }
                            recentServerText.Add(new ObservedGameText(lineObservedAt, normalizedText));
                            if (recentServerText.Count > MaximumRecentServerTextLines)
                            {
                                recentServerText.RemoveAt(0);
                            }
                        }
                        if (IsMissingLongRangeScannerMessage(GetOptionalString(root, "PlainText")))
                        {
                            observedHoloScannerInstalled = false;
                            observedDensityScannerInstalled = false;
                            scannerShipName = latestSnapshot?.Ship?.Name;
                            scannerShipType = latestSnapshot?.Ship?.ShipType;
                            if (latestSnapshot is not null)
                            {
                                latestSnapshot = latestSnapshot with
                                {
                                    HoloScannerInstalled = false,
                                    DensityScannerInstalled = false
                                };
                            }
                        }
                        break;
                    case "CurrentSectorChanged":
                        lastSectorChangeIndex = eventIndex;
                        break;
                    case "ServerPrompt":
                        if (presence != GamePresenceState.Connected ||
                            eventIndex <= connectionEventIndex)
                        {
                            break;
                        }
                        string? observedPrompt = GetOptionalString(root, "PromptSurface");
                        currentPrompt =
                            observedPrompt is not null &&
                            TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(
                                observedPrompt,
                                out string classifiedPrompt)
                                ? classifiedPrompt
                                : observedPrompt;
                        currentPromptObservedAt = GetOptionalTimestamp(root, "Timestamp");
                        if (IsAuthenticatedGamePrompt(currentPrompt))
                        {
                            authentication = GameAuthenticationState.Authenticated;
                        }
                        break;
                    case "PresenceHeartbeat":
                        if (presence == GamePresenceState.Connected &&
                            eventIndex > connectionEventIndex &&
                            IsConnectedHeartbeat(root))
                        {
                            lastHeartbeatIndex = eventIndex;
                            lastHeartbeatAt = GetOptionalTimestamp(root, "Timestamp");
                            string? heartbeatPrompt = GetOptionalString(root, "PromptSurface");
                            DateTimeOffset? heartbeatPromptObservedAt =
                                GetHeartbeatPromptObservedAt(root);
                            bool heartbeatConfirmsCurrentPrompt =
                                IsHeartbeatCurrentPromptConfirmed(root);
                            DateTimeOffset now = DateTimeOffset.UtcNow;
                            if (heartbeatPrompt is not null &&
                                TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(
                                    heartbeatPrompt,
                                    out string heartbeatClassifiedPrompt) &&
                                heartbeatPromptObservedAt is { } promptObservedAt &&
                                promptObservedAt <= now &&
                                (heartbeatConfirmsCurrentPrompt
                                    ? lastHeartbeatAt is { } freshHeartbeatAt &&
                                      freshHeartbeatAt <= now &&
                                      now - freshHeartbeatAt <= MaximumPresenceHeartbeatAge
                                    : now - promptObservedAt <= MaximumPromptAge))
                            {
                                currentPrompt = heartbeatClassifiedPrompt;
                                currentPromptObservedAt = heartbeatConfirmsCurrentPrompt
                                    ? lastHeartbeatAt
                                    : promptObservedAt;
                                authentication = IsAuthenticatedGamePrompt(currentPrompt)
                                    ? GameAuthenticationState.Authenticated
                                    : GameAuthenticationState.Unverified;
                            }
                        }
                        break;
                    case "SynchronizedShipStatus":
                        DateTimeOffset? statusObservedAt = GetOptionalTimestamp(root, "Timestamp");
                        GameSnapshot? statusSnapshot = TryCreateSnapshot(root);
                        if (statusSnapshot is null &&
                            !TryMergePartialShipStatus(root, statusObservedAt, latestSnapshot, out statusSnapshot))
                        {
                            break;
                        }

                        lastShipStatusIndex = eventIndex;
                        lastShipStatusObservedAt = statusObservedAt;
                        latestSnapshot = statusSnapshot;
                        if (latestSnapshot is { } shipSnapshot)
                        {
                            if (shipSnapshot.HoloScannerInstalled is not null ||
                                shipSnapshot.DensityScannerInstalled is not null)
                            {
                                observedHoloScannerInstalled = shipSnapshot.HoloScannerInstalled;
                                observedDensityScannerInstalled = shipSnapshot.DensityScannerInstalled;
                                scannerShipName = shipSnapshot.Ship?.Name;
                                scannerShipType = shipSnapshot.Ship?.ShipType;
                            }
                            else if (observedHoloScannerInstalled is not null ||
                                     observedDensityScannerInstalled is not null)
                            {
                                if (string.IsNullOrWhiteSpace(scannerShipName) ||
                                    string.IsNullOrWhiteSpace(scannerShipType))
                                {
                                    scannerShipName = shipSnapshot.Ship?.Name;
                                    scannerShipType = shipSnapshot.Ship?.ShipType;
                                    latestSnapshot = shipSnapshot with
                                    {
                                        HoloScannerInstalled =
                                            observedHoloScannerInstalled ?? shipSnapshot.HoloScannerInstalled,
                                        DensityScannerInstalled =
                                            observedDensityScannerInstalled ?? shipSnapshot.DensityScannerInstalled
                                    };
                                }
                                else if (IsSameShip(
                                    shipSnapshot,
                                    scannerShipName,
                                    scannerShipType))
                                {
                                    latestSnapshot = shipSnapshot with
                                    {
                                        HoloScannerInstalled =
                                            observedHoloScannerInstalled ?? shipSnapshot.HoloScannerInstalled,
                                        DensityScannerInstalled =
                                            observedDensityScannerInstalled ?? shipSnapshot.DensityScannerInstalled
                                    };
                                }
                                else
                                {
                                    observedHoloScannerInstalled = null;
                                    observedDensityScannerInstalled = null;
                                    scannerShipName = null;
                                    scannerShipType = null;
                                }
                            }
                        }
                        break;
                }

                eventIndex++;
                }
            }
        }

        if (presence == GamePresenceState.Connected &&
            (lastHeartbeatIndex <= connectionEventIndex ||
             lastHeartbeatAt is not { } heartbeatAt ||
             heartbeatAt > DateTimeOffset.UtcNow ||
             DateTimeOffset.UtcNow - heartbeatAt > MaximumPresenceHeartbeatAge))
        {
            presence = GamePresenceState.Stale;
            authentication = GameAuthenticationState.Unverified;
        }

        var synchronization = ResolveSynchronization(
            presence,
            connectionEventIndex,
            lastSectorChangeIndex,
            lastShipStatusIndex,
            lastShipStatusObservedAt,
            latestSnapshot);
        if (synchronization != GameSynchronizationState.Healthy)
        {
            latestSnapshot = null;
        }
        else
        {
            latestSnapshot = latestSnapshot! with
            {
                CurrentPrompt = currentPrompt,
                CurrentPromptObservedAt = currentPromptObservedAt,
                Authenticated = authentication == GameAuthenticationState.Authenticated
            };
        }

        var report = new GameSituationReport(
            DateTimeOffset.UtcNow,
            presence,
            authentication,
            synchronization,
            currentPrompt,
            currentPromptObservedAt,
            latestSnapshot,
            BuildUnknownFields(
                latestSnapshot,
                currentPrompt,
                presence,
                authentication,
                synchronization))
        {
            RecentServerText = authentication == GameAuthenticationState.Authenticated
                ? recentServerText
                    .Where(line =>
                        line.ObservedAt <= DateTimeOffset.UtcNow &&
                        DateTimeOffset.UtcNow - line.ObservedAt <= MaximumMenuTextAge)
                    .ToArray()
                : []
        };
        return report;
    }

    private static async Task<EventLogFileSnapshot> ReadCompleteEventLinesAsync(
        string eventFile,
        CancellationToken cancellationToken)
    {
        for (int attempt = 0; attempt < 3; attempt++)
        {
            await using var stream = new FileStream(
                eventFile,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete,
                bufferSize: 4096,
                FileOptions.Asynchronous | FileOptions.SequentialScan);
            if (stream.Length > int.MaxValue)
            {
                throw new InvalidDataException(
                    $"Game event log '{eventFile}' is too large to read safely.");
            }

            byte[] contents = new byte[(int)stream.Length];
            await stream.ReadExactlyAsync(contents, cancellationToken);
            bool complete = contents.Length == 0 ||
                contents[^1] is (byte)'\n' or (byte)'\r';
            if (!complete && attempt < 2)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(25 * (attempt + 1)), cancellationToken);
                continue;
            }

            if (!complete)
            {
                int lastLineEnding = Array.FindLastIndex(
                    contents,
                    static value => value is (byte)'\n' or (byte)'\r');
                Array.Resize(ref contents, lastLineEnding + 1);
            }

            using var contentStream = new MemoryStream(contents, writable: false);
            using var reader = new StreamReader(contentStream);
            var lines = new List<string>();
            while (await reader.ReadLineAsync(cancellationToken) is { } line)
            {
                lines.Add(line);
            }

            return new EventLogFileSnapshot(lines, contents.LongLength);
        }

        throw new InvalidOperationException("The game event log could not be read.");
    }

    private static bool HasEventLogGrown(string eventFile, long snapshotLength) =>
        File.Exists(eventFile) &&
        new FileInfo(eventFile).Length > snapshotLength;

    private sealed record EventLogFileSnapshot(IReadOnlyList<string> Lines, long Length);

    private static GameSynchronizationState ResolveSynchronization(
        GamePresenceState presence,
        int connectionEventIndex,
        int lastSectorChangeIndex,
        int lastShipStatusIndex,
        DateTimeOffset? lastShipStatusObservedAt,
        GameSnapshot? latestSnapshot)
    {
        if (lastShipStatusObservedAt is not { } observedAt)
        {
            return GameSynchronizationState.Unavailable;
        }

        if (presence != GamePresenceState.Connected ||
            lastShipStatusIndex <= connectionEventIndex ||
            latestSnapshot is null)
        {
            return GameSynchronizationState.Unavailable;
        }

        if (lastShipStatusIndex < lastSectorChangeIndex ||
            observedAt > DateTimeOffset.UtcNow ||
            DateTimeOffset.UtcNow - observedAt > MaximumSnapshotAge)
        {
            return GameSynchronizationState.Stale;
        }

        if (!GameSnapshotValidation.IsValid(latestSnapshot))
        {
            throw new InvalidDataException(
                "The latest synchronized ship-status event contains invalid game state.");
        }

        return GameSynchronizationState.Healthy;
    }

    private static IReadOnlyList<string> BuildUnknownFields(
        GameSnapshot? snapshot,
        string? currentPrompt,
        GamePresenceState presence,
        GameAuthenticationState authentication,
        GameSynchronizationState synchronization)
    {
        var unknownFields = new List<string>();
        if (presence is GamePresenceState.Unknown or GamePresenceState.Stale)
        {
            unknownFields.Add("Connection presence");
        }
        if (synchronization != GameSynchronizationState.Healthy)
        {
            unknownFields.Add("Synchronization health");
        }
        if (snapshot?.CurrentSector is null)
        {
            unknownFields.Add("Current sector");
        }
        if (snapshot?.SynchronizedCredits is null)
        {
            unknownFields.Add("Credits");
        }
        if (snapshot?.Turns is null)
        {
            unknownFields.Add("Turns");
        }
        if (snapshot?.Ship?.ShipType is null or "")
        {
            unknownFields.Add("Ship type");
        }
        if (snapshot?.Ship is null)
        {
            unknownFields.Add("Fighters");
        }
        if (snapshot?.HoldsEmpty is null || snapshot.HoldsTotal is null)
        {
            unknownFields.Add("Holds");
        }
        if (snapshot?.Experience is null)
        {
            unknownFields.Add("Experience");
        }
        if (snapshot?.Alignment is null)
        {
            unknownFields.Add("Alignment");
        }
        if (snapshot?.CorporationId is null)
        {
            unknownFields.Add("Corporation status");
        }
        if (snapshot?.HoloScannerInstalled is null)
        {
            unknownFields.Add("Holo Scanner");
        }
        if (snapshot?.DensityScannerInstalled is null)
        {
            unknownFields.Add("Density Scanner");
        }
        if (string.IsNullOrWhiteSpace(currentPrompt))
        {
            unknownFields.Add("Current prompt");
        }
        if (authentication != GameAuthenticationState.Authenticated)
        {
            unknownFields.Add("Authentication state");
        }

        return unknownFields;
    }

    private static IReadOnlyList<string> ResolveEventFiles(string configuredPath)
    {
        if (Directory.Exists(configuredPath))
        {
            return Directory
                .EnumerateFiles(configuredPath, "events-*.jsonl", SearchOption.TopDirectoryOnly)
                .Order(StringComparer.OrdinalIgnoreCase)
                .ToArray();
        }

        if (File.Exists(configuredPath))
        {
            return [configuredPath];
        }

        throw new FileNotFoundException(
            $"The configured game event-log path '{configuredPath}' does not exist.",
            configuredPath);
    }

    private static JsonDocument ParseEvent(string line, string eventFile, int lineNumber)
    {
        try
        {
            return JsonDocument.Parse(line);
        }
        catch (JsonException exception)
        {
            throw new InvalidDataException(
                $"Game event log '{eventFile}' contains invalid JSON at line {lineNumber}.",
                exception);
        }
    }

    private static string GetEventKind(JsonElement root)
    {
        if (!TryGetProperty(root, "Kind", out var kind))
        {
            throw new InvalidDataException("A game event-log entry is missing its event kind.");
        }

        if (kind.ValueKind == JsonValueKind.String)
        {
            return kind.GetString() ?? string.Empty;
        }

        if (kind.ValueKind == JsonValueKind.Number && kind.TryGetInt32(out var numericKind))
        {
            return numericKind switch
            {
                0 => "ServerLine",
                1 => "ServerPrompt",
                3 => "Connected",
                4 => "Disconnected",
                5 => "CurrentSectorChanged",
                8 => "SynchronizedShipStatus",
                9 => "PresenceHeartbeat",
                _ => string.Empty
            };
        }

        throw new InvalidDataException("A game event-log entry has an unsupported event kind.");
    }

    private static bool IsMissingLongRangeScannerMessage(string? text) =>
        text?.Trim().Equals(
            "You don't have a long range scanner.",
            StringComparison.OrdinalIgnoreCase) == true ||
        text?.Trim().Equals(
            "You don't have a long range scanner",
            StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsSameShip(
        GameSnapshot snapshot,
        string? shipName,
        string? shipType) =>
        snapshot.Ship is { } ship &&
        !string.IsNullOrWhiteSpace(shipName) &&
        !string.IsNullOrWhiteSpace(shipType) &&
        string.Equals(shipName, ship.Name, StringComparison.OrdinalIgnoreCase) &&
        string.Equals(shipType, ship.ShipType, StringComparison.OrdinalIgnoreCase);

    private static GameSnapshot? TryCreateSnapshot(JsonElement root)
    {
        if (!TryGetProperty(root, "Timestamp", out var timestampElement) ||
            !timestampElement.TryGetDateTimeOffset(out var observedAt) ||
            !TryGetProperty(root, "CurrentSector", out var sectorElement) ||
            !sectorElement.TryGetInt32(out var sector) ||
            !TryGetProperty(root, "Metadata", out var metadata) ||
            !TryGetMetadataInt32(metadata, "currentSector", out var observedSector) ||
            observedSector != sector ||
            !TryGetMetadataInt64(metadata, "credits", out var credits) ||
            !TryGetMetadataInt32(metadata, "turns", out var turns) ||
            !TryGetMetadataInt32(metadata, "fighters", out var fighters) ||
            !TryGetMetadataString(metadata, "shipType", out var shipType) ||
            string.IsNullOrWhiteSpace(shipType))
        {
            return null;
        }

        TryGetMetadataString(metadata, "shipName", out var shipName);
        TryGetMetadataInt32(metadata, "holdsEmpty", out var holdsEmpty);
        TryGetMetadataInt32(metadata, "holdsTotal", out var holdsTotal);
        TryGetMetadataInt32(metadata, "fuelOre", out var fuelOre);
        TryGetMetadataInt32(metadata, "organics", out var organics);
        TryGetMetadataInt32(metadata, "equipment", out var equipment);
        TryGetMetadataInt32(metadata, "shields", out var shields);
        TryGetMetadataInt64(metadata, "experience", out var experience);
        TryGetMetadataInt64(metadata, "alignment", out var alignment);
        TryGetMetadataInt32(metadata, "corporationId", out var corporationId);

        var snapshot = new GameSnapshot(
            sector,
            credits,
            turns,
            new Ship(
                shipName ?? string.Empty,
                string.Empty,
                shipType,
                sector,
                fighters,
                observedAt),
            observedAt);

        return snapshot with
        {
            HoldsEmpty = TryGetMetadataInt32(metadata, "holdsEmpty", out _) ? holdsEmpty : null,
            HoldsTotal = TryGetMetadataInt32(metadata, "holdsTotal", out _) ? holdsTotal : null,
            FuelOre = TryGetMetadataInt32(metadata, "fuelOre", out _) ? fuelOre : null,
            Organics = TryGetMetadataInt32(metadata, "organics", out _) ? organics : null,
            Equipment = TryGetMetadataInt32(metadata, "equipment", out _) ? equipment : null,
            Shields = TryGetMetadataInt32(metadata, "shields", out _) ? shields : null,
            Experience = TryGetMetadataInt64(metadata, "experience", out _) ? experience : null,
            Alignment = TryGetMetadataInt64(metadata, "alignment", out _) ? alignment : null,
            CorporationId = TryGetMetadataInt32(metadata, "corporationId", out _) ? corporationId : null,
            HoloScannerInstalled = TryGetMetadataString(metadata, "scannerType", out var scannerType)
                ? GameScannerType.IsHoloScanner(scannerType)
                : null,
            DensityScannerInstalled = TryGetMetadataString(metadata, "scannerType", out scannerType)
                ? GameScannerType.IsDensityScanner(scannerType)
                : null
        };
    }

    private static bool TryMergePartialShipStatus(
        JsonElement root,
        DateTimeOffset? observedAt,
        GameSnapshot? previousSnapshot,
        out GameSnapshot? mergedSnapshot)
    {
        mergedSnapshot = null;
        if (previousSnapshot?.Ship is not { } previousShip ||
            string.IsNullOrWhiteSpace(previousShip.ShipType) ||
            observedAt is not { } statusObservedAt ||
            statusObservedAt > DateTimeOffset.UtcNow ||
            DateTimeOffset.UtcNow - statusObservedAt > MaximumSnapshotAge ||
            statusObservedAt < previousSnapshot.ObservedAt ||
            !TryGetProperty(root, "CurrentSector", out var sectorElement) ||
            !sectorElement.TryGetInt32(out int sector) ||
            sector != previousSnapshot.CurrentSector ||
            !TryGetProperty(root, "Metadata", out var metadata) ||
            !TryGetMetadataInt32(metadata, "currentSector", out int observedSector) ||
            observedSector != sector ||
            !TryGetMetadataInt64(metadata, "credits", out long credits) ||
            !TryGetMetadataInt32(metadata, "turns", out int turns) ||
            !TryGetMetadataInt32(metadata, "fighters", out int fighters) ||
            !TryGetMetadataString(metadata, "scannerType", out string? scannerType))
        {
            return false;
        }

        mergedSnapshot = previousSnapshot with
        {
            CurrentSector = sector,
            SynchronizedCredits = credits,
            Turns = turns,
            Ship = previousShip with
            {
                SectorId = sector,
                Fighters = fighters,
                ObservedAt = statusObservedAt
            },
            ObservedAt = statusObservedAt,
            HoldsEmpty = TryGetMetadataInt32(metadata, "holdsEmpty", out int holdsEmpty)
                ? holdsEmpty
                : previousSnapshot.HoldsEmpty,
            HoldsTotal = TryGetMetadataInt32(metadata, "holdsTotal", out int holdsTotal)
                ? holdsTotal
                : previousSnapshot.HoldsTotal,
            FuelOre = TryGetMetadataInt32(metadata, "fuelOre", out int fuelOre)
                ? fuelOre
                : previousSnapshot.FuelOre,
            Organics = TryGetMetadataInt32(metadata, "organics", out int organics)
                ? organics
                : previousSnapshot.Organics,
            Equipment = TryGetMetadataInt32(metadata, "equipment", out int equipment)
                ? equipment
                : previousSnapshot.Equipment,
            Shields = TryGetMetadataInt32(metadata, "shields", out int shields)
                ? shields
                : previousSnapshot.Shields,
            Experience = TryGetMetadataInt64(metadata, "experience", out long experience)
                ? experience
                : previousSnapshot.Experience,
            Alignment = TryGetMetadataInt64(metadata, "alignment", out long alignment)
                ? alignment
                : previousSnapshot.Alignment,
            CorporationId = TryGetMetadataInt32(metadata, "corporationId", out int corporationId)
                ? corporationId
                : previousSnapshot.CorporationId,
            HoloScannerInstalled = GameScannerType.IsHoloScanner(scannerType),
            DensityScannerInstalled = GameScannerType.IsDensityScanner(scannerType)
        };
        return true;
    }

    private static bool IsAuthenticatedGamePrompt(string? prompt) =>
        prompt is not null &&
        (prompt.Equals("Command", StringComparison.OrdinalIgnoreCase) ||
         prompt.Equals("StopInSector", StringComparison.OrdinalIgnoreCase) ||
         prompt.Equals("AutopilotConfirmation", StringComparison.OrdinalIgnoreCase) ||
         prompt.Equals("PortTradeQuantity", StringComparison.OrdinalIgnoreCase) ||
         prompt.Equals("Citadel", StringComparison.OrdinalIgnoreCase) ||
         prompt.Equals("Computer", StringComparison.OrdinalIgnoreCase) ||
         prompt.Equals("Corporate", StringComparison.OrdinalIgnoreCase) ||
         prompt.Equals("Planet", StringComparison.OrdinalIgnoreCase) ||
         prompt.Equals("Port", StringComparison.OrdinalIgnoreCase) ||
         prompt.Equals("Sub-space radio", StringComparison.OrdinalIgnoreCase));

    private static bool IsConnectedHeartbeat(JsonElement root) =>
        TryGetProperty(root, "Metadata", out var metadata) &&
        TryGetMetadataString(metadata, "connected", out var connected) &&
        bool.TryParse(connected, out var isConnected) &&
        isConnected;

    private static DateTimeOffset? GetOptionalTimestamp(JsonElement root, string name) =>
        TryGetProperty(root, name, out var element) && element.TryGetDateTimeOffset(out var value)
            ? value
            : null;

    private static DateTimeOffset? GetHeartbeatPromptObservedAt(JsonElement root) =>
        TryGetProperty(root, "Metadata", out var metadata) &&
        TryGetMetadataString(metadata, "promptObservedAt", out var value) &&
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out var observedAt)
            ? observedAt
            : null;

    private static bool IsHeartbeatCurrentPromptConfirmed(JsonElement root) =>
        TryGetProperty(root, "Metadata", out var metadata) &&
        TryGetMetadataString(metadata, "promptIsCurrent", out var value) &&
        bool.TryParse(value, out var isCurrent) &&
        isCurrent;

    private static string? GetOptionalString(JsonElement root, string name) =>
        TryGetProperty(root, name, out var element) && element.ValueKind == JsonValueKind.String
            ? element.GetString()
            : null;

    private static bool TryGetMetadataInt32(JsonElement metadata, string key, out int value)
    {
        value = default;
        return TryGetMetadataString(metadata, key, out var text) &&
            int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetMetadataInt64(JsonElement metadata, string key, out long value)
    {
        value = default;
        return TryGetMetadataString(metadata, key, out var text) &&
            long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryGetMetadataString(JsonElement metadata, string key, out string? value)
    {
        value = null;
        if (metadata.ValueKind != JsonValueKind.Object ||
            !TryGetProperty(metadata, key, out var element) ||
            element.ValueKind != JsonValueKind.String)
        {
            return false;
        }

        value = element.GetString();
        return value is not null;
    }

    private static bool TryGetProperty(JsonElement element, string name, out JsonElement value)
    {
        if (element.ValueKind == JsonValueKind.Object)
        {
            foreach (var property in element.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    value = property.Value;
                    return true;
                }
            }
        }

        value = default;
        return false;
    }
}
