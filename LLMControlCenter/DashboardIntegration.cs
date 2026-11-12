using System;
using Avalonia.Controls;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;
using TWXProxy.Core;

namespace MTC;

public partial class MainWindow
{
    private static readonly string[] ReactiveStateFields =
    [
        "currentSector",
        "turns",
        "credits",
        "fighters",
        "shields",
        "holdsEmpty",
        "holdsTotal",
        "fuelOre",
        "organics",
        "equipment",
        "scannerType"
    ];

    private readonly DashboardHost _dashboardHost = new();
    private MtcSessionWindowHost? _mtcSessionWindowHost;
    private readonly SemaphoreSlim _dashboardStartGate = new(1, 1);
    private readonly object _reactiveSnapshotSync = new();
    private readonly object _reactiveObservationQueueSync = new();
    private readonly CancellationTokenSource _automaticAgentStartupCancellation = new();
    private string? _lastReactiveSnapshotFingerprint;
    private ReactiveEvent? _pendingReactiveObservation;
    private Task? _reactiveObservationTask;
    private long _coalescedReactiveObservationCount;
    private bool _reactiveObservationWorkerRunning;
    private NativeWebView? _dashboardWebView;
    private Uri? _dashboardAddress;
    private Task? _automaticAgentStartupTask;
    private int _automaticAgentStartupScheduled;
    private int _reactiveObserverAttached;
    private bool _assignedGameProfileLoadFailed;

    private Task OnOpenDashboardAsync() =>
        NavigateDashboardAsync("fleet", "/");

    private async Task NavigateDashboardAsync(string navigationId, string route)
    {
        await _dashboardStartGate.WaitAsync();
        try
        {
            if (_dashboardWebView is null)
            {
                string agentId = DefaultAgentId;
                string eventLogDirectory = Path.Combine(
                    AppPaths.TwxproxyGamesDir,
                    GetGameAgentGameName(),
                    "agent");
                Directory.CreateDirectory(eventLogDirectory);

                var commandSender = new MtcGameCommandSender(
                    agentId,
                    SendMtcRpcCommandAsync,
                    RunMtcRpcScriptAsync,
                    RunMtcRpcScriptAsync,
                    IsWorldTradeModeRunningAsync,
                    _gameAgent.IsAtTradeWarsCommandPrompt,
                    () => _sessionDb?.DBHeader.LastPortCIM ?? DateTime.MinValue,
                    WaitForCimCompletionAsync,
                    GetRecentAgentServerPrompts,
                    () => _gameInstance?.NativeHaggleEnabled == true,
                    commandsApproved: false,
                    runDockShopperScript: RunMtcRpcScriptAsync,
                    isDockShopperRunning: IsStarDockShopperRunningAsync,
                    stopScriptByName: name => StopMtcRpcScriptAsync(null, name),
                    getRecentServerLines: GetRecentAgentServerLines,
                    getCurrentScriptInputPrompt: _gameAgent.GetCurrentScriptInputPrompt,
                    setNativeHaggleEnabled: enabled =>
                        (_gameInstance ?? throw new InvalidOperationException(
                            "Native haggling cannot be changed because the game session is unavailable."))
                        .SetNativeHaggleEnabled(
                            enabled,
                            NativeHaggleChangeSource.Runtime),
                    submitScriptInput: SendMtcRpcScriptInputAsync);
                _dashboardAddress = await _dashboardHost.StartAsync(
                    agentId,
                    eventLogDirectory,
                    commandSender,
                    IsMtcRpcConnected,
                    CanConnectAgentSessionAsync,
                    ConfigureAgentConnectionAsync,
                    LoadSavedAgentConnectionAsync,
                    ConnectAgentSessionAsync,
                    DisconnectAgentSessionAsync,
                    PublishCurrentAgentPromptAsync,
                    () => GetGameAgentGameName(),
                    () => _sessionDb,
                    BuildIntegratedGameClientSnapshot,
                    () => _gameAgent.GetCurrentScriptInputPrompt(),
                    _mtcSessionWindowHost);
                if (string.IsNullOrWhiteSpace(
                        Environment.GetEnvironmentVariable("LLMCONTROL_GAME_PROFILE")))
                {
                    string? assignedProfile = await _dashboardHost
                        .GetAgentGameProfileAsync(agentId);
                    if (!string.IsNullOrWhiteSpace(assignedProfile))
                    {
                        navigationId = "runtime";
                        route = $"/agents/{Uri.EscapeDataString(agentId)}/runtime";
                        try
                        {
                            await LoadAssignedAgentGameProfileAsync(assignedProfile);
                            _assignedGameProfileLoadFailed = false;
                        }
                        catch (Exception exception)
                        {
                            _assignedGameProfileLoadFailed = true;
                            _dashboardHost.SetAgentStartupActivity(
                                agentId,
                                $"Startup blocked: assigned game profile could not be loaded ({exception.Message}).");
                            GlobalModules.DebugLog(
                                $"[Startup] saved agent game profile could not be loaded: {exception}\n");
                            _parser.Feed(
                                $"\x1b[1;31m[Assigned game profile failed to load: {exception.Message}]\x1b[0m\r\n");
                        }
                    }
                }
                if (Interlocked.Exchange(ref _reactiveObserverAttached, 1) == 0)
                {
                    _gameAgent.EventRecorded += OnReactiveGameAgentEventRecorded;
                }
                var webView = new NativeWebView
                {
                    Source = _dashboardAddress,
                    HorizontalAlignment = Avalonia.Layout.HorizontalAlignment.Stretch,
                    VerticalAlignment = Avalonia.Layout.VerticalAlignment.Stretch,
                };
                _dashboardWebView = webView;
                SetDashboardPage(webView);
            }

            if (_dashboardAddress is null || _dashboardWebView is null)
                throw new InvalidOperationException("The embedded dashboard did not initialize.");

            _activeWorkspaceNavigationId = IsDockedSessionWindow
                ? "mtc-local-session"
                : navigationId;
            SetWorkspaceView(
                IsDockedSessionWindow
                    ? WorkspaceView.AgentTerminal
                    : WorkspaceView.Dashboard);
            _dashboardWebView.Source = new Uri(_dashboardAddress, route.TrimStart('/'));
            RefreshWorkspaceNavigationState();
            if (Interlocked.Exchange(ref _automaticAgentStartupScheduled, 1) == 0)
                _automaticAgentStartupTask =
                    StartAgentWhenReadyAsync(_automaticAgentStartupCancellation.Token);
        }
        catch (Exception exception)
        {
            GlobalModules.DebugLog($"[Dashboard] failed to start embedded dashboard: {exception}\n");
            await ShowMessageAsync("Dashboard", $"Could not start the embedded dashboard:\n{exception.Message}");
        }
        finally
        {
            _dashboardStartGate.Release();
        }
    }

    private void OnReactiveGameAgentEventRecorded(GameAgentEvent gameEvent)
    {
        if (gameEvent.Kind is GameAgentEventKind.Connected or GameAgentEventKind.Disconnected)
        {
            lock (_reactiveSnapshotSync)
            {
                _lastReactiveSnapshotFingerprint = null;
            }
            return;
        }

        if (gameEvent.Kind != GameAgentEventKind.SynchronizedShipStatus ||
            !TryCreateReactiveStateEvent(gameEvent, out ReactiveEvent? reactiveEvent) ||
            reactiveEvent is null)
        {
            return;
        }

        QueueReactiveGameObservation(reactiveEvent);
    }

    private void QueueReactiveGameObservation(ReactiveEvent reactiveEvent)
    {
        bool startWorker = false;
        long coalescedCount = 0;
        lock (_reactiveObservationQueueSync)
        {
            if (_pendingReactiveObservation is not null)
            {
                coalescedCount = ++_coalescedReactiveObservationCount;
            }

            _pendingReactiveObservation = reactiveEvent;
            if (!_reactiveObservationWorkerRunning)
            {
                _reactiveObservationWorkerRunning = true;
                startWorker = true;
            }
        }

        if (coalescedCount > 0 && coalescedCount % 50 == 0)
        {
            GlobalModules.DebugLog(
                $"[Dashboard] coalesced {coalescedCount} superseded ship-state observations for the Reflex queue.\n");
        }

        if (startWorker)
        {
            _reactiveObservationTask = ProcessReactiveGameObservationsAsync(
                _automaticAgentStartupCancellation.Token);
        }
    }

    private async Task ProcessReactiveGameObservationsAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                ReactiveEvent? reactiveEvent;
                lock (_reactiveObservationQueueSync)
                {
                    reactiveEvent = _pendingReactiveObservation;
                    _pendingReactiveObservation = null;
                    if (reactiveEvent is null)
                    {
                        _reactiveObservationWorkerRunning = false;
                        return;
                    }
                }

                await SubmitReactiveGameObservationAsync(reactiveEvent, cancellationToken)
                    .ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private bool TryCreateReactiveStateEvent(
        GameAgentEvent gameEvent,
        out ReactiveEvent? reactiveEvent)
    {
        reactiveEvent = null;
        if (gameEvent.CurrentSector <= 0)
        {
            return false;
        }

        var observedValues = ReactiveStateFields
            .Where(field => gameEvent.Metadata.TryGetValue(field, out string? value) &&
                !string.IsNullOrWhiteSpace(value))
            .Select(field => new KeyValuePair<string, string>(field, gameEvent.Metadata[field]))
            .ToArray();
        if (observedValues.Length == 0 ||
            !gameEvent.Metadata.TryGetValue("currentSector", out string? sectorValue) ||
            !int.TryParse(
                sectorValue,
                System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture,
                out int sector) ||
            sector <= 0)
        {
            return false;
        }

        string fingerprint = string.Join(
            "\n",
            observedValues.Select(value => $"{value.Key}={value.Value}"));
        bool isInitialObservation;
        lock (_reactiveSnapshotSync)
        {
            if (string.Equals(
                    fingerprint,
                    _lastReactiveSnapshotFingerprint,
                    StringComparison.Ordinal))
            {
                return false;
            }

            isInitialObservation = _lastReactiveSnapshotFingerprint is null;
            _lastReactiveSnapshotFingerprint = fingerprint;
        }

        string observedState = string.Join(
            ", ",
            observedValues.Select(value => $"{value.Key}={value.Value}"));
        reactiveEvent = new ReactiveEvent(
            Guid.NewGuid().ToString("N"),
            DefaultAgentId,
            ReactiveEventType.WorldObservationChanged,
            ReactiveEventSource.GameSession,
            gameEvent.Timestamp,
            DateTimeOffset.UtcNow,
            sector,
            ActorName: null,
            ActorAlignment: null,
            Message: isInitialObservation
                ? $"Initial verified ship-state observation: {observedState}"
                : $"Verified ship-state values changed: {observedState}",
            SourceReference: "MTC synchronized ship status",
            SenderTrustAtReceipt: null);
        return true;
    }

    private async Task SubmitReactiveGameObservationAsync(
        ReactiveEvent reactiveEvent,
        CancellationToken cancellationToken)
    {
        try
        {
            ReactiveEventRecord record = await _dashboardHost
                .SubmitReactiveEventAsync(reactiveEvent, cancellationToken)
                .ConfigureAwait(false);
            GlobalModules.DebugLog(
                $"[Dashboard] Reflex processed MTC state observation {reactiveEvent.EventId}: " +
                $"{record.Decision.Origin} · {record.Decision.Summary}\n");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            GlobalModules.DebugLog(
                $"[Dashboard] Could not submit MTC state observation {reactiveEvent.EventId} to Reflex: {exception}\n");
        }
    }

    private IntegratedGameClientSnapshot BuildIntegratedGameClientSnapshot()
    {
        ModDatabase? database = _sessionDb;
        GameAgentContextSnapshot gameAgentContext = BuildGameAgentContextSnapshot();
        List<IntegratedGameSectorSnapshot> relevantSectors = [];
        if (gameAgentContext.CurrentSectorDetails is { } currentSector)
            relevantSectors.Add(ToIntegratedSectorSnapshot(currentSector));
        relevantSectors.AddRange(gameAgentContext.AdjacentSectors
            .Select(ToIntegratedSectorSnapshot));

        DateTime? lastPortCim = database?.DBHeader.LastPortCIM;
        DateTimeOffset? lastPortCimAt = lastPortCim is { } timestamp &&
            timestamp > DateTime.MinValue
                ? new DateTimeOffset(timestamp)
                : null;
        int? starDockSector = database?.DBHeader.StarDock is > 0
            ? database.DBHeader.StarDock
            : null;
        int? currentSectorNumber = _state.Sector > 0 ? _state.Sector : null;
        GameAgentScriptCatalogSnapshot scriptCatalog = GameAgentToolRegistry.ReadScriptCatalog(
            _appPrefs.ScriptsDirectory);

        return new IntegratedGameClientSnapshot(
            DateTimeOffset.UtcNow,
            IsMtcRpcConnected(),
            database is not null,
            _gameInstance is not null,
            database?.SectorCount is > 0 ? database.SectorCount : null,
            currentSectorNumber,
            starDockSector,
            lastPortCimAt,
            relevantSectors,
            gameAgentContext.RunningScripts
                .Select(script => new IntegratedGameScriptSnapshot(
                    script.Name,
                    script.Reference,
                    script.Paused,
                    script.IsSystemScript,
                    script.IsBot))
                .ToArray(),
            [
                new IntegratedGameScriptCapability(
                    "starDockShopper",
                    "ship-upgrades/ls-dock-shopper-v33.ts",
                    "Purchase the approved Density or Holo Scanner at StarDock after verified prerequisites.",
                    MayStart: true),
                new IntegratedGameScriptCapability(
                    "worldTrade",
                    "trading-cashing/2-worldtrade.ts",
                    "Earn credits through the approved World Trade script.",
                    MayStart: true)
            ])
        {
            ScriptCatalogStatus = scriptCatalog.Status,
            ScriptCatalogTotalEntries = scriptCatalog.TotalEntries,
            ScriptCatalogEntries = scriptCatalog.Entries
                .Select(entry => new IntegratedGameScriptCatalogEntry(
                    entry.File,
                    entry.Category))
                .ToArray()
        };
    }

    private static IntegratedGameSectorSnapshot ToIntegratedSectorSnapshot(
        GameAgentSectorSnapshot sector) =>
        new(
            sector.Number,
            sector.Explored,
            sector.Constellation,
            sector.Beacon,
            sector.Density,
            sector.WarpsOut,
            sector.Port,
            sector.Planets,
            sector.Traders,
            sector.Ships,
            sector.Fighters,
            sector.ArmidMines,
            sector.LimpetMines);

    private Task ConnectAgentSessionAsync() =>
        RunDashboardUiActionAsync(() => OnConnectAsync(propagateConnectionFailure: true));

    private Task ConfigureAgentConnectionAsync() =>
        RunDashboardUiActionAsync(OnNewConnectionAsync);

    private async Task<bool> LoadSavedAgentConnectionAsync()
    {
        string? assignedProfileFileName =
            Environment.GetEnvironmentVariable("LLMCONTROL_GAME_PROFILE");
        if (string.IsNullOrWhiteSpace(assignedProfileFileName))
        {
            assignedProfileFileName =
                await _dashboardHost.GetAgentGameProfileAsync(DefaultAgentId);
        }

        if (!string.IsNullOrWhiteSpace(assignedProfileFileName))
        {
            _assignedGameProfileLoadFailed = true;
            bool loaded = await LoadAssignedAgentGameProfileAsync(assignedProfileFileName);
            _assignedGameProfileLoadFailed = !loaded;
            return loaded;
        }

        _assignedGameProfileLoadFailed = false;
        return await RunDashboardUiActionAsync(async () =>
        {
            string? recentGameConfigPath = _appPrefs.RecentFiles.FirstOrDefault(path =>
                !string.IsNullOrWhiteSpace(path) &&
                string.Equals(Path.GetExtension(path), ".json", StringComparison.OrdinalIgnoreCase) &&
                File.Exists(path));
            if (recentGameConfigPath is null)
            {
                return await OnOpenConnectionAsync();
            }

            await OpenPathAsync(recentGameConfigPath, addToRecent: true);
            return true;
        });
    }

    private Task<bool> LoadAssignedAgentGameProfileAsync(string assignedProfileFileName) =>
        RunDashboardUiActionAsync(async () =>
        {
            if (!string.Equals(
                    Path.GetFileName(assignedProfileFileName),
                    assignedProfileFileName,
                    StringComparison.Ordinal) ||
                !string.Equals(
                    Path.GetExtension(assignedProfileFileName),
                    ".json",
                    StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException(
                    "The assigned game profile must be a .json file in the MTC games directory.");
            }

            string assignedProfilePath = Path.GetFullPath(
                Path.Combine(AppPaths.TwxproxyGamesDir, assignedProfileFileName));
            string currentProfilePath = Path.GetFullPath(AppPaths.TwxproxyGamesDir)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                + Path.DirectorySeparatorChar;
            if (!assignedProfilePath.StartsWith(
                    currentProfilePath,
                    StringComparison.OrdinalIgnoreCase) ||
                !File.Exists(assignedProfilePath))
            {
                throw new FileNotFoundException(
                    $"The assigned MTC game profile '{assignedProfileFileName}' was not found.",
                    assignedProfilePath);
            }
            if (!string.IsNullOrWhiteSpace(_currentProfilePath) &&
                string.Equals(
                    Path.GetFullPath(_currentProfilePath),
                    assignedProfilePath,
                    StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (GameFileLock.IsLockedByAnotherProcess(assignedProfilePath))
            {
                throw new InvalidOperationException(
                    $"The assigned game profile '{assignedProfileFileName}' is open in another MTC process. " +
                    "Close that session before launching this agent on the profile.");
            }

            await OpenPathAsync(assignedProfilePath, addToRecent: true);
            return true;
        });

    private async Task OpenAssignedAgentDashboardAsync()
    {
        try
        {
            if (!await LoadSavedAgentConnectionAsync())
            {
                throw new InvalidOperationException(
                    "The assigned MTC game profile could not be loaded.");
            }

            await NavigateDashboardAsync(
                "runtime",
                $"/agents/{Uri.EscapeDataString(DefaultAgentId)}/runtime");
        }
        catch (Exception exception)
        {
            GlobalModules.DebugLog(
                $"[Startup] assigned agent game profile could not be loaded: {exception}\n");
            _parser.Feed(
                $"\x1b[1;31m[Agent startup failed: {exception.Message}]\x1b[0m\r\n");
        }
    }

    private Task<bool> CanConnectAgentSessionAsync() =>
        InvokeMtcRpcUiAsync(() => Task.FromResult(
            _fileConnect.IsEnabled &&
            !string.IsNullOrWhiteSpace(_state.Host) &&
            _state.Port is > 0 and <= 65535));

    private async Task StartAgentWhenReadyAsync(CancellationToken cancellationToken)
    {
        try
        {
            if (!await _dashboardHost.IsAgentAutoStartEnabledAsync(
                    DefaultAgentId,
                    cancellationToken).ConfigureAwait(false))
            {
                _dashboardHost.SetAgentStartupActivity(
                    DefaultAgentId,
                    "Automatic launch is disabled for this agent; start it manually from Agent Runtime when ready.");
                return;
            }

            if (_assignedGameProfileLoadFailed)
            {
                _dashboardHost.SetAgentStartupActivity(
                    DefaultAgentId,
                    "Startup blocked because the saved game assignment could not be loaded. Correct it and reload the assigned profile.");
                return;
            }

            if (!IsMtcRpcConnected())
            {
                try
                {
                    await LoadSavedAgentConnectionAsync().ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    GlobalModules.DebugLog(
                        $"[Startup] could not load the most recent game profile: {exception}\n");
                }

                if (_assignedGameProfileLoadFailed)
                {
                    _dashboardHost.SetAgentStartupActivity(
                        DefaultAgentId,
                        "Startup blocked because the saved game assignment could not be loaded. Correct it and reload the assigned profile.");
                    return;
                }
            }

            while (!IsMtcRpcConnected())
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (await CanConnectAgentSessionAsync().ConfigureAwait(false))
                {
                    _dashboardHost.SetAgentStartupActivity(
                        DefaultAgentId,
                        $"Startup: connecting to {GetGameAgentGameName()} at {_state.Host}:{_state.Port}.");
                    GlobalModules.DebugLog(
                        $"[Startup] connecting to game profile {GetGameAgentGameName()} at {_state.Host}:{_state.Port}.\n");
                    try
                    {
                        await ConnectAgentSessionAsync().ConfigureAwait(false);
                    }
                    catch (Exception exception)
                    {
                        _dashboardHost.SetAgentStartupActivity(
                            DefaultAgentId,
                            $"Startup: game connection failed; retrying ({exception.Message}).");
                        GlobalModules.DebugLog(
                            $"[Startup] TradeWars connection failed; retrying: {exception}\n");
                    }
                }
                else
                {
                    _dashboardHost.SetAgentStartupActivity(
                        DefaultAgentId,
                        "Startup: waiting for a saved or manually configured game connection.");
                }

                if (!IsMtcRpcConnected())
                    await Task.Delay(TimeSpan.FromSeconds(10), cancellationToken).ConfigureAwait(false);
            }

            _dashboardHost.SetAgentStartupActivity(
                DefaultAgentId,
                "Startup: TradeWars is connected; starting the agent and account login.");
            await _dashboardHost.StartAgentAsync(DefaultAgentId, cancellationToken)
                .ConfigureAwait(false);
            _dashboardHost.SetAgentStartupActivity(
                DefaultAgentId,
                "Agent started; waiting for game authentication and the first model decision.");
            GlobalModules.DebugLog("[Startup] TradeWars is connected; agent started.\n");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        catch (Exception exception)
        {
            _dashboardHost.SetAgentStartupActivity(
                DefaultAgentId,
                $"Startup failed: {exception.Message}");
            GlobalModules.DebugLog($"[Startup] automatic agent startup failed: {exception}\n");
        }
    }

    private Task DisconnectAgentSessionAsync() =>
        RunDashboardUiActionAsync(OnDisconnectAsync);

    private async Task<bool> IsWorldTradeModeRunningAsync()
    {
        IReadOnlyList<GameAgentRunningScriptSnapshot> scripts = await ListMtcRpcScriptsAsync()
            .ConfigureAwait(false);
        return scripts.Any(script =>
            string.Equals(script.Name, "2-worldtrade", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(script.Name, "2-worldtrade.ts", StringComparison.OrdinalIgnoreCase) ||
            script.Reference.Replace('\\', '/').EndsWith(
                "trading-cashing/2-worldtrade.ts",
                StringComparison.OrdinalIgnoreCase));
    }

    private async Task<bool> IsStarDockShopperRunningAsync()
    {
        IReadOnlyList<GameAgentRunningScriptSnapshot> scripts = await ListMtcRpcScriptsAsync()
            .ConfigureAwait(false);
        return scripts.Any(script =>
            string.Equals(script.Name, "ls-dock-shopper-v33", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(script.Name, "ls-dock-shopper-v33.ts", StringComparison.OrdinalIgnoreCase) ||
            script.Reference.Replace('\\', '/').EndsWith(
                "ship-upgrades/ls-dock-shopper-v33.ts",
                StringComparison.OrdinalIgnoreCase));
    }

    private async Task WaitForCimCompletionAsync(
        DateTimeOffset submittedAt,
        CancellationToken cancellationToken)
    {
        DateTimeOffset deadline = DateTimeOffset.UtcNow.AddMinutes(3);
        while (DateTimeOffset.UtcNow < deadline)
        {
            IReadOnlyList<GameAgentEvent> recentEvents = _gameAgent.GetRecentEvents(700);
            GameAgentEvent? promptEvent = recentEvents.LastOrDefault(evt =>
                evt.Kind == GameAgentEventKind.ServerPrompt &&
                evt.Timestamp >= submittedAt);
            if (promptEvent is not null)
            {
                string promptText = string.IsNullOrWhiteSpace(promptEvent.PromptSurface)
                    ? promptEvent.PlainText
                    : promptEvent.PromptSurface;
                if (promptText.Contains("Confirmed? (Y/N)?", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        "CIM reached a quit confirmation unexpectedly. No response was sent; resolve the prompt before retrying.");
                }
            }

            if (GameAgentRuntime.HasCimCompletionMarker(recentEvents, submittedAt))
                return;

            await Task.Delay(TimeSpan.FromMilliseconds(200), cancellationToken)
                .ConfigureAwait(false);
        }

        throw new TimeoutException(
            "MTC did not observe the TWX CIM completion marker ': ENDINTERROG' after the script request.");
    }

    private IReadOnlyList<MtcServerPrompt> GetRecentAgentServerPrompts() =>
        _gameAgent.GetRecentEvents(700)
            .Where(evt =>
                evt.Kind == GameAgentEventKind.ServerPrompt ||
                evt.Kind == GameAgentEventKind.PresenceHeartbeat &&
                !string.IsNullOrWhiteSpace(evt.PromptSurface))
            .Select(evt => new MtcServerPrompt(
                evt.Timestamp,
                evt.Kind == GameAgentEventKind.ServerPrompt
                    ? evt.PlainText
                    : evt.PromptSurface,
                evt.Kind == GameAgentEventKind.PresenceHeartbeat,
                GetPromptObservedAt(evt),
                evt.Kind == GameAgentEventKind.PresenceHeartbeat &&
                    evt.Metadata.TryGetValue("promptIsCurrent", out string? isCurrent) &&
                    bool.TryParse(isCurrent, out bool current) &&
                    current))
            .ToArray();

    private IReadOnlyList<MtcServerPrompt> GetRecentAgentServerLines() =>
        _gameAgent.GetRecentEvents(700)
            .Where(evt => evt.Kind == GameAgentEventKind.ServerLine)
            .Select(evt => new MtcServerPrompt(evt.Timestamp, evt.PlainText))
            .ToArray();

    private Task PublishCurrentAgentPromptAsync() =>
        RunDashboardUiActionAsync(() =>
        {
            var snapshot = BuildGameAgentContextSnapshot();
            if (!snapshot.Connected)
            {
                return Task.CompletedTask;
            }

            var connectedEvent = new ProxyGameEvent(
                DateTimeOffset.UtcNow,
                "Connected",
                snapshot.CurrentSector > 0 ? snapshot.CurrentSector : null);
            if (!_dashboardHost.TryPublishGameEvent(DefaultAgentId, connectedEvent))
            {
                GlobalModules.DebugLog(
                    $"[Dashboard] agent event queue is full; dropped startup Connected for {DefaultAgentId}.\n");
            }

            IReadOnlyList<GameAgentEvent> recentEvents = _gameAgent.GetRecentEvents(120);
            GameAgentEvent? currentPrompt = FindCurrentAgentPrompt(
                recentEvents,
                DateTimeOffset.UtcNow);
            if (currentPrompt is null)
            {
                return Task.CompletedTask;
            }

            string promptText = string.IsNullOrWhiteSpace(currentPrompt.PromptSurface)
                ? currentPrompt.PlainText
                : currentPrompt.PromptSurface;
            string promptSurface = TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(
                promptText,
                out string classifiedSurface)
                ? classifiedSurface
                : promptText;
            DateTimeOffset? promptObservedAt = IsHeartbeatCurrentPromptConfirmed(currentPrompt)
                ? currentPrompt.Timestamp
                : GetPromptObservedAt(currentPrompt);
            if (promptObservedAt is not { } observedAt)
            {
                return Task.CompletedTask;
            }

            var promptEvent = new ProxyGameEvent(
                observedAt,
                "ServerPrompt",
                currentPrompt.CurrentSector > 0 ? currentPrompt.CurrentSector : null,
                promptSurface);
            if (!_dashboardHost.TryPublishGameEvent(DefaultAgentId, promptEvent))
            {
                GlobalModules.DebugLog(
                    $"[Dashboard] agent event queue is full; dropped startup prompt for {DefaultAgentId}.\n");
            }

            return Task.CompletedTask;
        });

    internal static GameAgentEvent? FindCurrentAgentPrompt(
        IReadOnlyList<GameAgentEvent> recentEvents,
        DateTimeOffset now)
    {
        TimeSpan maximumAgentPromptAge = TimeSpan.FromSeconds(45);
        int connectionBoundary = -1;
        bool connectedAfterBoundary = true;
        for (int index = 0; index < recentEvents.Count; index++)
        {
            if (recentEvents[index].Kind == GameAgentEventKind.Connected)
            {
                connectionBoundary = index;
                connectedAfterBoundary = true;
            }
            else if (recentEvents[index].Kind == GameAgentEventKind.Disconnected)
            {
                connectionBoundary = index;
                connectedAfterBoundary = false;
            }
        }

        if (connectionBoundary >= 0 && !connectedAfterBoundary)
        {
            return null;
        }

        GameAgentEvent? currentPrompt = null;
        int startIndex = connectionBoundary >= 0 ? connectionBoundary + 1 : 0;
        for (int index = startIndex; index < recentEvents.Count; index++)
        {
            GameAgentEvent candidate = recentEvents[index];
            if (string.IsNullOrWhiteSpace(candidate.PromptSurface) ||
                candidate.Timestamp > now)
            {
                continue;
            }

            TimeSpan maximumAge = candidate.Kind switch
            {
                GameAgentEventKind.ServerPrompt => TimeSpan.FromSeconds(45),
                GameAgentEventKind.PresenceHeartbeat => TimeSpan.FromSeconds(15),
                _ => TimeSpan.Zero,
            };
            bool heartbeatConfirmsCurrentPrompt =
                IsHeartbeatCurrentPromptConfirmed(candidate);
            DateTimeOffset? promptObservedAt = GetPromptObservedAt(candidate);
            if (maximumAge > TimeSpan.Zero &&
                now - candidate.Timestamp <= maximumAge &&
                promptObservedAt is { } observedAt &&
                observedAt <= now &&
                (now - observedAt <= maximumAgentPromptAge ||
                 heartbeatConfirmsCurrentPrompt))
            {
                currentPrompt = candidate;
            }
        }

        return currentPrompt;
    }

    private static DateTimeOffset? GetPromptObservedAt(GameAgentEvent gameEvent)
    {
        if (gameEvent.Kind != GameAgentEventKind.PresenceHeartbeat)
        {
            return gameEvent.Timestamp;
        }

        return gameEvent.Metadata.TryGetValue("promptObservedAt", out string? value) &&
            DateTimeOffset.TryParse(value, out DateTimeOffset observedAt)
                ? observedAt
                : null;
    }

    private static bool IsHeartbeatCurrentPromptConfirmed(GameAgentEvent gameEvent) =>
        gameEvent.Kind == GameAgentEventKind.PresenceHeartbeat &&
        gameEvent.Metadata.TryGetValue("promptIsCurrent", out string? value) &&
        bool.TryParse(value, out bool isCurrent) &&
        isCurrent;

    private Task RunDashboardUiActionAsync(Func<Task> action) =>
        InvokeMtcRpcUiAsync(async () =>
        {
            await action().ConfigureAwait(true);
            return true;
        });

    private Task<bool> RunDashboardUiActionAsync(Func<Task<bool>> action) =>
        InvokeMtcRpcUiAsync(action);

    private async Task StopDashboardHostAsync()
    {
        _automaticAgentStartupCancellation.Cancel();
        try
        {
            if (_automaticAgentStartupTask is { } startupTask)
                await startupTask;
            if (_reactiveObservationTask is { } reactiveObservationTask)
                await reactiveObservationTask;
            await _dashboardHost.DisposeAsync();
        }
        catch (Exception exception)
        {
            GlobalModules.DebugLog($"[Dashboard] failed to stop embedded dashboard: {exception}\n");
        }
    }
}
