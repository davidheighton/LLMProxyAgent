using System.Globalization;
using LLMSurvivingProxyAgent.Core;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace LLMSurvivingProxyAgent.Runtime;

public sealed class AutonomousTestWorker(
    string agentId,
    string modelName,
    Uri modelEndpoint,
    ProxyGameEventHub eventHub,
    IGameSituationProvider situationProvider,
    IAutonomousTestActionProvider actionProvider,
    IGameCommandSender commandSender,
    ILogger<AutonomousTestWorker> logger,
    IAutonomousMoveTargetProvider? moveTargetProvider = null,
    bool allowGameActions = false,
    int maximumActionsPerSession = 1,
    IAutonomousActionKnowledgeProvider? actionKnowledgeProvider = null,
    IAutonomousActionObjectiveProvider? actionObjectiveProvider = null,
    IAutonomousMoveApprovalGate? moveApprovalGate = null,
    AutonomousTestWorkerStatusStore? statusStore = null,
    IAutonomousGameLoginCredentialProvider? loginCredentialProvider = null,
    IAutonomousGameLoginInputSender? loginInputSender = null,
    bool requireMovementApproval = true,
    IAutonomousGameWorldProvider? gameWorldProvider = null,
    IAutonomousGameScriptRunner? gameScriptRunner = null,
    IAutonomousPortTradeOpportunityProvider? portTradeOpportunityProvider = null,
    IAgentStore? agentStore = null,
    AutonomousActionWatchdog? actionWatchdog = null,
    IReactiveEventStore? reactiveEventStore = null,
    IAutonomousFedSpaceFastStartProvider? fedSpaceFastStartProvider = null) : BackgroundService
{
    public const int UnlimitedActionsPerSession = 0;
    private const int KnownHighRiskSector = 99;

    private readonly AutonomousActionWatchdog _actionWatchdog =
        actionWatchdog ?? new AutonomousActionWatchdog();
    private static readonly TimeSpan MaximumCommandPromptAge = TimeSpan.FromSeconds(45);
    private static readonly TimeSpan MaximumPromptRecoveryHeartbeatAge = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaximumSnapshotAge = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan ShipStatusRefreshRetryInterval = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan CimRefreshRetryInterval = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MovementVerificationGracePeriod = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan DockShopperStartupGracePeriod = TimeSpan.FromSeconds(10);
    private string _pilotModelName = modelName;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (maximumActionsPerSession < UnlimitedActionsPerSession)
        {
            throw new InvalidOperationException(
                "Maximum autonomous actions per session must be zero (unlimited) or positive.");
        }

        if (agentStore is not null &&
            await agentStore.GetAgentAsync(agentId, stoppingToken).ConfigureAwait(false) is { } agent &&
            agent.Configuration.TryGetValue("Brains:Pilot:Model", out string? configuredPilotModel) &&
            !string.IsNullOrWhiteSpace(configuredPilotModel))
        {
            _pilotModelName = configuredPilotModel.Trim();
        }
        statusStore?.SetModelConfiguration(
            agentId,
            actionProvider.Id,
            _pilotModelName,
            _pilotModelName.StartsWith("qwen3", StringComparison.OrdinalIgnoreCase)
                ? "Thinking enabled (think=true; response traces retained)"
                : "Provider default (think not specified)");

        var promptsHandled = 0;
        DateTimeOffset? lastCommandPromptAt = null;
        DateTimeOffset? lastObservedPromptAt = null;
        DateTimeOffset? pendingCommandPromptAt = null;
        DateTimeOffset? lastLoginPromptAt = null;
        string? lastLoginPromptSurface = null;
        bool portTradeRecoveryAttempted = false;
        var connectionInitialized = false;
        var sessionState = new AutonomousTestSessionState();
        AutonomousGameLoginCredentials? loginCredentials =
            loginCredentialProvider?.GetCredentials(agentId);
        if (loginCredentials is not null && loginInputSender is null)
        {
            throw new InvalidOperationException(
                "Autonomous game login credentials are configured without a terminal input sender.");
        }

        AutonomousExistingAccountLoginSession? loginSession = CreateLoginSession(loginCredentials);
        var loginFailed = false;
        var unclassifiedLoginPromptLogged = false;
        var stalePromptRecoveryPending = false;
        await foreach (var sessionEvent in eventHub.ReadAllAsync(stoppingToken))
        {
            if (!string.Equals(sessionEvent.AgentId, agentId, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (sessionEvent.Event.Kind == "Connected")
            {
                _actionWatchdog.Reset(agentId);
                if (connectionInitialized)
                {
                    promptsHandled = 0;
                    lastCommandPromptAt = null;
                    lastObservedPromptAt = null;
                    pendingCommandPromptAt = null;
                    lastLoginPromptAt = null;
                    lastLoginPromptSurface = null;
                    portTradeRecoveryAttempted = false;
                    sessionState.Reset();
                    loginSession = CreateLoginSession(loginCredentials);
                    loginFailed = false;
                    unclassifiedLoginPromptLogged = false;
                }

                connectionInitialized = true;
                portTradeRecoveryAttempted = false;
                statusStore?.MarkConnected(agentId);
                actionObjectiveProvider?.RequestStrategicReview(
                    agentId,
                    "A new or reconnected gameplay session requires strategic review.");
                continue;
            }
            if (sessionEvent.Event.Kind == "Disconnected")
            {
                _actionWatchdog.Reset(agentId);
                promptsHandled = 0;
                lastCommandPromptAt = null;
                lastObservedPromptAt = null;
                pendingCommandPromptAt = null;
                lastLoginPromptAt = null;
                lastLoginPromptSurface = null;
                portTradeRecoveryAttempted = false;
                sessionState.Reset();
                loginSession = CreateLoginSession(loginCredentials);
                loginFailed = false;
                unclassifiedLoginPromptLogged = false;
                connectionInitialized = false;
                statusStore?.MarkDisconnected(agentId);
                actionObjectiveProvider?.RequestStrategicReview(
                    agentId,
                    "The gameplay session disconnected; review synchronization after reconnection.");
                continue;
            }
            if (sessionEvent.Event.Kind == "PresenceHeartbeat")
            {
                statusStore?.MarkHeartbeat(agentId);
                TimeSpan heartbeatAge = DateTimeOffset.UtcNow - sessionEvent.Event.Timestamp;
                DateTimeOffset? heartbeatPromptAt = GetHeartbeatPromptObservedAt(sessionEvent.Event);
                bool heartbeatConfirmsCurrentPrompt =
                    IsHeartbeatCurrentPromptConfirmed(sessionEvent.Event);
                TimeSpan heartbeatPromptAge = heartbeatPromptAt is { } observedPromptAt
                    ? DateTimeOffset.UtcNow - observedPromptAt
                    : TimeSpan.MaxValue;
                bool hasFreshHeartbeatPrompt =
                    !string.IsNullOrWhiteSpace(sessionEvent.Event.PromptSurface) &&
                    heartbeatAge >= TimeSpan.Zero &&
                    heartbeatAge <= MaximumPromptRecoveryHeartbeatAge &&
                    heartbeatPromptAt is { } &&
                    heartbeatPromptAt <= DateTimeOffset.UtcNow &&
                    heartbeatPromptAge >= TimeSpan.Zero &&
                    (heartbeatPromptAge <= MaximumCommandPromptAge ||
                     heartbeatConfirmsCurrentPrompt);
                bool isUnprocessedHeartbeatPrompt =
                    lastObservedPromptAt is null ||
                    heartbeatPromptAt > lastObservedPromptAt;
                bool confirmedCommandPromptNeedsStatusRefresh =
                    heartbeatConfirmsCurrentPrompt &&
                    statusStore?.GetStatus(agentId).Situation is { } lastSituation &&
                    lastSituation.Presence == GamePresenceState.Connected &&
                    lastSituation.Authentication == GameAuthenticationState.Authenticated &&
                    string.Equals(lastSituation.CurrentPrompt, "Command", StringComparison.OrdinalIgnoreCase) &&
                    NeedsShipStatusRefresh(lastSituation);
                if (pendingCommandPromptAt is null &&
                    heartbeatPromptAt is { } heartbeatPromptObservedAt &&
                    hasFreshHeartbeatPrompt &&
                    (stalePromptRecoveryPending ||
                     isUnprocessedHeartbeatPrompt ||
                     confirmedCommandPromptNeedsStatusRefresh))
                {
                    ProxySessionEvent refreshedPrompt = sessionEvent with
                    {
                        Event = sessionEvent.Event with
                        {
                            Kind = "ServerPrompt",
                            Timestamp = heartbeatConfirmsCurrentPrompt
                                ? sessionEvent.Event.Timestamp
                                : heartbeatPromptObservedAt
                        }
                    };
                    if (eventHub.TryPublish(refreshedPrompt))
                    {
                        stalePromptRecoveryPending = false;
                        lastObservedPromptAt = heartbeatPromptObservedAt;
                        statusStore?.SetActivity(agentId, "Recovered a fresh prompt from the live session heartbeat");
                        logger.LogInformation(
                            "Recovered an unprocessed fresh prompt from the live session heartbeat for agent {AgentId}.",
                            agentId);
                    }
                    else
                    {
                        statusStore?.SetActivity(agentId, "Could not re-queue the fresh prompt from the live session heartbeat");
                        logger.LogWarning(
                            "Could not re-queue the fresh prompt from the live session heartbeat for agent {AgentId}.",
                            agentId);
                    }
                }

                if (pendingCommandPromptAt is { } pendingPromptAt)
                {
                    if (DateTimeOffset.UtcNow - pendingPromptAt > MaximumCommandPromptAge)
                    {
                        pendingCommandPromptAt = null;
                    }
                    else if (HasRemainingActionBudget(promptsHandled))
                    {
                        try
                        {
                            AutonomousTestActionResult actionResult =
                                await RunTestActionAsync(
                                    agentId,
                                    sessionState,
                                    promptsHandled,
                                    stoppingToken)
                                    .ConfigureAwait(false);
                            if (!ShouldRetryOnPresenceHeartbeat(actionResult, sessionState))
                            {
                                if (actionResult == AutonomousTestActionResult.Handled)
                                {
                                    promptsHandled++;
                                }
                                pendingCommandPromptAt = null;
                            }
                            else
                            {
                                pendingCommandPromptAt = sessionEvent.Event.Timestamp;
                            }
                        }
                        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                        {
                            throw;
                        }
                        catch (Exception exception)
                        {
                            promptsHandled++;
                            pendingCommandPromptAt = null;
                            statusStore?.SetActivity(agentId, FormatActionFailure(exception));
                            logger.LogError(
                                exception,
                                "Autonomous action failed for agent {AgentId}; no action was sent for this prompt.",
                                agentId);
                        }
                    }
                }
                continue;
            }
            if (sessionEvent.Event.Kind == "SynchronizedShipStatus")
            {
                continue;
            }
            if (sessionEvent.Event.Kind != "ServerPrompt")
            {
                continue;
            }

            if (DateTimeOffset.UtcNow - sessionEvent.Event.Timestamp > MaximumCommandPromptAge)
            {
                stalePromptRecoveryPending = true;
                statusStore?.SetActivity(agentId, "Ignored a stale queued game prompt");
                logger.LogDebug(
                    "Ignored stale queued game prompt for agent {AgentId}; event age exceeded {MaximumAgeSeconds} seconds.",
                    agentId,
                    MaximumCommandPromptAge.TotalSeconds);
                continue;
            }

            lastObservedPromptAt = sessionEvent.Event.Timestamp;
            stalePromptRecoveryPending = false;
            string promptSurface = sessionEvent.Event.PromptSurface ?? string.Empty;
            bool hasClassifiedPromptSurface = TradeWarsTelnetTelemetryDecoder.TryGetPromptSurface(
                promptSurface,
                out string classifiedSurface);
            if (hasClassifiedPromptSurface)
            {
                promptSurface = classifiedSurface;
            }
            string observedPromptSurface = hasClassifiedPromptSurface
                ? promptSurface
                : $"unclassified (raw text omitted; length={promptSurface.Length})";

            if (loginFailed)
            {
                continue;
            }

            if (loginSession is { IsAuthenticated: false })
            {
                if (string.Equals(
                        lastLoginPromptSurface,
                        promptSurface,
                        StringComparison.OrdinalIgnoreCase) &&
                    lastLoginPromptAt is { } previousPromptAt &&
                    sessionEvent.Event.Timestamp >= previousPromptAt &&
                    sessionEvent.Event.Timestamp - previousPromptAt <= TimeSpan.FromSeconds(1))
                {
                    continue;
                }

                lastLoginPromptSurface = promptSurface;
                lastLoginPromptAt = sessionEvent.Event.Timestamp;
                try
                {
                    AutonomousExistingAccountLoginResult loginResult = await loginSession.HandlePromptAsync(
                        promptSurface,
                        (input, token) => loginInputSender!.SendLoginInputAsync(agentId, input, token),
                        stoppingToken).ConfigureAwait(false);
                    if (loginResult == AutonomousExistingAccountLoginResult.DeclinedAccountCreation)
                    {
                        loginFailed = true;
                        statusStore?.SetActivity(
                            agentId,
                            "Login blocked: existing character not recognized; declined new-character creation");
                        logger.LogWarning(
                            "Autonomous login for agent {AgentId} stopped after safely declining new-character creation.",
                            agentId);
                        continue;
                    }
                    if (loginResult != AutonomousExistingAccountLoginResult.Authenticated)
                    {
                        if (loginResult == AutonomousExistingAccountLoginResult.Ignored &&
                            !hasClassifiedPromptSurface &&
                            !unclassifiedLoginPromptLogged)
                        {
                            logger.LogWarning(
                                "Autonomous login for agent {AgentId} ignored an unclassified terminal surface (textLength={TextLength}); no input was sent and raw text was omitted.",
                                agentId,
                                promptSurface.Length);
                            unclassifiedLoginPromptLogged = true;
                        }

                        statusStore?.SetActivity(
                            agentId,
                            loginResult == AutonomousExistingAccountLoginResult.InputSent
                                ? $"Submitted {promptSurface} input from the saved login profile"
                                : $"Waiting for a recognized login prompt; observed surface: " +
                                  observedPromptSurface);
                        continue;
                    }

                    statusStore?.SetActivity(agentId, "Login authenticated; checking live session");
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    loginFailed = true;
                    promptsHandled++;
                    statusStore?.SetActivity(agentId, $"Login blocked: {exception.Message}");
                    logger.LogError(
                        exception,
                        "Autonomous login stopped for agent {AgentId}; no account-creation or unsupported input was sent.",
                        agentId);
                    continue;
                }
            }

            if (string.Equals(promptSurface, "PortTradeQuantity", StringComparison.OrdinalIgnoreCase))
            {
                if (sessionState.PendingPortTrade is not null ||
                    portTradeRecoveryAttempted ||
                    lastCommandPromptAt is { } handledCommandAt &&
                    sessionEvent.Event.Timestamp <= handledCommandAt)
                {
                    continue;
                }

                portTradeRecoveryAttempted = true;
                if (commandSender is not IAutonomousPortTradePromptRecovery portTradeRecovery)
                {
                    statusStore?.SetActivity(
                        agentId,
                        "Untracked port trade prompt requires safe recovery support");
                    logger.LogWarning(
                        "Agent {AgentId} is at an untracked port trade prompt, but its command sender does not support safe recovery.",
                        agentId);
                    continue;
                }

                try
                {
                    TradeProduct? followUpBuyProduct = null;
                    if (portTradeOpportunityProvider is not null)
                    {
                        GameSituationReport recoverySituation =
                            await situationProvider.CaptureSituationAsync(agentId, stoppingToken)
                                .ConfigureAwait(false);
                        if (recoverySituation.Snapshot is { } recoverySnapshot &&
                            GameSnapshotValidation.IsValid(recoverySnapshot) &&
                            recoverySnapshot.CurrentSector is { } recoverySector)
                        {
                            AutonomousPortTradeOpportunity? recoveryOpportunity =
                                await portTradeOpportunityProvider.GetOpportunityAsync(
                                    recoverySector,
                                    sessionState.PreviousSector,
                                    nextSector: null,
                                    recoverySnapshot,
                                    stoppingToken,
                                    sessionState.PortTradeAttemptedSectors).ConfigureAwait(false);
                            if (recoveryOpportunity is not null &&
                                (recoveryOpportunity.TradeSector is null ||
                                 recoveryOpportunity.TradeSector == recoverySector))
                            {
                                followUpBuyProduct = recoveryOpportunity.FollowUpBuyProduct ??
                                    (recoveryOpportunity.SellingExistingCargo
                                        ? null
                                        : recoveryOpportunity.Product);
                            }
                        }
                    }

                    await portTradeRecovery.RecoverUntrackedPortTradeAsync(
                        agentId,
                        sessionEvent.Event.Timestamp,
                        followUpBuyProduct,
                        stoppingToken).ConfigureAwait(false);
                    statusStore?.SetActivity(
                        agentId,
                        "Skipped an untracked port trade and returned to the Command prompt");
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    statusStore?.SetActivity(
                        agentId,
                        $"Untracked port trade recovery failed: {exception.Message}");
                    logger.LogError(
                        exception,
                        "Could not safely recover an untracked port trade for agent {AgentId}.",
                        agentId);
                }

                continue;
            }

            if (string.Equals(promptSurface, "AutopilotConfirmation", StringComparison.OrdinalIgnoreCase))
            {
                GameSituationReport autopilotPromptSituation =
                    await situationProvider.CaptureSituationAsync(agentId, stoppingToken)
                        .ConfigureAwait(false);
                AutonomousTestActionResult autopilotResult =
                    await HandleAutopilotConfirmationPromptAsync(
                        agentId,
                        sessionState,
                        autopilotPromptSituation,
                        promptsHandled,
                        stoppingToken).ConfigureAwait(false);
                if (autopilotResult == AutonomousTestActionResult.Handled)
                {
                    promptsHandled++;
                }
                else if (autopilotResult == AutonomousTestActionResult.WaitingForFreshPrompt)
                {
                    pendingCommandPromptAt = sessionEvent.Event.Timestamp;
                }

                continue;
            }

            if (string.Equals(promptSurface, "NavPointSelection", StringComparison.OrdinalIgnoreCase))
            {
                if (!allowGameActions)
                {
                    statusStore?.SetActivity(
                        agentId,
                        "NavPoint selection requires game-action approval");
                    continue;
                }

                if (!HasRemainingActionBudget(promptsHandled))
                {
                    statusStore?.SetActivity(agentId, "Autonomous action limit reached");
                    continue;
                }

                try
                {
                    await commandSender.SendAsync(
                        agentId,
                        new GameCommandRequest(GameCommand.ExitNavPointSelection),
                        stoppingToken).ConfigureAwait(false);
                    promptsHandled++;
                    statusStore?.SetActivity(
                        agentId,
                        "Exited NavPoint selection without changing course");
                    logger.LogInformation(
                        "Exited the NavPoint selection prompt without setting a course for agent {AgentId}.",
                        agentId);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    statusStore?.SetActivity(
                        agentId,
                        $"NavPoint selection exit failed: {exception.Message}");
                    logger.LogError(
                        exception,
                        "Could not safely exit the NavPoint selection prompt for agent {AgentId}.",
                        agentId);
                }

                continue;
            }

            if (string.Equals(promptSurface, "StopInSector", StringComparison.OrdinalIgnoreCase))
            {
                GameSituationReport stopPromptSituation =
                    await situationProvider.CaptureSituationAsync(agentId, stoppingToken)
                        .ConfigureAwait(false);
                if (!allowGameActions)
                {
                    statusStore?.SetActivity(
                        agentId,
                        "Stop-in-sector response requires game-action approval",
                        stopPromptSituation);
                    continue;
                }

                if (!HasRemainingActionBudget(promptsHandled))
                {
                    statusStore?.SetActivity(
                        agentId,
                        "Autonomous action limit reached",
                        stopPromptSituation);
                    continue;
                }

                if (!IsStopInSectorPromptEligible(stopPromptSituation))
                {
                    statusStore?.SetActivity(
                        agentId,
                        "Waiting for a fresh authenticated Stop-in-sector prompt",
                        stopPromptSituation);
                    continue;
                }

                DateTimeOffset stopPromptObservedAt = stopPromptSituation.CurrentPromptObservedAt!.Value;
                if (sessionState.LastHandledStopPromptAt == stopPromptObservedAt)
                {
                    continue;
                }

                try
                {
                    await commandSender.SendAsync(
                        agentId,
                        new GameCommandRequest(GameCommand.StopAtCurrentSector),
                        stoppingToken).ConfigureAwait(false);
                    sessionState.LastHandledStopPromptAt = stopPromptObservedAt;
                    promptsHandled++;
                    statusStore?.RecordDecision(
                        agentId,
                        "Stopped at current sector",
                        GameCommand.StopAtCurrentSector.ToString(),
                        "Stop after this one-hop movement and recheck local port-trade opportunities.",
                        stopPromptSituation);
                    logger.LogInformation(
                        "Stopped the TWX autopilot at the current sector for agent {AgentId}; trade opportunities will be rechecked before another move.",
                        agentId);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    statusStore?.SetActivity(
                        agentId,
                        $"Stop-in-sector response failed: {exception.Message}",
                        stopPromptSituation);
                    logger.LogError(
                        exception,
                        "Could not stop the TWX autopilot at the current sector for agent {AgentId}.",
                        agentId);
                }
                continue;
            }

            if (!string.Equals(promptSurface, "Command", StringComparison.OrdinalIgnoreCase))
            {
                if (string.Equals(promptSurface, "Citadel", StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(promptSurface, "Planet", StringComparison.OrdinalIgnoreCase))
                {
                    sessionState.FedSpaceFastStartSkipped = true;
                }
                continue;
            }

            portTradeRecoveryAttempted = false;
            if (lastCommandPromptAt == sessionEvent.Event.Timestamp ||
                !HasRemainingActionBudget(promptsHandled))
            {
                continue;
            }

            lastCommandPromptAt = sessionEvent.Event.Timestamp;
            statusStore?.SetActivity(agentId, "Checking authenticated Command prompt");
            try
            {
                AutonomousTestActionResult actionResult =
                    await RunTestActionAsync(
                        agentId,
                        sessionState,
                        promptsHandled,
                        stoppingToken).ConfigureAwait(false);
                if (actionResult == AutonomousTestActionResult.Handled)
                {
                    promptsHandled++;
                }
                if (ShouldRetryOnPresenceHeartbeat(actionResult, sessionState))
                {
                    pendingCommandPromptAt = sessionEvent.Event.Timestamp;
                }
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                promptsHandled++;
                actionObjectiveProvider?.RequestStrategicReview(
                    agentId,
                    $"A pilot action failed and requires strategic review: {exception.Message}");
                statusStore?.SetActivity(agentId, FormatActionFailure(exception));
                logger.LogError(
                    exception,
                    "Autonomous action failed for agent {AgentId}; no action was sent for this prompt.",
                    agentId);
            }
        }
    }

    private static DateTimeOffset? GetHeartbeatPromptObservedAt(ProxyGameEvent gameEvent) =>
        gameEvent.Metadata is { } metadata &&
        metadata.TryGetValue("promptObservedAt", out string? value) &&
        DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.RoundtripKind,
            out DateTimeOffset observedAt)
            ? observedAt
            : null;

    private static bool IsHeartbeatCurrentPromptConfirmed(ProxyGameEvent gameEvent) =>
        gameEvent.Metadata is { } metadata &&
        metadata.TryGetValue("promptIsCurrent", out string? value) &&
        bool.TryParse(value, out bool isCurrent) &&
        isCurrent;

    private static AutonomousExistingAccountLoginSession? CreateLoginSession(
        AutonomousGameLoginCredentials? credentials) =>
        credentials is null ? null : new AutonomousExistingAccountLoginSession(credentials);

    private static string FormatActionFailure(Exception exception) =>
        string.IsNullOrWhiteSpace(exception.Message)
            ? $"Action failed ({exception.GetType().Name})"
            : $"Action failed ({exception.GetType().Name}): {exception.Message}";

    private static bool IsPortTradeVerified(PendingPortTrade pendingTrade, GameSnapshot result)
    {
        long? startingCredits = pendingTrade.StartingSnapshot.SynchronizedCredits;
        long? resultingCredits = result.SynchronizedCredits;
        int? startingCargo = GetCargoQuantity(
            pendingTrade.StartingSnapshot,
            pendingTrade.Opportunity.Product);
        int? resultingCargo = GetCargoQuantity(result, pendingTrade.Opportunity.Product);
        if (startingCredits is not { } creditsBefore ||
            resultingCredits is not { } creditsAfter ||
            startingCargo is not { } cargoBefore ||
            resultingCargo is not { } cargoAfter)
        {
            return false;
        }

        return pendingTrade.Opportunity.SellingExistingCargo
            ? pendingTrade.Opportunity.FollowUpBuyProduct is { } followUpProduct
                ? cargoAfter < cargoBefore &&
                  GetCargoQuantity(pendingTrade.StartingSnapshot, followUpProduct) is { } followUpBefore &&
                  GetCargoQuantity(result, followUpProduct) is { } followUpAfter &&
                  followUpAfter > followUpBefore
                : cargoAfter < cargoBefore && creditsAfter > creditsBefore
            : cargoAfter > cargoBefore && creditsAfter < creditsBefore;
    }

    private static int? GetCargoQuantity(GameSnapshot snapshot, TradeProduct product) =>
        product switch
        {
            TradeProduct.FuelOre => snapshot.FuelOre,
            TradeProduct.Organics => snapshot.Organics,
            TradeProduct.Equipment => snapshot.Equipment,
            _ => null
        };

    private static bool HasCarriedCargo(GameSnapshot snapshot) =>
        snapshot.FuelOre is > 0 ||
        snapshot.Organics is > 0 ||
        snapshot.Equipment is > 0;

    private async Task<AutonomousTestActionResult> HandleAutopilotConfirmationPromptAsync(
        string currentAgentId,
        AutonomousTestSessionState sessionState,
        GameSituationReport situation,
        int promptsHandled,
        CancellationToken cancellationToken)
    {
        if (!allowGameActions)
        {
            statusStore?.SetActivity(
                currentAgentId,
                "Autopilot declined: game actions are not approved",
                situation);
            return AutonomousTestActionResult.PromptIgnored;
        }
        if (!HasRemainingActionBudget(promptsHandled))
        {
            statusStore?.SetActivity(
                currentAgentId,
                "Autopilot declined: autonomous action limit reached",
                situation);
            return AutonomousTestActionResult.PromptIgnored;
        }

        if (!IsAutopilotConfirmationPromptEligible(situation))
        {
            statusStore?.SetActivity(
                currentAgentId,
                "Autopilot declined: waiting for a fresh authenticated confirmation prompt",
                situation);
            return AutonomousTestActionResult.WaitingForFreshPrompt;
        }

        DateTimeOffset autopilotPromptObservedAt = situation.CurrentPromptObservedAt!.Value;
        if (sessionState.LastHandledAutopilotPromptAt == autopilotPromptObservedAt)
        {
            return AutonomousTestActionResult.PromptIgnored;
        }

        try
        {
            await commandSender.SendAsync(
                currentAgentId,
                new GameCommandRequest(GameCommand.DeclineAutopilotEngagement),
                cancellationToken).ConfigureAwait(false);
            sessionState.LastHandledAutopilotPromptAt = autopilotPromptObservedAt;
            statusStore?.RecordDecision(
                currentAgentId,
                "Autopilot engagement declined",
                GameCommand.DeclineAutopilotEngagement.ToString(),
                "Declined the multi-hop autopilot prompt; Meridian will resume only from a verified Command prompt and consider one adjacent sector at a time.",
                situation);
            logger.LogInformation(
                "Declined autopilot engagement for agent {AgentId} to preserve verified one-sector movement.",
                currentAgentId);
            return AutonomousTestActionResult.Handled;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            statusStore?.SetActivity(
                currentAgentId,
                $"Autopilot decline failed: {exception.Message}",
                situation);
            logger.LogError(
                exception,
                "Could not decline autopilot engagement for agent {AgentId}.",
                currentAgentId);
            return AutonomousTestActionResult.WaitingForFreshPrompt;
        }
    }

    private async Task<AutonomousTestActionDecision> DecideWithTelemetryAsync(
        string currentAgentId,
        IAutonomousTestActionProvider provider,
        AutonomousTestActionContext actionContext,
        CancellationToken cancellationToken)
    {
        actionContext = actionContext with { AgentId = currentAgentId };
        DateTimeOffset startedAt = DateTimeOffset.UtcNow;
        statusStore?.MarkModelRequestStarted(currentAgentId, startedAt);
        try
        {
            AutonomousTestActionDecision decision = await provider.DecideAsync(
                new ModelConfiguration(provider.Id, _pilotModelName, modelEndpoint),
                actionContext,
                cancellationToken).ConfigureAwait(false);
            statusStore?.RecordModelRequestCompleted(
                currentAgentId,
                startedAt,
                DateTimeOffset.UtcNow,
                succeeded: true,
                cancelled: false,
                error: null);
            return decision;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            statusStore?.RecordModelRequestCompleted(
                currentAgentId,
                startedAt,
                DateTimeOffset.UtcNow,
                succeeded: false,
                cancelled: true,
                error: null);
            throw;
        }
        catch (Exception exception)
        {
            statusStore?.RecordModelRequestCompleted(
                currentAgentId,
                startedAt,
                DateTimeOffset.UtcNow,
                succeeded: false,
                cancelled: false,
                error: exception.Message);
            throw;
        }
    }

    private async Task<AutonomousTestActionResult> RunTestActionAsync(
        string currentAgentId,
        AutonomousTestSessionState sessionState,
        int promptsHandled,
        CancellationToken cancellationToken)
    {
        var situation = await situationProvider.CaptureSituationAsync(currentAgentId, cancellationToken)
            .ConfigureAwait(false);
        if (statusStore is not null)
        {
            AutonomousActionWatchdogObservation watchdogObservation = _actionWatchdog.Observe(
                currentAgentId,
                situation,
                statusStore.GetStatus(currentAgentId).LastAction,
                DateTimeOffset.UtcNow);
            statusStore.SetWatchdogStatus(currentAgentId, watchdogObservation);
            if (watchdogObservation.RecoveryReason is { } recoveryReason)
            {
                actionObjectiveProvider?.RequestStrategicReview(currentAgentId, recoveryReason);
                statusStore.SetActivity(
                    currentAgentId,
                    $"Action watchdog requested strategic recovery after {watchdogObservation.UnchangedCycles} unchanged cycles",
                    situation);
                logger.LogWarning(
                    "Action watchdog requested a strategic recovery review for agent {AgentId} after {UnchangedCycles} unchanged Command cycles.",
                    currentAgentId,
                    watchdogObservation.UnchangedCycles);
            }
        }
        if (situation.Synchronization != GameSynchronizationState.Healthy)
        {
            actionObjectiveProvider?.RequestStrategicReview(
                currentAgentId,
                "Synchronization is not healthy; review the learning objective after recovery.");
        }
        if (sessionState.PendingRefurbVerification is { } pendingRefurb)
        {
            if (situation.Presence != GamePresenceState.Connected ||
                situation.Authentication != GameAuthenticationState.Authenticated ||
                situation.Synchronization != GameSynchronizationState.Healthy ||
                situation.Snapshot is not { } refurbSnapshot ||
                !GameSnapshotValidation.IsValid(refurbSnapshot) ||
                refurbSnapshot.ObservedAt <= pendingRefurb.SentAt)
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "Waiting for fresh ship status to verify the Refurb purchase",
                    situation);
                return AutonomousTestActionResult.StatusRefreshPending;
            }

            bool componentIncreased = pendingRefurb.Upgrade switch
            {
                AutonomousRefurbUpgrade.CargoHolds =>
                    refurbSnapshot.HoldsTotal is { } currentHolds &&
                    pendingRefurb.StartingSnapshot.HoldsTotal is { } priorHolds &&
                    currentHolds > priorHolds,
                AutonomousRefurbUpgrade.Fighters =>
                    refurbSnapshot.Ship is { } currentShip &&
                    pendingRefurb.StartingSnapshot.Ship is { } priorShip &&
                    currentShip.Fighters > priorShip.Fighters,
                AutonomousRefurbUpgrade.Shields =>
                    refurbSnapshot.Shields is { } currentShields &&
                    pendingRefurb.StartingSnapshot.Shields is { } priorShields &&
                    currentShields > priorShields,
                _ => false
            };
            bool creditsDecreased =
                refurbSnapshot.SynchronizedCredits is { } currentCredits &&
                pendingRefurb.StartingSnapshot.SynchronizedCredits is { } priorCredits &&
                currentCredits < priorCredits;
            sessionState.PendingRefurbVerification = null;
            statusStore?.RecordDecision(
                currentAgentId,
                componentIncreased && creditsDecreased
                    ? $"Verified {pendingRefurb.Upgrade} Refurb purchase"
                    : $"Could not verify {pendingRefurb.Upgrade} Refurb purchase",
                "Refurb purchase verification",
                componentIncreased && creditsDecreased
                    ? $"Fresh ship status confirms {pendingRefurb.Upgrade} increased and credits decreased."
                    : $"Fresh ship status did not confirm both an increase to {pendingRefurb.Upgrade} and a credit decrease; no purchase is recorded as successful.",
                situation);
            return AutonomousTestActionResult.Handled;
        }

        if (situation.ScriptInputPrompt is not null)
        {
            return await HandleScriptInputPromptAsync(
                currentAgentId,
                sessionState,
                situation,
                cancellationToken).ConfigureAwait(false);
        }
        if (sessionState.StarDockShopperStage != StarDockShopperStage.Inactive)
        {
            return await HandleStarDockShopperStageAsync(
                currentAgentId,
                sessionState,
                situation,
                cancellationToken).ConfigureAwait(false);
        }
        if (sessionState.RefurbMenuStage != AutonomousRefurbMenuStage.Inactive)
        {
            return await HandleRefurbMenuStageAsync(
                currentAgentId,
                sessionState,
                situation,
                cancellationToken).ConfigureAwait(false);
        }

        if (string.Equals(
                situation.CurrentPrompt,
                "AutopilotConfirmation",
                StringComparison.OrdinalIgnoreCase))
        {
            return await HandleAutopilotConfirmationPromptAsync(
                currentAgentId,
                sessionState,
                situation,
                promptsHandled,
                cancellationToken).ConfigureAwait(false);
        }

        statusStore?.SetActivity(currentAgentId, "Checking live session", situation);
        if (sessionState.WorldTradeModeStarted &&
            gameScriptRunner is not null &&
            !IsCommandPromptEligible(situation))
        {
            if (await gameScriptRunner.IsWorldTradeModeRunningAsync(
                    currentAgentId,
                    cancellationToken).ConfigureAwait(false))
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "TWX World Trade mode is active; waiting for its next Command prompt",
                    situation);
                return AutonomousTestActionResult.ScriptModeRunning;
            }

            sessionState.WorldTradeModeStarted = false;
            sessionState.LastWorldTradeAttemptSector = null;
            sessionState.ActiveScriptObjective = null;
            statusStore?.SetActivity(
                currentAgentId,
                "TWX World Trade mode stopped outside a Command prompt; waiting for the authenticated prompt",
                situation);
        }
        if (!IsCommandPromptEligible(situation))
        {
            statusStore?.SetActivity(
                currentAgentId,
                "Waiting for a fresh authenticated Command prompt",
                situation);
            logger.LogInformation(
                "Skipping autonomous smoke-test action for agent {AgentId}: session is not connected, authenticated, and at the Command prompt.",
                currentAgentId);
            return AutonomousTestActionResult.WaitingForFreshPrompt;
        }

        if (sessionState.WorldTradeModeStarted && gameScriptRunner is not null)
        {
            if (await gameScriptRunner.IsWorldTradeModeRunningAsync(
                    currentAgentId,
                    cancellationToken).ConfigureAwait(false))
            {
                if (allowGameActions &&
                    situation.Synchronization == GameSynchronizationState.Healthy &&
                    situation.Snapshot is { } worldTradeSnapshot &&
                    GameSnapshotValidation.IsValid(worldTradeSnapshot) &&
                    worldTradeSnapshot.SynchronizedCredits is
                        >= AutonomousTradePolicy.MinimumWorldTradeUpgradeCredits &&
                    worldTradeSnapshot.ObservedAt <= DateTimeOffset.UtcNow &&
                    DateTimeOffset.UtcNow - worldTradeSnapshot.ObservedAt <= MaximumSnapshotAge)
                {
                    var stopContext = new AutonomousTestActionContext(situation, [])
                    {
                        GameActionsEnabled = true,
                        ActiveObjective = new AutonomousActionObjective(
                            "Stop World Trade once the verified refurb funding threshold is met.",
                            "The fixed script must return the game to an authenticated Command prompt before it can be stopped."),
                        RecentReactiveSignals = await GetRecentReactiveSignalsAsync(
                            currentAgentId,
                            cancellationToken).ConfigureAwait(false),
                        WorldTradeModeRunning = true,
                        WorldTradeStopReady = true
                    };
                    AutonomousTestActionDecision stopDecision = await DecideWithTelemetryAsync(
                        currentAgentId,
                        actionProvider,
                        stopContext,
                        cancellationToken).ConfigureAwait(false);
                    if (stopDecision.Action == AutonomousTestAction.StopWorldTradeMode)
                    {
                        GameSituationReport stopVerification = await situationProvider.CaptureSituationAsync(
                            currentAgentId,
                            cancellationToken).ConfigureAwait(false);
                        bool fundingStillReady =
                            IsCommandPromptEligible(stopVerification) &&
                            stopVerification.Synchronization == GameSynchronizationState.Healthy &&
                            stopVerification.Snapshot is { } stopSnapshot &&
                            GameSnapshotValidation.IsValid(stopSnapshot) &&
                            stopSnapshot.SynchronizedCredits is
                                >= AutonomousTradePolicy.MinimumWorldTradeUpgradeCredits &&
                            stopSnapshot.ObservedAt <= DateTimeOffset.UtcNow &&
                            DateTimeOffset.UtcNow - stopSnapshot.ObservedAt <= MaximumSnapshotAge;
                        if (!fundingStillReady ||
                            !await gameScriptRunner.IsWorldTradeModeRunningAsync(
                                currentAgentId,
                                cancellationToken).ConfigureAwait(false))
                        {
                            statusStore?.SetActivity(
                                currentAgentId,
                                "World Trade stop deferred because the funding, prompt, or script state changed",
                                stopVerification);
                            return AutonomousTestActionResult.ScriptModeRunning;
                        }

                        await gameScriptRunner.StopWorldTradeModeAsync(
                            currentAgentId,
                            cancellationToken).ConfigureAwait(false);
                        sessionState.WorldTradeModeStarted = false;
                        sessionState.ActiveScriptObjective = null;
                        sessionState.LastWorldTradeAttemptSector = null;
                        sessionState.RefurbUpgradesAttemptedAtCurrentSector.Clear();
                        statusStore?.RecordDecision(
                            currentAgentId,
                            "TWX World Trade mode stopped at the funding target",
                            stopDecision.Action.ToString(),
                            stopDecision.Rationale,
                            stopVerification);
                        return AutonomousTestActionResult.Handled;
                    }
                }

                statusStore?.SetActivity(
                    currentAgentId,
                    "TWX World Trade mode is scouting and trading",
                    situation);
                return AutonomousTestActionResult.ScriptModeRunning;
            }

            sessionState.WorldTradeModeStarted = false;
            sessionState.ActiveScriptObjective = null;
            sessionState.LastWorldTradeAttemptSector = null;
            statusStore?.SetActivity(
                currentAgentId,
                "TWX World Trade mode ended; resuming autonomous decisions",
                situation);
        }

        if (sessionState.PendingMovementTarget is { } pendingTarget)
        {
            if (situation.Synchronization == GameSynchronizationState.Healthy &&
                situation.Snapshot is { } snapshot &&
                GameSnapshotValidation.IsValid(snapshot) &&
                sessionState.PendingMovementSentAt is { } sentAt &&
                snapshot.ObservedAt > sentAt)
            {
                if (snapshot.CurrentSector != pendingTarget)
                {
                    int? requestedOrigin = sessionState.PendingMovementOrigin;
                    if (snapshot.CurrentSector == requestedOrigin &&
                        DateTimeOffset.UtcNow - sentAt <= MovementVerificationGracePeriod)
                    {
                        statusStore?.SetActivity(
                            currentAgentId,
                            $"Waiting for movement from sector {requestedOrigin} to sector {pendingTarget} to settle",
                            situation);
                        return AutonomousTestActionResult.StatusRefreshPending;
                    }

                    sessionState.MovementDisabled = true;
                    sessionState.ClearPendingMovement();
                    statusStore?.RecordDecision(
                        currentAgentId,
                        "Movement verification failed",
                        GameCommand.MoveToSector.ToString(),
                        $"Requested an adjacent move from sector {requestedOrigin} to {pendingTarget}, but the fresh snapshot shows sector {snapshot.CurrentSector}. Further movement is paused; local game actions remain available.",
                        situation);
                    logger.LogWarning(
                        "Autonomous movement for agent {AgentId} from sector {OriginSector} to {TargetSector} was not verified; the latest snapshot shows sector {ObservedSector}. Further movement is disabled for this session.",
                        currentAgentId,
                        requestedOrigin,
                        pendingTarget,
                        snapshot.CurrentSector);
                    return AutonomousTestActionResult.Handled;
                }

                sessionState.PreviousSector = sessionState.PendingMovementOrigin;
                sessionState.ClearPendingMovement();
                if (moveTargetProvider?.RequiresCurrentSectorDisplayBeforeMovement == true)
                {
                    sessionState.DisplayedSectors.Remove(pendingTarget);
                }
            }
            else
            {
                if (sessionState.PendingShipStatusRefreshSentAt is { } pendingRefreshAt &&
                    DateTimeOffset.UtcNow - pendingRefreshAt < ShipStatusRefreshRetryInterval)
                {
                    statusStore?.SetActivity(
                        currentAgentId,
                        "Waiting for a fresh ship-status response",
                        situation);
                    return AutonomousTestActionResult.StatusRefreshPending;
                }

                await commandSender.SendAsync(
                    currentAgentId,
                    new GameCommandRequest(GameCommand.RequestShipInformation),
                    cancellationToken).ConfigureAwait(false);
                logger.LogInformation(
                    "Requested a fresh ship snapshot for agent {AgentId} before considering another movement.",
                    currentAgentId);
                sessionState.PendingShipStatusRefreshSentAt = DateTimeOffset.UtcNow;
                return AutonomousTestActionResult.StatusRefreshSent;
            }
        }

        if (sessionState.PendingScannerVerification is { } pendingScannerVerification)
        {
            if (situation.Synchronization == GameSynchronizationState.Healthy &&
                situation.Snapshot is { } scannerResultSnapshot &&
                GameSnapshotValidation.IsValid(scannerResultSnapshot) &&
                scannerResultSnapshot.ObservedAt > pendingScannerVerification.SentAt)
            {
                long? creditsSpent = null;
                if (pendingScannerVerification.StartingSnapshot.SynchronizedCredits is { } startingCredits &&
                    scannerResultSnapshot.SynchronizedCredits is { } remainingCredits &&
                    remainingCredits < startingCredits)
                {
                    creditsSpent = startingCredits - remainingCredits;
                }

                if (scannerResultSnapshot.CurrentSector == pendingScannerVerification.Sector &&
                    IsScannerInstalled(scannerResultSnapshot, pendingScannerVerification.Target) &&
                    scannerResultSnapshot.SynchronizedCredits is { } verifiedCredits)
                {
                    string scannerName = GetScannerDisplayName(pendingScannerVerification.Target);
                    statusStore?.RecordDecision(
                        currentAgentId,
                        creditsSpent is > 0
                            ? $"{scannerName} ownership and spend verified"
                            : $"{scannerName} ownership and balance verified",
                        "Dock Shopper scanner verification",
                        creditsSpent is > 0
                            ? $"Verified {scannerName} ownership, a {creditsSpent:N0}-credit reduction, and a remaining balance of {verifiedCredits:N0}."
                            : $"Verified {scannerName} ownership and a remaining balance of {verifiedCredits:N0}; the price could not be established from the observed credit change.",
                        situation);
                    logger.LogInformation(
                        "Verified {ScannerName} ownership for agent {AgentId} at sector {Sector}; observed credit change {CreditChange}, remaining credits {Credits}.",
                        scannerName,
                        currentAgentId,
                        scannerResultSnapshot.CurrentSector,
                        creditsSpent,
                        verifiedCredits);
                }
                else
                {
                    statusStore?.RecordDecision(
                        currentAgentId,
                        "Dock Shopper result not verified",
                        "Dock Shopper scanner verification",
                        $"The fresh ship status did not confirm {GetScannerDisplayName(pendingScannerVerification.Target)} ownership; scanner-dependent play remains disabled.",
                        situation);
                    logger.LogWarning(
                        "The Dock Shopper {ScannerName} result for agent {AgentId} was not verified from fresh ship status.",
                        GetScannerDisplayName(pendingScannerVerification.Target),
                        currentAgentId);
                }

                sessionState.PendingScannerVerification = null;
                sessionState.PendingHoloScannerStatusRefreshSentAt = null;
            }
            else
            {
                if (sessionState.PendingHoloScannerStatusRefreshSentAt is { } scannerRefreshAt &&
                    DateTimeOffset.UtcNow - scannerRefreshAt < ShipStatusRefreshRetryInterval)
                {
                    statusStore?.SetActivity(
                        currentAgentId,
                        "Waiting for fresh ship status to verify the Dock Shopper result",
                        situation);
                    return AutonomousTestActionResult.StatusRefreshPending;
                }

                await commandSender.SendAsync(
                    currentAgentId,
                    new GameCommandRequest(GameCommand.RequestShipInformation),
                    cancellationToken).ConfigureAwait(false);
                sessionState.PendingHoloScannerStatusRefreshSentAt = DateTimeOffset.UtcNow;
                statusStore?.SetActivity(
                    currentAgentId,
                    "Refreshing ship status to verify the Dock Shopper result",
                    situation);
                return AutonomousTestActionResult.StatusRefreshSent;
            }
        }

        if (sessionState.PendingPortTrade is { } pendingTrade)
        {
            if (situation.Synchronization == GameSynchronizationState.Healthy &&
                situation.Snapshot is { } tradeResultSnapshot &&
                GameSnapshotValidation.IsValid(tradeResultSnapshot) &&
                tradeResultSnapshot.ObservedAt > pendingTrade.SentAt)
            {
                sessionState.PortTradeAttemptedSectors.Add(pendingTrade.Sector);
                bool tradeVerified =
                    tradeResultSnapshot.CurrentSector == pendingTrade.Sector &&
                    IsPortTradeVerified(pendingTrade, tradeResultSnapshot);
                if (tradeVerified)
                {
                    sessionState.PendingTradePartnerSector = pendingTrade.Opportunity.SellingExistingCargo
                        ? pendingTrade.Opportunity.FollowUpPartnerSector
                        : pendingTrade.Opportunity.PartnerSector;
                    statusStore?.RecordDecision(
                        currentAgentId,
                        "Port trade verified",
                        GameCommand.TradeAtCurrentPort.ToString(),
                        $"Verified {pendingTrade.Opportunity.Product} cargo and credits changed as expected.",
                        situation);
                    logger.LogInformation(
                        "Verified port trade for agent {AgentId} at sector {Sector}: {Product} cargo and credits changed as expected.",
                        currentAgentId,
                        pendingTrade.Sector,
                        pendingTrade.Opportunity.Product);
                }
                else
                {
                    sessionState.PendingTradePartnerSector = null;
                    statusStore?.RecordDecision(
                        currentAgentId,
                        "Port trade not verified",
                        GameCommand.TradeAtCurrentPort.ToString(),
                        $"The fresh ship snapshot did not confirm the expected {pendingTrade.Opportunity.Product} and credit changes; another port will be considered.",
                        situation);
                    logger.LogWarning(
                        "Port trade for agent {AgentId} at sector {Sector} was not verified from the fresh ship snapshot; the sector will not be retried this session.",
                        currentAgentId,
                        pendingTrade.Sector);
                }

                sessionState.PendingPortTrade = null;
                sessionState.PendingShipStatusRefreshSentAt = null;
            }
            else
            {
                if (sessionState.PendingShipStatusRefreshSentAt is { } pendingTradeRefreshAt &&
                    DateTimeOffset.UtcNow - pendingTradeRefreshAt < ShipStatusRefreshRetryInterval)
                {
                    statusStore?.SetActivity(
                        currentAgentId,
                        "Waiting for a fresh ship-status response to verify the port trade",
                        situation);
                    return AutonomousTestActionResult.StatusRefreshPending;
                }

                await commandSender.SendAsync(
                    currentAgentId,
                    new GameCommandRequest(GameCommand.RequestShipInformation),
                    cancellationToken).ConfigureAwait(false);
                sessionState.PendingShipStatusRefreshSentAt = DateTimeOffset.UtcNow;
                statusStore?.RecordDecision(
                    currentAgentId,
                    "Trade verification refresh sent",
                    AutonomousTestAction.RequestShipInformation.ToString(),
                    "A fresh ship snapshot is required to confirm the port trade before routing or trading again.",
                    situation);
                logger.LogInformation(
                    "Requested a fresh ship snapshot for agent {AgentId} to verify the port trade at sector {Sector}.",
                    currentAgentId,
                    pendingTrade.Sector);
                return AutonomousTestActionResult.StatusRefreshSent;
            }
        }

        if (NeedsShipStatusRefresh(situation))
        {
            if (sessionState.PendingShipStatusRefreshSentAt is { } refreshSentAt &&
                DateTimeOffset.UtcNow - refreshSentAt < ShipStatusRefreshRetryInterval)
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "Waiting for a fresh ship-status response",
                    situation);
                return AutonomousTestActionResult.StatusRefreshPending;
            }

            await commandSender.SendAsync(
                currentAgentId,
                new GameCommandRequest(GameCommand.RequestShipInformation),
                cancellationToken).ConfigureAwait(false);
            sessionState.PendingShipStatusRefreshSentAt = DateTimeOffset.UtcNow;
            statusStore?.RecordDecision(
                currentAgentId,
                "Status refresh sent",
                AutonomousTestAction.RequestShipInformation.ToString(),
                "The verified ship snapshot is unavailable or stale; refreshing status before reasoning about gameplay.",
                situation);
            logger.LogInformation(
                "Requested a fresh ship snapshot for agent {AgentId} before invoking the gameplay model.",
                currentAgentId);
            return AutonomousTestActionResult.StatusRefreshSent;
        }

        sessionState.PendingShipStatusRefreshSentAt = null;
        ArgumentException.ThrowIfNullOrWhiteSpace(_pilotModelName);

        int? starDockSector = gameWorldProvider is not null
            ? await gameWorldProvider.GetStarDockSectorAsync(cancellationToken).ConfigureAwait(false)
            : null;
        int? currentSector = situation.Snapshot?.CurrentSector;
        if (sessionState.FedSpaceFastStartPhase == FedSpaceFastStartPhase.NotStarted)
        {
            sessionState.FedSpaceFastStartPhase =
                allowGameActions &&
                !sessionState.FedSpaceFastStartSkipped &&
                currentSector is >= 1 and <= 10
                    ? FedSpaceFastStartPhase.Sweeping
                    : FedSpaceFastStartPhase.Skipped;
        }
        if (starDockSector is not null)
        {
            sessionState.LastStarDockDiscoverySentAt = null;
        }
        else if (allowGameActions &&
                 gameWorldProvider is not null &&
                 currentSector is > 0 &&
                 sessionState.FedSpaceFastStartPhase is
                     FedSpaceFastStartPhase.Complete or FedSpaceFastStartPhase.Skipped)
        {
            if (sessionState.LastStarDockDiscoverySentAt is { } discoverySentAt &&
                DateTimeOffset.UtcNow - discoverySentAt < CimRefreshRetryInterval)
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "Waiting for the StarDock location lookup response",
                    situation);
                return AutonomousTestActionResult.StatusRefreshPending;
            }

            await commandSender.SendAsync(
                currentAgentId,
                new GameCommandRequest(GameCommand.FindStarDockSector),
                cancellationToken).ConfigureAwait(false);
            sessionState.LastStarDockDiscoverySentAt = DateTimeOffset.UtcNow;
            statusStore?.RecordDecision(
                currentAgentId,
                "StarDock location lookup sent",
                GameCommand.FindStarDockSector.ToString(),
                "StarDock is not in the local database; query its location at the Command prompt before selecting a route.",
                situation);
            return AutonomousTestActionResult.StatusRefreshSent;
        }
        bool? currentSectorIsFederationSpace =
            currentSector is > 0 && gameWorldProvider is not null
                ? await gameWorldProvider.IsFederationSpaceAsync(
                    currentSector.Value,
                    cancellationToken).ConfigureAwait(false)
                : null;
        if (sessionState.LastStarDockShopperAttemptSector != currentSector)
        {
            sessionState.LastStarDockShopperAttemptSector = null;
        }
        if (sessionState.LastStarDockFighterDeploymentAttemptSector != currentSector)
        {
            sessionState.LastStarDockFighterDeploymentAttemptSector = null;
        }

        if (situation.Snapshot is
            {
                DensityScannerInstalled: true,
                HoloScannerInstalled: false
            } densityScannerSnapshot)
        {
            sessionState.DensityScannerStartingSnapshot ??= densityScannerSnapshot;
        }

        ScannerPurchaseTarget? scannerTarget = GetScannerPurchaseTarget(
            situation.Snapshot,
            sessionState.DensityScannerStartingSnapshot);
        bool needsFuelOreForDockShopper =
            allowGameActions &&
            NeedsFuelOreForDockShopper(situation.Snapshot, starDockSector, scannerTarget);
        bool preserveFuelOreForDockShopper =
            allowGameActions &&
            starDockSector is > 0 &&
            situation.Snapshot is
            {
                SynchronizedCredits: >= AutonomousTradePolicy.MinimumScannerUpgradeCredits,
                HoldsEmpty: 0,
                HoldsTotal: > 0
            } fuelReadySnapshot &&
            scannerTarget is not null &&
            fuelReadySnapshot.FuelOre == fuelReadySnapshot.HoldsTotal &&
            fuelReadySnapshot.Organics == 0 &&
            fuelReadySnapshot.Equipment == 0;
        bool starDockHasOwnedFighter = false;
        if (starDockSector is > 0 &&
            currentSector == starDockSector &&
            scannerTarget is not null &&
            gameWorldProvider is not null)
        {
            starDockHasOwnedFighter = await gameWorldProvider.HasFreshOwnedFighterInSectorAsync(
                starDockSector.Value,
                cancellationToken).ConfigureAwait(false);
        }
        bool starDockFighterRequirementSatisfied =
            starDockHasOwnedFighter ||
            currentSector == starDockSector && currentSectorIsFederationSpace is true;
        bool starDockFighterDeploymentAvailable =
            allowGameActions &&
            IsCommandPromptEligible(situation) &&
            starDockSector is > 0 &&
            currentSector == starDockSector &&
            currentSectorIsFederationSpace is false &&
            situation.Synchronization == GameSynchronizationState.Healthy &&
            situation.Snapshot is { } fighterDeploymentSnapshot &&
            GameSnapshotValidation.IsValid(fighterDeploymentSnapshot) &&
            IsScannerTargetAbsent(fighterDeploymentSnapshot, scannerTarget) &&
            fighterDeploymentSnapshot.SynchronizedCredits is >=
                AutonomousTradePolicy.MinimumScannerUpgradeCredits &&
            fighterDeploymentSnapshot.HoldsEmpty == 0 &&
            fighterDeploymentSnapshot.HoldsTotal is > 0 &&
            fighterDeploymentSnapshot.FuelOre == fighterDeploymentSnapshot.HoldsTotal &&
            fighterDeploymentSnapshot.Organics == 0 &&
            fighterDeploymentSnapshot.Equipment == 0 &&
            fighterDeploymentSnapshot.Ship is { Fighters: > 1 } &&
            !starDockHasOwnedFighter &&
            sessionState.LastStarDockFighterDeploymentAttemptSector != currentSector &&
            sessionState.LastStarDockShopperAttemptSector != currentSector &&
            sessionState.StarDockShopperStage == StarDockShopperStage.Inactive;
        bool starDockShopperReady =
            allowGameActions &&
            situation.Synchronization == GameSynchronizationState.Healthy &&
            situation.Snapshot is { } readySnapshot &&
            GameSnapshotValidation.IsValid(readySnapshot) &&
            readySnapshot.CurrentSector == starDockSector &&
            IsScannerTargetAbsent(readySnapshot, scannerTarget) &&
            readySnapshot.SynchronizedCredits is >= AutonomousTradePolicy.MinimumScannerUpgradeCredits &&
            readySnapshot.HoldsEmpty == 0 &&
            readySnapshot.HoldsTotal is > 0 &&
            readySnapshot.FuelOre == readySnapshot.HoldsTotal &&
            readySnapshot.Organics == 0 &&
            readySnapshot.Equipment == 0 &&
            starDockFighterRequirementSatisfied &&
            sessionState.LastStarDockShopperAttemptSector != currentSector;

        if (situation.Snapshot?.HoloScannerInstalled is true)
        {
            sessionState.LastHoloScannerStatusRefreshSentAt = null;
        }
        else if (allowGameActions &&
                 starDockSector is > 0 &&
                 currentSector == starDockSector &&
                 situation.Synchronization == GameSynchronizationState.Healthy &&
                 situation.Snapshot is { } scannerSnapshot &&
                 GameSnapshotValidation.IsValid(scannerSnapshot))
        {
            if (!AreScannerStatusesKnown(scannerSnapshot))
            {
                if (sessionState.LastHoloScannerStatusRefreshSentAt is not { } scannerRefreshAt ||
                    DateTimeOffset.UtcNow - scannerRefreshAt >= ShipStatusRefreshRetryInterval)
                {
                    await commandSender.SendAsync(
                        currentAgentId,
                        new GameCommandRequest(GameCommand.RequestShipInformation),
                        cancellationToken).ConfigureAwait(false);
                    sessionState.LastHoloScannerStatusRefreshSentAt = DateTimeOffset.UtcNow;
                    statusStore?.SetActivity(
                        currentAgentId,
                        "Refreshing ship status to verify Holo Scanner ownership",
                        situation);
                    return AutonomousTestActionResult.StatusRefreshSent;
                }

                statusStore?.SetActivity(
                    currentAgentId,
                    "Waiting for ship status to verify Holo Scanner ownership",
                    situation);
                return AutonomousTestActionResult.StatusRefreshPending;
            }

            sessionState.LastHoloScannerStatusRefreshSentAt = null;
        }

        IReadOnlyList<int> freshAdjacentSectors =
            moveTargetProvider is not null
            ? await moveTargetProvider.GetFreshAdjacentSectorIdsAsync(
                situation.Snapshot?.CurrentSector,
                cancellationToken).ConfigureAwait(false)
            : Array.Empty<int>();
        if (allowGameActions &&
            (freshAdjacentSectors.Count == 0 ||
             moveTargetProvider?.RequiresCurrentSectorDisplayBeforeMovement == true) &&
            situation.Snapshot?.CurrentSector is { } scanSector &&
            !sessionState.DisplayedSectors.Contains(scanSector))
        {
            await commandSender.SendAsync(
                currentAgentId,
                new GameCommandRequest(GameCommand.DisplayCurrentSector),
                cancellationToken).ConfigureAwait(false);
            sessionState.DisplayedSectors.Add(scanSector);
            statusStore?.RecordDecision(
                currentAgentId,
                "Current sector displayed",
                GameCommand.DisplayCurrentSector.ToString(),
                "Refreshing local sector and warp information before selecting a route.",
                situation);
            return AutonomousTestActionResult.Handled;
        }

        int? fedSpaceSweepTarget = null;
        if (sessionState.FedSpaceFastStartPhase == FedSpaceFastStartPhase.Sweeping)
        {
            if (currentSector is not (>= 1 and <= 10) || fedSpaceFastStartProvider is null)
            {
                sessionState.FedSpaceFastStartPhase = FedSpaceFastStartPhase.Skipped;
            }
            else
            {
                sessionState.FedSpaceVisitedSectors.Add(currentSector.Value);
                if (sessionState.FedSpaceVisitedSectors.Count == 10)
                {
                    sessionState.FedSpaceFastStartPhase = FedSpaceFastStartPhase.RefreshingCim;
                }
                else
                {
                    int? nextFedSpaceSector =
                        await fedSpaceFastStartProvider.GetNextFedSpaceSweepSectorAsync(
                            currentSector.Value,
                            sessionState.FedSpaceVisitedSectors,
                            cancellationToken).ConfigureAwait(false);
                    if (nextFedSpaceSector is not > 0 ||
                        !freshAdjacentSectors.Contains(nextFedSpaceSector.Value))
                    {
                        statusStore?.SetActivity(
                            currentAgentId,
                            "Waiting for a fresh in-FedSpace route to finish the sectors 1-10 sweep",
                            situation);
                        return AutonomousTestActionResult.StatusRefreshPending;
                    }

                    fedSpaceSweepTarget = nextFedSpaceSector;
                }
            }
        }

        if (sessionState.FedSpaceFastStartPhase == FedSpaceFastStartPhase.RefreshingCim)
        {
            if (gameScriptRunner is not IAutonomousGameCimRefresher cimRefresher ||
                fedSpaceFastStartProvider is null)
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "FedSpace sweep complete; waiting for the configured port-and-warp CIM refresh integration",
                    situation);
                return AutonomousTestActionResult.StatusRefreshPending;
            }

            DateTimeOffset refreshNow = DateTimeOffset.UtcNow;
            if (sessionState.LastFedSpaceCimRefreshAttemptAt is { } lastRefreshAt &&
                refreshNow - lastRefreshAt < CimRefreshRetryInterval)
            {
                bool refreshed = fedSpaceFastStartProvider is IAutonomousPortCimFreshnessProvider freshness &&
                    freshness.IsPortCimFresh();
                if (!refreshed)
                {
                    statusStore?.SetActivity(
                        currentAgentId,
                        "Waiting for fresh port and warp CIM data after the FedSpace sweep",
                        situation);
                    return AutonomousTestActionResult.StatusRefreshPending;
                }
            }
            else
            {
                sessionState.LastFedSpaceCimRefreshAttemptAt = refreshNow;
                try
                {
                    await cimRefresher.RefreshCimAsync(currentAgentId, cancellationToken)
                        .ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    statusStore?.SetActivity(
                        currentAgentId,
                        $"FedSpace CIM refresh failed: {exception.Message}",
                        situation);
                    logger.LogError(
                        exception,
                        "Could not refresh port and warp CIM after the FedSpace sweep for agent {AgentId}.",
                        currentAgentId);
                    return AutonomousTestActionResult.StatusRefreshPending;
                }

                if (fedSpaceFastStartProvider is not IAutonomousPortCimFreshnessProvider refreshedCim ||
                    !refreshedCim.IsPortCimFresh())
                {
                    statusStore?.SetActivity(
                        currentAgentId,
                        "FedSpace CIM refresh returned without verified fresh port CIM data",
                        situation);
                    return AutonomousTestActionResult.StatusRefreshPending;
                }

                sessionState.FedSpaceFastStartPhase = FedSpaceFastStartPhase.TradingPairs;
                sessionState.LastFedSpaceCimRefreshAttemptAt = null;
                statusStore?.RecordDecision(
                    currentAgentId,
                    "FedSpace survey complete; port and warp CIM refreshed",
                    "RefreshCim",
                    "Sectors 1-10 were visited before refreshing CIM; compatible port-pair planning is now enabled for sectors 2-10.",
                    situation);
            }

            if (sessionState.FedSpaceFastStartPhase == FedSpaceFastStartPhase.RefreshingCim &&
                fedSpaceFastStartProvider is IAutonomousPortCimFreshnessProvider currentCim &&
                currentCim.IsPortCimFresh())
            {
                sessionState.FedSpaceFastStartPhase = FedSpaceFastStartPhase.TradingPairs;
                sessionState.LastFedSpaceCimRefreshAttemptAt = null;
            }
        }

        IReadOnlyList<AdjacentSectorKnowledge> adjacentSectorKnowledge =
            freshAdjacentSectors.Count > 0 && moveTargetProvider is not null
                ? await moveTargetProvider.GetSectorKnowledgeAsync(
                    freshAdjacentSectors,
                    cancellationToken).ConfigureAwait(false)
                : Array.Empty<AdjacentSectorKnowledge>();
        var gameActionsEnabled = allowGameActions;
        var movementEnabled = gameActionsEnabled && !sessionState.MovementDisabled;
        IReadOnlyList<int> availableAdjacentSectors = movementEnabled
            ? freshAdjacentSectors
            : Array.Empty<int>();
        if (sessionState.LastWorldTradeAttemptSector != situation.Snapshot?.CurrentSector)
        {
            sessionState.LastWorldTradeAttemptSector = null;
        }
        var activeObjective =
            actionObjectiveProvider is not null &&
            situation.Synchronization == GameSynchronizationState.Healthy &&
            situation.Snapshot is { } validSnapshot &&
            GameSnapshotValidation.IsValid(validSnapshot)
                ? await actionObjectiveProvider.GetOrCreateActiveObjectiveAsync(
                    currentAgentId,
                    situation,
                    cancellationToken).ConfigureAwait(false)
                : null;
        bool scannerAcquisitionRouteRequired =
            gameActionsEnabled &&
            scannerTarget is not null &&
            situation.Snapshot is { } scannerAcquisitionSnapshot &&
            IsScannerTargetAbsent(scannerAcquisitionSnapshot, scannerTarget) &&
            starDockSector is > 0 &&
            currentSector is > 0 &&
            currentSector != starDockSector &&
            sessionState.FedSpaceFastStartPhase is not (
                FedSpaceFastStartPhase.Sweeping or
                FedSpaceFastStartPhase.RefreshingCim or
                FedSpaceFastStartPhase.TradingPairs);
        if (scannerAcquisitionRouteRequired)
        {
            activeObjective = new AutonomousActionObjective(
                $"Reach StarDock sector {starDockSector} to acquire a scanner before World Trade.",
                "No Density or Holo Scanner is verified. Follow the known shortest-hop route and trade only at freshly verified compatible ports.");
        }
        if (movementEnabled &&
            activeObjective is not null &&
            ObjectiveTargetsStarDock(activeObjective) &&
            situation.Snapshot?.CurrentSector is { } routeOriginSector &&
            starDockSector is { } dockSector &&
            routeOriginSector != dockSector &&
            gameWorldProvider is not null)
        {
            int? nextSector = await gameWorldProvider.GetNextSectorTowardStarDockAsync(
                routeOriginSector,
                cancellationToken).ConfigureAwait(false);
            availableAdjacentSectors = nextSector is > 0 &&
                freshAdjacentSectors.Contains(nextSector.Value)
                ? [nextSector.Value]
                : [];
        }
        if (sessionState.PreviousSector is { } previousSector &&
            availableAdjacentSectors.Count > 1)
        {
            var nonBacktrackingSectors = availableAdjacentSectors
                .Where(sectorId => sectorId != previousSector)
                .ToArray();
            if (nonBacktrackingSectors.Length > 0)
            {
                availableAdjacentSectors = nonBacktrackingSectors;
            }
        }
        if (preserveFuelOreForDockShopper)
        {
            if (currentSector == starDockSector)
            {
                activeObjective = new AutonomousActionObjective(
                    $"Run the Dock Shopper at StarDock sector {starDockSector}.",
                    "The full Fuel Ore holds are in place; preserve them and use the approved scanner script.")
                {
                    PlannedScripts =
                    [
                        new AutonomousScriptPlan(
                            AutonomousScriptId.StarDockShopper,
                            "Purchase the verified missing Holo Scanner at StarDock.")
                    ]
                };
                availableAdjacentSectors = Array.Empty<int>();
            }
            else
            {
                activeObjective = new AutonomousActionObjective(
                    $"Carry full Fuel Ore holds to StarDock sector {starDockSector}.",
                    "Preserve the Dock Shopper prerequisite cargo until reaching StarDock.")
                {
                    PlannedScripts =
                    [
                        new AutonomousScriptPlan(
                            AutonomousScriptId.StarDockShopper,
                            "Use the approved scanner-upgrade script after reaching StarDock.")
                    ]
                };
                if (movementEnabled &&
                    currentSector is { } fuelRouteOrigin &&
                    gameWorldProvider is not null)
                {
                    int? nextFuelRouteSector = await gameWorldProvider.GetNextSectorTowardStarDockAsync(
                        fuelRouteOrigin,
                        cancellationToken).ConfigureAwait(false);
                    availableAdjacentSectors = nextFuelRouteSector is > 0 &&
                        freshAdjacentSectors.Contains(nextFuelRouteSector.Value)
                        ? [nextFuelRouteSector.Value]
                        : Array.Empty<int>();
                }
            }
        }
        int? currentRefurbPortClass =
            gameWorldProvider is not null && currentSector is { } refurbSector
                ? await gameWorldProvider.GetCurrentRefurbPortClassAsync(
                    refurbSector,
                    cancellationToken).ConfigureAwait(false)
                : null;
        if (gameActionsEnabled &&
            situation.Snapshot is { HoloScannerInstalled: true } postScannerSnapshot)
        {
            long credits = postScannerSnapshot.SynchronizedCredits ?? 0;
            if (credits < AutonomousTradePolicy.MinimumWorldTradeUpgradeCredits)
            {
                activeObjective = new AutonomousActionObjective(
                    $"Earn at least {AutonomousTradePolicy.MinimumWorldTradeUpgradeCredits:N0} credits with World Trade at StarDock.",
                    "Build a verified refurb budget, then travel to StarDock and start the fixed World Trade routine.")
                {
                    PlannedScripts =
                    [
                        new AutonomousScriptPlan(
                            AutonomousScriptId.WorldTrade,
                            "Earn the verified refurb budget with the approved World Trade script.")
                    ]
                };
                if (currentSector != starDockSector &&
                    currentSector is { } earningOrigin &&
                    starDockSector is > 0 &&
                    gameWorldProvider is not null)
                {
                    int? nextEarningSector = await gameWorldProvider.GetNextSectorTowardStarDockAsync(
                        earningOrigin,
                        cancellationToken).ConfigureAwait(false);
                    availableAdjacentSectors = nextEarningSector is > 0 &&
                        freshAdjacentSectors.Contains(nextEarningSector.Value)
                        ? [nextEarningSector.Value]
                        : Array.Empty<int>();
                }
                else if (currentSector != starDockSector)
                {
                    availableAdjacentSectors = Array.Empty<int>();
                }
                else if (currentSector == starDockSector)
                {
                    availableAdjacentSectors = Array.Empty<int>();
                }
            }
            else
            {
                activeObjective = new AutonomousActionObjective(
                    "Upgrade Cargo Holds, Fighters, and Shields at the nearest verified Class 0 port or StarDock.",
                    $"The current verified funding is {credits:N0} credits; preserve at least {AutonomousTradePolicy.RefurbCreditReserve:N0} credits after each upgrade.");
                if (currentRefurbPortClass is 0 or 9)
                {
                    availableAdjacentSectors = Array.Empty<int>();
                }
                else if (currentSector is { } refurbOrigin &&
                         gameWorldProvider is not null)
                {
                    int? nextRefurbSector = await gameWorldProvider.GetNextSectorTowardNearestRefurbPortAsync(
                        refurbOrigin,
                        cancellationToken).ConfigureAwait(false);
                    availableAdjacentSectors = nextRefurbSector is > 0 &&
                        freshAdjacentSectors.Contains(nextRefurbSector.Value)
                        ? [nextRefurbSector.Value]
                        : Array.Empty<int>();
                }
                else
                {
                    availableAdjacentSectors = Array.Empty<int>();
                }
            }
        }
        else if (gameActionsEnabled &&
                 situation.Snapshot is
                 {
                     DensityScannerInstalled: true,
                     HoloScannerInstalled: false
                 } preHoloSnapshot &&
                 !IsHoloScannerPurchaseReady(
                     preHoloSnapshot,
                     sessionState.DensityScannerStartingSnapshot))
        {
            long credits = preHoloSnapshot.SynchronizedCredits ?? 0;
            if (credits < AutonomousTradePolicy.MinimumWorldTradeUpgradeCredits)
            {
                activeObjective = new AutonomousActionObjective(
                    $"Earn at least {AutonomousTradePolicy.MinimumWorldTradeUpgradeCredits:N0} credits with compatible port-pair trading before the Holo Scanner upgrade.",
                    "Keep using fresh, profitable port pairs; prioritize the visible Density Scanner path and improve the ship before purchasing the Holo Scanner.");
            }
            else
            {
                activeObjective = new AutonomousActionObjective(
                    "Improve Fighters and Shields at the nearest verified Class 0 port or StarDock before buying the Holo Scanner.",
                    $"The Density Scanner is verified and the current funding is {credits:N0} credits. Upgrade both Fighters and Shields from live refurb quotes while preserving at least {AutonomousTradePolicy.RefurbCreditReserve:N0} credits; buy Cargo Holds only when a live quote exposes a verified price.");
                if (currentRefurbPortClass is 0 or 9)
                {
                    availableAdjacentSectors = Array.Empty<int>();
                }
                else if (currentSector is { } refurbOrigin &&
                         gameWorldProvider is not null)
                {
                    int? nextRefurbSector = await gameWorldProvider.GetNextSectorTowardNearestRefurbPortAsync(
                        refurbOrigin,
                        cancellationToken).ConfigureAwait(false);
                    availableAdjacentSectors = nextRefurbSector is > 0 &&
                        freshAdjacentSectors.Contains(nextRefurbSector.Value)
                        ? [nextRefurbSector.Value]
                        : Array.Empty<int>();
                }
                else
                {
                    availableAdjacentSectors = Array.Empty<int>();
                }
            }
        }
        if (sessionState.RefurbAttemptedSector != currentSector)
        {
            sessionState.RefurbUpgradesAttemptedAtCurrentSector.Clear();
            sessionState.RefurbAttemptedSector = currentSector;
        }
        if (sessionState.FedSpaceFastStartPhase == FedSpaceFastStartPhase.Sweeping)
        {
            activeObjective = new AutonomousActionObjective(
                "Visit each FedSpace sector from 1 through 10 before leaving FedSpace.",
                "The first-login FedSpace survey is bounded to sectors 1-10 and uses one verified adjacent move at a time.");
        }
        else if (sessionState.FedSpaceFastStartPhase == FedSpaceFastStartPhase.TradingPairs)
        {
            activeObjective = new AutonomousActionObjective(
                "Trade compatible port pairs in FedSpace sectors 2 through 10 after refreshing CIM.",
                "Use only known compatible pairs whose endpoints are within three directed hops, and verify every trade before continuing.");
        }
        else if (sessionState.FedSpaceFastStartPhase is
                     FedSpaceFastStartPhase.Complete or FedSpaceFastStartPhase.Skipped &&
                 scannerTarget == ScannerPurchaseTarget.Density &&
                 starDockSector is > 0)
        {
            bool atStarDock = currentSector == starDockSector;
            activeObjective = atStarDock
                ? new AutonomousActionObjective(
                    $"Acquire the Density Scanner at StarDock sector {starDockSector} before World Trade.",
                    "No scanner is verified. Run the approved Dock Shopper when ready; if funding is insufficient, use only a verified compatible port-pair trade whose next hop is in sectors 1-10, StarDock, or already explored. Never enter sector 99 or an unexplored sector without a Density or Holo Scanner.")
                : new AutonomousActionObjective(
                    $"Reach StarDock sector {starDockSector} to buy a Density Scanner before leaving safe early-game play.",
                    "The FedSpace survey/trading phase is inactive. Follow the known shortest-hop route and trade only at freshly verified compatible ports.");
            if (movementEnabled &&
                currentSector is { } fastStartOrigin &&
                fastStartOrigin != starDockSector &&
                gameWorldProvider is not null)
            {
                int? nextToDock = await gameWorldProvider.GetNextSectorTowardStarDockAsync(
                    fastStartOrigin,
                    cancellationToken).ConfigureAwait(false);
                availableAdjacentSectors = nextToDock is > 0 &&
                    freshAdjacentSectors.Contains(nextToDock.Value)
                    ? [nextToDock.Value]
                    : [];
            }
        }
        int? tradeSectorNow = situation.Snapshot?.CurrentSector;
        if (preserveFuelOreForDockShopper)
        {
            sessionState.PendingTradePartnerSector = null;
            sessionState.PlannedTradeSellerSector = null;
        }
        int? pendingPartnerSector = sessionState.PendingTradePartnerSector;
        int? forcedTradeRouteSector = null;
        if (pendingPartnerSector == tradeSectorNow &&
            situation.Snapshot is { HoldsEmpty: { } empty, HoldsTotal: { } total } &&
            empty >= total)
        {
            sessionState.PendingTradePartnerSector = null;
            pendingPartnerSector = null;
        }
        if (movementEnabled &&
            pendingPartnerSector is { } partnerTarget &&
            tradeSectorNow is { } currentSectorForPartner &&
            currentSectorForPartner != partnerTarget)
        {
            int? nextTowardPartner = freshAdjacentSectors.Contains(partnerTarget)
                ? partnerTarget
                : sessionState.FedSpaceFastStartPhase == FedSpaceFastStartPhase.TradingPairs &&
                  fedSpaceFastStartProvider is not null
                    ? await fedSpaceFastStartProvider.GetNextSectorTowardFedSpaceTradeTargetAsync(
                        currentSectorForPartner,
                        partnerTarget,
                        cancellationToken).ConfigureAwait(false)
                    : gameWorldProvider is not null
                    ? await gameWorldProvider.GetNextSectorTowardSectorAsync(
                        currentSectorForPartner,
                        partnerTarget,
                        cancellationToken).ConfigureAwait(false)
                    : null;
            if (nextTowardPartner is > 0 && freshAdjacentSectors.Contains(nextTowardPartner.Value))
            {
                availableAdjacentSectors = [nextTowardPartner.Value];
                forcedTradeRouteSector = nextTowardPartner.Value;
            }
        }
        int? plannedTradeSellerSector = sessionState.PlannedTradeSellerSector;
        if (movementEnabled &&
            pendingPartnerSector is null &&
            plannedTradeSellerSector is { } plannedSeller &&
            tradeSectorNow is { } currentSectorForTrade &&
            currentSectorForTrade != plannedSeller)
        {
            int? nextTowardSeller = gameWorldProvider is not null
                ? sessionState.FedSpaceFastStartPhase == FedSpaceFastStartPhase.TradingPairs &&
                  fedSpaceFastStartProvider is not null
                    ? await fedSpaceFastStartProvider.GetNextSectorTowardFedSpaceTradeTargetAsync(
                        currentSectorForTrade,
                        plannedSeller,
                        cancellationToken).ConfigureAwait(false)
                    : await gameWorldProvider.GetNextSectorTowardSectorAsync(
                    currentSectorForTrade,
                    plannedSeller,
                    cancellationToken).ConfigureAwait(false)
                : null;
            if (nextTowardSeller is > 0 && freshAdjacentSectors.Contains(nextTowardSeller.Value))
            {
                availableAdjacentSectors = [nextTowardSeller.Value];
                forcedTradeRouteSector = nextTowardSeller.Value;
            }
            else
            {
                sessionState.PortTradeAttemptedSectors.Add(plannedSeller);
                sessionState.PlannedTradeSellerSector = null;
                plannedTradeSellerSector = null;
            }
        }
        int? nextRouteSector = availableAdjacentSectors.Count == 1
            ? availableAdjacentSectors[0]
            : null;
        AutonomousPortTradeOpportunity? portTradeOpportunity = null;
        if (gameActionsEnabled &&
            !preserveFuelOreForDockShopper &&
            situation.Snapshot is { } tradeSnapshot &&
            GameSnapshotValidation.IsValid(tradeSnapshot) &&
            tradeSnapshot.CurrentSector is { } tradeSector)
        {
            if (sessionState.FedSpaceFastStartPhase == FedSpaceFastStartPhase.TradingPairs &&
                fedSpaceFastStartProvider is not null)
            {
                portTradeOpportunity = await fedSpaceFastStartProvider.GetFedSpaceTradeOpportunityAsync(
                    tradeSector,
                    nextRouteSector,
                    tradeSnapshot,
                    sessionState.PortTradeAttemptedSectors,
                    needsFuelOreForDockShopper ? TradeProduct.FuelOre : null,
                    cancellationToken).ConfigureAwait(false);
                if (portTradeOpportunity is null)
                {
                    sessionState.FedSpaceFastStartPhase = FedSpaceFastStartPhase.Complete;
                }
            }
            else if (sessionState.FedSpaceFastStartPhase != FedSpaceFastStartPhase.Complete &&
                     portTradeOpportunityProvider is not null)
            {
                portTradeOpportunity = await portTradeOpportunityProvider.GetOpportunityAsync(
                    tradeSector,
                    sessionState.PreviousSector,
                    nextRouteSector,
                    tradeSnapshot,
                    cancellationToken,
                    sessionState.PortTradeAttemptedSectors,
                    needsFuelOreForDockShopper ? TradeProduct.FuelOre : null).ConfigureAwait(false);
            }
        }
        if (gameActionsEnabled &&
            currentRefurbPortClass == 9 &&
            situation.Snapshot is { CurrentSector: { } starDockTradeSector } starDockTradeSnapshot &&
            !sessionState.PortTradeAttemptedSectors.Contains(starDockTradeSector) &&
            PortTradeCompatibility.FindSellableCargo(9, starDockTradeSnapshot) is { } starDockCargoProduct)
        {
            portTradeOpportunity = new AutonomousPortTradeOpportunity(
                starDockTradeSector,
                starDockCargoProduct,
                SellingExistingCargo: true);
        }
        if (pendingPartnerSector is not null &&
            portTradeOpportunity?.TradeSector is { } deferredTradeTarget &&
            deferredTradeTarget != situation.Snapshot?.CurrentSector)
        {
            portTradeOpportunity = null;
        }
        if (plannedTradeSellerSector is { } sellerAtCurrentSector &&
            sellerAtCurrentSector == tradeSectorNow &&
            (portTradeOpportunity is null || portTradeOpportunity.TradeSector is not null))
        {
            sessionState.PortTradeAttemptedSectors.Add(sellerAtCurrentSector);
            sessionState.PlannedTradeSellerSector = null;
            plannedTradeSellerSector = null;
        }
        if (plannedTradeSellerSector is { } existingSeller &&
            tradeSectorNow != existingSeller &&
            portTradeOpportunity?.TradeSector is { } alternateSeller &&
            alternateSeller != existingSeller &&
            portTradeOpportunity.RouteSector != alternateSeller)
        {
            portTradeOpportunity = null;
        }
        if (movementEnabled &&
            situation.Snapshot is
            {
                DensityScannerInstalled: not true,
                HoloScannerInstalled: not true
            })
        {
            availableAdjacentSectors = availableAdjacentSectors
                .Where(targetSector => IsScannerlessDestinationAllowed(
                    targetSector,
                    starDockSector,
                    adjacentSectorKnowledge))
                .ToArray();

            if (portTradeOpportunity?.RouteSector is { } scannerlessTradeRouteSector &&
                !IsScannerlessDestinationAllowed(
                    scannerlessTradeRouteSector,
                    starDockSector,
                    adjacentSectorKnowledge))
            {
                portTradeOpportunity = null;
            }
        }
        bool remoteTradeOpportunity =
            portTradeOpportunity?.TradeSector is { } tradeTargetSector &&
            tradeTargetSector != situation.Snapshot?.CurrentSector &&
            portTradeOpportunity.RouteSector is { } tradeRouteSector &&
            freshAdjacentSectors.Contains(tradeRouteSector) &&
            !sessionState.PortTradeAttemptedSectors.Contains(tradeTargetSector);
        if (remoteTradeOpportunity &&
            portTradeOpportunity?.TradeSector is { } selectedTradeTargetSector)
        {
            availableAdjacentSectors = [portTradeOpportunity!.RouteSector!.Value];
            forcedTradeRouteSector = portTradeOpportunity.RouteSector.Value;
            sessionState.PlannedTradeSellerSector = selectedTradeTargetSector;
        }
        else if (portTradeOpportunity?.TradeSector is { } invalidTradeTarget &&
                 invalidTradeTarget != situation.Snapshot?.CurrentSector &&
                 sessionState.PendingTradePartnerSector is null)
        {
            portTradeOpportunity = null;
        }
        IAutonomousPortCimFreshnessProvider? cimFreshnessProvider =
            portTradeOpportunityProvider as IAutonomousPortCimFreshnessProvider;
        bool portCimFresh = cimFreshnessProvider?.IsPortCimFresh() == true;
        if (portCimFresh)
        {
            sessionState.LastPortCimRefreshAttemptAt = null;
            sessionState.LastPortCimRefreshError = null;
        }
        else if (gameActionsEnabled &&
                 !preserveFuelOreForDockShopper &&
                 portTradeOpportunity is null &&
                 cimFreshnessProvider is not null &&
                 gameScriptRunner is IAutonomousGameCimRefresher cimRefresher)
        {
            bool mayContinueWithoutPortCim =
                situation.Snapshot is { } currentSnapshot &&
                !HasCarriedCargo(currentSnapshot);
            DateTimeOffset now = DateTimeOffset.UtcNow;
            if (sessionState.LastPortCimRefreshAttemptAt is { } lastRefreshAt &&
                now - lastRefreshAt < CimRefreshRetryInterval)
            {
                string activity = sessionState.LastPortCimRefreshError is { } refreshError
                    ? $"CIM refresh retry scheduled: {refreshError}"
                    : "Waiting for the port and warp CIM refresh to complete";
                if (!mayContinueWithoutPortCim)
                {
                    statusStore?.SetActivity(currentAgentId, activity, situation);
                    return AutonomousTestActionResult.StatusRefreshPending;
                }

                statusStore?.SetActivity(
                    currentAgentId,
                    $"{activity}; no cargo is aboard, continuing with non-trade decisions",
                    situation);
            }
            else
            {
                sessionState.LastPortCimRefreshAttemptAt = now;
                statusStore?.SetActivity(
                    currentAgentId,
                    "Refreshing stale port and warp CIM before trade planning",
                    situation);
                try
                {
                    await cimRefresher.RefreshCimAsync(currentAgentId, cancellationToken)
                        .ConfigureAwait(false);
                    sessionState.LastPortCimRefreshAttemptAt = null;
                    sessionState.LastPortCimRefreshError = null;
                    statusStore?.RecordDecision(
                        currentAgentId,
                        "Port and warp CIM refreshed",
                        "RefreshCim",
                        "The port and warp data was stale; refreshed both before resuming trade and movement decisions.",
                        situation);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    sessionState.LastPortCimRefreshError = exception.Message;
                    logger.LogError(
                        exception,
                        "Could not refresh stale port and warp CIM for agent {AgentId}.",
                        currentAgentId);
                    if (!mayContinueWithoutPortCim)
                    {
                        statusStore?.SetActivity(
                            currentAgentId,
                            $"Port and warp CIM refresh failed: {exception.Message}",
                            situation);
                        return AutonomousTestActionResult.StatusRefreshPending;
                    }

                    statusStore?.SetActivity(
                        currentAgentId,
                        $"Port and warp CIM refresh failed: {exception.Message}; no cargo is aboard, continuing with non-trade decisions",
                        situation);
                }

                if (sessionState.LastPortCimRefreshError is null)
                {
                    return AutonomousTestActionResult.StatusRefreshSent;
                }
            }
        }

        if (gameActionsEnabled &&
            situation.Snapshot is { } cargoSnapshot &&
            HasCarriedCargo(cargoSnapshot) &&
            portTradeOpportunity is null &&
            !preserveFuelOreForDockShopper &&
            !portCimFresh)
        {
            statusStore?.SetActivity(
                currentAgentId,
                "Waiting for fresh port CIM before moving with carried cargo",
                situation);
            statusStore?.RecordDecision(
                currentAgentId,
                "Waiting for port CIM",
                "No fresh compatible buyer is known for carried cargo; refresh CIM before moving.",
                "Hold position until fresh port data identifies a buyer for the cargo already aboard.",
                situation);
            return AutonomousTestActionResult.Handled;
        }
        if (gameActionsEnabled &&
            situation.Snapshot is { } knownCargoSnapshot &&
            HasCarriedCargo(knownCargoSnapshot) &&
            portTradeOpportunity is null &&
            !preserveFuelOreForDockShopper &&
            portCimFresh)
        {
            statusStore?.SetActivity(
                currentAgentId,
                "No compatible cargo buyer in fresh CIM; evaluating adjacent progress",
                situation);
        }
        bool portTradeAttemptedAtCurrentSector =
            situation.Snapshot?.CurrentSector is { } currentTradeSector &&
            sessionState.PortTradeAttemptedSectors.Contains(currentTradeSector);
        var relevantKnowledge = actionKnowledgeProvider is not null
            ? await actionKnowledgeProvider.GetRelevantKnowledgeAsync(
                currentAgentId,
                cancellationToken).ConfigureAwait(false)
            : Array.Empty<AutonomousActionKnowledgeItem>();
        var recentReactiveSignals = await GetRecentReactiveSignalsAsync(
            currentAgentId,
            cancellationToken).ConfigureAwait(false);
        if (fedSpaceSweepTarget is { } verifiedSweepTarget && movementEnabled)
        {
            availableAdjacentSectors = [verifiedSweepTarget];
        }
        bool scannerlessStarDockHold =
            movementEnabled &&
            currentSector == starDockSector &&
            situation.Snapshot is
            {
                SynchronizedCredits: <= 1,
                DensityScannerInstalled: not true,
                HoloScannerInstalled: not true,
                FuelOre: 0,
                Organics: 0,
                Equipment: 0
            } &&
            !starDockShopperReady &&
            portTradeOpportunity is null &&
            freshAdjacentSectors.Any(targetSector => IsScannerlessDestinationAllowed(
                targetSector,
                starDockSector,
                adjacentSectorKnowledge));
        if (scannerlessStarDockHold)
        {
            activeObjective = new AutonomousActionObjective(
                $"Acquire a scanner at StarDock sector {starDockSector} before exploring.",
                "The Dock Shopper is not ready and no verified compatible scanner-funding trade is available. Stay at StarDock; do not leave for a sector merely because it is known or adjacent.");
            availableAdjacentSectors = Array.Empty<int>();
        }
        var actionContext = new AutonomousTestActionContext(situation, availableAdjacentSectors)
        {
            GameActionsEnabled = gameActionsEnabled,
            ActiveObjective = activeObjective,
            StarDockSector = starDockSector,
            PortTradeOpportunity = portTradeOpportunity,
            PortTradeAttemptedAtCurrentSector = portTradeAttemptedAtCurrentSector,
            WorldTradeModeAttemptedAtCurrentSector =
                sessionState.LastWorldTradeAttemptSector == situation.Snapshot?.CurrentSector,
            WorldTradeModeRunning = false,
            CurrentRefurbPortClass = currentRefurbPortClass,
            RefurbMenuStage = sessionState.RefurbMenuStage,
            RefurbQuote = sessionState.RefurbQuote,
            RefurbUpgradesAttemptedAtCurrentSector =
                sessionState.RefurbUpgradesAttemptedAtCurrentSector,
            StarDockShopperReady = starDockShopperReady,
            StarDockShopperAttemptedAtCurrentSector =
                sessionState.LastStarDockShopperAttemptSector == situation.Snapshot?.CurrentSector,
            StarDockSectorHasOwnedFighter = starDockHasOwnedFighter,
            CurrentSectorIsFederationSpace = currentSectorIsFederationSpace,
            StarDockFighterDeploymentAvailable = starDockFighterDeploymentAvailable,
            StarDockShopperStage = sessionState.StarDockShopperStage,
            ScannerTarget = scannerTarget,
            PreferredTradeProduct = needsFuelOreForDockShopper ? TradeProduct.FuelOre : null,
            RecentReactiveSignals = recentReactiveSignals,
            AdjacentSectorKnowledge = adjacentSectorKnowledge,
            RelevantKnowledge = relevantKnowledge
        };
        AutonomousTestActionDecision decision;
        if (fedSpaceSweepTarget is { } sweepTarget &&
            movementEnabled &&
            availableAdjacentSectors.Contains(sweepTarget))
        {
            decision = new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                $"Continue the first-login FedSpace survey through verified adjacent sector {sweepTarget}.",
                sweepTarget);
        }
        else if (sessionState.FedSpaceFastStartPhase == FedSpaceFastStartPhase.Sweeping)
        {
            statusStore?.SetActivity(
                currentAgentId,
                "Waiting for an approved fresh adjacent route to complete the FedSpace survey",
                situation);
            return AutonomousTestActionResult.StatusRefreshPending;
        }
        else if (scannerlessStarDockHold)
        {
            decision = new AutonomousTestActionDecision(
                AutonomousTestAction.None,
                "Hold at StarDock until a verified compatible trade can fund the required scanner.");
        }
        else if (portTradeOpportunity is not null &&
            (portTradeOpportunity.TradeSector is null ||
             portTradeOpportunity.TradeSector == situation.Snapshot?.CurrentSector) &&
            !portTradeAttemptedAtCurrentSector)
        {
            string tradeRationale = portTradeOpportunity.SellingExistingCargo
                ? $"Sell the {portTradeOpportunity.Product} already aboard at this port, which buys it."
                : $"Buy {portTradeOpportunity.Product} here for the compatible buyer port in adjacent sector {portTradeOpportunity.PartnerSector}.";
            if (portTradeOpportunity.SellingExistingCargo &&
                portTradeOpportunity.FollowUpBuyProduct is { } followUpProduct &&
                portTradeOpportunity.FollowUpPartnerSector is { } followUpPartnerSector)
            {
                tradeRationale +=
                    $" Then buy {followUpProduct} here for the compatible buyer port in adjacent sector {followUpPartnerSector}.";
            }

            decision = new AutonomousTestActionDecision(
                AutonomousTestAction.TradeAtCurrentPort,
                tradeRationale);
        }
        else if (remoteTradeOpportunity)
        {
            int targetSector = portTradeOpportunity!.RouteSector!.Value;
            decision = new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                $"Move to sector {targetSector} to buy {portTradeOpportunity.Product} from a known compatible port pair.",
                targetSector);
        }
        else if (forcedTradeRouteSector is { } forcedTargetSector &&
                 availableAdjacentSectors.Contains(forcedTargetSector))
        {
            decision = new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                $"Continue toward the selected port trade target in sector {sessionState.PlannedTradeSellerSector ?? pendingPartnerSector}.",
                forcedTargetSector);
        }
        else if (movementEnabled &&
                 currentSector != starDockSector &&
                 starDockSector is > 0 &&
                 situation.Snapshot is
                 {
                     DensityScannerInstalled: false,
                     HoloScannerInstalled: false
                 } &&
                 availableAdjacentSectors.Count == 1)
        {
            int targetSector = availableAdjacentSectors[0];
            decision = new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                $"Prioritize reaching StarDock to buy the required Density Scanner via verified adjacent sector {targetSector}.",
                targetSector);
        }
        else if (movementEnabled &&
                 currentSector != starDockSector &&
                 starDockSector is > 0 &&
                 situation.Snapshot is { SynchronizedCredits: { } routeCredits } &&
                 (situation.Snapshot.DensityScannerInstalled is true ||
                  situation.Snapshot.HoloScannerInstalled is true) &&
                 routeCredits < AutonomousTradePolicy.MinimumWorldTradeUpgradeCredits &&
                 availableAdjacentSectors.Count == 1)
        {
            int targetSector = availableAdjacentSectors[0];
            decision = new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                $"Follow the active World Trade objective toward StarDock via verified adjacent sector {targetSector}.",
                targetSector);
        }
        else if (gameActionsEnabled &&
                 gameScriptRunner is not null &&
                 currentSector == starDockSector &&
                 starDockSector is > 0 &&
                 situation.Snapshot is { SynchronizedCredits: { } worldTradeCredits } &&
                 (situation.Snapshot.DensityScannerInstalled is true ||
                  situation.Snapshot.HoloScannerInstalled is true) &&
                 worldTradeCredits < AutonomousTradePolicy.MinimumWorldTradeUpgradeCredits &&
                 !sessionState.WorldTradeModeStarted &&
                 sessionState.LastWorldTradeAttemptSector != currentSector)
        {
            decision = new AutonomousTestActionDecision(
                AutonomousTestAction.StartWorldTradeMode,
                "The active strategy plan is to earn the verified refurb budget with the fixed World Trade script.");
        }
        else
        {
            statusStore?.SetActivity(currentAgentId, "Thinking", situation);
            decision = await DecideWithTelemetryAsync(
                currentAgentId,
                actionProvider,
                actionContext,
                cancellationToken).ConfigureAwait(false);
        }
        string decisionRationale = decision.Rationale;
        if (preserveFuelOreForDockShopper &&
            currentSector == starDockSector &&
            decision.Action is AutonomousTestAction.None or AutonomousTestAction.RunStarDockShopper &&
            situation.Snapshot is { } dockShopperSnapshot)
        {
            decisionRationale +=
                $" Dock Shopper readiness: ready={starDockShopperReady}; " +
                $"freshOwnedOrCorporateFighter={starDockHasOwnedFighter}; " +
                $"federationSpace={currentSectorIsFederationSpace}; " +
                $"fighterDeploymentAvailable={starDockFighterDeploymentAvailable}; " +
                $"scannerTarget={scannerTarget}; " +
                $"densityScanner={dockShopperSnapshot.DensityScannerInstalled}; " +
                $"holoScanner={dockShopperSnapshot.HoloScannerInstalled}; " +
                $"credits={dockShopperSnapshot.SynchronizedCredits}; " +
                $"FuelOre={dockShopperSnapshot.FuelOre}/{dockShopperSnapshot.HoldsTotal}; " +
                $"shipFighters={dockShopperSnapshot.Ship?.Fighters}.";
        }
        statusStore?.RecordDecision(
            currentAgentId,
            decision.Action == AutonomousTestAction.None ? "Holding" : "Decision received",
            decision.Action.ToString(),
            decisionRationale,
            situation);
        if (decision.Action == AutonomousTestAction.None)
        {
            if (decision.TargetSector != 0)
            {
                throw new InvalidDataException("A no-action decision cannot include a target sector.");
            }

            logger.LogInformation(
                "Autonomous smoke-test model chose no action for agent {AgentId}: {Rationale}",
                currentAgentId,
                decision.Rationale);
            return AutonomousTestActionResult.Handled;
        }
        if (decision.Action == AutonomousTestAction.RequestShipInformation)
        {
            if (decision.TargetSector != 0)
            {
                throw new InvalidDataException(
                    "A ship-information request cannot include a target sector.");
            }

            var refreshedStatusSituation = await situationProvider.CaptureSituationAsync(
                currentAgentId,
                cancellationToken).ConfigureAwait(false);
            if (!IsCommandPromptEligible(refreshedStatusSituation))
            {
                logger.LogWarning(
                    "Autonomous status request was blocked for agent {AgentId} because its session is no longer eligible.",
                    currentAgentId);
                return AutonomousTestActionResult.Handled;
            }
            if (!NeedsShipStatusRefresh(refreshedStatusSituation))
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "Status refresh suppressed; snapshot is already fresh",
                    refreshedStatusSituation);
                logger.LogInformation(
                    "Blocked an unnecessary ship-information request for agent {AgentId} because its synchronized snapshot is already fresh.",
                    currentAgentId);
                return AutonomousTestActionResult.Handled;
            }

            await commandSender.SendAsync(
                currentAgentId,
                new GameCommandRequest(GameCommand.RequestShipInformation),
                cancellationToken).ConfigureAwait(false);
            statusStore?.RecordDecision(
                currentAgentId,
                "Status refresh sent",
                decision.Action.ToString(),
                decision.Rationale,
                refreshedStatusSituation);
            sessionState.PendingShipStatusRefreshSentAt = DateTimeOffset.UtcNow;
            logger.LogInformation(
                "Autonomous smoke test sent the allowlisted ship-information request for agent {AgentId}.",
                currentAgentId);
            return AutonomousTestActionResult.StatusRefreshSent;
        }

        if (decision.Action == AutonomousTestAction.TradeAtCurrentPort)
        {
            if (decision.TargetSector != 0)
            {
                throw new InvalidDataException(
                    "A current-port trade decision cannot include a target sector.");
            }
            if (!allowGameActions ||
                portTradeOpportunityProvider is null ||
                portTradeOpportunity is null ||
                situation.Snapshot?.CurrentSector is not { } tradeCommandSector ||
                sessionState.PortTradeAttemptedSectors.Contains(tradeCommandSector))
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "Port trade blocked because no fresh compatible port opportunity is available",
                    situation);
                return AutonomousTestActionResult.Handled;
            }

            var refreshedTradeSituation = await situationProvider.CaptureSituationAsync(
                currentAgentId,
                cancellationToken).ConfigureAwait(false);
            if (!IsCommandPromptEligible(refreshedTradeSituation))
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "Port trade paused while waiting for a fresh Command prompt",
                    refreshedTradeSituation);
                return AutonomousTestActionResult.Handled;
            }
            if (NeedsShipStatusRefresh(refreshedTradeSituation))
            {
                if (sessionState.PendingShipStatusRefreshSentAt is { } refreshSentAt &&
                    DateTimeOffset.UtcNow - refreshSentAt < ShipStatusRefreshRetryInterval)
                {
                    statusStore?.SetActivity(
                        currentAgentId,
                        "Waiting for a fresh ship-status response before trading",
                        refreshedTradeSituation);
                    return AutonomousTestActionResult.StatusRefreshPending;
                }

                await commandSender.SendAsync(
                    currentAgentId,
                    new GameCommandRequest(GameCommand.RequestShipInformation),
                    cancellationToken).ConfigureAwait(false);
                sessionState.PendingShipStatusRefreshSentAt = DateTimeOffset.UtcNow;
                statusStore?.RecordDecision(
                    currentAgentId,
                    "Status refresh sent",
                    AutonomousTestAction.RequestShipInformation.ToString(),
                    "The ship snapshot became stale before trading; refresh it before interacting with a port.",
                    refreshedTradeSituation);
                return AutonomousTestActionResult.StatusRefreshSent;
            }

            if (!IsMovementSituationEligible(refreshedTradeSituation) ||
                refreshedTradeSituation.Snapshot!.CurrentSector != tradeCommandSector)
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "Port trade blocked because the verified ship state changed",
                    refreshedTradeSituation);
                return AutonomousTestActionResult.Handled;
            }
            if (IsEscapePodOrSingleHoldShip(refreshedTradeSituation.Snapshot))
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "Port trading blocked; recover a viable ship before trading",
                    refreshedTradeSituation);
                return AutonomousTestActionResult.Handled;
            }

            TradeProduct? preferredTradeProduct = NeedsFuelOreForDockShopper(
                refreshedTradeSituation.Snapshot,
                starDockSector,
                GetScannerPurchaseTarget(
                    refreshedTradeSituation.Snapshot,
                    sessionState.DensityScannerStartingSnapshot))
                ? TradeProduct.FuelOre
                : null;
            AutonomousPortTradeOpportunity? refreshedOpportunity =
                sessionState.FedSpaceFastStartPhase == FedSpaceFastStartPhase.TradingPairs &&
                fedSpaceFastStartProvider is not null
                    ? await fedSpaceFastStartProvider.GetFedSpaceTradeOpportunityAsync(
                        tradeCommandSector,
                        nextRouteSector,
                        refreshedTradeSituation.Snapshot,
                        sessionState.PortTradeAttemptedSectors,
                        preferredTradeProduct,
                        cancellationToken).ConfigureAwait(false)
                    : portTradeOpportunityProvider is not null
                        ? await portTradeOpportunityProvider.GetOpportunityAsync(
                            tradeCommandSector,
                            sessionState.PreviousSector,
                            nextRouteSector,
                            refreshedTradeSituation.Snapshot,
                            cancellationToken,
                            sessionState.PortTradeAttemptedSectors,
                            preferredTradeProduct).ConfigureAwait(false)
                        : null;
            bool verifiedStarDockCargoSale =
                refreshedOpportunity is null &&
                portTradeOpportunity.SellingExistingCargo &&
                portTradeOpportunity.TradeSector is null &&
                portTradeOpportunity.PartnerSector == tradeCommandSector &&
                gameWorldProvider is not null &&
                await gameWorldProvider.GetCurrentRefurbPortClassAsync(
                    tradeCommandSector,
                    cancellationToken).ConfigureAwait(false) == 9 &&
                PortTradeCompatibility.FindSellableCargo(
                    9,
                    refreshedTradeSituation.Snapshot!) == portTradeOpportunity.Product;
            if (refreshedOpportunity != portTradeOpportunity && !verifiedStarDockCargoSale)
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "Port trade opportunity changed; retrying on the next heartbeat",
                    refreshedTradeSituation);
                return AutonomousTestActionResult.Retryable;
            }

            AutonomousPortTradeOpportunity verifiedOpportunity =
                verifiedStarDockCargoSale ? portTradeOpportunity : refreshedOpportunity!;
            await commandSender.SendAsync(
                currentAgentId,
                new GameCommandRequest(
                    GameCommand.TradeAtCurrentPort,
                    Product: verifiedOpportunity.Product)
                {
                    SellingExistingCargo = verifiedOpportunity.SellingExistingCargo,
                    FollowUpBuyProduct = verifiedOpportunity.FollowUpBuyProduct,
                },
                cancellationToken).ConfigureAwait(false);
            sessionState.PendingPortTrade = new PendingPortTrade(
                tradeCommandSector,
                verifiedOpportunity,
                refreshedTradeSituation.Snapshot!,
                DateTimeOffset.UtcNow);
            sessionState.PendingShipStatusRefreshSentAt = null;
            sessionState.PlannedTradeSellerSector = null;
            statusStore?.RecordDecision(
                currentAgentId,
                "Port trade sent; awaiting verification",
                GameCommand.TradeAtCurrentPort.ToString(),
                $"{decision.Rationale} A fresh ship snapshot will be checked before planning the next action.",
                refreshedTradeSituation);
            if (verifiedOpportunity.SellingExistingCargo)
            {
                logger.LogInformation(
                    "Sent one bounded local port-sale command for agent {AgentId} at sector {Sector} to sell carried {Product}.",
                    currentAgentId,
                    tradeCommandSector,
                    verifiedOpportunity.Product);
            }
            else
            {
                logger.LogInformation(
                    "Sent one bounded local port-trade command for agent {AgentId} at sector {Sector}; compatible sector {PartnerSector} buys {Product}.",
                    currentAgentId,
                    tradeCommandSector,
                    verifiedOpportunity.PartnerSector,
                    verifiedOpportunity.Product);
            }
            return AutonomousTestActionResult.Handled;
        }

        if (decision.Action == AutonomousTestAction.DeployStarDockShopperPrerequisiteFighter)
        {
            if (decision.TargetSector != 0)
            {
                throw new InvalidDataException(
                    "A Dock Shopper fighter-deployment decision cannot include a target sector.");
            }
            if (!starDockFighterDeploymentAvailable ||
                starDockSector is not > 0 ||
                gameWorldProvider is null)
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "StarDock fighter deployment deferred until its prerequisites are verified",
                    situation);
                return AutonomousTestActionResult.Handled;
            }

            GameSituationReport refreshedDeploymentSituation =
                await situationProvider.CaptureSituationAsync(
                    currentAgentId,
                    cancellationToken).ConfigureAwait(false);
            GameSnapshot? refreshedDeploymentSnapshot = refreshedDeploymentSituation.Snapshot;
            bool freshOwnedFighterAlreadyPresent =
                await gameWorldProvider.HasFreshOwnedFighterInSectorAsync(
                    starDockSector.Value,
                    cancellationToken).ConfigureAwait(false);
            bool? refreshedSectorIsFederationSpace =
                await gameWorldProvider.IsFederationSpaceAsync(
                    starDockSector.Value,
                    cancellationToken).ConfigureAwait(false);
            if (!IsCommandPromptEligible(refreshedDeploymentSituation) ||
                NeedsShipStatusRefresh(refreshedDeploymentSituation) ||
                refreshedDeploymentSnapshot is not { } deploymentSnapshot ||
                !GameSnapshotValidation.IsValid(deploymentSnapshot) ||
                deploymentSnapshot.CurrentSector != starDockSector ||
                !IsScannerTargetAbsent(deploymentSnapshot, scannerTarget) ||
                deploymentSnapshot.SynchronizedCredits is not >=
                    AutonomousTradePolicy.MinimumScannerUpgradeCredits ||
                deploymentSnapshot.HoldsEmpty != 0 ||
                deploymentSnapshot.HoldsTotal is not > 0 ||
                deploymentSnapshot.FuelOre != deploymentSnapshot.HoldsTotal ||
                deploymentSnapshot.Organics != 0 ||
                deploymentSnapshot.Equipment != 0 ||
                deploymentSnapshot.Ship is not { Fighters: > 1 } deploymentShip ||
                freshOwnedFighterAlreadyPresent ||
                refreshedSectorIsFederationSpace is not false ||
                sessionState.LastStarDockFighterDeploymentAttemptSector == starDockSector ||
                sessionState.LastStarDockShopperAttemptSector == starDockSector ||
                sessionState.StarDockShopperStage != StarDockShopperStage.Inactive)
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "StarDock fighter deployment deferred because a fresh prerequisite changed",
                    refreshedDeploymentSituation);
                return AutonomousTestActionResult.Handled;
            }

            sessionState.LastStarDockFighterDeploymentAttemptSector = starDockSector.Value;
            await commandSender.SendAsync(
                currentAgentId,
                new GameCommandRequest(
                    GameCommand.DeployOneFighterAtCurrentSector,
                    starDockSector.Value)
                {
                    ExpectedShipFighterCount = deploymentShip.Fighters,
                    CurrentSectorIsFederationSpace = refreshedSectorIsFederationSpace
                },
                cancellationToken).ConfigureAwait(false);
            statusStore?.RecordDecision(
                currentAgentId,
                "One StarDock fighter deployed; verifying the sector record",
                decision.Action.ToString(),
                decision.Rationale,
                refreshedDeploymentSituation);
            logger.LogInformation(
                "Agent {AgentId} deployed one prerequisite fighter at StarDock sector {Sector}; Dock Shopper remains blocked until the fresh sector record confirms ownership.",
                currentAgentId,
                starDockSector.Value);
            return AutonomousTestActionResult.Handled;
        }

        if (decision.Action == AutonomousTestAction.RunStarDockShopper)
        {
            if (decision.TargetSector != 0)
            {
                throw new InvalidDataException(
                    "A Dock Shopper decision cannot include a target sector.");
            }
            if (!allowGameActions || gameWorldProvider is null || gameScriptRunner is null)
            {
                throw new InvalidOperationException(
                    "TWX Dock Shopper requires enabled game actions, verified StarDock prerequisites, and a script runner.");
            }
            if (!starDockShopperReady ||
                sessionState.StarDockShopperStage != StarDockShopperStage.Inactive ||
                scannerTarget is null ||
                situation.Snapshot is not { } shopperStartingSnapshot ||
                shopperStartingSnapshot.CurrentSector is not { } shopperSector)
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "Dock Shopper deferred until all script prerequisites are verified",
                    situation);
                return AutonomousTestActionResult.Handled;
            }

            sessionState.LastStarDockShopperAttemptSector = shopperSector;
            await gameScriptRunner.StartStarDockShopperAsync(
                currentAgentId,
                cancellationToken).ConfigureAwait(false);
            sessionState.PendingStarDockShopper = new PendingStarDockShopper(
                shopperStartingSnapshot,
                shopperSector,
                scannerTarget.Value,
                DateTimeOffset.UtcNow);
            sessionState.ActiveScriptObjective = activeObjective;
            sessionState.StarDockShopperStage = StarDockShopperStage.SelectHoloScanner;
            statusStore?.RecordDecision(
                currentAgentId,
                $"Dock Shopper started for {GetScannerDisplayName(scannerTarget.Value)}; Pilot will reason over each observed script prompt",
                decision.Action.ToString(),
                decision.Rationale,
                situation);
            logger.LogInformation(
                "Started the allowlisted Dock Shopper for agent {AgentId} at StarDock sector {Sector}; awaiting model-selected menu inputs.",
                currentAgentId,
                shopperSector);
            return AutonomousTestActionResult.Handled;
        }

        if (decision.Action == AutonomousTestAction.StartWorldTradeMode)
        {
            if (decision.TargetSector != 0)
            {
                throw new InvalidDataException(
                    "A World Trade mode decision cannot include a target sector.");
            }
            if (!allowGameActions || gameWorldProvider is null || gameScriptRunner is null)
            {
                throw new InvalidOperationException(
                    "TWX World Trade mode requires enabled game actions, a StarDock source, and a script runner.");
            }

            int? verifiedStarDockSector = await gameWorldProvider.GetStarDockSectorAsync(
                cancellationToken).ConfigureAwait(false);
            int? worldTradeCurrentSector = situation.Snapshot?.CurrentSector;
            if (situation.Snapshot?.DensityScannerInstalled is not true &&
                situation.Snapshot?.HoloScannerInstalled is not true)
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "World Trade deferred until Density or Holo scanning capability is verified",
                    situation);
                return AutonomousTestActionResult.Handled;
            }
            if (situation.Snapshot.SynchronizedCredits is not { } worldTradeCredits ||
                worldTradeCredits >= AutonomousTradePolicy.MinimumWorldTradeUpgradeCredits)
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "World Trade deferred because the refurb funding target is already reached",
                    situation);
                return AutonomousTestActionResult.Handled;
            }
            if (verifiedStarDockSector is not > 0 || worldTradeCurrentSector != verifiedStarDockSector)
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "Waiting to reach the known StarDock before starting World Trade",
                    situation);
                logger.LogInformation(
                    "TWX World Trade mode for agent {AgentId} was deferred until the verified StarDock sector is reached.",
                    currentAgentId);
                return AutonomousTestActionResult.Handled;
            }
            if (sessionState.LastWorldTradeAttemptSector == worldTradeCurrentSector)
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "World Trade mode was already attempted in this sector",
                    situation);
                return AutonomousTestActionResult.Handled;
            }

            await gameScriptRunner.StartWorldTradeModeAsync(
                currentAgentId,
                cancellationToken).ConfigureAwait(false);
            sessionState.LastWorldTradeAttemptSector = worldTradeCurrentSector;
            sessionState.WorldTradeModeStarted = true;
            sessionState.ActiveScriptObjective = activeObjective;
            statusStore?.RecordDecision(
                currentAgentId,
                "TWX World Trade mode started",
                decision.Action.ToString(),
                decision.Rationale,
                situation);
            logger.LogInformation(
                "TWX World Trade mode for agent {AgentId} started at StarDock sector {StarDockSector}.",
                currentAgentId,
                verifiedStarDockSector);
            return AutonomousTestActionResult.Handled;
        }

        if (decision.Action == AutonomousTestAction.InspectRefurbMenu)
        {
            if (!allowGameActions ||
                gameWorldProvider is null ||
                gameScriptRunner is null ||
                situation.Snapshot is not { CurrentSector: { } purchaseSector } refurbSnapshot ||
                currentRefurbPortClass is not (0 or 9) ||
                refurbSnapshot.HoloScannerInstalled is not true &&
                refurbSnapshot.DensityScannerInstalled is not true ||
                refurbSnapshot.SynchronizedCredits is not
                    >= AutonomousTradePolicy.MinimumWorldTradeUpgradeCredits)
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "Refurb deferred until the scanner, funding, location, and port are verified",
                    situation);
                return AutonomousTestActionResult.Handled;
            }

            AutonomousRefurbQuote quote = await gameScriptRunner.OpenRefurbMenuAsync(
                currentAgentId,
                purchaseSector,
                currentRefurbPortClass.Value,
                cancellationToken).ConfigureAwait(false);
            sessionState.RefurbQuote = quote;
            sessionState.RefurbMenuStage = AutonomousRefurbMenuStage.SelectingUpgrade;
            statusStore?.RecordDecision(
                currentAgentId,
                "Refurb menu opened and quote observed",
                decision.Action.ToString(),
                $"Fresh quote rows were parsed at sector {quote.Sector}; the model will select an affordable component while preserving the credit reserve.",
                situation);
            GameSituationReport quoteSituation = await situationProvider.CaptureSituationAsync(
                currentAgentId,
                cancellationToken).ConfigureAwait(false);
            if (quoteSituation.Presence != GamePresenceState.Connected ||
                quoteSituation.Authentication != GameAuthenticationState.Authenticated ||
                quoteSituation.Snapshot?.CurrentSector != quote.Sector)
            {
                await gameScriptRunner.CloseRefurbMenuAsync(
                    currentAgentId,
                    quote.PortClass,
                    cancellationToken).ConfigureAwait(false);
                sessionState.RefurbMenuStage = AutonomousRefurbMenuStage.Inactive;
                sessionState.RefurbQuote = null;
                statusStore?.SetActivity(
                    currentAgentId,
                    "Closed Refurb because the authenticated sector changed after quote observation",
                    quoteSituation);
                return AutonomousTestActionResult.WaitingForFreshPrompt;
            }

            return await HandleRefurbMenuStageAsync(
                currentAgentId,
                sessionState,
                quoteSituation,
                cancellationToken).ConfigureAwait(false);
        }

        if (decision.Action != AutonomousTestAction.MoveToAdjacentSector)
        {
            throw new InvalidDataException(
                $"Autonomous smoke-test action '{decision.Action}' is not allowlisted.");
        }

        if (!allowGameActions ||
            sessionState.MovementDisabled ||
            moveTargetProvider is null ||
            activeObjective is null)
        {
            logger.LogWarning(
                "Autonomous movement was blocked for agent {AgentId} because game actions are disabled or there is no active objective.",
                currentAgentId);
            return AutonomousTestActionResult.Handled;
        }

        if (!IsMovementSituationEligible(situation) ||
            decision.TargetSector <= 0 ||
            !availableAdjacentSectors.Contains(decision.TargetSector))
        {
            logger.LogWarning(
                "Autonomous movement was blocked for agent {AgentId} because its target was not eligible in the captured game state.",
                currentAgentId);
            return AutonomousTestActionResult.Handled;
        }

        if (requireMovementApproval && moveApprovalGate is null)
        {
            logger.LogWarning(
                "Autonomous movement was blocked for agent {AgentId} because no operator approval gate is configured.",
                currentAgentId);
            return AutonomousTestActionResult.Handled;
        }

        var approvalRequest = new AutonomousMoveApprovalRequest(
            Guid.NewGuid().ToString("N"),
            currentAgentId,
            situation.Snapshot!.CurrentSector!.Value,
            decision.TargetSector,
            TurnCost: 1,
            activeObjective!.Goal);
        if (requireMovementApproval)
        {
            statusStore?.RecordDecision(
                currentAgentId,
                "Awaiting movement approval",
                $"MoveToSector {approvalRequest.TargetSector}",
                decision.Rationale,
                situation);
            logger.LogInformation(
                "Autonomous movement for agent {AgentId} is awaiting operator approval: sector {CurrentSector} to {TargetSector}, costing {TurnCost} turn.",
                currentAgentId,
                approvalRequest.CurrentSector,
                approvalRequest.TargetSector,
                approvalRequest.TurnCost);
            if (!await moveApprovalGate!.RequestApprovalAsync(approvalRequest, cancellationToken)
                    .ConfigureAwait(false))
            {
                statusStore?.SetActivity(currentAgentId, "Movement declined by operator", situation);
                logger.LogInformation(
                    "Autonomous movement for agent {AgentId} was declined by the operator.",
                    currentAgentId);
                return AutonomousTestActionResult.Handled;
            }
        }
        else
        {
            statusStore?.SetActivity(
                currentAgentId,
                "Automatic one-turn movement enabled; revalidating route",
                situation);
        }

        var refreshedMovementSituation = await situationProvider.CaptureSituationAsync(
            currentAgentId,
            cancellationToken).ConfigureAwait(false);
        if (!IsCommandPromptEligible(refreshedMovementSituation))
        {
            statusStore?.SetActivity(
                currentAgentId,
                "Movement paused while waiting for a fresh Command prompt",
                refreshedMovementSituation);
            return AutonomousTestActionResult.Handled;
        }
        if (NeedsShipStatusRefresh(refreshedMovementSituation))
        {
            if (sessionState.PendingShipStatusRefreshSentAt is { } refreshSentAt &&
                DateTimeOffset.UtcNow - refreshSentAt < ShipStatusRefreshRetryInterval)
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "Waiting for a fresh ship-status response before movement",
                    refreshedMovementSituation);
                return AutonomousTestActionResult.StatusRefreshPending;
            }

            await commandSender.SendAsync(
                currentAgentId,
                new GameCommandRequest(GameCommand.RequestShipInformation),
                cancellationToken).ConfigureAwait(false);
            sessionState.PendingShipStatusRefreshSentAt = DateTimeOffset.UtcNow;
            statusStore?.RecordDecision(
                currentAgentId,
                "Status refresh sent",
                AutonomousTestAction.RequestShipInformation.ToString(),
                "The ship snapshot became stale while the model was reasoning; refresh it before moving.",
                refreshedMovementSituation);
            return AutonomousTestActionResult.StatusRefreshSent;
        }

        var currentAdjacentSectors = await moveTargetProvider.GetFreshAdjacentSectorIdsAsync(
            refreshedMovementSituation.Snapshot?.CurrentSector,
            cancellationToken).ConfigureAwait(false);
        if (!IsMovementSituationEligible(refreshedMovementSituation) ||
            refreshedMovementSituation.Snapshot!.CurrentSector != situation.Snapshot!.CurrentSector ||
            !currentAdjacentSectors.Contains(decision.TargetSector))
        {
            statusStore?.SetActivity(
                currentAgentId,
                "Movement blocked because the verified sector or route changed during reasoning",
                refreshedMovementSituation);
            logger.LogWarning(
                "Autonomous movement was blocked for agent {AgentId} because its session or verified route changed before execution.",
                currentAgentId);
            return AutonomousTestActionResult.Handled;
        }

        GameSnapshot movementSnapshot = refreshedMovementSituation.Snapshot!;
        if (movementSnapshot.DensityScannerInstalled is not true &&
            movementSnapshot.HoloScannerInstalled is not true &&
            (decision.TargetSector == KnownHighRiskSector ||
             (decision.TargetSector is > 10 && decision.TargetSector != starDockSector)))
        {
            IReadOnlyList<AdjacentSectorKnowledge> targetKnowledge =
                decision.TargetSector == KnownHighRiskSector
                    ? Array.Empty<AdjacentSectorKnowledge>()
                    : await moveTargetProvider.GetSectorKnowledgeAsync(
                        [decision.TargetSector],
                        cancellationToken).ConfigureAwait(false);
            if (!IsScannerlessDestinationAllowed(
                    decision.TargetSector,
                    starDockSector,
                    targetKnowledge))
            {
                string blockReason = decision.TargetSector == KnownHighRiskSector
                    ? $"Movement blocked: sector {decision.TargetSector} requires a verified scanner due to prior ship-loss risk"
                    : $"Movement blocked: no scanner is verified and sector {decision.TargetSector} is not recorded as explored";
                statusStore?.SetActivity(
                    currentAgentId,
                    blockReason,
                    refreshedMovementSituation);
                logger.LogWarning(
                    "Autonomous movement for agent {AgentId} was blocked because sector {TargetSector} requires a verified scanner or fresh safe exploration evidence.",
                    currentAgentId,
                    decision.TargetSector);
                return AutonomousTestActionResult.Handled;
            }
        }

        await commandSender.SendAsync(
            currentAgentId,
            new GameCommandRequest(GameCommand.MoveToSector, decision.TargetSector),
            cancellationToken).ConfigureAwait(false);
        statusStore?.RecordDecision(
            currentAgentId,
            "Movement sent; awaiting verified postcondition",
            $"MoveToSector {decision.TargetSector}",
            decision.Rationale,
            refreshedMovementSituation);
        sessionState.PendingMovementTarget = decision.TargetSector;
        sessionState.PendingMovementOrigin = situation.Snapshot.CurrentSector;
        sessionState.PendingMovementSentAt = DateTimeOffset.UtcNow;
        logger.LogInformation(
            "Autonomous movement sent one revalidated adjacent-sector request for agent {AgentId}.",
            currentAgentId);
        return AutonomousTestActionResult.Handled;
    }

    private static bool IsCommandPromptEligible(GameSituationReport situation) =>
        situation.Presence == GamePresenceState.Connected &&
        situation.Authentication == GameAuthenticationState.Authenticated &&
        string.Equals(situation.CurrentPrompt, "Command", StringComparison.OrdinalIgnoreCase) &&
        situation.CurrentPromptObservedAt is { } promptAt &&
        promptAt <= DateTimeOffset.UtcNow &&
        DateTimeOffset.UtcNow - promptAt <= MaximumCommandPromptAge;

    private async Task<AutonomousTestActionResult> HandleRefurbMenuStageAsync(
        string currentAgentId,
        AutonomousTestSessionState sessionState,
        GameSituationReport situation,
        CancellationToken cancellationToken)
    {
        if (situation.Presence != GamePresenceState.Connected ||
            situation.Authentication != GameAuthenticationState.Authenticated)
        {
            statusStore?.SetActivity(
                currentAgentId,
                "Waiting for the authenticated session while Refurb is open",
                situation);
            return AutonomousTestActionResult.WaitingForFreshPrompt;
        }
        if (!allowGameActions || gameScriptRunner is null ||
            sessionState.RefurbQuote is not { } quote ||
            sessionState.RefurbMenuStage != AutonomousRefurbMenuStage.SelectingUpgrade)
        {
            throw new InvalidOperationException(
                "The active Refurb stage requires approved game actions, a runner, and a verified live quote.");
        }
        if (situation.Snapshot is not { } snapshot ||
            situation.Synchronization != GameSynchronizationState.Healthy ||
            !GameSnapshotValidation.IsValid(snapshot) ||
            snapshot.CurrentSector != quote.Sector ||
            quote.ObservedAt > DateTimeOffset.UtcNow ||
            DateTimeOffset.UtcNow - quote.ObservedAt > TimeSpan.FromSeconds(20))
        {
            await gameScriptRunner.CloseRefurbMenuAsync(
                currentAgentId,
                quote.PortClass,
                cancellationToken).ConfigureAwait(false);
            sessionState.RefurbMenuStage = AutonomousRefurbMenuStage.Inactive;
            sessionState.RefurbQuote = null;
            statusStore?.SetActivity(
                currentAgentId,
                "Closed Refurb because its quote or ship snapshot was stale",
                situation);
            return AutonomousTestActionResult.WaitingForFreshPrompt;
        }

        var context = new AutonomousTestActionContext(situation, [])
        {
            GameActionsEnabled = true,
            ActiveObjective = new AutonomousActionObjective(
                "Choose one affordable ship component from the live Refurb quote.",
                $"Preserve {AutonomousTradePolicy.RefurbCreditReserve:N0} credits and only use values from this verified quote."),
            RecentReactiveSignals = await GetRecentReactiveSignalsAsync(
                currentAgentId,
                cancellationToken).ConfigureAwait(false),
            CurrentRefurbPortClass = quote.PortClass,
            RefurbMenuStage = sessionState.RefurbMenuStage,
            RefurbQuote = quote,
            RefurbUpgradesAttemptedAtCurrentSector =
                sessionState.RefurbUpgradesAttemptedAtCurrentSector
        };
        AutonomousTestActionDecision decision = await DecideWithTelemetryAsync(
            currentAgentId,
            actionProvider,
            context,
            cancellationToken).ConfigureAwait(false);
        statusStore?.RecordDecision(
            currentAgentId,
            decision.Action == AutonomousTestAction.None ? "Closing Refurb menu" : "Refurb decision received",
            decision.Action.ToString(),
            decision.Rationale,
            situation);

        AutonomousRefurbUpgrade? upgrade = decision.Action switch
        {
            AutonomousTestAction.BuyCargoHolds => AutonomousRefurbUpgrade.CargoHolds,
            AutonomousTestAction.BuyFighters => AutonomousRefurbUpgrade.Fighters,
            AutonomousTestAction.BuyShields => AutonomousRefurbUpgrade.Shields,
            _ => null
        };
        if (upgrade is null)
        {
            await gameScriptRunner.CloseRefurbMenuAsync(
                currentAgentId,
                quote.PortClass,
                cancellationToken).ConfigureAwait(false);
            sessionState.RefurbUpgradesAttemptedAtCurrentSector.UnionWith(
                Enum.GetValues<AutonomousRefurbUpgrade>());
            sessionState.RefurbMenuStage = AutonomousRefurbMenuStage.Inactive;
            sessionState.RefurbQuote = null;
            return AutonomousTestActionResult.Handled;
        }

        GameSituationReport refreshedSituation = await situationProvider.CaptureSituationAsync(
            currentAgentId,
            cancellationToken).ConfigureAwait(false);
        if (refreshedSituation.Presence != GamePresenceState.Connected ||
            refreshedSituation.Authentication != GameAuthenticationState.Authenticated ||
            refreshedSituation.Synchronization != GameSynchronizationState.Healthy ||
            refreshedSituation.Snapshot is not { } refreshedSnapshot ||
            !GameSnapshotValidation.IsValid(refreshedSnapshot) ||
            refreshedSnapshot.CurrentSector != quote.Sector ||
            refreshedSnapshot.ObservedAt > DateTimeOffset.UtcNow ||
            DateTimeOffset.UtcNow - refreshedSnapshot.ObservedAt > MaximumSnapshotAge)
        {
            await gameScriptRunner.CloseRefurbMenuAsync(
                currentAgentId,
                quote.PortClass,
                cancellationToken).ConfigureAwait(false);
            sessionState.RefurbMenuStage = AutonomousRefurbMenuStage.Inactive;
            sessionState.RefurbQuote = null;
            statusStore?.SetActivity(
                currentAgentId,
                "Closed Refurb because the sector or synchronized ship snapshot changed during model reasoning",
                refreshedSituation);
            return AutonomousTestActionResult.WaitingForFreshPrompt;
        }

        snapshot = refreshedSnapshot;
        if (snapshot.SynchronizedCredits is not { } credits ||
            sessionState.RefurbUpgradesAttemptedAtCurrentSector.Contains(upgrade.Value) ||
            AutonomousRefurbPurchasePolicy.GetMaximumPlannedQuantity(
                quote,
                upgrade.Value,
                credits) <= 0 ||
            upgrade == AutonomousRefurbUpgrade.Shields && snapshot.Shields is null ||
            upgrade == AutonomousRefurbUpgrade.CargoHolds && snapshot.HoldsTotal is null)
        {
            throw new InvalidOperationException(
                $"The selected {upgrade} purchase no longer meets the verified quote, balance, or status prerequisites.");
        }

        await gameScriptRunner.ExecuteRefurbUpgradeAsync(
            currentAgentId,
            upgrade.Value,
            quote,
            credits,
            cancellationToken).ConfigureAwait(false);
        sessionState.RefurbUpgradesAttemptedAtCurrentSector.Add(upgrade.Value);
        sessionState.RefurbMenuStage = AutonomousRefurbMenuStage.Inactive;
        sessionState.RefurbQuote = null;
        sessionState.PendingRefurbVerification = new PendingRefurbVerification(
            snapshot,
            upgrade.Value,
            DateTimeOffset.UtcNow);
        await commandSender.SendAsync(
            currentAgentId,
            new GameCommandRequest(GameCommand.RequestShipInformation),
            cancellationToken).ConfigureAwait(false);
        statusStore?.SetActivity(
            currentAgentId,
            $"Refreshing ship status to verify {upgrade} upgrade",
            refreshedSituation);
        return AutonomousTestActionResult.StatusRefreshSent;
    }

    private async Task<AutonomousTestActionResult> HandleStarDockShopperStageAsync(
        string currentAgentId,
        AutonomousTestSessionState sessionState,
        GameSituationReport situation,
        CancellationToken cancellationToken)
    {
        if (situation.Presence != GamePresenceState.Connected ||
            situation.Authentication != GameAuthenticationState.Authenticated)
        {
            statusStore?.SetActivity(
                currentAgentId,
                "Waiting for the authenticated session while Dock Shopper is active",
                situation);
            return AutonomousTestActionResult.WaitingForFreshPrompt;
        }
        if (!allowGameActions || gameScriptRunner is null)
        {
            throw new InvalidOperationException(
                "The active Dock Shopper stage requires enabled game actions and an MTC script runner.");
        }

        if (sessionState.StarDockShopperStage == StarDockShopperStage.AwaitingCompletion)
        {
            if (await gameScriptRunner.IsStarDockShopperRunningAsync(
                    currentAgentId,
                    cancellationToken).ConfigureAwait(false))
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "Dock Shopper is running; waiting for its result",
                    situation);
                return AutonomousTestActionResult.ScriptModeRunning;
            }
            if (!IsCommandPromptEligible(situation))
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "Dock Shopper stopped; waiting for a fresh Command prompt before verification",
                    situation);
                return AutonomousTestActionResult.WaitingForFreshPrompt;
            }
            if (sessionState.PendingStarDockShopper is not { } completedShopper)
            {
                throw new InvalidOperationException(
                    "Dock Shopper completed without a recorded starting snapshot.");
            }

            sessionState.PendingScannerVerification = new PendingScannerVerification(
                completedShopper.StartingSnapshot,
                completedShopper.Sector,
                completedShopper.Target,
                DateTimeOffset.UtcNow);
            sessionState.PendingStarDockShopper = null;
            sessionState.ActiveScriptObjective = null;
            sessionState.StarDockShopperStage = StarDockShopperStage.Inactive;
            sessionState.StarDockShopperRunningObserved = false;
            await commandSender.SendAsync(
                currentAgentId,
                new GameCommandRequest(GameCommand.RequestShipInformation),
                cancellationToken).ConfigureAwait(false);
            sessionState.PendingHoloScannerStatusRefreshSentAt = DateTimeOffset.UtcNow;
            statusStore?.RecordDecision(
                currentAgentId,
                $"Dock Shopper ended; refreshing ship status to verify {GetScannerDisplayName(completedShopper.Target)}",
                "Dock Shopper scanner verification",
                $"The script stopped; a fresh ship snapshot is required to verify {GetScannerDisplayName(completedShopper.Target)} ownership before scanner-dependent play.",
                situation);
            return AutonomousTestActionResult.StatusRefreshSent;
        }

        if (!await gameScriptRunner.IsStarDockShopperRunningAsync(
                currentAgentId,
                cancellationToken).ConfigureAwait(false))
        {
            if (sessionState.PendingStarDockShopper is { } startingShopper &&
                !sessionState.StarDockShopperRunningObserved &&
                DateTimeOffset.UtcNow - startingShopper.StartedAt < DockShopperStartupGracePeriod)
            {
                statusStore?.SetActivity(
                    currentAgentId,
                    "Waiting for MTC to report the Dock Shopper as running",
                    situation);
                return AutonomousTestActionResult.ScriptModeRunning;
            }

            sessionState.StarDockShopperStage = StarDockShopperStage.AwaitingCompletion;
            if (sessionState.StarDockShopperRunningObserved)
            {
                return await HandleStarDockShopperStageAsync(
                    currentAgentId,
                    sessionState,
                    situation,
                    cancellationToken).ConfigureAwait(false);
            }
            statusStore?.SetActivity(
                currentAgentId,
                "Dock Shopper stopped before the next menu action; checking its result",
                situation);
            return AutonomousTestActionResult.ScriptModeRunning;
        }

        if (sessionState.PendingStarDockShopper is null)
        {
            throw new InvalidOperationException(
                "Dock Shopper is active without a recorded starting snapshot.");
        }

        statusStore?.SetActivity(
            currentAgentId,
            "Dock Shopper is running; waiting for its next observed script prompt",
            situation);
        return AutonomousTestActionResult.ScriptModeRunning;
    }

    private async Task<AutonomousTestActionResult> HandleScriptInputPromptAsync(
        string currentAgentId,
        AutonomousTestSessionState sessionState,
        GameSituationReport situation,
        CancellationToken cancellationToken)
    {
        GameScriptInputPrompt prompt = situation.ScriptInputPrompt!;
        if (!allowGameActions || gameScriptRunner is null)
        {
            statusStore?.SetActivity(
                currentAgentId,
                "Script prompt observed; waiting for approved game actions and a script runner",
                situation);
            return AutonomousTestActionResult.WaitingForFreshPrompt;
        }
        if (!ScriptInputPromptPolicy.IsApprovedScript(prompt.ScriptName))
        {
            statusStore?.SetActivity(
                currentAgentId,
                "Unapproved script prompt observed; no response was sent",
                situation);
            return AutonomousTestActionResult.PromptIgnored;
        }
        if (situation.Presence != GamePresenceState.Connected ||
            situation.Authentication != GameAuthenticationState.Authenticated ||
            prompt.ObservedAt > DateTimeOffset.UtcNow ||
            DateTimeOffset.UtcNow - prompt.ObservedAt > MaximumCommandPromptAge)
        {
            statusStore?.SetActivity(
                currentAgentId,
                "Waiting for a fresh authenticated script input prompt",
                situation);
            return AutonomousTestActionResult.WaitingForFreshPrompt;
        }
        if (sessionState.LastEvaluatedScriptPromptAt == prompt.ObservedAt)
        {
            return AutonomousTestActionResult.ScriptModeRunning;
        }
        if (!await gameScriptRunner.IsApprovedScriptRunningAsync(
                currentAgentId,
                prompt.ScriptName,
                cancellationToken).ConfigureAwait(false))
        {
            sessionState.LastEvaluatedScriptPromptAt = prompt.ObservedAt;
            statusStore?.SetActivity(
                currentAgentId,
                "Script prompt is not from a verified running allowlisted script",
                situation);
            return AutonomousTestActionResult.PromptIgnored;
        }
        if (Path.GetFileName(prompt.ScriptName).Equals(
                "ls-dock-shopper-v33.ts",
                StringComparison.OrdinalIgnoreCase))
        {
            sessionState.StarDockShopperRunningObserved = true;
        }

        AutonomousActionObjective? activeObjective = sessionState.ActiveScriptObjective;
        if (activeObjective is null &&
            actionObjectiveProvider is not null &&
            situation.Synchronization == GameSynchronizationState.Healthy &&
            situation.Snapshot is { } snapshot &&
            GameSnapshotValidation.IsValid(snapshot))
        {
            activeObjective = await actionObjectiveProvider.GetOrCreateActiveObjectiveAsync(
                currentAgentId,
                situation,
                cancellationToken).ConfigureAwait(false);
            sessionState.ActiveScriptObjective = activeObjective;
        }
        var context = new AutonomousTestActionContext(situation, [])
        {
            GameActionsEnabled = true,
            ActiveObjective = activeObjective,
            RecentReactiveSignals = await GetRecentReactiveSignalsAsync(
                currentAgentId,
                cancellationToken).ConfigureAwait(false),
            StarDockShopperStage = sessionState.StarDockShopperStage,
            ScannerTarget = sessionState.PendingStarDockShopper?.Target,
            WorldTradeModeRunning = Path.GetFileName(prompt.ScriptName).Equals(
                "2-worldtrade.ts",
                StringComparison.OrdinalIgnoreCase)
        };
        AutonomousTestActionDecision decision = await DecideWithTelemetryAsync(
            currentAgentId,
            actionProvider,
            context,
            cancellationToken).ConfigureAwait(false);
        sessionState.LastEvaluatedScriptPromptAt = prompt.ObservedAt;

        if (decision.Action == AutonomousTestAction.None)
        {
            statusStore?.SetActivity(
                currentAgentId,
                "Waiting for a safely supported Pilot response to the script prompt",
                situation);
            return AutonomousTestActionResult.ScriptModeRunning;
        }
        if (decision.Action != AutonomousTestAction.RespondToScriptInputPrompt ||
            decision.TargetSector != 0 ||
            !ScriptInputPromptPolicy.IsSafeResponse(prompt, decision.ScriptInputResponse) ||
            !IsExpectedScannerSelectionResponse(
                prompt,
                sessionState.PendingStarDockShopper?.Target,
                decision.ScriptInputResponse))
        {
            statusStore?.SetActivity(
                currentAgentId,
                "Blocked an unsupported script prompt response",
                situation);
            logger.LogWarning(
                "Blocked script prompt response action {Action} for agent {AgentId} and script {ScriptName}.",
                decision.Action,
                currentAgentId,
                prompt.ScriptName);
            return AutonomousTestActionResult.ScriptModeRunning;
        }

        await gameScriptRunner.SendScriptInputResponseAsync(
            currentAgentId,
            prompt,
            decision.ScriptInputResponse,
            cancellationToken).ConfigureAwait(false);
        statusStore?.RecordDecision(
            currentAgentId,
            "Approved script prompt answered",
            decision.Action.ToString(),
            decision.Rationale,
            situation);
        logger.LogInformation(
            "Sent a validated response to the observed prompt for approved script {ScriptName}, agent {AgentId}.",
            prompt.ScriptName,
            currentAgentId);
        return AutonomousTestActionResult.Handled;
    }

    private async Task<IReadOnlyList<ReactivePilotSignal>> GetRecentReactiveSignalsAsync(
        string currentAgentId,
        CancellationToken cancellationToken)
    {
        if (reactiveEventStore is null)
        {
            return Array.Empty<ReactivePilotSignal>();
        }

        DateTimeOffset now = DateTimeOffset.UtcNow;
        var events = await reactiveEventStore.GetRecentAsync(
            currentAgentId,
            limit: 20,
            cancellationToken).ConfigureAwait(false);
        return events
            .Where(item =>
                item.ProcessedAt <= now &&
                now - item.ProcessedAt <= TimeSpan.FromMinutes(15))
            .Take(5)
            .Select(item => new ReactivePilotSignal(
                item.ProcessedAt,
                item.Event.Type,
                item.Event.Source,
                item.Event.SectorId,
                item.Decision.Category,
                item.Decision.Urgency,
                item.Decision.Confidence,
                item.Decision.Summary,
                item.Decision.ActionIntents))
            .ToArray();
    }

    private static bool ShouldRetryOnPresenceHeartbeat(
        AutonomousTestActionResult actionResult,
        AutonomousTestSessionState sessionState) =>
        actionResult is
            AutonomousTestActionResult.Retryable or
            AutonomousTestActionResult.WaitingForFreshPrompt or
            AutonomousTestActionResult.StatusRefreshSent or
            AutonomousTestActionResult.StatusRefreshPending ||
        sessionState.PendingMovementTarget is not null ||
        sessionState.PendingShipStatusRefreshSentAt is not null ||
        sessionState.PendingPortTrade is not null ||
        sessionState.PendingScannerVerification is not null ||
        sessionState.StarDockShopperStage != StarDockShopperStage.Inactive ||
        sessionState.WorldTradeModeStarted;

    private static bool IsStopInSectorPromptEligible(GameSituationReport situation) =>
        situation.Presence == GamePresenceState.Connected &&
        situation.Authentication == GameAuthenticationState.Authenticated &&
        string.Equals(situation.CurrentPrompt, "StopInSector", StringComparison.OrdinalIgnoreCase) &&
        situation.CurrentPromptObservedAt is { } promptAt &&
        promptAt <= DateTimeOffset.UtcNow &&
        DateTimeOffset.UtcNow - promptAt <= MaximumCommandPromptAge;

    private static bool IsAutopilotConfirmationPromptEligible(GameSituationReport situation) =>
        situation.Presence == GamePresenceState.Connected &&
        situation.Authentication == GameAuthenticationState.Authenticated &&
        string.Equals(situation.CurrentPrompt, "AutopilotConfirmation", StringComparison.OrdinalIgnoreCase) &&
        situation.CurrentPromptObservedAt is { } promptAt &&
        promptAt <= DateTimeOffset.UtcNow &&
        DateTimeOffset.UtcNow - promptAt <= MaximumCommandPromptAge;

    private bool HasRemainingActionBudget(int promptsHandled) =>
        maximumActionsPerSession == UnlimitedActionsPerSession ||
        promptsHandled < maximumActionsPerSession;

    private static bool IsMovementSituationEligible(GameSituationReport situation) =>
        IsCommandPromptEligible(situation) &&
        situation.Synchronization == GameSynchronizationState.Healthy &&
        situation.Snapshot is { } snapshot &&
        GameSnapshotValidation.IsValid(snapshot) &&
        snapshot.CurrentSector is > 0 &&
        snapshot.Turns is > 0 &&
        snapshot.ObservedAt <= DateTimeOffset.UtcNow &&
        DateTimeOffset.UtcNow - snapshot.ObservedAt <= MaximumSnapshotAge;

    private static bool NeedsFuelOreForDockShopper(
        GameSnapshot? snapshot,
        int? starDockSector,
        ScannerPurchaseTarget? scannerTarget) =>
        starDockSector is > 0 &&
        scannerTarget is not null &&
        snapshot is
        {
            SynchronizedCredits: >= AutonomousTradePolicy.MinimumScannerUpgradeCredits,
            HoldsTotal: > 0
        } fuelNeedSnapshot &&
        fuelNeedSnapshot.FuelOre != fuelNeedSnapshot.HoldsTotal;

    private static ScannerPurchaseTarget? GetScannerPurchaseTarget(
        GameSnapshot? snapshot,
        GameSnapshot? densityScannerStartingSnapshot)
    {
        if (snapshot?.HoloScannerInstalled is true)
        {
            return null;
        }

        if (snapshot?.DensityScannerInstalled is false)
        {
            return ScannerPurchaseTarget.Density;
        }

        if (snapshot is not null &&
            IsHoloScannerPurchaseReady(snapshot, densityScannerStartingSnapshot))
        {
            return ScannerPurchaseTarget.Holo;
        }

        return null;
    }

    private static bool IsHoloScannerPurchaseReady(
        GameSnapshot snapshot,
        GameSnapshot? densityScannerStartingSnapshot) =>
        snapshot.DensityScannerInstalled is true &&
        snapshot.HoloScannerInstalled is false &&
        densityScannerStartingSnapshot is not null &&
        snapshot.Ship is { } currentShip &&
        densityScannerStartingSnapshot.Ship is { } startingShip &&
        currentShip.Fighters > startingShip.Fighters &&
        snapshot.Shields is { } shields &&
        densityScannerStartingSnapshot.Shields is { } startingShields &&
        shields > startingShields;

    private static bool AreScannerStatusesKnown(GameSnapshot snapshot) =>
        snapshot.HoloScannerInstalled is not null &&
        snapshot.DensityScannerInstalled is not null;

    private static bool IsScannerTargetAbsent(
        GameSnapshot snapshot,
        ScannerPurchaseTarget? target) =>
        target switch
        {
            ScannerPurchaseTarget.Density =>
                snapshot.DensityScannerInstalled is false &&
                snapshot.HoloScannerInstalled is not true,
            ScannerPurchaseTarget.Holo =>
                snapshot.DensityScannerInstalled is true &&
                snapshot.HoloScannerInstalled is false,
            _ => false
        };

    private static bool IsEscapePodOrSingleHoldShip(GameSnapshot? snapshot) =>
        snapshot?.HoldsTotal is <= 1 ||
        snapshot?.Ship?.ShipType.Contains("escape pod", StringComparison.OrdinalIgnoreCase) == true;

    private static bool IsScannerInstalled(
        GameSnapshot snapshot,
        ScannerPurchaseTarget target) =>
        target switch
        {
            ScannerPurchaseTarget.Density => snapshot.DensityScannerInstalled is true,
            ScannerPurchaseTarget.Holo => snapshot.HoloScannerInstalled is true,
            _ => false
        };

    private static bool IsScannerlessDestinationAllowed(
        int targetSector,
        int? starDockSector,
        IReadOnlyList<AdjacentSectorKnowledge> sectorKnowledge) =>
        targetSector != KnownHighRiskSector &&
        (targetSector is >= 1 and <= 10 ||
         targetSector == starDockSector ||
         sectorKnowledge.Any(knowledge =>
             knowledge.SectorId == targetSector &&
             knowledge.Exploration == ExplorationStatus.Explored));

    private static string GetScannerDisplayName(ScannerPurchaseTarget target) =>
        target == ScannerPurchaseTarget.Density ? "Density Scanner" : "Holo Scanner";

    private static bool IsExpectedScannerSelectionResponse(
        GameScriptInputPrompt prompt,
        ScannerPurchaseTarget? target,
        string response)
    {
        if (!prompt.Text.Contains("scanner type", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        char expectedResponse = target switch
        {
            ScannerPurchaseTarget.Density => 'D',
            ScannerPurchaseTarget.Holo => 'Y',
            _ => '\0'
        };
        return response.Length == 1 &&
            char.ToUpperInvariant(response[0]) == expectedResponse;
    }

    private static bool NeedsShipStatusRefresh(GameSituationReport situation) =>
        situation.Synchronization != GameSynchronizationState.Healthy ||
        situation.Snapshot is not { } snapshot ||
        !GameSnapshotValidation.IsValid(snapshot) ||
        snapshot.ObservedAt > DateTimeOffset.UtcNow ||
        DateTimeOffset.UtcNow - snapshot.ObservedAt > MaximumSnapshotAge;

    private static bool ObjectiveTargetsStarDock(AutonomousActionObjective objective) =>
        objective.Goal.Contains("StarDock", StringComparison.OrdinalIgnoreCase) ||
        objective.Rationale.Contains("StarDock", StringComparison.OrdinalIgnoreCase);

    private enum AutonomousTestActionResult
    {
        Handled,
        PromptIgnored,
        Retryable,
        ScriptModeRunning,
        StatusRefreshSent,
        StatusRefreshPending,
        WaitingForFreshPrompt
    }

    private enum FedSpaceFastStartPhase
    {
        NotStarted,
        Sweeping,
        RefreshingCim,
        TradingPairs,
        Complete,
        Skipped
    }

    private sealed record PendingPortTrade(
        int Sector,
        AutonomousPortTradeOpportunity Opportunity,
        GameSnapshot StartingSnapshot,
        DateTimeOffset SentAt);

    private sealed record PendingScannerVerification(
        GameSnapshot StartingSnapshot,
        int Sector,
        ScannerPurchaseTarget Target,
        DateTimeOffset SentAt);

    private sealed record PendingStarDockShopper(
        GameSnapshot StartingSnapshot,
        int Sector,
        ScannerPurchaseTarget Target,
        DateTimeOffset StartedAt);

    private sealed record PendingRefurbVerification(
        GameSnapshot StartingSnapshot,
        AutonomousRefurbUpgrade Upgrade,
        DateTimeOffset SentAt);

    private sealed class AutonomousTestSessionState
    {
        public int? PendingMovementTarget { get; set; }
        public int? PendingMovementOrigin { get; set; }
        public DateTimeOffset? PendingMovementSentAt { get; set; }
        public DateTimeOffset? PendingShipStatusRefreshSentAt { get; set; }
        public bool MovementDisabled { get; set; }
        public int? PreviousSector { get; set; }
        public HashSet<int> DisplayedSectors { get; } = [];
        public HashSet<int> PortTradeAttemptedSectors { get; } = [];
        public PendingPortTrade? PendingPortTrade { get; set; }
        public PendingScannerVerification? PendingScannerVerification { get; set; }
        public PendingStarDockShopper? PendingStarDockShopper { get; set; }
        public GameSnapshot? DensityScannerStartingSnapshot { get; set; }
        public AutonomousActionObjective? ActiveScriptObjective { get; set; }
        public PendingRefurbVerification? PendingRefurbVerification { get; set; }
        public StarDockShopperStage StarDockShopperStage { get; set; }
        public AutonomousRefurbMenuStage RefurbMenuStage { get; set; }
        public AutonomousRefurbQuote? RefurbQuote { get; set; }
        public int? RefurbAttemptedSector { get; set; }
        public HashSet<AutonomousRefurbUpgrade> RefurbUpgradesAttemptedAtCurrentSector { get; } = [];
        public int? PendingTradePartnerSector { get; set; }
        public int? PlannedTradeSellerSector { get; set; }
        public DateTimeOffset? LastPortCimRefreshAttemptAt { get; set; }
        public string? LastPortCimRefreshError { get; set; }
        public DateTimeOffset? LastStarDockDiscoverySentAt { get; set; }
        public DateTimeOffset? LastHandledStopPromptAt { get; set; }
        public DateTimeOffset? LastHandledAutopilotPromptAt { get; set; }
        public DateTimeOffset? LastEvaluatedScriptPromptAt { get; set; }
        public int? LastWorldTradeAttemptSector { get; set; }
        public int? LastStarDockShopperAttemptSector { get; set; }
        public int? LastStarDockFighterDeploymentAttemptSector { get; set; }
        public DateTimeOffset? LastHoloScannerStatusRefreshSentAt { get; set; }
        public DateTimeOffset? PendingHoloScannerStatusRefreshSentAt { get; set; }
        public bool WorldTradeModeStarted { get; set; }
        public bool StarDockShopperRunningObserved { get; set; }
        public FedSpaceFastStartPhase FedSpaceFastStartPhase { get; set; }
        public bool FedSpaceFastStartSkipped { get; set; }
        public HashSet<int> FedSpaceVisitedSectors { get; } = [];
        public DateTimeOffset? LastFedSpaceCimRefreshAttemptAt { get; set; }

        public void ClearPendingMovement()
        {
            PendingMovementTarget = null;
            PendingMovementOrigin = null;
            PendingMovementSentAt = null;
        }

        public void Reset()
        {
            ClearPendingMovement();
            PendingShipStatusRefreshSentAt = null;
            MovementDisabled = false;
            PreviousSector = null;
            DisplayedSectors.Clear();
            PortTradeAttemptedSectors.Clear();
            PendingPortTrade = null;
            PendingScannerVerification = null;
            PendingStarDockShopper = null;
            DensityScannerStartingSnapshot = null;
            ActiveScriptObjective = null;
            PendingRefurbVerification = null;
            StarDockShopperStage = StarDockShopperStage.Inactive;
            RefurbMenuStage = AutonomousRefurbMenuStage.Inactive;
            RefurbQuote = null;
            RefurbAttemptedSector = null;
            RefurbUpgradesAttemptedAtCurrentSector.Clear();
            PendingTradePartnerSector = null;
            PlannedTradeSellerSector = null;
            LastPortCimRefreshAttemptAt = null;
            LastPortCimRefreshError = null;
            LastStarDockDiscoverySentAt = null;
            LastHandledStopPromptAt = null;
            LastHandledAutopilotPromptAt = null;
            LastEvaluatedScriptPromptAt = null;
            LastWorldTradeAttemptSector = null;
            LastStarDockShopperAttemptSector = null;
            LastStarDockFighterDeploymentAttemptSector = null;
            LastHoloScannerStatusRefreshSentAt = null;
            PendingHoloScannerStatusRefreshSentAt = null;
            WorldTradeModeStarted = false;
            StarDockShopperRunningObserved = false;
            FedSpaceFastStartPhase = FedSpaceFastStartPhase.NotStarted;
            FedSpaceFastStartSkipped = false;
            FedSpaceVisitedSectors.Clear();
            LastFedSpaceCimRefreshAttemptAt = null;
        }
    }
}
