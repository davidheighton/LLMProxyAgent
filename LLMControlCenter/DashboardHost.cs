using System.Net;
using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;
using Core = TWXProxy.Core;

namespace MTC;

internal sealed record GameAgentBackgroundStatusSnapshot(
    bool DashboardHostAvailable,
    bool AutonomousWorkerRunning,
    bool LearningModeRunning,
    AutonomousTestWorkerStatus? Worker,
    AgentRuntimeStatus? Runtime);

internal sealed class DashboardHost : IAsyncDisposable
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private WebApplication? _application;
    private bool _disposed;

    public Uri? Address { get; private set; }

    public GameAgentBackgroundStatusSnapshot GetAgentMonitorSnapshot(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        WebApplication? application = _application;
        if (application is null)
            return new GameAgentBackgroundStatusSnapshot(false, false, false, null, null);

        IServiceProvider services = application.Services;
        AutonomousTestWorkerStatus? workerStatus =
            services.GetRequiredService<AutonomousTestWorkerStatusStore>().GetStatus(agentId);
        AgentRuntimeStatus? runtimeStatus =
            services.GetRequiredService<IAgentRuntime>().GetStatus();
        bool workerRunning =
            services.GetService<IAutonomousTestWorkerController>()?.IsRunning == true;
        bool learningModeRunning =
            services.GetService<IAgentLearningSessionController>()?.IsRunning == true;
        return new GameAgentBackgroundStatusSnapshot(
            true,
            workerRunning,
            learningModeRunning,
            workerStatus,
            runtimeStatus);
    }

    public bool CanControlAgent(string agentId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        IAgentSessionController? sessionController = _application?.Services
            .GetService<IAgentSessionController>();
        return sessionController is { IsAvailable: true } &&
               sessionController.SupportsAgent(agentId) &&
               _application?.Services.GetService<IAutonomousTestWorkerController>() is not null &&
               _application?.Services.GetService<IAgentLearningSessionController>() is not null;
    }

    public async Task<AgentSessionControlStatus> SetAgentRunningAsync(
        string agentId,
        bool running,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        WebApplication application = _application
            ?? throw new InvalidOperationException("The embedded dashboard is not running.");
        IAgentSessionController sessionController = application.Services
            .GetService<IAgentSessionController>()
            ?? throw new InvalidOperationException("Agent session controls are unavailable.");
        if (!sessionController.IsAvailable || !sessionController.SupportsAgent(agentId))
        {
            throw new InvalidOperationException(
                $"Agent '{agentId}' cannot be controlled by the active MTC session.");
        }

        AgentSessionControlStatus status = await sessionController
            .GetStatusAsync(agentId, cancellationToken)
            .ConfigureAwait(false);
        if (status.IsAgentRunning != running)
        {
            if (running)
            {
                await sessionController.StartAgentAsync(agentId, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await sessionController.StopAgentAsync(agentId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        status = await sessionController
            .GetStatusAsync(agentId, cancellationToken)
            .ConfigureAwait(false);
        if (status.IsAgentRunning != running)
        {
            throw new InvalidOperationException(
                running
                    ? "The autonomous agent did not start."
                    : "The autonomous agent did not stop.");
        }

        return status;
    }

    public async Task<AgentSessionControlStatus> SetAgentLearningAsync(
        string agentId,
        bool learning,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        WebApplication application = _application
            ?? throw new InvalidOperationException("The embedded dashboard is not running.");
        IAgentSessionController sessionController = application.Services
            .GetService<IAgentSessionController>()
            ?? throw new InvalidOperationException("Agent session controls are unavailable.");
        if (!sessionController.IsAvailable || !sessionController.SupportsAgent(agentId))
        {
            throw new InvalidOperationException(
                $"Agent '{agentId}' cannot be controlled by the active MTC session.");
        }

        AgentSessionControlStatus status = await sessionController
            .GetStatusAsync(agentId, cancellationToken)
            .ConfigureAwait(false);
        if (status.IsLearning != learning)
        {
            if (learning)
            {
                await sessionController.StartLearningAsync(agentId, cancellationToken)
                    .ConfigureAwait(false);
            }
            else
            {
                await sessionController.StopLearningAsync(agentId, cancellationToken)
                    .ConfigureAwait(false);
            }
        }

        status = await sessionController
            .GetStatusAsync(agentId, cancellationToken)
            .ConfigureAwait(false);
        if (status.IsLearning != learning)
        {
            throw new InvalidOperationException(
                learning ? "Learn mode did not start." : "Learn mode did not stop.");
        }

        return status;
    }

    public void SetAgentStartupActivity(string agentId, string activity)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(activity);
        _application?.Services
            .GetRequiredService<AutonomousTestWorkerStatusStore>()
            .SetActivity(agentId, activity);
    }

    public async Task<string?> GetAgentGameProfileAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        WebApplication application = _application
            ?? throw new InvalidOperationException("The embedded dashboard is not running.");
        var agent = await application.Services.GetRequiredService<IAgentStore>()
            .GetAgentAsync(agentId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Agent '{agentId}' is not configured.");
        return agent.Configuration.GetValueOrDefault("Session:GameProfile");
    }

    public async Task<bool> IsAgentAutoStartEnabledAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        WebApplication application = _application
            ?? throw new InvalidOperationException("The embedded dashboard is not running.");
        var agent = await application.Services.GetRequiredService<IAgentStore>()
            .GetAgentAsync(agentId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Agent '{agentId}' is not configured.");
        return AgentSessionStartupSettings.IsAutoStartEnabled(agent);
    }

    public async Task WaitForAgentModelAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        WebApplication application = _application
            ?? throw new InvalidOperationException("The embedded dashboard is not running.");
        var agent = await application.Services
            .GetRequiredService<IAgentStore>()
            .GetAgentAsync(agentId, cancellationToken)
            .ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Agent '{agentId}' is not configured.");
        string modelName = agent.Configuration.TryGetValue("Brains:Pilot:Model", out string? configuredModel) &&
            !string.IsNullOrWhiteSpace(configuredModel)
                ? configuredModel.Trim()
                : agent.PreferredModel.Trim();
        ArgumentException.ThrowIfNullOrWhiteSpace(modelName);

        IConfiguration configuration = application.Services.GetRequiredService<IConfiguration>();
        string endpointValue = configuration["Reasoning:OllamaEndpoint"] ?? "http://localhost:11434/";
        if (!Uri.TryCreate(endpointValue, UriKind.Absolute, out Uri? endpoint) ||
            (endpoint.Scheme != Uri.UriSchemeHttp && endpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                "Reasoning:OllamaEndpoint must be an absolute HTTP or HTTPS URL.");
        }

        Uri modelsEndpoint = new(endpoint, "api/tags");
        var statusStore = application.Services.GetRequiredService<AutonomousTestWorkerStatusStore>();
        using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
        bool ollamaLaunchAttempted = false;

        while (true)
        {
            cancellationToken.ThrowIfCancellationRequested();
            statusStore.SetActivity(agentId, $"Startup: waiting for Ollama model {modelName}.");

            try
            {
                using HttpResponseMessage response = await client
                    .GetAsync(modelsEndpoint, cancellationToken)
                    .ConfigureAwait(false);
                response.EnsureSuccessStatusCode();
                await using Stream responseStream = await response.Content
                    .ReadAsStreamAsync(cancellationToken)
                    .ConfigureAwait(false);
                using JsonDocument document = await JsonDocument
                    .ParseAsync(responseStream, cancellationToken: cancellationToken)
                    .ConfigureAwait(false);
                bool modelAvailable =
                    document.RootElement.TryGetProperty("models", out JsonElement models) &&
                    models.ValueKind == JsonValueKind.Array &&
                    models.EnumerateArray().Any(model =>
                        model.TryGetProperty("name", out JsonElement name) &&
                        name.ValueKind == JsonValueKind.String &&
                        string.Equals(name.GetString(), modelName, StringComparison.OrdinalIgnoreCase));
                if (modelAvailable)
                {
                    statusStore.SetActivity(agentId, $"Startup: Ollama model {modelName} is ready.");
                    return;
                }

                statusStore.SetActivity(
                    agentId,
                    $"Startup: Ollama is responding, waiting for model {modelName} to be installed.");
            }
            catch (HttpRequestException exception)
            {
                if (!ollamaLaunchAttempted && endpoint.IsLoopback)
                {
                    ollamaLaunchAttempted = true;
                    try
                    {
                        using Process? process = Process.Start(new ProcessStartInfo
                        {
                            FileName = "ollama",
                            UseShellExecute = false,
                            CreateNoWindow = true,
                            ArgumentList = { "serve" }
                        });
                        if (process is null)
                        {
                            throw new InvalidOperationException("The Ollama server process did not start.");
                        }
                    }
                    catch (Win32Exception launchException)
                    {
                        Core.GlobalModules.DebugLog(
                            $"[Dashboard] could not start local Ollama automatically: {launchException}\n");
                    }
                }

                statusStore.SetActivity(
                    agentId,
                    $"Startup: waiting for Ollama at {endpoint.Host}:{endpoint.Port} ({exception.Message}).");
            }
            catch (TaskCanceledException exception) when (!cancellationToken.IsCancellationRequested)
            {
                statusStore.SetActivity(
                    agentId,
                    $"Startup: Ollama did not respond in time ({exception.Message}).");
            }

            await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        }
    }

    public async Task StartAgentAsync(
        string agentId,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        WebApplication application = _application
            ?? throw new InvalidOperationException("The embedded dashboard is not running.");
        await application.Services.GetRequiredService<IAgentSessionController>()
            .StartAgentAsync(agentId, cancellationToken)
            .ConfigureAwait(false);
    }

    public async Task<Uri> StartAsync(
        string agentId,
        string eventLogDirectory,
        MtcGameCommandSender commandSender,
        Func<bool> isConnected,
        Func<Task<bool>> canConnectAsync,
        Func<Task> configureConnectionAsync,
        Func<Task<bool>> loadConnectionAsync,
        Func<Task> connectAsync,
        Func<Task> disconnectAsync,
        Func<Task> publishCurrentPromptAsync,
        Func<string> getCurrentGameName,
        Func<Core.ModDatabase?> getSessionDatabase,
        Func<IntegratedGameClientSnapshot> getIntegratedGameClientSnapshot,
        Func<GameScriptInputPrompt?> getCurrentScriptInputPrompt,
        MtcSessionWindowHost? sessionWindowHost = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentException.ThrowIfNullOrWhiteSpace(eventLogDirectory);
        ArgumentNullException.ThrowIfNull(commandSender);
        ArgumentNullException.ThrowIfNull(getSessionDatabase);
        ArgumentNullException.ThrowIfNull(getIntegratedGameClientSnapshot);
        ArgumentNullException.ThrowIfNull(getCurrentScriptInputPrompt);

        await _lifecycle.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_application is not null)
                return Address!;

            WebApplication application = await DashboardApplication.BuildAsync(
                [],
                configureBuilder: builder =>
                {
                    builder.Configuration["LLMControlCenter:EmbeddedHost"] = "true";
                    builder.Configuration["Dashboard:HttpsRedirection"] = "false";
                    builder.Configuration["AutonomousTesting:Enabled"] = "true";
                    builder.Configuration["AutonomousTesting:AutoStart"] = "false";
                    builder.Configuration["AutonomousTesting:AgentId"] = agentId;
                    builder.Configuration["AutonomousTesting:TeamId"] =
                        Environment.GetEnvironmentVariable("LLMCONTROL_TEAM_ID") ?? string.Empty;
                    string sessionName = getCurrentGameName();
                    builder.Configuration["AutonomousTesting:SessionName"] =
                        string.IsNullOrWhiteSpace(sessionName) ? agentId : sessionName;
                    builder.Configuration["AutonomousGameplay:Enabled"] = "true";
                    builder.Configuration["AutonomousGameplay:MaximumActionsPerSession"] =
                        AutonomousTestWorker.UnlimitedActionsPerSession.ToString(
                            System.Globalization.CultureInfo.InvariantCulture);
                    builder.Configuration["AutonomousGameplay:RequireMovementApproval"] = "false";
                    builder.Configuration[$"GameState:EventLogPaths:{agentId}"] =
                        Path.GetFullPath(eventLogDirectory);
                    EpisodeCommandLine.ApplyEnvironment(builder.Configuration);
                    builder.WebHost.ConfigureKestrel(server =>
                        server.Listen(IPAddress.Loopback, 0));
                },
                configureServices: services =>
                {
                    services.AddSingleton<IShipCapacityProvider>(new ShipCatalogCapacityProvider(() =>
                    {
                        string gameName = getCurrentGameName();
                        return string.IsNullOrWhiteSpace(gameName)
                            ? null
                            : Path.Combine(AppPaths.TwxproxyGamesDir, gameName, "ships.cfg");
                    }));
                    if (EpisodeCommandLine.ReadEnvironment() is { } episodeOptions)
                    {
                        services.AddSingleton<IEpisodeCompletionHandler>(serviceProvider =>
                            new MtcEpisodeCompletionHandler(
                                episodeOptions,
                                serviceProvider.GetRequiredService<ITrajectoryStore>()));
                    }
                    var sectorProvider = new MtcFreshAdjacentSectorProvider(getSessionDatabase);
                    services.AddSingleton<IGameCommandSender>(commandSender);
                    services.AddSingleton<IAutonomousGameLoginInputSender>(commandSender);
                    services.AddSingleton<IAutonomousGameScriptRunner>(commandSender);
                    services.AddSingleton<IAutonomousMoveTargetProvider>(sectorProvider);
                    services.AddSingleton<IAutonomousGameWorldProvider>(sectorProvider);
                    services.AddSingleton<IAutonomousPortTradeOpportunityProvider>(sectorProvider);
                    services.AddSingleton<IAutonomousFedSpaceFastStartProvider>(sectorProvider);
                    var snapshotProvider = new DynamicMtcGameSituationProvider(
                        agentId,
                        getCurrentGameName,
                        getIntegratedGameClientSnapshot,
                        getCurrentScriptInputPrompt);
                    services.AddSingleton(snapshotProvider);
                    services.AddSingleton<ISynchronizedGameSnapshotProvider>(snapshotProvider);
                    services.AddSingleton<IGameSituationProvider>(snapshotProvider);
                    services.AddSingleton<GameSituationObservationService>();
                    services.AddSingleton<IAgentLearningSessionController>(serviceProvider =>
                        new AgentLearningSessionController(
                            serviceProvider.GetRequiredService<GameSituationObservationService>(),
                            serviceProvider.GetRequiredService<
                                Microsoft.Extensions.Logging.ILogger<AgentLearningSessionController>>(),
                            TimeSpan.FromSeconds(15)));
                    string loginProfilePath = Path.Combine(
                        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                        "LLMSurvivingProxyAgent",
                        "game-login.json");
                    var loginCredentialProvider = new MtcAutonomousGameLoginCredentialProvider(
                        loginProfilePath,
                        getCurrentGameName,
                        string.Equals(
                            Environment.GetEnvironmentVariable("LLMCONTROL_CREATE_ACCOUNT_IF_NEEDED"),
                            "true",
                            StringComparison.OrdinalIgnoreCase));
                    commandSender.ConfigureScriptPasswordProvider(
                        () => loginCredentialProvider.GetCredentials(agentId)?.Password,
                        provider =>
                        {
                            Core.ModDatabase? database = getSessionDatabase();
                            if (database is null && provider is not null)
                            {
                                throw new InvalidOperationException(
                                    "The MTC session database is unavailable for script credentials.");
                            }

                            database?.SetScriptPasswordProvider(provider);
                        });
                    services.AddSingleton<IAutonomousGameLoginCredentialProvider>(
                        loginCredentialProvider);
                    services.AddSingleton<IAgentSessionController>(serviceProvider =>
                        new EmbeddedMtcAgentSessionController(
                            agentId,
                            serviceProvider.GetRequiredService<IAgentStore>(),
                            serviceProvider.GetRequiredService<IAutonomousTestWorkerController>(),
                            serviceProvider.GetRequiredService<IAgentLearningSessionController>(),
                            commandSender,
                            isConnected,
                            () => getSessionDatabase() is not null,
                            canConnectAsync,
                            configureConnectionAsync,
                            loadConnectionAsync,
                            connectAsync,
                            disconnectAsync,
                            publishCurrentPromptAsync));
                    services.AddSingleton<IAgentSessionLauncher>(serviceProvider =>
                        new MtcAgentSessionLauncher(
                            serviceProvider.GetRequiredService<IAgentStore>(),
                            sessionWindowHost));
                },
                environmentName: Environments.Production);

            try
            {
                await application.StartAsync(cancellationToken).ConfigureAwait(false);
                string? address = application.Services
                    .GetRequiredService<IServer>()
                    .Features
                    .Get<IServerAddressesFeature>()?
                    .Addresses
                    .SingleOrDefault(value => value.StartsWith("http://127.0.0.1:", StringComparison.Ordinal));
                if (!Uri.TryCreate(address, UriKind.Absolute, out Uri? dashboardAddress))
                {
                    throw new InvalidOperationException(
                        "The embedded dashboard server did not report its loopback address.");
                }

                _application = application;
                Address = dashboardAddress;
                return dashboardAddress;
            }
            catch
            {
                await application.DisposeAsync().ConfigureAwait(false);
                throw;
            }
        }
        finally
        {
            _lifecycle.Release();
        }
    }

    public bool TryPublishGameEvent(string agentId, ProxyGameEvent gameEvent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(agentId);
        ArgumentNullException.ThrowIfNull(gameEvent);
        if (_application is null ||
            _application.Services.GetService<IAutonomousTestWorkerController>()?.IsRunning != true)
        {
            return true;
        }

        return _application.Services.GetRequiredService<ProxyGameEventHub>()
            .TryPublish(new ProxySessionEvent(agentId, gameEvent));
    }

    public Task<ReactiveEventRecord> SubmitReactiveEventAsync(
        ReactiveEvent reactiveEvent,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(reactiveEvent);
        WebApplication application = _application
            ?? throw new InvalidOperationException("The embedded dashboard is not running.");
        return application.Services.GetRequiredService<IAgentRuntime>()
            .SubmitReactiveEventAsync(reactiveEvent, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        await _lifecycle.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed)
                return;

            _disposed = true;
            if (_application is not null)
            {
                await _application.StopAsync().ConfigureAwait(false);
                await _application.DisposeAsync().ConfigureAwait(false);
                _application = null;
                Address = null;
            }
        }
        finally
        {
            _lifecycle.Release();
            _lifecycle.Dispose();
        }
    }
}

internal sealed class DynamicMtcGameSituationProvider(
    string agentId,
    Func<string> getCurrentGameName,
    Func<IntegratedGameClientSnapshot> getIntegratedGameClientSnapshot,
    Func<GameScriptInputPrompt?> getCurrentScriptInputPrompt) :
    ISynchronizedGameSnapshotProvider,
    IGameSituationProvider
{
    public Task<GameSnapshot?> CaptureSynchronizedSnapshotAsync(
        string requestedAgentId,
        CancellationToken cancellationToken = default) =>
        CreateProvider(requestedAgentId).CaptureSynchronizedSnapshotAsync(
            requestedAgentId,
            cancellationToken);

    public async Task<GameSituationReport> CaptureSituationAsync(
        string requestedAgentId,
        CancellationToken cancellationToken = default)
    {
        GameSituationReport report = await CreateProvider(requestedAgentId).CaptureSituationAsync(
            requestedAgentId,
            cancellationToken).ConfigureAwait(false);
        return report with
        {
            IntegratedGameClient = getIntegratedGameClientSnapshot(),
            ScriptInputPrompt = getCurrentScriptInputPrompt()
        };
    }

    private EventLogGameSnapshotProvider CreateProvider(string requestedAgentId)
    {
        if (!string.Equals(requestedAgentId, agentId, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Agent '{requestedAgentId}' is not attached to the active MTC session.");
        }

        string gameName = getCurrentGameName();
        if (string.IsNullOrWhiteSpace(gameName))
        {
            throw new InvalidOperationException("MTC has no selected game for the active agent session.");
        }

        string eventLogDirectory = Path.Combine(
            AppPaths.TwxproxyGamesDir,
            gameName,
            "agent");
        return new EventLogGameSnapshotProvider(
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                [agentId] = eventLogDirectory
            });
    }
}
