using LLMSurvivingProxyAgent.Agent;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Dashboard;
using LLMSurvivingProxyAgent.Dashboard.Components;
using LLMSurvivingProxyAgent.Goals;
using LLMSurvivingProxyAgent.Knowledge;
using LLMSurvivingProxyAgent.Macros;
using LLMSurvivingProxyAgent.Models;
using LLMSurvivingProxyAgent.Packaging;
using LLMSurvivingProxyAgent.Reactive;
using LLMSurvivingProxyAgent.Runtime;
using LLMSurvivingProxyAgent.TradeWars;
using Microsoft.AspNetCore.Hosting.StaticWebAssets;
using Microsoft.Data.Sqlite;

var app = await DashboardApplication.BuildAsync(args);
await app.RunAsync();

public static class DashboardApplication
{
    public static async Task<WebApplication> BuildAsync(
        string[] args,
        Action<WebApplicationBuilder>? configureBuilder = null,
        Action<IServiceCollection>? configureServices = null,
        string? environmentName = null)
    {
        var applicationDirectory = Path.GetDirectoryName(typeof(DashboardApplication).Assembly.Location)
            ?? throw new InvalidOperationException("The dashboard assembly has no application directory.");
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            Args = args,
            ApplicationName = typeof(DashboardApplication).Assembly.GetName().Name,
            ContentRootPath = applicationDirectory,
            EnvironmentName = environmentName,
        });
        StaticWebAssetsLoader.UseStaticWebAssets(builder.Environment, builder.Configuration);
        configureBuilder?.Invoke(builder);

        var loginProfilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "LLMSurvivingProxyAgent",
        "game-login.json");
        if (builder.Environment.IsDevelopment())
        {
        builder.Configuration.AddJsonFile(loginProfilePath, optional: true, reloadOnChange: false);
        builder.Configuration.AddEnvironmentVariables();
        }
        builder.Configuration.AddCommandLine(args);

        builder.Services.AddRazorComponents()
        .AddInteractiveServerComponents();
        builder.Services.AddHttpClient<IAutonomousLoginIdentityProvider, OllamaAutonomousLoginIdentityProvider>(
            client => client.Timeout = TimeSpan.FromMinutes(2));
        builder.Services.AddSingleton(new AutonomousLoginCredentialStore(loginProfilePath));

        var databasePath = Path.GetFullPath(builder.Configuration["AgentDatabasePath"]
        ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "LLMSurvivingProxyAgent",
            "agents.db"));
        var dataDirectory = Path.GetDirectoryName(databasePath)
        ?? throw new InvalidOperationException("AgentDatabasePath must include a directory.");
        Directory.CreateDirectory(dataDirectory);
        var seedKnowledgePath = Path.GetFullPath(builder.Configuration["Knowledge:SeedDatabasePath"]
        ?? Path.Combine(dataDirectory, "knowledge.seed.db"));
        var runtimeKnowledgePath = Path.GetFullPath(builder.Configuration["Knowledge:RuntimeDatabasePath"]
        ?? Path.Combine(dataDirectory, "knowledge.runtime.db"));
        var trajectoryDatabasePath = Path.GetFullPath(builder.Configuration["Learning:TrajectoryDatabasePath"]
        ?? Path.Combine(dataDirectory, "trajectories.db"));
        // Cross-life memory lives beside the agent registry, outside any one game's folder, so it survives
        // character deaths, game resets, and harness learner cleanup.
        var playbookDatabasePath = Path.GetFullPath(builder.Configuration["Learning:PlaybookDatabasePath"]
        ?? Path.Combine(dataDirectory, "playbook.db"));
        if (seedKnowledgePath.Equals(runtimeKnowledgePath, StringComparison.OrdinalIgnoreCase) ||
        seedKnowledgePath.Equals(databasePath, StringComparison.OrdinalIgnoreCase) ||
        runtimeKnowledgePath.Equals(databasePath, StringComparison.OrdinalIgnoreCase) ||
        trajectoryDatabasePath.Equals(databasePath, StringComparison.OrdinalIgnoreCase) ||
        trajectoryDatabasePath.Equals(seedKnowledgePath, StringComparison.OrdinalIgnoreCase) ||
        trajectoryDatabasePath.Equals(runtimeKnowledgePath, StringComparison.OrdinalIgnoreCase) ||
        new[] { databasePath, seedKnowledgePath, runtimeKnowledgePath, trajectoryDatabasePath }
            .Contains(playbookDatabasePath, StringComparer.OrdinalIgnoreCase))
        {
        throw new InvalidOperationException(
            "Agent, seed knowledge, runtime knowledge, trajectory, and playbook databases must use separate file paths.");
        }
        Directory.CreateDirectory(Path.GetDirectoryName(seedKnowledgePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(runtimeKnowledgePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(trajectoryDatabasePath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(playbookDatabasePath)!);

        static string BuildConnectionString(string path) => new SqliteConnectionStringBuilder
        {
        DataSource = path,
        ForeignKeys = true
        }.ToString();
        var connectionString = BuildConnectionString(databasePath);
        var seedKnowledgeStore = new SqliteKnowledgeStore(BuildConnectionString(seedKnowledgePath));
        var runtimeKnowledgeStore = new SqliteKnowledgeStore(BuildConnectionString(runtimeKnowledgePath));
        builder.Services.AddSingleton<IAgentStore>(_ => new SqliteAgentStore(connectionString));
        builder.Services.AddSingleton<IAgentTeamStore>(_ => new SqliteAgentTeamStore(connectionString));
        builder.Services.AddSingleton<IAgentSessionController, UnavailableAgentSessionController>();
        builder.Services.AddSingleton<IAgentSessionLauncher, UnavailableAgentSessionLauncher>();
        builder.Services.AddSingleton<IWorldStateStore>(_ => new SqliteWorldStateStore(connectionString));
        builder.Services.AddSingleton<NavigationService>();
        builder.Services.AddSingleton<CimImportService>();
        builder.Services.AddSingleton<IMacroTelemetryStore>(_ => new SqliteMacroTelemetryStore(connectionString));
        builder.Services.AddSingleton<IAgentExperienceRecorder>(
        _ => new AgentExperienceKnowledgeRecorder(runtimeKnowledgeStore));
        builder.Services.AddSingleton<IKnowledgeStore>(_ =>
        new CompositeKnowledgeStore(seedKnowledgeStore, runtimeKnowledgeStore));
        builder.Services.AddSingleton<ProxyGameEventHub>();
        builder.Services.AddSingleton<ProxyGameCommandBroker>();
        builder.Services.AddSingleton<IAutonomousMoveApprovalGate, AutonomousMoveApprovalGate>();
        builder.Services.AddSingleton<AutonomousTestWorkerStatusStore>();
        builder.Services.AddSingleton<ILlmInteractionHistoryStore>(
        _ => new SqliteLlmInteractionHistoryStore(BuildConnectionString(trajectoryDatabasePath)));
        builder.Services.AddSingleton<ITrajectoryStore>(_ =>
        {
            var trajectoryStore = new SqliteTrajectoryStore(BuildConnectionString(trajectoryDatabasePath));
            trajectoryStore.InitializeAsync().GetAwaiter().GetResult();
            return trajectoryStore;
        });
        builder.Services.AddSingleton<IPlaybookStore>(_ =>
        {
            var playbookStore = new SqlitePlaybookStore(BuildConnectionString(playbookDatabasePath));
            playbookStore.InitializeAsync().GetAwaiter().GetResult();
            return playbookStore;
        });
        builder.Services.AddSingleton<IPlaybookGuidanceProvider, PlaybookGuidanceProvider>();
        builder.Services.AddSingleton<ITrajectoryObserver>(serviceProvider =>
            serviceProvider.GetRequiredService<PlaybookLearner>());
        builder.Services.AddSingleton<ITrajectoryRecorder, TrajectoryRecorder>();
        builder.Services.AddSingleton<IGameCommandSender>(
        serviceProvider => serviceProvider.GetRequiredService<ProxyGameCommandBroker>());
        builder.Services.AddSingleton<IAutonomousMoveTargetProvider, FreshAdjacentSectorProvider>();
        builder.Services.AddSingleton<
        IAutonomousActionKnowledgeProvider,
        KnowledgeBackedAutonomousActionKnowledgeProvider>();
        var proxyListeners = builder.Configuration
        .GetSection("GameProxy:Listeners")
        .Get<List<GameProxyListenerOptions>>() ?? [];
        var isEmbeddedControlCenterHost =
        builder.Configuration.GetValue<bool>("LLMControlCenter:EmbeddedHost");
        if (isEmbeddedControlCenterHost && proxyListeners.Count > 0)
        {
        throw new InvalidOperationException(
            "The embedded control-center host cannot start a second GameProxy listener.");
        }
        var autonomousTestingEnabled = builder.Configuration.GetValue<bool>("AutonomousTesting:Enabled");
        var autonomousTestingAutoStart =
        builder.Configuration.GetValue<bool?>("AutonomousTesting:AutoStart") ?? autonomousTestingEnabled;
        var autonomousGameplayEnabled = builder.Configuration.GetValue<bool>("AutonomousGameplay:Enabled");
        var allowLegacyStandaloneHost =
        builder.Configuration.GetValue<bool>("LLMControlCenter:AllowLegacyStandaloneHost");
        var autonomousTestAgentId = builder.Configuration["AutonomousTesting:AgentId"] ?? "meridian";
        var autonomousTestTeamId = builder.Configuration["AutonomousTesting:TeamId"] ?? string.Empty;
        var autonomousTestSessionName = builder.Configuration["AutonomousTesting:SessionName"] ?? string.Empty;
        bool standaloneHostWouldControlGameplay =
        proxyListeners.Count > 0 ||
        autonomousGameplayEnabled ||
        builder.Configuration.GetValue<bool>("AutonomousLogin:Enabled");
        if (!isEmbeddedControlCenterHost &&
        standaloneHostWouldControlGameplay &&
        (!allowLegacyStandaloneHost || !builder.Environment.IsDevelopment()))
        {
        throw new InvalidOperationException(
            "The standalone dashboard cannot own a TradeWars connection or autonomous gameplay. " +
            "Run LLMControlCenter to use the integrated MTC client, TWX database, and agent runtime. " +
            "For isolated development only, explicitly enable " +
            "LLMControlCenter:AllowLegacyStandaloneHost.");
        }
        var pilotModel = builder.Configuration["Brains:Pilot:Model"] ??
            builder.Configuration["AutonomousTesting:Model"] ?? string.Empty;
        var reactiveModel = builder.Configuration["Brains:Reactive:Model"] ?? "qwen3:1.7b";
        var strategicModel = builder.Configuration["Brains:Strategic:Model"] ?? "qwen3:8b";
        var ollamaEndpointValue = builder.Configuration["Reasoning:OllamaEndpoint"] ?? "http://localhost:11434/";
        if (!Uri.TryCreate(ollamaEndpointValue, UriKind.Absolute, out var ollamaEndpoint) ||
            (ollamaEndpoint.Scheme != Uri.UriSchemeHttp && ollamaEndpoint.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException("Reasoning:OllamaEndpoint must be an absolute HTTP or HTTPS URL.");
        }
        var playbookMemoryId = builder.Configuration["Learning:PlaybookMemoryId"] is { Length: > 0 } configuredMemoryId
            ? configuredMemoryId
            : EpisodeOptionsConfiguration.Read(builder.Configuration)?.MemoryId;
        if (playbookMemoryId is not null && !PlaybookMemory.IsValidMemoryId(playbookMemoryId))
        {
            throw new InvalidOperationException(
                "Learning:PlaybookMemoryId must be 1-64 characters using letters, digits, '.', '_' or '-'.");
        }
        var playbookGameKey = string.IsNullOrWhiteSpace(autonomousTestSessionName) ? null : autonomousTestSessionName;
        builder.Services.AddSingleton(new PlaybookSettings(playbookMemoryId, playbookGameKey));
        builder.Services.AddSingleton(serviceProvider => new PlaybookCoachingService(
            serviceProvider.GetRequiredService<IPlaybookStore>(),
            serviceProvider.GetRequiredService<PlaybookSettings>(),
            serviceProvider.GetService<IGameSituationProvider>()));
        builder.Services.AddHttpClient<ILessonDistiller, OllamaLessonDistiller>(
            client => client.Timeout = TimeSpan.FromMinutes(5));
        builder.Services.AddSingleton(serviceProvider => new PlaybookLearner(
            serviceProvider.GetRequiredService<IPlaybookStore>(),
            serviceProvider.GetRequiredService<ITrajectoryStore>(),
            serviceProvider.GetRequiredService<ILogger<PlaybookLearner>>(),
            new PlaybookLearnerOptions
            {
                GameKey = playbookGameKey,
                MemoryId = playbookMemoryId,
                // Reflection sends game observations to the model, so it only runs against a local endpoint.
                DistillationModel = ollamaEndpoint.IsLoopback &&
                                    !builder.Configuration.GetValue("Learning:DisableDistillation", false)
                    ? new ModelConfiguration("Ollama", strategicModel, ollamaEndpoint)
                    : null
            },
            serviceProvider.GetService<ILessonDistiller>()));
        if (autonomousGameplayEnabled &&
            !builder.Environment.IsDevelopment() &&
            !isEmbeddedControlCenterHost)
        {
        throw new InvalidOperationException(
            "Autonomous gameplay actions are available only in Development or the embedded Control Center.");
        }
        if (autonomousGameplayEnabled && !autonomousTestingEnabled)
        {
        throw new InvalidOperationException(
            "AutonomousGameplay:Enabled requires AutonomousTesting to be enabled.");
        }
        if (isEmbeddedControlCenterHost && autonomousTestingAutoStart)
        {
        throw new InvalidOperationException(
            "The embedded Control Center requires an explicit agent start action.");
        }
        foreach (var listener in proxyListeners)
        {
        if (string.IsNullOrWhiteSpace(listener.EventLogDirectory))
        {
            throw new InvalidOperationException(
                $"GameProxy listener '{listener.AgentId}' must configure an EventLogDirectory.");
        }

        listener.EventLogDirectory = Path.GetFullPath(
            listener.EventLogDirectory,
            builder.Environment.ContentRootPath);
        }
        if (proxyListeners.Count > 0)
        {
        builder.Services.AddSingleton<IReadOnlyList<GameProxyListenerOptions>>(proxyListeners);
        builder.Services.AddHostedService<TradeWarsTcpProxyService>();
        }

        var gameEventLogPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var entry in builder.Configuration.GetSection("GameState:EventLogPaths").GetChildren())
        {
        if (string.IsNullOrWhiteSpace(entry.Key) || string.IsNullOrWhiteSpace(entry.Value))
        {
            throw new InvalidOperationException(
                "GameState:EventLogPaths entries must map an agent ID to a game event-log file or directory.");
        }

        if (!gameEventLogPaths.TryAdd(
            entry.Key,
            Path.GetFullPath(entry.Value, builder.Environment.ContentRootPath)))
        {
            throw new InvalidOperationException(
                $"GameState:EventLogPaths contains a duplicate agent ID '{entry.Key}'.");
        }
        }
        foreach (var listener in proxyListeners)
        {
        if (!gameEventLogPaths.TryAdd(listener.AgentId, listener.EventLogDirectory))
        {
            throw new InvalidOperationException(
                $"Agent '{listener.AgentId}' has more than one configured game event-log source.");
        }
        }
        if (gameEventLogPaths.Count > 0)
        {
        var snapshotProvider = new EventLogGameSnapshotProvider(gameEventLogPaths);
        builder.Services.AddSingleton<ISynchronizedGameSnapshotProvider>(snapshotProvider);
        builder.Services.AddSingleton<IGameSituationProvider>(snapshotProvider);
        builder.Services.AddSingleton<GameSituationObservationService>();
        }
        builder.Services.AddSingleton(sp => new AgentExportPackageService(
        sp.GetRequiredService<IAgentStore>(),
        sp.GetRequiredService<IGoalProposalStore>(),
        sp.GetRequiredService<IMacroTelemetryStore>(),
        sp.GetRequiredService<IReactiveEventStore>(),
        sp.GetRequiredService<IStrategicReviewStore>(),
        runtimeKnowledgeStore,
        seedKnowledgeStore));
        builder.Services.AddSingleton<IGoalProposalStore>(_ => new SqliteGoalProposalStore(connectionString));
        builder.Services.AddSingleton<IStrategicReviewStore>(_ => new SqliteStrategicReviewStore(connectionString));
        builder.Services.AddSingleton<IReactiveEventStore>(_ => new SqliteReactiveEventStore(connectionString));
        builder.Services.AddHttpClient<IReflexReasoningProvider, OllamaReflexReasoningProvider>();
        builder.Services.AddSingleton(sp => new ReactiveIntelligenceService(
            sp.GetRequiredService<IReactiveEventStore>(),
            sp.GetRequiredService<IReflexReasoningProvider>(),
            new ModelConfiguration("Ollama", reactiveModel, ollamaEndpoint),
            sp.GetRequiredService<IAgentStore>()));
        builder.Services.AddSingleton<AgentRuntimeQueue>();
        builder.Services.AddSingleton<IAgentRuntime>(sp => sp.GetRequiredService<AgentRuntimeQueue>());
        builder.Services.AddHostedService<AgentRuntimeWorker>();
        builder.Services.AddSingleton<GoalProposalService>();
        builder.Services.AddHttpClient<IStrategicReasoningProvider, OllamaReasoningProvider>();
        if (autonomousTestingEnabled)
        {
        if (!ollamaEndpoint.IsLoopback)
        {
            throw new InvalidOperationException(
                "Autonomous testing requires a loopback Ollama endpoint so game observations stay local.");
        }
        if (!proxyListeners.Any(listener =>
                string.Equals(listener.AgentId, autonomousTestAgentId, StringComparison.OrdinalIgnoreCase)) &&
            !gameEventLogPaths.ContainsKey(autonomousTestAgentId))
        {
            throw new InvalidOperationException(
                $"Autonomous testing requires a game session or event-log source for agent '{autonomousTestAgentId}'.");
        }
        if (autonomousTestingAutoStart)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(pilotModel);
        }

        builder.Services.AddHttpClient<IAutonomousTestActionProvider, OllamaAutonomousTestActionProvider>();
        EpisodeOptions? episodeOptions = EpisodeOptionsConfiguration.Read(builder.Configuration);
        AutonomousTestWorker CreateAutonomousTestWorker(IServiceProvider serviceProvider, string modelName)
        {
            var objectiveProvider = new StrategicReviewAutonomousActionObjectiveProvider(
                serviceProvider.GetRequiredService<IGoalProposalStore>(),
                serviceProvider.GetRequiredService<GoalProposalService>(),
                serviceProvider.GetRequiredService<StrategicReviewService>(),
                new ModelConfiguration("Ollama", strategicModel, ollamaEndpoint),
                serviceProvider.GetRequiredService<ILogger<StrategicReviewAutonomousActionObjectiveProvider>>());
            return new AutonomousTestWorker(
                autonomousTestAgentId,
                modelName,
                ollamaEndpoint,
                serviceProvider.GetRequiredService<ProxyGameEventHub>(),
                serviceProvider.GetRequiredService<IGameSituationProvider>(),
                serviceProvider.GetRequiredService<IAutonomousTestActionProvider>(),
                serviceProvider.GetRequiredService<IGameCommandSender>(),
                serviceProvider.GetRequiredService<ILogger<AutonomousTestWorker>>(),
                serviceProvider.GetRequiredService<IAutonomousMoveTargetProvider>(),
                autonomousGameplayEnabled,
                autonomousGameplayEnabled
                    ? AutonomousTestWorker.UnlimitedActionsPerSession
                    : 1,
                serviceProvider.GetRequiredService<IAutonomousActionKnowledgeProvider>(),
                objectiveProvider,
                serviceProvider.GetRequiredService<IAutonomousMoveApprovalGate>(),
                serviceProvider.GetRequiredService<AutonomousTestWorkerStatusStore>(),
                serviceProvider.GetService<IAutonomousGameLoginCredentialProvider>(),
                serviceProvider.GetService<IAutonomousGameLoginInputSender>(),
                requireMovementApproval: builder.Configuration.GetValue(
                    "AutonomousGameplay:RequireMovementApproval",
                    defaultValue: false),
                serviceProvider.GetService<IAutonomousGameWorldProvider>(),
                serviceProvider.GetService<IAutonomousGameScriptRunner>(),
                serviceProvider.GetService<IAutonomousPortTradeOpportunityProvider>(),
                serviceProvider.GetRequiredService<IAgentStore>(),
                reactiveEventStore: serviceProvider.GetRequiredService<IReactiveEventStore>(),
                fedSpaceFastStartProvider: serviceProvider.GetService<IAutonomousFedSpaceFastStartProvider>(),
                teamStore: serviceProvider.GetRequiredService<IAgentTeamStore>(),
                teamId: autonomousTestTeamId,
                sessionName: autonomousTestSessionName,
                loginIdentityProvider: serviceProvider.GetService<IAutonomousLoginIdentityProvider>(),
                loginCredentialStore: serviceProvider.GetService<AutonomousLoginCredentialStore>(),
                trajectoryRecorder: serviceProvider.GetService<ITrajectoryRecorder>(),
                episodeSupervisor: episodeOptions is null
                    ? null
                    : new EpisodeSupervisor(
                        episodeOptions,
                        serviceProvider.GetRequiredService<ITrajectoryRecorder>(),
                        serviceProvider.GetService<IShipCapacityProvider>(),
                        serviceProvider.GetRequiredService<ILogger<EpisodeSupervisor>>()),
                episodeCompletionHandler: episodeOptions is null
                    ? null
                    : serviceProvider.GetService<IEpisodeCompletionHandler>() ??
                      (episodeOptions.ResultPath is { } resultPath
                          ? new EpisodeResultFileWriter(resultPath, serviceProvider.GetRequiredService<ITrajectoryStore>())
                          : null),
                identityModelName: reactiveModel,
                playbookGuidanceProvider: serviceProvider.GetRequiredService<IPlaybookGuidanceProvider>(),
                playbookMemoryId: playbookMemoryId);
        }

        if (autonomousTestingAutoStart)
        {
            builder.Services.AddHostedService(serviceProvider =>
                CreateAutonomousTestWorker(serviceProvider, pilotModel));
        }
        else
        {
            builder.Services.AddSingleton<IAutonomousTestWorkerController>(serviceProvider =>
                new AutonomousTestWorkerController(
                    modelName => CreateAutonomousTestWorker(serviceProvider, modelName)));
        }
        }
        var autonomousLoginOptions = builder.Configuration
        .GetSection("AutonomousLogin")
        .Get<AutonomousLoginOptions>() ?? new AutonomousLoginOptions();
        if (autonomousLoginOptions.Enabled)
        {
        if (!builder.Environment.IsDevelopment())
        {
            throw new InvalidOperationException(
                "Autonomous game login is available only in Development.");
        }
        if (!autonomousTestingEnabled)
        {
            throw new InvalidOperationException(
                "Autonomous game login requires AutonomousTesting to be enabled.");
        }
        ArgumentException.ThrowIfNullOrWhiteSpace(autonomousLoginOptions.AgentId);
        if (!string.IsNullOrWhiteSpace(autonomousLoginOptions.LoginName))
        {
            ValidatePlainTextLoginValue(
                autonomousLoginOptions.LoginName,
                "AutonomousLogin:LoginName",
                maximumLength: 64);
        }
        if (string.IsNullOrWhiteSpace(autonomousLoginOptions.SelectedGame))
        {
            throw new InvalidOperationException(
                "AutonomousLogin:SelectedGame must select one configured per-game profile.");
        }
        if (!string.Equals(
                autonomousLoginOptions.AgentId,
                autonomousTestAgentId,
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "AutonomousLogin:AgentId must match AutonomousTesting:AgentId.");
        }
        var loginListener = proxyListeners.SingleOrDefault(listener =>
            string.Equals(
                listener.AgentId,
                autonomousLoginOptions.AgentId,
                StringComparison.OrdinalIgnoreCase));
        if (loginListener is null)
        {
            throw new InvalidOperationException(
                $"Autonomous login requires a GameProxy listener for agent '{autonomousLoginOptions.AgentId}'.");
        }
        var configuredGames = autonomousLoginOptions.Games
            ?? throw new InvalidOperationException("AutonomousLogin:Games must configure per-game profiles.");
        var selectedProfile = configuredGames.FirstOrDefault(profile =>
            string.Equals(
                profile.Key,
                autonomousLoginOptions.SelectedGame,
                StringComparison.OrdinalIgnoreCase)).Value;
        if (selectedProfile is null)
        {
            throw new InvalidOperationException(
                $"AutonomousLogin:SelectedGame '{autonomousLoginOptions.SelectedGame}' has no configured game profile.");
        }
        if (string.IsNullOrWhiteSpace(selectedProfile.GameLetter) ||
            selectedProfile.GameLetter.Length != 1 ||
            !char.IsAsciiLetter(selectedProfile.GameLetter[0]))
        {
            throw new InvalidOperationException(
                "The selected autonomous login profile must have a single-letter GameLetter.");
        }
        if (!string.IsNullOrWhiteSpace(selectedProfile.TraderAlias))
        {
            ValidatePlainTextLoginValue(
                selectedProfile.TraderAlias,
                $"AutonomousLogin:Games:{autonomousLoginOptions.SelectedGame}:TraderAlias",
                maximumLength: 64);
        }
        if (!string.IsNullOrWhiteSpace(selectedProfile.Password))
        {
            ValidatePlainTextLoginValue(
                selectedProfile.Password,
                $"AutonomousLogin:Games:{autonomousLoginOptions.SelectedGame}:Password",
                maximumLength: 128);
        }

        builder.Services.AddHostedService<AutonomousLoginWorker>(serviceProvider =>
            new AutonomousLoginWorker(
                loginListener,
                autonomousLoginOptions,
                autonomousLoginOptions.SelectedGame,
                selectedProfile,
                pilotModel,
                ollamaEndpoint,
                serviceProvider.GetRequiredService<IAutonomousLoginIdentityProvider>(),
                serviceProvider.GetRequiredService<AutonomousLoginCredentialStore>(),
                serviceProvider.GetRequiredService<ProxyGameCommandBroker>(),
                serviceProvider.GetRequiredService<ILogger<AutonomousLoginWorker>>()));
        }

        static void ValidatePlainTextLoginValue(string value, string settingName, int maximumLength)
        {
        if (string.IsNullOrEmpty(value) ||
            value.Length > maximumLength ||
            value.Any(character => character is < ' ' or > '~'))
        {
            throw new InvalidOperationException(
                $"{settingName} must contain 1 to {maximumLength} printable ASCII characters without control characters.");
        }
        }

        builder.Services.AddSingleton(sp => new StrategicReviewService(
        sp.GetRequiredService<IAgentStore>(),
        sp.GetRequiredService<IKnowledgeStore>(),
        sp.GetRequiredService<IMacroTelemetryStore>(),
        sp.GetRequiredService<IReactiveEventStore>(),
        sp.GetRequiredService<IGoalProposalStore>(),
        sp.GetRequiredService<GoalProposalService>(),
        sp.GetRequiredService<IStrategicReviewStore>(),
        sp.GetRequiredService<IStrategicReasoningProvider>(),
        ollamaEndpoint,
        new ModelConfiguration("Ollama", strategicModel, ollamaEndpoint),
        sp.GetRequiredService<ILlmInteractionHistoryStore>(),
        sp.GetRequiredService<IPlaybookStore>(),
        playbookGameKey,
        playbookMemoryId));
        var knowledgeRoots = builder.Configuration
        .GetSection("Knowledge:Roots")
        .GetChildren()
        .Select(root => new KnowledgeSourceRoot(
            root["Id"] ?? throw new InvalidOperationException("Every configured knowledge root must have an Id."),
            Path.GetFullPath(
                root["Path"] ?? throw new InvalidOperationException("Every configured knowledge root must have a Path."),
                builder.Environment.ContentRootPath)))
        .ToArray();
        builder.Services.AddSingleton(sp => new KnowledgeMiningService(
        sp.GetRequiredService<IKnowledgeStore>(),
        knowledgeRoots));
        builder.Services.AddSingleton<IMacro, PairPortTradeMacro>();
        builder.Services.AddSingleton<IMacro, TradePreflightMacro>();
        builder.Services.AddSingleton<IMacro, ExploreAdjacentSectorsMacro>();
        builder.Services.AddSingleton<IMacro, RefreshCimMacro>();
        builder.Services.AddSingleton<IMacro, FindNearestStardockMacro>();
        builder.Services.AddSingleton<MacroRegistry>();
        builder.Services.AddSingleton<MacroExecutionEngine>();

        configureServices?.Invoke(builder.Services);
        var app = builder.Build();
        var agentStore = app.Services.GetRequiredService<IAgentStore>();
        await agentStore.InitializeAsync();
        await app.Services.GetRequiredService<IAgentTeamStore>().InitializeAsync();
        await app.Services.GetRequiredService<IWorldStateStore>().InitializeAsync();
        await app.Services.GetRequiredService<IMacroTelemetryStore>().InitializeAsync();
        await app.Services.GetRequiredService<IKnowledgeStore>().InitializeAsync();
        var legacyKnowledgeStore = new SqliteKnowledgeStore(connectionString);
        await legacyKnowledgeStore.InitializeAsync();
        await new LegacyKnowledgeSeedMigration(
        legacyKnowledgeStore,
        seedKnowledgeStore,
        runtimeKnowledgeStore).MigrateAsync();
        await app.Services.GetRequiredService<IGoalProposalStore>().InitializeAsync();
        await app.Services.GetRequiredService<IStrategicReviewStore>().InitializeAsync();
        await app.Services.GetRequiredService<IReactiveEventStore>().InitializeAsync();

        var activeAgent = await agentStore.GetAgentAsync(autonomousTestAgentId);
        if (activeAgent is null)
        {
            activeAgent = new AgentContainer
            {
                AgentId = autonomousTestAgentId,
                DisplayName = string.Equals(
                    autonomousTestAgentId,
                    "meridian",
                    StringComparison.OrdinalIgnoreCase)
                        ? "Meridian"
                        : autonomousTestAgentId,
                Description = "Persistent TradeWars player",
                PreferredProvider = "Ollama",
                PreferredModel = "qwen3:4b"
            };
            activeAgent.Journal.Add(new AgentJournalEntry(
                DateTimeOffset.UtcNow,
                "System",
                "Agent identity created."));
            await agentStore.SaveAgentAsync(activeAgent);
        }
        else if (activeAgent.Status != AgentStatus.Offline)
        {
            activeAgent.Status = AgentStatus.Offline;
            await agentStore.SaveAgentAsync(activeAgent);
        }
        if (EpisodeOptionsConfiguration.Read(app.Configuration)?.PilotModel is { } episodePilotModel &&
            !string.Equals(
                activeAgent.Configuration.GetValueOrDefault("Brains:Pilot:Model"),
                episodePilotModel,
                StringComparison.Ordinal))
        {
            activeAgent.PreferredModel = episodePilotModel;
            activeAgent.Configuration["Brains:Pilot:Model"] = episodePilotModel;
            await agentStore.SaveAgentAsync(activeAgent);
        }

        if (!app.Environment.IsDevelopment())
        {
        app.UseExceptionHandler("/Error", createScopeForErrors: true);
        app.UseHsts();
        }
        app.UseStatusCodePagesWithReExecute("/not-found", createScopeForStatusCodePages: true);
        if (builder.Configuration.GetValue("Dashboard:HttpsRedirection", true))
        app.UseHttpsRedirection();
        app.UseAntiforgery();

        string staticAssetsManifest = Path.Combine(
            Path.GetDirectoryName(typeof(DashboardApplication).Assembly.Location)
                ?? throw new InvalidOperationException("Could not locate the dashboard assembly directory."),
            "LLMSurvivingProxyAgent.Dashboard.staticwebassets.endpoints.json");
        if (!File.Exists(staticAssetsManifest))
        {
            throw new FileNotFoundException(
                "The dashboard static-assets manifest was not found.",
                staticAssetsManifest);
        }
        app.MapStaticAssets(staticAssetsManifest);
        app.MapRazorComponents<App>()
        .AddInteractiveServerRenderMode();

        if (gameEventLogPaths.Count > 0)
        {
        app.MapGet(
            "/api/learning/episodes",
            async (HttpContext context, ITrajectoryStore trajectoryStore, string? agentId, int? limit) =>
            {
                if (context.Connection.RemoteIpAddress is not { } remoteAddress ||
                    !System.Net.IPAddress.IsLoopback(remoteAddress))
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }

                return Results.Ok(await trajectoryStore.GetRecentEpisodesAsync(
                    string.IsNullOrWhiteSpace(agentId) ? null : agentId,
                    Math.Clamp(limit ?? 50, 1, 1000),
                    context.RequestAborted));
            });
        app.MapGet(
            "/api/learning/episodes/{episodeId}/steps",
            async (HttpContext context, ITrajectoryStore trajectoryStore, string episodeId) =>
            {
                if (context.Connection.RemoteIpAddress is not { } remoteAddress ||
                    !System.Net.IPAddress.IsLoopback(remoteAddress))
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }

                return Results.Ok(await trajectoryStore.GetStepsAsync(episodeId, context.RequestAborted));
            });
        app.MapGet(
            "/api/autonomous/moves/pending",
            (HttpContext context, IAutonomousMoveApprovalGate approvalGate) =>
            {
                if (context.Connection.RemoteIpAddress is not { } remoteAddress ||
                    !System.Net.IPAddress.IsLoopback(remoteAddress))
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }

                return approvalGate.PendingRequest is { } pending
                    ? Results.Ok(pending)
                    : Results.NoContent();
            });
        app.MapPost(
            "/api/agents/{agentId}/coach",
            async (string agentId, CoachCommandRequest request, HttpContext context, PlaybookCoachingService coaching) =>
            {
                if (context.Connection.RemoteIpAddress is not { } remoteAddress ||
                    !System.Net.IPAddress.IsLoopback(remoteAddress))
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }

                string text = request.Command?.Trim() ?? string.Empty;
                if (!CoachCommandParser.IsCoachCommand(text))
                {
                    text = $"{CoachCommandParser.Prefix} {text}";
                }
                CoachingResult result = await coaching.ApplyCommandAsync(agentId, text, context.RequestAborted);
                return result.Succeeded ? Results.Ok(result) : Results.BadRequest(result);
            });
        app.MapGet(
            "/api/agents/{agentId}/playbook",
            async (string agentId, HttpContext context, IPlaybookStore playbookStore, PlaybookSettings playbookSettings) =>
            {
                if (context.Connection.RemoteIpAddress is not { } remoteAddress ||
                    !System.Net.IPAddress.IsLoopback(remoteAddress))
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }

                string memoryId = playbookSettings.ResolveMemoryId(agentId);
                IReadOnlyList<AgentLifeRecord> lives = await playbookStore.GetLivesAsync(memoryId, 200, context.RequestAborted);
                return Results.Ok(new
                {
                    memoryId,
                    entries = await playbookStore.GetEntriesAsync(memoryId, includeRetired: true, context.RequestAborted),
                    lives,
                    progression = ProgressionBenchmark.Compute(lives)
                });
            });
        app.MapPost(
            "/api/autonomous/moves/{approvalId}/decision",
            (
                string approvalId,
                AutonomousMoveApprovalDecisionRequest request,
                HttpContext context,
                IAutonomousMoveApprovalGate approvalGate) =>
            {
                if (context.Connection.RemoteIpAddress is not { } remoteAddress ||
                    !System.Net.IPAddress.IsLoopback(remoteAddress))
                {
                    return Results.StatusCode(StatusCodes.Status403Forbidden);
                }

                return approvalGate.Resolve(approvalId, request.Approved)
                    ? Results.Ok(new { approved = request.Approved })
                    : Results.Conflict(new { error = "The movement approval is no longer pending." });
            });
        app.MapPost(
            "/api/agents/{agentId}/situation",
            async (
                string agentId,
                GameSituationObservationRequest request,
                GameSituationObservationService service,
                CancellationToken cancellationToken) =>
            {
                if (string.IsNullOrWhiteSpace(request.SessionId) || request.SessionId.Length > 200)
                {
                    return Results.BadRequest(new { error = "SessionId must contain 1 to 200 characters." });
                }

                var result = await service.CaptureAndRecordAsync(
                    agentId,
                    request.SessionId,
                    cancellationToken);
                return result.Recorded ? Results.Ok(result) : Results.Conflict(result);
            });
        }

        return app;
    }
}

public sealed record AutonomousMoveApprovalDecisionRequest(bool Approved);

public sealed record CoachCommandRequest(string? Command);
