using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;
using Core = TWXProxy.Core;

namespace MTC;

internal enum GameAgentEventKind
{
    ServerLine,
    ServerPrompt,
    ClientInput,
    Connected,
    Disconnected,
    CurrentSectorChanged,
    ShipStatus,
    System,
    SynchronizedShipStatus,
    PresenceHeartbeat
}

internal sealed class GameAgentEvent
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string GameName { get; init; } = string.Empty;
    public GameAgentEventKind Kind { get; init; }
    public string PlainText { get; init; } = string.Empty;
    public string AnsiText { get; init; } = string.Empty;
    public int CurrentSector { get; init; }
    public string PromptSurface { get; init; } = string.Empty;
    public Dictionary<string, string> Metadata { get; init; } = [];
}

internal sealed class GameAgentContextSnapshot
{
    public DateTimeOffset Timestamp { get; init; } = DateTimeOffset.UtcNow;
    public string GameName { get; init; } = string.Empty;
    public bool Connected { get; init; }
    public string Host { get; init; } = string.Empty;
    public int Port { get; init; }
    public string TraderName { get; init; } = string.Empty;
    public int Corp { get; init; }
    public int CurrentSector { get; init; }
    public long Credits { get; init; }
    public int Fighters { get; init; }
    public int Shields { get; init; }
    public int HoldsEmpty { get; init; }
    public int HoldsTotal { get; init; }
    public string CurrentPrompt { get; init; } = string.Empty;
    public GameScriptInputPrompt? ScriptInputPrompt { get; init; }
    public string EventLogPath { get; init; } = string.Empty;
    public GameAgentBotSnapshot Bot { get; init; } = new();
    public IReadOnlyList<string> OnlinePlayers { get; init; } = [];
    public IReadOnlyList<GameAgentRunningScriptSnapshot> RunningScripts { get; init; } = [];
    public IReadOnlyList<string> RecentPrompts { get; init; } = [];
    public IReadOnlyList<string> Hazards { get; init; } = [];
    public GameAgentSectorSnapshot? CurrentSectorDetails { get; init; }
    public IReadOnlyList<GameAgentSectorSnapshot> AdjacentSectors { get; init; } = [];
    public IReadOnlyList<GameAgentEvent> RecentEvents { get; init; } = [];
    public GameAgentCopilotRecommendation CopilotRecommendation { get; init; } = new();
}

internal sealed class GameAgentBotSnapshot
{
    public bool NativeMombotRunning { get; init; }
    public string ExternalBotName { get; init; } = string.Empty;
    public string BotName { get; init; } = string.Empty;
    public string TeamName { get; init; } = string.Empty;
    public string Mode { get; init; } = string.Empty;
    public string LastLoadedModule { get; init; } = string.Empty;
    public bool WatcherAttached { get; init; }
    public bool AcceptsSelfCommands { get; init; }
    public bool AcceptsSubspaceCommands { get; init; }
    public bool AcceptsPrivateCommands { get; init; }
}

internal sealed class GameAgentRunningScriptSnapshot
{
    public int Id { get; init; }
    public string Name { get; init; } = string.Empty;
    public string Reference { get; init; } = string.Empty;
    public bool IsSystemScript { get; init; }
    public bool IsBot { get; init; }
    public bool Paused { get; init; }
}

internal sealed class GameAgentSectorSnapshot
{
    public int Number { get; init; }
    public string Explored { get; init; } = string.Empty;
    public string Constellation { get; init; } = string.Empty;
    public string Beacon { get; init; } = string.Empty;
    public int NavHaz { get; init; }
    public bool Anomaly { get; init; }
    public int Density { get; init; }
    public IReadOnlyList<int> WarpsOut { get; init; } = [];
    public IReadOnlyList<int> WarpsIn { get; init; } = [];
    public string Port { get; init; } = string.Empty;
    public IReadOnlyList<string> Planets { get; init; } = [];
    public IReadOnlyList<string> Traders { get; init; } = [];
    public IReadOnlyList<string> Ships { get; init; } = [];
    public string Fighters { get; init; } = string.Empty;
    public string ArmidMines { get; init; } = string.Empty;
    public string LimpetMines { get; init; } = string.Empty;
}

internal sealed class GameAgentReplaySnapshot
{
    public string SourcePath { get; init; } = string.Empty;
    public int EventIndex { get; init; }
    public int EventCount { get; init; }
    public DateTimeOffset Timestamp { get; init; }
    public string GameName { get; init; } = string.Empty;
    public bool Connected { get; init; }
    public int CurrentSector { get; init; }
    public string CurrentPrompt { get; init; } = string.Empty;
    public long Credits { get; init; }
    public int Fighters { get; init; }
    public int Shields { get; init; }
    public int HoldsEmpty { get; init; }
    public int HoldsTotal { get; init; }
    public IReadOnlyList<GameAgentEvent> RecentEvents { get; init; } = [];
}

internal sealed class GameAgentTrainingSample
{
    public DateTimeOffset ExportedAt { get; init; } = DateTimeOffset.UtcNow;
    public string Schema { get; init; } = "mtc.game-agent.training-sample.v1";
    public string Purpose { get; init; } = "observer-snapshot";
    public GameAgentContextSnapshot Context { get; init; } = new();
    public IReadOnlyList<GameAgentToolDescriptor> AvailableTools { get; init; } = [];
    public IReadOnlyList<GameAgentToolCallResult> ToolDryRuns { get; init; } = [];
}

internal sealed class GameAgentCorrectionSample
{
    public DateTimeOffset ExportedAt { get; init; } = DateTimeOffset.UtcNow;
    public string Schema { get; init; } = "mtc.game-agent.correction-sample.v1";
    public string Purpose { get; init; } = "interactive-training-correction";
    public GameAgentContextSnapshot Context { get; init; } = new();
    public GameAgentCopilotRecommendation Recommendation { get; init; } = new();
    public string CorrectAction { get; init; } = string.Empty;
    public string Note { get; init; } = string.Empty;
}

internal sealed class GameAgentRuntime : IDisposable
{
    private const int MaxRecentEvents = 700;
    private const int MaxQueuedEvents = 10000;

    private readonly object _sync = new();
    private readonly Queue<GameAgentEvent> _recentEvents = new();
    private readonly BlockingCollection<GameAgentEvent> _writeQueue = new(MaxQueuedEvents);
    private readonly Task _writerTask;
    private readonly JsonSerializerOptions _jsonOptions = new()
    {
        WriteIndented = false,
    };

    private static readonly JsonSerializerOptions PrettyJsonOptions = new()
    {
        WriteIndented = true,
    };

    private string _gameName = "game";
    private string _eventLogPath = string.Empty;
    private string _connectionPromptSurface = string.Empty;
    private GameScriptInputPrompt? _currentScriptInputPrompt;
    private DateTimeOffset? _connectionStartedAt;
    private bool _disposed;

    public GameAgentRuntime()
    {
        _writerTask = Task.Run(WriterLoop);
    }

    public string EventLogPath
    {
        get
        {
            lock (_sync)
                return _eventLogPath;
        }
    }

    public event Action<GameAgentEvent>? EventRecorded;

    public void SetGameName(string gameName)
    {
        string normalized = NormalizeGameName(gameName);
        lock (_sync)
        {
            if (string.Equals(_gameName, normalized, StringComparison.Ordinal))
                return;

            _gameName = normalized;
            _connectionPromptSurface = string.Empty;
            _currentScriptInputPrompt = null;
            _connectionStartedAt = null;
            _eventLogPath = string.Empty;
        }
    }

    public void Record(GameAgentEvent evt)
    {
        if (_disposed)
            return;

        GameAgentEvent normalized = NormalizeEvent(evt);
        lock (_sync)
        {
            switch (normalized.Kind)
            {
                case GameAgentEventKind.Connected:
                    _connectionStartedAt = normalized.Timestamp;
                    _connectionPromptSurface = string.Empty;
                    _currentScriptInputPrompt = null;
                    break;
                case GameAgentEventKind.Disconnected:
                    _connectionStartedAt = null;
                    _connectionPromptSurface = string.Empty;
                    _currentScriptInputPrompt = null;
                    break;
                case GameAgentEventKind.ServerPrompt
                    when _connectionStartedAt is { } connectedAt &&
                         normalized.Timestamp >= connectedAt:
                    _connectionPromptSurface = ResolveObservedPromptSurface(evt);
                    break;
            }

            _recentEvents.Enqueue(normalized);
            while (_recentEvents.Count > MaxRecentEvents)
                _recentEvents.Dequeue();
        }

        if (!_writeQueue.TryAdd(normalized))
        {
            RecordInMemoryOnly(new GameAgentEvent
            {
                GameName = normalized.GameName,
                Kind = GameAgentEventKind.System,
                PlainText = "Game agent event queue overflow; dropped an event.",
                CurrentSector = normalized.CurrentSector,
                Metadata = new Dictionary<string, string>
                {
                    ["droppedKind"] = normalized.Kind.ToString(),
                },
            });
        }

        EventRecorded?.Invoke(normalized);
    }

    public IReadOnlyList<GameAgentEvent> GetRecentEvents(int count = 120)
    {
        lock (_sync)
        {
            return _recentEvents
                .Reverse()
                .Take(Math.Max(1, count))
                .Reverse()
                .ToArray();
        }
    }

    public void SetScriptInputPrompt(GameScriptInputPrompt prompt)
    {
        ArgumentNullException.ThrowIfNull(prompt);
        if (string.IsNullOrWhiteSpace(prompt.ScriptName) ||
            string.IsNullOrWhiteSpace(prompt.Text) ||
            prompt.ObservedAt > DateTimeOffset.UtcNow.AddMinutes(1))
        {
            return;
        }

        lock (_sync)
            _currentScriptInputPrompt = prompt;
    }

    public void ClearScriptInputPrompt()
    {
        lock (_sync)
            _currentScriptInputPrompt = null;
    }

    public GameScriptInputPrompt? GetCurrentScriptInputPrompt()
    {
        lock (_sync)
            return _currentScriptInputPrompt;
    }

    public GameAgentContextSnapshot BuildContextSnapshot(
        GameState state,
        Core.ModDatabase? database,
        GameAgentBotSnapshot? bot = null,
        IReadOnlyList<string>? onlinePlayers = null,
        IReadOnlyList<Core.RunningScriptInfo>? runningScripts = null,
        int recentEventCount = 80,
        GameScriptInputPrompt? scriptInputPrompt = null)
    {
        string gameName = NormalizeGameName(state.GameName);
        if (string.Equals(gameName, "game", StringComparison.OrdinalIgnoreCase))
            gameName = _gameName;

        string prompt;
        lock (_sync)
            prompt = _connectionPromptSurface;
        IReadOnlyList<GameAgentEvent> recentEvents = GetRecentEvents(recentEventCount);
        GameAgentSectorSnapshot? currentSector = BuildSectorSnapshot(database, state.Sector);
        IReadOnlyList<GameAgentSectorSnapshot> adjacentSectors = BuildAdjacentSectorSnapshots(database, state.Sector);
        return new GameAgentContextSnapshot
        {
            Timestamp = DateTimeOffset.UtcNow,
            GameName = gameName,
            Connected = state.Connected,
            Host = state.Host,
            Port = state.Port,
            TraderName = state.TraderName,
            Corp = state.Corp,
            CurrentSector = state.Sector,
            Credits = state.Credits,
            Fighters = state.Fighters,
            Shields = state.Shields,
            HoldsEmpty = state.HoldsEmpty,
            HoldsTotal = state.HoldsTotal,
            CurrentPrompt = prompt,
            ScriptInputPrompt = scriptInputPrompt,
            EventLogPath = EventLogPath,
            Bot = bot ?? new GameAgentBotSnapshot(),
            OnlinePlayers = onlinePlayers?.Where(player => !string.IsNullOrWhiteSpace(player)).Take(40).ToArray() ?? [],
            RunningScripts = BuildRunningScriptSnapshots(runningScripts),
            RecentPrompts = BuildRecentPrompts(recentEvents),
            Hazards = BuildHazards(currentSector, adjacentSectors),
            CurrentSectorDetails = currentSector,
            AdjacentSectors = adjacentSectors,
            RecentEvents = recentEvents,
            CopilotRecommendation = GameAgentCopilot.Recommend(prompt, state.Sector, recentEvents),
        };
    }

    public bool IsAtTradeWarsCommandPrompt()
    {
        lock (_sync)
            return IsSafeOnlineRefreshPrompt(_connectionPromptSurface);
    }

    internal static bool HasCimCompletionMarker(
        IEnumerable<GameAgentEvent> events,
        DateTimeOffset submittedAt) =>
        events.Any(evt =>
            evt.Timestamp >= submittedAt &&
            evt.Kind is GameAgentEventKind.ServerLine or GameAgentEventKind.ServerPrompt &&
            string.Concat(evt.PlainText, " ", evt.PromptSurface)
                .Contains("ENDINTERROG", StringComparison.OrdinalIgnoreCase));

    public static bool IsSafeOnlineRefreshPrompt(string? promptSurface) =>
        string.Equals(promptSurface?.Trim(), "Command", StringComparison.OrdinalIgnoreCase);

    public static GameAgentReplaySnapshot BuildReplaySnapshot(string sourcePath, IReadOnlyList<GameAgentEvent> events, int index, int recentEventCount = 40)
    {
        if (events.Count == 0)
        {
            return new GameAgentReplaySnapshot
            {
                SourcePath = sourcePath,
                EventIndex = 0,
                EventCount = 0,
            };
        }

        int safeIndex = Math.Clamp(index, 0, events.Count - 1);
        bool connected = false;
        int currentSector = 0;
        string currentPrompt = string.Empty;
        long credits = 0;
        int fighters = 0;
        int shields = 0;
        int holdsEmpty = 0;
        int holdsTotal = 0;

        for (int i = 0; i <= safeIndex; i++)
        {
            GameAgentEvent evt = events[i];
            if (evt.CurrentSector > 0)
                currentSector = evt.CurrentSector;

            switch (evt.Kind)
            {
                case GameAgentEventKind.Connected:
                    connected = true;
                    break;
                case GameAgentEventKind.Disconnected:
                    connected = false;
                    break;
                case GameAgentEventKind.ServerPrompt:
                    currentPrompt = !string.IsNullOrWhiteSpace(evt.PromptSurface) ? evt.PromptSurface : evt.PlainText;
                    break;
                case GameAgentEventKind.CurrentSectorChanged:
                    if (evt.CurrentSector > 0)
                        currentSector = evt.CurrentSector;
                    break;
                case GameAgentEventKind.ShipStatus:
                    credits = ReadLongMetadata(evt, "credits", credits);
                    fighters = ReadIntMetadata(evt, "fighters", fighters);
                    shields = ReadIntMetadata(evt, "shields", shields);
                    holdsEmpty = ReadIntMetadata(evt, "holdsEmpty", holdsEmpty);
                    holdsTotal = ReadIntMetadata(evt, "holdsTotal", holdsTotal);
                    break;
            }
        }

        GameAgentEvent current = events[safeIndex];
        return new GameAgentReplaySnapshot
        {
            SourcePath = sourcePath,
            EventIndex = safeIndex,
            EventCount = events.Count,
            Timestamp = current.Timestamp,
            GameName = current.GameName,
            Connected = connected,
            CurrentSector = currentSector,
            CurrentPrompt = currentPrompt,
            Credits = credits,
            Fighters = fighters,
            Shields = shields,
            HoldsEmpty = holdsEmpty,
            HoldsTotal = holdsTotal,
            RecentEvents = events
                .Take(safeIndex + 1)
                .Reverse()
                .Take(Math.Max(1, recentEventCount))
                .Reverse()
                .ToArray(),
        };
    }

    public static IEnumerable<GameAgentEvent> ReadEvents(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            yield break;

        foreach (string line in File.ReadLines(path))
        {
            if (string.IsNullOrWhiteSpace(line))
                continue;

            GameAgentEvent? evt = null;
            try
            {
                evt = JsonSerializer.Deserialize<GameAgentEvent>(line);
            }
            catch
            {
                // Skip malformed lines so a partial write does not poison replay.
            }

            if (evt != null)
                yield return evt;
        }
    }

    public static string ExportTrainingSample(GameAgentContextSnapshot context)
    {
        GameAgentTrainingSample sample = new()
        {
            Context = context,
            AvailableTools = GameAgentToolRegistry.DescribeTools(),
            ToolDryRuns =
            [
                GameAgentToolRegistry.ObserveContext(context),
                GameAgentToolRegistry.RecommendAction(context),
                GameAgentToolRegistry.ListScripts(context.RunningScripts),
            ],
        };

        string dir = BuildAgentDirectory(context.GameName);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"sample-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(sample, PrettyJsonOptions), Encoding.UTF8);
        return path;
    }

    public static string ExportCorrectionSample(
        GameAgentContextSnapshot context,
        GameAgentCopilotRecommendation recommendation,
        string correctAction,
        string note)
    {
        GameAgentCorrectionSample sample = new()
        {
            Context = context,
            Recommendation = recommendation,
            CorrectAction = correctAction.Trim(),
            Note = note.Trim(),
        };

        string dir = BuildAgentDirectory(context.GameName);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"correction-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(sample, PrettyJsonOptions), Encoding.UTF8);
        return path;
    }

    public static string ExportSnapshot(GameAgentContextSnapshot context)
    {
        string dir = BuildAgentDirectory(context.GameName);
        Directory.CreateDirectory(dir);
        string path = Path.Combine(dir, $"snapshot-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json");
        File.WriteAllText(path, JsonSerializer.Serialize(context, PrettyJsonOptions), Encoding.UTF8);
        return path;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _writeQueue.CompleteAdding();
        try { _writerTask.Wait(TimeSpan.FromSeconds(1)); } catch { }
        _writeQueue.Dispose();
    }

    private GameAgentEvent NormalizeEvent(GameAgentEvent evt)
    {
        string gameName = NormalizeGameName(string.IsNullOrWhiteSpace(evt.GameName) ? _gameName : evt.GameName);
        return new GameAgentEvent
        {
            Timestamp = evt.Timestamp == default ? DateTimeOffset.UtcNow : evt.Timestamp,
            GameName = gameName,
            Kind = evt.Kind,
            PlainText = evt.PlainText ?? string.Empty,
            AnsiText = evt.AnsiText ?? string.Empty,
            CurrentSector = evt.CurrentSector,
            PromptSurface = evt.Kind == GameAgentEventKind.PresenceHeartbeat
                ? evt.PromptSurface ?? string.Empty
                : string.IsNullOrWhiteSpace(evt.PromptSurface) ? ResolvePromptSurface() : evt.PromptSurface,
            Metadata = evt.Metadata ?? [],
        };
    }

    private void RecordInMemoryOnly(GameAgentEvent evt)
    {
        lock (_sync)
        {
            _recentEvents.Enqueue(NormalizeEvent(evt));
            while (_recentEvents.Count > MaxRecentEvents)
                _recentEvents.Dequeue();
        }
    }

    private void WriterLoop()
    {
        foreach (GameAgentEvent evt in _writeQueue.GetConsumingEnumerable())
        {
            try
            {
                string path = BuildEventLogPath(evt.GameName);
                lock (_sync)
                    _eventLogPath = path;
                string json = JsonSerializer.Serialize(evt, _jsonOptions);
                EventLogFileAppender.AppendLine(path, json);
            }
            catch (Exception ex)
            {
                RecordInMemoryOnly(new GameAgentEvent
                {
                    GameName = evt.GameName,
                    Kind = GameAgentEventKind.System,
                    PlainText = $"Game agent event write failed: {ex.Message}",
                    CurrentSector = evt.CurrentSector,
                });
            }
        }
    }

    private static string BuildEventLogPath(string gameName)
    {
        string? processOnlyOverride = Environment.GetEnvironmentVariable("MTC_GAME_AGENT_EVENT_LOG_PATH");
        if (string.Equals(
                Environment.GetEnvironmentVariable("MTC_RPC_PROCESS_ONLY"),
                "1",
                StringComparison.Ordinal) &&
            !string.IsNullOrWhiteSpace(processOnlyOverride))
        {
            return Path.GetFullPath(processOnlyOverride);
        }

        string dir = BuildAgentDirectory(gameName);
        return Path.Combine(dir, $"events-{DateTime.UtcNow:yyyyMMdd}.jsonl");
    }

    private static string BuildAgentDirectory(string gameName)
    {
        string safeGameName = NormalizeGameName(gameName);
        return Path.Combine(AppPaths.TwxproxyGamesDir, safeGameName, "agent");
    }

    private static string NormalizeGameName(string? gameName)
    {
        string safe = Core.SharedPaths.SanitizeFileComponent(gameName ?? string.Empty);
        return string.IsNullOrWhiteSpace(safe) ? "game" : safe;
    }

    private static string ResolvePromptSurface()
    {
        string currentLine;
        try
        {
            currentLine = Core.ScriptRef.GetCurrentLine();
            if (string.IsNullOrWhiteSpace(currentLine))
                return string.Empty;
        }
        catch
        {
            return string.Empty;
        }

        string trimmed = Core.AnsiCodes.NormalizeTerminalText(currentLine).Trim();
        int marker = trimmed.IndexOf(" [TL=", StringComparison.OrdinalIgnoreCase);
        if (marker > 0)
            return trimmed[..marker].Trim();

        marker = trimmed.IndexOf(" command", StringComparison.OrdinalIgnoreCase);
        if (marker > 0)
            return trimmed[..marker].Trim();

        return trimmed.Length <= 80 ? trimmed : trimmed[..80];
    }

    private static string ResolveObservedPromptSurface(GameAgentEvent evt)
    {
        string promptText = string.IsNullOrWhiteSpace(evt.PromptSurface)
            ? evt.PlainText
            : evt.PromptSurface;
        return TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(
            promptText,
            out string promptSurface)
            ? promptSurface
            : string.Empty;
    }

    private static IReadOnlyList<GameAgentSectorSnapshot> BuildAdjacentSectorSnapshots(Core.ModDatabase? database, int currentSector)
    {
        try
        {
            Core.SectorData? sector = database?.GetSector(currentSector);
            if (database == null || sector == null)
                return [];

            return sector.Warp
                .Where(warp => warp > 0)
                .Distinct()
                .Take(8)
                .Select(warp => BuildSectorSnapshot(database, warp))
                .Where(snapshot => snapshot != null)
                .Cast<GameAgentSectorSnapshot>()
                .ToArray();
        }
        catch
        {
            return [];
        }
    }

    internal static GameAgentSectorSnapshot? BuildSectorSnapshot(Core.ModDatabase? database, int sectorNumber)
    {
        if (database == null || sectorNumber <= 0)
            return null;

        try
        {
            Core.SectorData? sector = database.GetSector(sectorNumber);
            if (sector == null)
                return new GameAgentSectorSnapshot { Number = sectorNumber, Explored = "Unknown" };

            return new GameAgentSectorSnapshot
            {
                Number = sectorNumber,
                Explored = sector.Explored.ToString(),
                Constellation = sector.Constellation ?? string.Empty,
                Beacon = sector.Beacon ?? string.Empty,
                NavHaz = sector.NavHaz,
                Anomaly = sector.Anomaly,
                Density = sector.Density,
                WarpsOut = sector.Warp.Where(warp => warp > 0).Select(warp => (int)warp).ToArray(),
                WarpsIn = sector.WarpsIn.Where(warp => warp > 0).Select(warp => (int)warp).OrderBy(warp => warp).ToArray(),
                Port = FormatPort(sector.SectorPort),
                Planets = database.GetPlanetNamesInSector(sectorNumber).Where(name => !string.IsNullOrWhiteSpace(name)).Take(12).ToArray(),
                Traders = sector.Traders.Select(FormatTrader).Where(value => value.Length > 0).Take(12).ToArray(),
                Ships = sector.Ships.Select(FormatShip).Where(value => value.Length > 0).Take(12).ToArray(),
                Fighters = FormatSpaceObject(sector.Fighters, includeType: true),
                ArmidMines = FormatSpaceObject(sector.MinesArmid, includeType: false),
                LimpetMines = FormatSpaceObject(sector.MinesLimpet, includeType: false),
            };
        }
        catch
        {
            return new GameAgentSectorSnapshot { Number = sectorNumber, Explored = "Unavailable" };
        }
    }

    private static IReadOnlyList<GameAgentRunningScriptSnapshot> BuildRunningScriptSnapshots(IReadOnlyList<Core.RunningScriptInfo>? runningScripts)
    {
        if (runningScripts == null || runningScripts.Count == 0)
            return [];

        return runningScripts
            .Take(40)
            .Select(script => new GameAgentRunningScriptSnapshot
            {
                Id = script.Id,
                Name = script.Name,
                Reference = script.Reference,
                IsSystemScript = script.IsSystemScript,
                IsBot = script.IsBot,
                Paused = script.Paused,
            })
            .ToArray();
    }

    private static IReadOnlyList<string> BuildRecentPrompts(IReadOnlyList<GameAgentEvent> recentEvents)
        => recentEvents
            .Where(evt => evt.Kind == GameAgentEventKind.ServerPrompt)
            .Select(evt => string.IsNullOrWhiteSpace(evt.PromptSurface) ? evt.PlainText : evt.PromptSurface)
            .Where(prompt => !string.IsNullOrWhiteSpace(prompt))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .TakeLast(8)
            .ToArray();

    private static IReadOnlyList<string> BuildHazards(GameAgentSectorSnapshot? currentSector, IReadOnlyList<GameAgentSectorSnapshot> adjacentSectors)
    {
        var hazards = new List<string>();
        AddSectorHazards(hazards, "Current", currentSector);
        foreach (GameAgentSectorSnapshot sector in adjacentSectors)
            AddSectorHazards(hazards, "Adjacent", sector);
        return hazards.Take(20).ToArray();
    }

    private static void AddSectorHazards(List<string> hazards, string prefix, GameAgentSectorSnapshot? sector)
    {
        if (sector == null)
            return;

        if (sector.NavHaz > 0)
            hazards.Add($"{prefix} sector {sector.Number} has {sector.NavHaz}% navhaz; navhaz is the relevant movement damage risk.");
        if (sector.Anomaly)
            hazards.Add($"{prefix} sector {sector.Number} has an anomaly signal; do not treat anomaly alone as hull/cargo damage.");
        if (!string.IsNullOrWhiteSpace(sector.ArmidMines))
            hazards.Add($"{prefix} sector {sector.Number} has armid mines: {sector.ArmidMines}; ownership matters, and your own deployed mines do not damage your ship when leaving.");
        if (!string.IsNullOrWhiteSpace(sector.LimpetMines))
            hazards.Add($"{prefix} sector {sector.Number} has limpet mines: {sector.LimpetMines}; limpets track ships and do not cause ship damage.");
        if (sector.Traders.Count > 0)
            hazards.Add($"{prefix} sector {sector.Number} has trader contacts: {string.Join("; ", sector.Traders.Take(3))}.");
        if (sector.Ships.Count > 0)
            hazards.Add($"{prefix} sector {sector.Number} has visible ships: {string.Join("; ", sector.Ships.Take(3))}.");
    }

    private static string FormatPort(Core.Port? port)
    {
        if (port == null || port.Dead || string.IsNullOrWhiteSpace(port.Name))
            return string.Empty;

        return $"{port.Name.Trim()} class {port.ClassIndex} {FormatPortProducts(port)}".TrimEnd();
    }

    private static string FormatPortProducts(Core.Port port)
    {
        static char Product(Core.Port p, Core.ProductType type)
            => p.BuyProduct.TryGetValue(type, out bool buys) && buys ? 'B' : 'S';

        if (port.ClassIndex is 0 or 9)
            return "(special)";

        char fuel = Product(port, Core.ProductType.FuelOre);
        char organics = Product(port, Core.ProductType.Organics);
        char equipment = Product(port, Core.ProductType.Equipment);
        return $"({fuel}{organics}{equipment}: Fuel Ore {PortProductMeaning(fuel)}, Organics {PortProductMeaning(organics)}, Equipment {PortProductMeaning(equipment)}; B=buys from player, S=sells to player)";
    }

    private static string PortProductMeaning(char code)
        => code == 'B' ? "buying" : "selling";

    private static string FormatTrader(Core.Trader trader)
    {
        string name = (trader.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            return string.Empty;

        string ship = string.IsNullOrWhiteSpace(trader.ShipType) ? string.Empty : $" in {trader.ShipType.Trim()}";
        string fighters = trader.Fighters > 0 ? $" with {trader.Fighters:N0} figs" : string.Empty;
        return $"{name}{ship}{fighters}";
    }

    private static string FormatShip(Core.Ship ship)
    {
        string name = (ship.Name ?? string.Empty).Trim();
        if (name.Length == 0)
            return string.Empty;

        string owner = string.IsNullOrWhiteSpace(ship.Owner) ? string.Empty : $" owned by {ship.Owner.Trim()}";
        string fighters = ship.Fighters > 0 ? $" with {ship.Fighters:N0} figs" : string.Empty;
        return $"{name}{owner}{fighters}";
    }

    private static string FormatSpaceObject(Core.SpaceObject? obj, bool includeType)
    {
        if (obj == null || obj.Quantity <= 0)
            return string.Empty;

        string owner = string.IsNullOrWhiteSpace(obj.Owner) ? string.Empty : $" ({obj.Owner.Trim()})";
        string type = includeType ? $" {obj.FigType}" : string.Empty;
        return $"{obj.Quantity:N0}{type}{owner}".Trim();
    }

    private static int ReadIntMetadata(GameAgentEvent evt, string key, int fallback)
        => evt.Metadata.TryGetValue(key, out string? value) && int.TryParse(value, out int parsed) ? parsed : fallback;

    private static long ReadLongMetadata(GameAgentEvent evt, string key, long fallback)
        => evt.Metadata.TryGetValue(key, out string? value) && long.TryParse(value, out long parsed) ? parsed : fallback;
}
