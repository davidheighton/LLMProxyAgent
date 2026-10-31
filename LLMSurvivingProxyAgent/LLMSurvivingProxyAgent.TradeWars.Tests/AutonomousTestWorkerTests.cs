using System.Collections.Concurrent;
using System.Text;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Runtime;
using Microsoft.Extensions.Logging.Abstractions;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class AutonomousTestWorkerTests
{
    [Fact]
    public async Task ExitsFreshNavPointSelectionWithoutCallingTheActionModel()
    {
        var eventHub = new ProxyGameEventHub();
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "No action."));
        var commandSender = new RecordingCommandSender();
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(CreateMovementEligibleSituation() with
            {
                CurrentPrompt = "NavPointSelection",
                CurrentPromptObservedAt = observedAt
            }),
            actionProvider,
            commandSender,
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    observedAt.AddMilliseconds(1),
                    "ServerPrompt",
                    PromptSurface: "NavPointSelection")));

            await commandSender.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(
                GameCommand.ExitNavPointSelection,
                Assert.Single(commandSender.Commands).Command);
            Assert.Equal(0, actionProvider.CallCount);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task PassesRecentReactiveAssessmentsToPilotContext()
    {
        var eventHub = new ProxyGameEventHub();
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        var reactiveEvent = new ReactiveEvent(
            "reactive-1",
            "meridian",
            ReactiveEventType.TraderEnteredSector,
            ReactiveEventSource.Cim,
            observedAt,
            observedAt,
            552,
            "Nearby trader",
            null,
            null,
            null,
            null);
        var reactiveDecision = new ReactiveDecision(
            ReactiveMode.Informational,
            "test-opportunity",
            "A nearby trade opportunity was classified.",
            false,
            [new ReactiveActionIntent(ReactiveActionKind.RaiseAlert, "Check only against verified port data.")],
            "Audit signal only.")
        {
            Category = ReactiveClassificationCategory.Opportunity,
            Urgency = ReactiveUrgency.Routine,
            Confidence = 0.9m
        };
        var reactiveStore = new RecordingReactiveEventStore(
        [
            new ReactiveEventRecord(reactiveEvent, reactiveDecision, observedAt)
        ]);
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "No action."));
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(CreateMovementEligibleSituation() with
            {
                CurrentPromptObservedAt = observedAt
            }),
            actionProvider,
            new RecordingCommandSender(),
            allowGameActions: false,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            reactiveEventStore: reactiveStore);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));

            await WaitForActionCallCountAsync(actionProvider, 1);

            ReactivePilotSignal signal = Assert.Single(actionProvider.ActionContext!.RecentReactiveSignals);
            Assert.Equal(ReactiveEventType.TraderEnteredSector, signal.Type);
            Assert.Equal(ReactiveClassificationCategory.Opportunity, signal.Category);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DoesNotRecoverStalePromptFromHeartbeatWithoutPromptObservation()
    {
        var eventHub = new ProxyGameEventHub();
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "No action."));
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(CreateMovementEligibleSituation()),
            actionProvider,
            new RecordingCommandSender(),
            allowGameActions: false,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(now.AddMinutes(-1), "ServerPrompt", PromptSurface: "Command")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(now, "PresenceHeartbeat", PromptSurface: "Command")));

            await Task.Delay(100);

            Assert.Equal(0, actionProvider.CallCount);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RecoversFromStalePromptWhenHeartbeatConfirmsItIsStillCurrent()
    {
        var eventHub = new ProxyGameEventHub();
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "No action."));
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(CreateMovementEligibleSituation()),
            actionProvider,
            new RecordingCommandSender(),
            allowGameActions: false,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(now.AddMinutes(-1), "ServerPrompt", PromptSurface: "Command")));
            var heartbeatMetadata = new Dictionary<string, string>
            {
                ["promptObservedAt"] = now.AddMinutes(-1).ToString("O"),
                ["promptIsCurrent"] = "true"
            };
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    now,
                    "PresenceHeartbeat",
                    PromptSurface: "Command",
                    Metadata: heartbeatMetadata)));

            await WaitForActionCallCountAsync(actionProvider, 1);

            Assert.Equal(1, actionProvider.CallCount);
            Assert.Equal("Command", actionProvider.ActionContext!.Situation.CurrentPrompt);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DoesNotOfferPreviouslyExploredHighRiskSectorToScannerlessPilot()
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        var situationProvider = new RecordingSituationProvider(
            new GameSituationReport(
                observedAt,
                GamePresenceState.Connected,
                GameAuthenticationState.Authenticated,
                GameSynchronizationState.Healthy,
                "Command",
                observedAt,
                new GameSnapshot(551, 64, 1, null, observedAt)
                {
                    DensityScannerInstalled = false,
                    HoloScannerInstalled = false
                },
                []));
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                "Return to StarDock.",
                99));
        var commandSender = new RecordingCommandSender();
        var eventHub = new ProxyGameEventHub();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            new RecordingMoveTargetProvider(
                [99],
                [new AdjacentSectorKnowledge(99, ExplorationStatus.Explored, true, false)]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            actionObjectiveProvider: new RecordingActionObjectiveProvider(
                new AutonomousActionObjective("Return to StarDock", "Use the known route.")),
            gameWorldProvider: new FixedGameWorldProvider(
                551,
                nextSector: null,
                isFederationSpace: false),
            moveApprovalGate: new ImmediateMoveApprovalGate());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));
            await WaitForActionCallCountAsync(actionProvider, 1);

            Assert.DoesNotContain(99, actionProvider.ActionContext!.AvailableAdjacentSectors);
            Assert.Empty(commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RefreshesStaleSnapshotFromConfirmedCurrentPromptHeartbeat()
    {
        DateTimeOffset promptAt = DateTimeOffset.UtcNow.AddSeconds(-2);
        GameSituationReport baseSituation = CreateMovementEligibleSituation();
        GameSituationReport freshSituation = baseSituation with
        {
            CapturedAt = promptAt,
            CurrentPromptObservedAt = promptAt,
            Snapshot = baseSituation.Snapshot! with { ObservedAt = promptAt }
        };
        DateTimeOffset heartbeatAt = DateTimeOffset.UtcNow;
        DateTimeOffset staleSnapshotAt = heartbeatAt.AddSeconds(-31);
        GameSituationReport staleSituation = freshSituation with
        {
            CapturedAt = heartbeatAt,
            CurrentPromptObservedAt = heartbeatAt,
            Snapshot = freshSituation.Snapshot! with
            {
                ObservedAt = staleSnapshotAt,
                Ship = freshSituation.Snapshot.Ship is { } ship
                    ? ship with { ObservedAt = staleSnapshotAt }
                    : null
            }
        };
        var eventHub = new ProxyGameEventHub();
        var situationProvider = new SequenceSituationProvider(freshSituation, staleSituation);
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "Wait for the next game update."));
        var commandSender = new RecordingCommandSender();
        var statusStore = new AutonomousTestWorkerStatusStore();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            allowGameActions: false,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            statusStore: statusStore);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt.AddMilliseconds(-1), "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt, "ServerPrompt", PromptSurface: "Command")));
            await WaitForActionCallCountAsync(actionProvider, 1);
            statusStore.SetActivity("meridian", "Snapshot aged", staleSituation);

            var heartbeatMetadata = new Dictionary<string, string>
            {
                ["promptObservedAt"] = promptAt.ToString("O"),
                ["promptIsCurrent"] = "true"
            };
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    heartbeatAt,
                    "PresenceHeartbeat",
                    PromptSurface: "Command",
                    Metadata: heartbeatMetadata)));

            await commandSender.StatusRequestSent.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Contains(
                commandSender.Commands,
                request => request.Command == GameCommand.RequestShipInformation);
            Assert.Equal(1, actionProvider.CallCount);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ProcessesFreshCommandPromptFromHeartbeatWhenWorkerAttachesMidSession()
    {
        var eventHub = new ProxyGameEventHub();
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "No action."));
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(CreateMovementEligibleSituation()),
            actionProvider,
            new RecordingCommandSender(),
            allowGameActions: false,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset promptAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt.AddMilliseconds(-1), "Connected")));
            var promptMetadata = new Dictionary<string, string>
            {
                ["promptObservedAt"] = promptAt.ToString("O")
            };
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    promptAt,
                    "PresenceHeartbeat",
                    PromptSurface: "Command",
                    Metadata: promptMetadata)));

            await WaitForActionCallCountAsync(actionProvider, 1);
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    promptAt.AddSeconds(1),
                    "PresenceHeartbeat",
                    PromptSurface: "Command",
                    Metadata: promptMetadata)));
            await Task.Delay(100);

            Assert.Equal(1, actionProvider.CallCount);
            Assert.Equal("Command", actionProvider.ActionContext!.Situation.CurrentPrompt);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DoesNotRecoverStalePromptFromFreshHeartbeatTimestamp()
    {
        var eventHub = new ProxyGameEventHub();
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "No action."));
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(CreateMovementEligibleSituation()),
            actionProvider,
            new RecordingCommandSender(),
            allowGameActions: false,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset stalePromptAt = DateTimeOffset.UtcNow.AddMinutes(-1);
            DateTimeOffset heartbeatAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(stalePromptAt.AddMilliseconds(-1), "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(stalePromptAt, "ServerPrompt", PromptSurface: "Command")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    heartbeatAt,
                    "PresenceHeartbeat",
                    PromptSurface: "Command",
                    Metadata: new Dictionary<string, string>
                    {
                        ["promptObservedAt"] = stalePromptAt.ToString("O")
                    })));

            await Task.Delay(100);

            Assert.Equal(0, actionProvider.CallCount);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task WatchdogRequestsStrategicRecoveryAfterRepeatedStagnantCommandCycles()
    {
        var eventHub = new ProxyGameEventHub();
        DateTimeOffset startedAt = DateTimeOffset.UtcNow.AddSeconds(-5);
        GameSituationReport[] situations = Enumerable.Range(0, AutonomousActionWatchdog.RequiredUnchangedCycles + 1)
            .Select(index =>
            {
                DateTimeOffset observedAt = startedAt.AddSeconds(index);
                return new GameSituationReport(
                    observedAt,
                    GamePresenceState.Connected,
                    GameAuthenticationState.Authenticated,
                    GameSynchronizationState.Healthy,
                    "Command",
                    observedAt,
                    new GameSnapshot(551, 25_000, 100, null, observedAt),
                    []);
            })
            .ToArray();
        var actionProvider = new SequenceActionProvider(
            Enumerable.Repeat(
                new AutonomousTestActionDecision(AutonomousTestAction.None, "No action."),
                situations.Length).ToArray());
        var objectiveProvider = new RecordingActionObjectiveProvider(
            new AutonomousActionObjective(
                "Learn verified trading behavior.",
                "Use observed game state to make progress toward the active objective."));
        var statusStore = new AutonomousTestWorkerStatusStore();
        using var worker = CreateWorker(
            eventHub,
            new SequenceSituationProvider(situations),
            actionProvider,
            new RecordingCommandSender(),
            moveTargetProvider: new RecordingMoveTargetProvider([550, 552]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            actionObjectiveProvider: objectiveProvider,
            statusStore: statusStore);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(startedAt, "Connected")));

            for (int promptIndex = 0; promptIndex < situations.Length; promptIndex++)
            {
                DateTimeOffset promptAt = situations[promptIndex].CurrentPromptObservedAt!.Value;
                eventHub.TryPublish(new ProxySessionEvent(
                    "meridian",
                    new ProxyGameEvent(promptAt, "ServerPrompt", PromptSurface: "Command")));
                await WaitForActionCallCountAsync(actionProvider, promptIndex + 1);
            }

            Assert.Contains(
                objectiveProvider.RequestedReasons,
                reason => reason.Contains("identify the blocking precondition", StringComparison.Ordinal));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public void PrefersEquipmentWhenTheAdjacentPortBuysIt()
    {
        Assert.Equal(
            TradeProduct.Equipment,
            PortTradeCompatibility.FindTradeProduct(
                sellerClass: 5,
                buyerClass: 4,
                preferredProduct: TradeProduct.Equipment));
    }

    [Fact]
    public void FallsBackToFuelOreWhenEquipmentIsNotCompatible()
    {
        Assert.Equal(
            TradeProduct.FuelOre,
            PortTradeCompatibility.FindTradeProduct(
                sellerClass: 5,
                buyerClass: 6,
                preferredProduct: TradeProduct.Equipment));
    }

    [Fact]
    public async Task RecoversAnUntrackedPortTradePromptOnlyOnceUntilCommandReturns()
    {
        var eventHub = new ProxyGameEventHub();
        DateTimeOffset connectedAt = DateTimeOffset.UtcNow;
        var situation = new GameSituationReport(
            connectedAt,
            GamePresenceState.Connected,
            GameAuthenticationState.Authenticated,
            GameSynchronizationState.Stale,
            "Command",
            connectedAt,
            new GameSnapshot(369, 300, 20, null, connectedAt),
            ["Synchronization health"]);
        var commandSender = new RecordingCommandSender();
        var actionProvider = new SequenceActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "No model action."));
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(situation),
            actionProvider,
            commandSender,
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(connectedAt, "Connected")));
            var firstPromptAt = connectedAt.AddMilliseconds(1);
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(firstPromptAt, "ServerPrompt", PromptSurface: "PortTradeQuantity")));
            await commandSender.PortTradeRecoveryRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));

            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    firstPromptAt.AddSeconds(5),
                    "ServerPrompt",
                    PromptSurface: "PortTradeQuantity")));
            await Task.Delay(100);
            Assert.Equal([firstPromptAt], commandSender.UntrackedPortTradePrompts);

            var commandAt = firstPromptAt.AddSeconds(10);
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(commandAt, "ServerPrompt", PromptSurface: "Command")));

            var nextPromptAt = commandAt.AddSeconds(1);
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(nextPromptAt, "ServerPrompt", PromptSurface: "PortTradeQuantity")));
            await commandSender.SecondPortTradeRecoveryRequested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal([firstPromptAt, nextPromptAt], commandSender.UntrackedPortTradePrompts);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StopsAtCurrentSectorOnFreshAuthenticatedStopPrompt()
    {
        var eventHub = new ProxyGameEventHub();
        var promptAt = DateTimeOffset.UtcNow;
        var situation = new GameSituationReport(
            promptAt,
            GamePresenceState.Connected,
            GameAuthenticationState.Authenticated,
            GameSynchronizationState.Stale,
            "StopInSector",
            promptAt,
            new GameSnapshot(261, 300, 20, null, promptAt),
            ["Synchronization health"]);
        var commandSender = new RecordingCommandSender();
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "No model action."));
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(situation),
            actionProvider,
            commandSender,
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    promptAt.AddMilliseconds(1),
                    "ServerPrompt",
                    PromptSurface: "Stop in this sector (Y,N,E,I,R,S,D,P,?) (?=Help) [N] ?")));

            await commandSender.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(
                [new GameCommandRequest(GameCommand.StopAtCurrentSector)],
                commandSender.Commands);
            Assert.Equal(0, actionProvider.CallCount);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DeclinesFreshAuthenticatedAutopilotPrompt()
    {
        var eventHub = new ProxyGameEventHub();
        var promptAt = DateTimeOffset.UtcNow;
        var situation = new GameSituationReport(
            promptAt,
            GamePresenceState.Connected,
            GameAuthenticationState.Authenticated,
            GameSynchronizationState.Stale,
            "AutopilotConfirmation",
            promptAt,
            new GameSnapshot(1000, 300, 20, null, promptAt),
            ["Synchronization health"]);
        var commandSender = new RecordingCommandSender();
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "No model action."));
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(situation),
            actionProvider,
            commandSender,
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    promptAt.AddMilliseconds(1),
                    "ServerPrompt",
                    PromptSurface: "Engage the Autopilot? (Y/N/Single step/Express) [Y]")));

            await commandSender.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(
                [new GameCommandRequest(GameCommand.DeclineAutopilotEngagement)],
                commandSender.Commands);
            Assert.Equal(0, actionProvider.CallCount);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DeclinesAutopilotPromptFromHeartbeatWhenPromptEventWasMissed()
    {
        var eventHub = new ProxyGameEventHub();
        DateTimeOffset heartbeatAt = DateTimeOffset.UtcNow;
        DateTimeOffset commandAt = heartbeatAt.AddMilliseconds(-100);
        DateTimeOffset autopilotAt = heartbeatAt;
        var situationProvider = new SequenceSituationProvider(
            CreateMovementEligibleSituation() with
            {
                Authentication = GameAuthenticationState.Unverified
            },
            new GameSituationReport(
                autopilotAt,
                GamePresenceState.Connected,
                GameAuthenticationState.Authenticated,
                GameSynchronizationState.Stale,
                "AutopilotConfirmation",
                autopilotAt,
                new GameSnapshot(1000, 300, 20, null, autopilotAt),
                ["Synchronization health"]));
        var commandSender = new RecordingCommandSender();
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "No model action."));
        var statusStore = new AutonomousTestWorkerStatusStore();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            statusStore: statusStore);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(commandAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(commandAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));

            await WaitForActivityAsync(statusStore, "Waiting for a fresh authenticated Command prompt");
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    heartbeatAt,
                    "PresenceHeartbeat",
                    PromptSurface: "AutopilotConfirmation")));

            Task completed = await Task.WhenAny(
                commandSender.Sent.Task,
                Task.Delay(TimeSpan.FromSeconds(5)));
            AutonomousTestWorkerStatus status = statusStore.GetStatus("meridian");
            Assert.True(
                completed == commandSender.Sent.Task,
                $"No command was sent after {situationProvider.CallCount} situation captures. Activity: {status.Activity}; prompt: {status.Situation?.CurrentPrompt}; authentication: {status.Situation?.Authentication}.");

            Assert.Equal(
                [new GameCommandRequest(GameCommand.DeclineAutopilotEngagement)],
                commandSender.Commands);
            Assert.Equal(0, actionProvider.CallCount);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ControllerStartsAndStopsTheWorkerOnDemand()
    {
        var eventHub = new ProxyGameEventHub();
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.RequestShipInformation,
                "Read the current ship status."));
        var commandSender = new RecordingCommandSender();
        await using var controller = new AutonomousTestWorkerController(
            modelName => CreateWorker(
                eventHub,
                new RecordingSituationProvider(CreateEligibleSituation()),
                actionProvider,
                commandSender,
                modelName: modelName));

        Assert.False(controller.IsRunning);
        await controller.StartAsync("qwen3:4b");
        Assert.True(controller.IsRunning);

        eventHub.TryPublish(new ProxySessionEvent(
            "meridian",
            new ProxyGameEvent(DateTimeOffset.UtcNow, "Connected")));
        eventHub.TryPublish(new ProxySessionEvent(
            "meridian",
            new ProxyGameEvent(DateTimeOffset.UtcNow, "ServerPrompt", PromptSurface: "Command")));
        await commandSender.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));

        await controller.StopAsync();
        Assert.False(controller.IsRunning);
        Assert.Equal(0, actionProvider.CallCount);
    }

    [Fact]
    public async Task StartupPromptReplayDoesNotResetOrRepeatLoginInput()
    {
        var eventHub = new ProxyGameEventHub();
        var statusStore = new AutonomousTestWorkerStatusStore();
        var loginInputSender = new RecordingLoginInputSender();
        using var worker = new AutonomousTestWorker(
            "meridian",
            "qwen3:8b",
            new Uri("http://localhost:11434/"),
            eventHub,
            new RecordingSituationProvider(CreateEligibleSituation()),
            new RecordingActionProvider(
                new AutonomousTestActionDecision(
                    AutonomousTestAction.None,
                    "Wait for a command prompt.")),
            new RecordingCommandSender(),
            NullLogger<AutonomousTestWorker>.Instance,
            statusStore: statusStore,
            loginCredentialProvider: new FixedLoginCredentialProvider(
                new AutonomousGameLoginCredentials("Meridian8", "D", "Secret42")),
            loginInputSender: loginInputSender);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset observedAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "ServerPrompt", PromptSurface: "BBSName")));
            await loginInputSender.InputSent.Task.WaitAsync(TimeSpan.FromSeconds(5));

            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt.AddMilliseconds(10), "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt.AddMilliseconds(20), "ServerPrompt", PromptSurface: "BBSName")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt.AddMilliseconds(30), "Disconnected")));

            await WaitForActivityAsync(statusStore, "Disconnected");

            Assert.Equal(["Meridian8\r"], loginInputSender.Inputs);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ReconnectedSessionResumesExistingAccountLogin()
    {
        var eventHub = new ProxyGameEventHub();
        var statusStore = new AutonomousTestWorkerStatusStore();
        var loginInputSender = new RecordingLoginInputSender();
        using var worker = new AutonomousTestWorker(
            "meridian",
            "qwen3:8b",
            new Uri("http://localhost:11434/"),
            eventHub,
            new RecordingSituationProvider(CreateEligibleSituation()),
            new RecordingActionProvider(
                new AutonomousTestActionDecision(
                    AutonomousTestAction.None,
                    "Wait for a command prompt.")),
            new RecordingCommandSender(),
            NullLogger<AutonomousTestWorker>.Instance,
            statusStore: statusStore,
            loginCredentialProvider: new FixedLoginCredentialProvider(
                new AutonomousGameLoginCredentials("Meridian8", "D", "Secret42")),
            loginInputSender: loginInputSender);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset observedAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    observedAt.AddMilliseconds(10),
                    "ServerPrompt",
                    PromptSurface: "BBSName")));
            await WaitForLoginInputCountAsync(loginInputSender, 1);

            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    observedAt.AddMilliseconds(20),
                    "Disconnected")));
            await WaitForActivityAsync(statusStore, "Disconnected");
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    observedAt.AddMilliseconds(30),
                    "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    observedAt.AddMilliseconds(40),
                    "ServerPrompt",
                    PromptSurface: "BBSName")));

            await WaitForLoginInputCountAsync(loginInputSender, 2);

            Assert.Equal(["Meridian8\r", "Meridian8\r"], loginInputSender.Inputs);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task LoginWaitStatusReportsClassifiedPromptWithoutRawTerminalText()
    {
        var eventHub = new ProxyGameEventHub();
        var statusStore = new AutonomousTestWorkerStatusStore();
        var loginInputSender = new RecordingLoginInputSender();
        using var worker = new AutonomousTestWorker(
            "meridian",
            "qwen3:8b",
            new Uri("http://localhost:11434/"),
            eventHub,
            new RecordingSituationProvider(CreateEligibleSituation()),
            new RecordingActionProvider(
                new AutonomousTestActionDecision(
                    AutonomousTestAction.None,
                    "Wait for a command prompt.")),
            new RecordingCommandSender(),
            NullLogger<AutonomousTestWorker>.Instance,
            statusStore: statusStore,
            loginCredentialProvider: new FixedLoginCredentialProvider(
                new AutonomousGameLoginCredentials("Meridian8", "D", "Secret42")),
            loginInputSender: loginInputSender);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset observedAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    observedAt.AddMilliseconds(10),
                    "ServerPrompt",
                    PromptSurface: "DailyLog")));

            await WaitForActivityAsync(
                statusStore,
                "Waiting for a recognized login prompt; observed surface: DailyLog");

            const string unrecognizedPrompt = "Unrecognized prompt text";
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    observedAt.AddSeconds(2),
                    "ServerPrompt",
                    PromptSurface: unrecognizedPrompt)));
            await WaitForActivityAsync(
                statusStore,
                $"Waiting for a recognized login prompt; observed surface: unclassified (raw text omitted; length={unrecognizedPrompt.Length})");
            Assert.DoesNotContain(unrecognizedPrompt, statusStore.GetStatus("meridian").Activity);
            Assert.Empty(loginInputSender.Inputs);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DeclinesNewCharacterPromptAndStopsBeforeGameplay()
    {
        var eventHub = new ProxyGameEventHub();
        var statusStore = new AutonomousTestWorkerStatusStore();
        var loginInputSender = new RecordingLoginInputSender();
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "No action."));
        var commandSender = new RecordingCommandSender();
        using var worker = new AutonomousTestWorker(
            "meridian",
            "qwen3:8b",
            new Uri("http://localhost:11434/"),
            eventHub,
            new RecordingSituationProvider(CreateEligibleSituation()),
            actionProvider,
            commandSender,
            NullLogger<AutonomousTestWorker>.Instance,
            statusStore: statusStore,
            loginCredentialProvider: new FixedLoginCredentialProvider(
                new AutonomousGameLoginCredentials("Meridian8", "D", "Secret42")),
            loginInputSender: loginInputSender);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset observedAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            foreach (var (offset, prompt) in new[]
                     {
                         (1, "BBSName"),
                         (2, "ServerRegistration"),
                         (3, "Pause"),
                         (4, "Menu"),
                         (5, "NewCharacter")
                     })
            {
                eventHub.TryPublish(new ProxySessionEvent(
                    "meridian",
                    new ProxyGameEvent(
                        observedAt.AddMilliseconds(offset),
                        "ServerPrompt",
                        PromptSurface: prompt)));
            }

            await WaitForActivityAsync(
                statusStore,
                "Login blocked: existing character not recognized; declined new-character creation");

            Assert.Equal(["Meridian8\r", "D", "\r", "T\r", "N"], loginInputSender.Inputs);
            Assert.Equal(0, actionProvider.CallCount);
            Assert.Empty(commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RefreshesStaleShipStatusWithoutSpendingGameplayActionBudget()
    {
        var eventHub = new ProxyGameEventHub();
        var situationProvider = new SequenceSituationProvider(
            CreateEligibleSituation(),
            CreateMovementEligibleSituation());
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.None,
                "Hold after checking the current state."));
        var commandSender = new RecordingCommandSender();
        var statusStore = new AutonomousTestWorkerStatusStore();
        var agentStore = new RecordingAgentStore(new AgentContainer
        {
            AgentId = "meridian",
            DisplayName = "Meridian",
            Configuration = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Brains:Pilot:Model"] = "qwen3:30b"
            }
        });
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            maximumActionsPerSession: 1,
            statusStore: statusStore,
            agentStore: agentStore);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(DateTimeOffset.UtcNow, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    DateTimeOffset.UtcNow,
                    "ServerPrompt",
                    PromptSurface: "Command")));

            await commandSender.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(0, actionProvider.CallCount);
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(DateTimeOffset.UtcNow, "SynchronizedShipStatus")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(DateTimeOffset.UtcNow.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));
            await WaitForActivityAsync(statusStore, "Holding");

            Assert.Equal(
                [new GameCommandRequest(GameCommand.RequestShipInformation)],
                commandSender.Commands);
            Assert.Equal(1, actionProvider.CallCount);
            Assert.Equal(2, situationProvider.CallCount);
            Assert.Equal("qwen3:30b", actionProvider.ModelConfiguration?.Model);
            var workerStatus = statusStore.GetStatus("meridian");
            Assert.True(workerStatus.IsConnected);
            Assert.Equal("Holding", workerStatus.Activity);
            Assert.Equal("None", workerStatus.LastAction);
            Assert.Equal("Hold after checking the current state.", workerStatus.LastRationale);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DoesNotAskTheModelOrSendWhenAuthenticationIsUnverified()
    {
        var eventHub = new ProxyGameEventHub();
        var situationProvider = new RecordingSituationProvider(
            CreateEligibleSituation() with { Authentication = GameAuthenticationState.Unverified });
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.RequestShipInformation, "test"));
        var commandSender = new RecordingCommandSender();
        using var worker = CreateWorker(eventHub, situationProvider, actionProvider, commandSender);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(DateTimeOffset.UtcNow, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    DateTimeOffset.UtcNow,
                    "ServerPrompt",
                    PromptSurface: "Command")));

            await situationProvider.Captured.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(0, actionProvider.CallCount);
            Assert.Empty(commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RetriesCommandPromptWhenPresenceHeartbeatArrives()
    {
        var eventHub = new ProxyGameEventHub();
        var situationProvider = new SequenceSituationProvider(
            CreateEligibleSituation() with { Authentication = GameAuthenticationState.Unverified },
            CreateMovementEligibleSituation());
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "Wait for verified game state."));
        var commandSender = new RecordingCommandSender();
        var statusStore = new AutonomousTestWorkerStatusStore();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            statusStore: statusStore);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(DateTimeOffset.UtcNow, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(DateTimeOffset.UtcNow, "ServerPrompt", PromptSurface: "Command")));

            await WaitForActivityAsync(statusStore, "Waiting for a fresh authenticated Command prompt");
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(DateTimeOffset.UtcNow, "PresenceHeartbeat")));
            await WaitForActivityAsync(statusStore, "Holding");

            Assert.Equal(2, situationProvider.CallCount);
            Assert.Equal(1, actionProvider.CallCount);
            Assert.Empty(commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task StaleCommandPromptDoesNotConsumeTheActionBudget()
    {
        var eventHub = new ProxyGameEventHub();
        var now = DateTimeOffset.UtcNow;
        var situationProvider = new SequenceSituationProvider(
            CreateEligibleSituation() with { CurrentPromptObservedAt = now.AddMinutes(-1) },
            CreateMovementEligibleSituation() with { CurrentPromptObservedAt = now });
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.None,
                "Wait after checking the current ship status."));
        var commandSender = new RecordingCommandSender();
        var statusStore = new AutonomousTestWorkerStatusStore();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            statusStore: statusStore);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(DateTimeOffset.UtcNow, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    DateTimeOffset.UtcNow,
                    "ServerPrompt",
                    PromptSurface: "Command")));
            await Task.Delay(50);
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    DateTimeOffset.UtcNow.AddMilliseconds(1),
                    "ServerPrompt",
                    PromptSurface: "Command")));

            await WaitForActivityAsync(statusStore, "Holding");

            Assert.Equal(1, actionProvider.CallCount);
            Assert.Empty(commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task IgnoresStaleQueuedCommandEventBeforeCapturingSituation()
    {
        var eventHub = new ProxyGameEventHub();
        var now = DateTimeOffset.UtcNow;
        var situationProvider = new SequenceSituationProvider(
            CreateMovementEligibleSituation(),
            CreateMovementEligibleSituation());
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.None,
                "Hold after refreshing the current ship status."));
        var commandSender = new RecordingCommandSender();
        var statusStore = new AutonomousTestWorkerStatusStore();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            statusStore: statusStore);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(now, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(now.AddMinutes(-1), "ServerPrompt", PromptSurface: "Command")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(now.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));

            await WaitForActivityAsync(statusStore, "Holding");
            await Task.Delay(50);

            Assert.Equal(1, situationProvider.CallCount);
            Assert.Equal(1, actionProvider.CallCount);
            Assert.Empty(commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BlocksShipInformationRefreshWhenSnapshotIsAlreadyFresh()
    {
        var eventHub = new ProxyGameEventHub();
        var situationProvider = new RecordingSituationProvider(CreateMovementEligibleSituation());
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.RequestShipInformation,
                "Refresh the ship snapshot."));
        var commandSender = new RecordingCommandSender();
        using var worker = CreateWorker(eventHub, situationProvider, actionProvider, commandSender);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(DateTimeOffset.UtcNow, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    DateTimeOffset.UtcNow,
                    "ServerPrompt",
                    PromptSurface: "Command")));

            await situationProvider.Captured.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);

            Assert.Equal(1, actionProvider.CallCount);
            Assert.Empty(commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task SendsMovementOnlyToAStillAdjacentSectorFromAHealthySnapshot()
    {
        var eventHub = new ProxyGameEventHub();
        var situation = CreateMovementEligibleSituation();
        var situationProvider = new RecordingSituationProvider(situation);
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                "Move one verified hop.",
                3));
        var commandSender = new RecordingCommandSender();
        var moveTargetProvider = new RecordingMoveTargetProvider(
            [2, 3],
            [
                new AdjacentSectorKnowledge(2, ExplorationStatus.WarpOnly, true, false),
                new AdjacentSectorKnowledge(3, ExplorationStatus.Unknown, false, false)
            ]);
        var moveApprovalGate = new PendingMoveApprovalGate();
        var objectiveProvider = new RecordingActionObjectiveProvider(
            new AutonomousActionObjective(
                "Explore and map an adjacent sector safely.",
                "Expand verified local knowledge before trading."));
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            moveTargetProvider,
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            actionObjectiveProvider: objectiveProvider,
            moveApprovalGate: moveApprovalGate);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(DateTimeOffset.UtcNow, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    DateTimeOffset.UtcNow,
                    "ServerPrompt",
                    PromptSurface: "Command")));
            var approvalRequest = await moveApprovalGate.Requested.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, approvalRequest.CurrentSector);
            Assert.Equal(3, approvalRequest.TargetSector);
            Assert.Equal(1, approvalRequest.TurnCost);
            Assert.Empty(commandSender.Commands);

            moveApprovalGate.Resolve(approved: true);
            await WaitForCommandCountAsync(commandSender, 1);

            Assert.Equal(
                [new GameCommandRequest(GameCommand.MoveToSector, 3)],
                commandSender.Commands);
            Assert.Equal([2, 3], actionProvider.ActionContext?.AvailableAdjacentSectors);
            Assert.Equal(
                [
                    new AdjacentSectorKnowledge(2, ExplorationStatus.WarpOnly, true, false),
                    new AdjacentSectorKnowledge(3, ExplorationStatus.Unknown, false, false)
                ],
                actionProvider.ActionContext?.AdjacentSectorKnowledge);
            Assert.Equal(
                "Explore and map an adjacent sector safely.",
                actionProvider.ActionContext?.ActiveObjective?.Goal);
            Assert.Contains(
                objectiveProvider.RequestedReasons,
                reason => reason.Contains("new or reconnected gameplay session", StringComparison.Ordinal));
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task FedSpaceFastStartMovesThroughItsDeterministicSweepRoute()
    {
        var eventHub = new ProxyGameEventHub();
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "The model should not select the sweep route."));
        var commandSender = new RecordingCommandSender();
        var moveApprovalGate = new PendingMoveApprovalGate();
        var fastStartProvider = new RecordingFedSpaceFastStartProvider(nextSweepSector: 2);
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(CreateMovementEligibleSituation()),
            actionProvider,
            commandSender,
            new RecordingMoveTargetProvider([2, 3]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            moveApprovalGate: moveApprovalGate,
            fedSpaceFastStartProvider: fastStartProvider);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(now, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(now.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));

            var approvalRequest = await moveApprovalGate.Requested.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Equal(1, approvalRequest.CurrentSector);
            Assert.Equal(2, approvalRequest.TargetSector);
            Assert.Equal(new HashSet<int> { 1 }, fastStartProvider.LastVisitedSectors);
            Assert.Equal(0, actionProvider.CallCount);

            moveApprovalGate.Resolve(approved: true);
            await WaitForCommandCountAsync(commandSender, 1);
            Assert.Equal(
                [new GameCommandRequest(GameCommand.MoveToSector, 2)],
                commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task FedSpaceFastStartIsSkippedWhenTheFirstLocationPromptIsAPlanet()
    {
        var eventHub = new ProxyGameEventHub();
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "No fast-start action."));
        var fastStartProvider = new RecordingFedSpaceFastStartProvider(nextSweepSector: 2);
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(CreateMovementEligibleSituation()),
            actionProvider,
            new RecordingCommandSender(),
            new RecordingMoveTargetProvider([2, 3]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            fedSpaceFastStartProvider: fastStartProvider);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset now = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(now, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(now.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Citadel")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(now.AddMilliseconds(2), "ServerPrompt", PromptSurface: "Command")));

            await WaitForActionCallCountAsync(actionProvider, 1);
            Assert.Equal(0, fastStartProvider.SweepCallCount);
            Assert.Equal(1, actionProvider.CallCount);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task UnlimitedSessionBudgetAllowsMultipleVerifiedAdjacentMoves()
    {
        var eventHub = new ProxyGameEventHub();
        var situationProvider = new SectorSequenceSituationProvider(1, 1, 2, 2);
        var actionProvider = new SequenceActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                "Move to the first verified adjacent sector.",
                2),
            new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                "Continue to the next verified adjacent sector.",
                3));
        var commandSender = new RecordingCommandSender();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            new RecordingMoveTargetProvider([2, 3, 4]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            actionObjectiveProvider: new RecordingActionObjectiveProvider(
                new AutonomousActionObjective(
                    "Explore adjacent sectors.",
                    "Expand verified local knowledge.")),
            moveApprovalGate: new ImmediateMoveApprovalGate());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            var firstPromptAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(firstPromptAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(firstPromptAt, "ServerPrompt", PromptSurface: "Command")));
            await WaitForCommandCountAsync(commandSender, 1);
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    firstPromptAt.AddMilliseconds(1),
                    "ServerPrompt",
                    PromptSurface: "Command")));
            await WaitForCommandCountAsync(commandSender, 2);

            Assert.Equal(
                [
                    new GameCommandRequest(GameCommand.MoveToSector, 2),
                    new GameCommandRequest(GameCommand.MoveToSector, 3)
                ],
                commandSender.Commands);
            Assert.Equal(2, actionProvider.CallCount);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AvoidsImmediateBacktrackingWhenAnotherFreshRouteExists()
    {
        var eventHub = new ProxyGameEventHub();
        var situationProvider = new SectorSequenceSituationProvider(1, 1, 2, 2);
        var actionProvider = new SequenceActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                "Move to the verified adjacent sector.",
                2),
            new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                "Continue away from the sector we just left.",
                3));
        var commandSender = new RecordingCommandSender();
        var moveTargetProvider = new SectorMoveTargetProvider(
            new Dictionary<int, IReadOnlyList<int>>
            {
                [1] = [2],
                [2] = [1, 3]
            });
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            moveTargetProvider,
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            actionObjectiveProvider: new RecordingActionObjectiveProvider(
                new AutonomousActionObjective(
                    "Reach StarDock using adjacent sectors.",
                    "Make verified forward progress.")),
            moveApprovalGate: new ImmediateMoveApprovalGate());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset promptAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt, "Connected")));

            for (var promptIndex = 0; promptIndex < 2; promptIndex++)
            {
                eventHub.TryPublish(new ProxySessionEvent(
                    "meridian",
                    new ProxyGameEvent(
                        promptAt.AddMilliseconds(promptIndex + 1),
                        "ServerPrompt",
                        PromptSurface: "Command")));
                await WaitForCommandCountAsync(commandSender, promptIndex + 1);
            }

            Assert.Equal(
                [
                    new GameCommandRequest(GameCommand.MoveToSector, 2),
                    new GameCommandRequest(GameCommand.MoveToSector, 3)
                ],
                commandSender.Commands);
            Assert.Equal([3], actionProvider.ActionContexts[1].AvailableAdjacentSectors);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RestrictsMovementToKnownShortestHopTowardStarDock()
    {
        var eventHub = new ProxyGameEventHub();
        var situationProvider = new RecordingSituationProvider(CreateMovementEligibleSituation());
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                "Follow the verified route toward StarDock.",
                3));
        var commandSender = new RecordingCommandSender();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            new RecordingMoveTargetProvider([2, 3]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            actionObjectiveProvider: new RecordingActionObjectiveProvider(
                new AutonomousActionObjective(
                    "Reach StarDock sector 551 by verified adjacent routes.",
                    "Follow the shortest known route toward StarDock.")),
            gameWorldProvider: new FixedGameWorldProvider(551, 3),
            moveApprovalGate: new ImmediateMoveApprovalGate());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset promptAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));
            await WaitForCommandCountAsync(commandSender, 1);

            Assert.Equal([3], actionProvider.ActionContext?.AvailableAdjacentSectors);
            Assert.Equal(
                new GameCommandRequest(GameCommand.MoveToSector, 3),
                commandSender.Commands.Last());
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RoutesScannerlessShipTowardStarDockDespiteStaleTradingObjective()
    {
        var eventHub = new ProxyGameEventHub();
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        var situationProvider = new RecordingSituationProvider(
            new GameSituationReport(
                observedAt,
                GamePresenceState.Connected,
                GameAuthenticationState.Authenticated,
                GameSynchronizationState.Healthy,
                "Command",
                observedAt,
                new GameSnapshot(20, 64, 500, null, observedAt)
                {
                    DensityScannerInstalled = false,
                    HoloScannerInstalled = false
                },
                []));
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                "Follow the route toward StarDock.",
                3));
        var commandSender = new RecordingCommandSender();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            new RecordingMoveTargetProvider([3, 5]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            actionObjectiveProvider: new RecordingActionObjectiveProvider(
                new AutonomousActionObjective(
                    "Learn trading",
                    "Find another port trade.")),
            gameWorldProvider: new FixedGameWorldProvider(
                551,
                nextSector: 3,
                isFederationSpace: false),
            moveApprovalGate: new ImmediateMoveApprovalGate());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));
            await WaitForCommandCountAsync(commandSender, 1);

            Assert.Equal(
                new GameCommandRequest(GameCommand.MoveToSector, 3),
                commandSender.Commands.Single());
            Assert.Equal(0, actionProvider.CallCount);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task KeepsScannerAcquisitionObjectiveAtStarDockDespiteStaleTradingObjective()
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        var situationProvider = new RecordingSituationProvider(
            new GameSituationReport(
                observedAt,
                GamePresenceState.Connected,
                GameAuthenticationState.Authenticated,
                GameSynchronizationState.Healthy,
                "Command",
                observedAt,
                new GameSnapshot(551, 64, 500, null, observedAt)
                {
                    DensityScannerInstalled = false,
                    HoloScannerInstalled = false
                },
                []));
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.None,
                "Wait for a verified scanner purchase opportunity."));
        var eventHub = new ProxyGameEventHub();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            new RecordingCommandSender(),
            new RecordingMoveTargetProvider([172]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            actionObjectiveProvider: new RecordingActionObjectiveProvider(
                new AutonomousActionObjective("Learn trading", "Find another port trade.")),
            gameWorldProvider: new FixedGameWorldProvider(
                551,
                nextSector: 172,
                isFederationSpace: true));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    observedAt.AddMilliseconds(1),
                    "ServerPrompt",
                    PromptSurface: "Command")));
            await WaitForActionCallCountAsync(actionProvider, 1);

            Assert.StartsWith(
                "Acquire the Density Scanner at StarDock sector 551",
                actionProvider.ActionContext?.ActiveObjective?.Goal,
                StringComparison.Ordinal);
            Assert.Contains(
                "verified compatible port-pair trade",
                actionProvider.ActionContext?.ActiveObjective?.Rationale,
                StringComparison.Ordinal);
            Assert.Contains(
                "Never enter sector 99 or an unexplored sector",
                actionProvider.ActionContext?.ActiveObjective?.Rationale,
                StringComparison.Ordinal);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BlocksScannerlessMovementIntoUnexploredSector()
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        var situationProvider = new RecordingSituationProvider(
            new GameSituationReport(
                observedAt,
                GamePresenceState.Connected,
                GameAuthenticationState.Authenticated,
                GameSynchronizationState.Healthy,
                "Command",
                observedAt,
                new GameSnapshot(551, 64, 1, null, observedAt)
                {
                    DensityScannerInstalled = false,
                    HoloScannerInstalled = false
                },
                []));
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                "Travel to the funding trade.",
                172));
        var commandSender = new RecordingCommandSender();
        var eventHub = new ProxyGameEventHub();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            new RecordingMoveTargetProvider(
                [172],
                [new AdjacentSectorKnowledge(172, ExplorationStatus.Unknown, false, false)]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            actionObjectiveProvider: new RecordingActionObjectiveProvider(
                new AutonomousActionObjective("Fund the scanner", "Trade on a safe route.")),
            gameWorldProvider: new FixedGameWorldProvider(
                551,
                nextSector: null,
                isFederationSpace: false),
            moveApprovalGate: new ImmediateMoveApprovalGate());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));
            await WaitForActionCallCountAsync(actionProvider, 1);

            Assert.DoesNotContain(172, actionProvider.ActionContext!.AvailableAdjacentSectors);
            Assert.Empty(commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task HidesUnexploredPortTradeRouteFromScannerlessPilot()
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        var situationProvider = new RecordingSituationProvider(
            new GameSituationReport(
                observedAt,
                GamePresenceState.Connected,
                GameAuthenticationState.Authenticated,
                GameSynchronizationState.Healthy,
                "Command",
                observedAt,
                new GameSnapshot(551, 65441, 1, null, observedAt)
                {
                    DensityScannerInstalled = false,
                    HoloScannerInstalled = false
                },
                []));
        var eventHub = new ProxyGameEventHub();
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.None,
                "Wait for a safe scanner-funding option."));
        var commandSender = new RecordingCommandSender();
        var statusStore = new AutonomousTestWorkerStatusStore();
        var opportunity = new AutonomousPortTradeOpportunity(
            PartnerSector: 206,
            TradeProduct.Organics,
            SellingExistingCargo: false)
        {
            TradeSector = 172,
            RouteSector = 172
        };
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            new RecordingMoveTargetProvider(
                [172],
                [new AdjacentSectorKnowledge(172, ExplorationStatus.Unknown, false, false)]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            actionObjectiveProvider: new RecordingActionObjectiveProvider(
                new AutonomousActionObjective(
                    "Acquire the Density Scanner at StarDock sector 551 before World Trade.",
                    "Use only a scanner-safe route to fund the scanner.")),
            gameWorldProvider: new FixedGameWorldProvider(551, isFederationSpace: false),
            portTradeOpportunityProvider: new RecordingPortTradeOpportunityProvider(opportunity),
            statusStore: statusStore);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (actionProvider.CallCount == 0 && DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            Assert.True(
                actionProvider.CallCount > 0,
                statusStore.GetStatus("meridian").Activity);

            Assert.DoesNotContain(172, actionProvider.ActionContext!.AvailableAdjacentSectors);
            Assert.Null(actionProvider.ActionContext.PortTradeOpportunity);
            Assert.Empty(commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task HoldsAtStarDockWithoutScannerWhenNoVerifiedFundingTradeExists()
    {
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        var situationProvider = new RecordingSituationProvider(
            new GameSituationReport(
                observedAt,
                GamePresenceState.Connected,
                GameAuthenticationState.Authenticated,
                GameSynchronizationState.Healthy,
                "Command",
                observedAt,
                new GameSnapshot(551, 1, 64, null, observedAt)
                {
                    HoldsEmpty = 20,
                    HoldsTotal = 20,
                    FuelOre = 0,
                    Organics = 0,
                    Equipment = 0,
                    DensityScannerInstalled = false,
                    HoloScannerInstalled = false
                },
                []));
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                "Travel to the adjacent port.",
                172));
        var commandSender = new RecordingCommandSender();
        var eventHub = new ProxyGameEventHub();
        var statusStore = new AutonomousTestWorkerStatusStore();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            new RecordingMoveTargetProvider(
                [172, 750],
                [
                    new AdjacentSectorKnowledge(172, ExplorationStatus.Explored, false, false),
                    new AdjacentSectorKnowledge(750, ExplorationStatus.Explored, false, false)
                ]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            actionObjectiveProvider: new RecordingActionObjectiveProvider(
                new AutonomousActionObjective("Increase income", "Find a safe scanner-funding trade.")),
            gameWorldProvider: new FixedGameWorldProvider(551, isFederationSpace: true),
            portTradeOpportunityProvider: new RecordingPortTradeOpportunityProvider(null),
            statusStore: statusStore);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));
            DateTimeOffset deadline = DateTimeOffset.UtcNow.AddSeconds(5);
            while (statusStore.GetStatus("meridian").LastAction is null &&
                   DateTimeOffset.UtcNow < deadline)
            {
                await Task.Delay(10);
            }

            AutonomousTestWorkerStatus status = statusStore.GetStatus("meridian");
            Assert.Equal(0, actionProvider.CallCount);
            Assert.Equal("None", status.LastAction);
            Assert.Contains("StarDock", status.LastRationale, StringComparison.Ordinal);
            Assert.Empty(commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RefreshesStatusWhenTheSnapshotExpiresDuringModelReasoning()
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var freshSituation = CreateMovementEligibleSituation();
        var staleSituation = freshSituation with
        {
            CapturedAt = now,
            CurrentPromptObservedAt = now,
            Snapshot = freshSituation.Snapshot! with
            {
                ObservedAt = now.AddSeconds(-31)
            }
        };
        var eventHub = new ProxyGameEventHub();
        var situationProvider = new SequenceSituationProvider(
            freshSituation,
            staleSituation);
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                "Move one verified hop.",
                3));
        var commandSender = new RecordingCommandSender();
        var statusStore = new AutonomousTestWorkerStatusStore();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            new RecordingMoveTargetProvider([2, 3]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            actionObjectiveProvider: new RecordingActionObjectiveProvider(
                new AutonomousActionObjective(
                    "Reach StarDock via verified adjacent sectors.",
                    "Use the current route to move one hop at a time.")),
            moveApprovalGate: new ImmediateMoveApprovalGate(),
            statusStore: statusStore);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset promptAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));
            await commandSender.StatusRequestSent.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(
                [new GameCommandRequest(GameCommand.RequestShipInformation)],
                commandSender.Commands);
            await WaitForActivityAsync(statusStore, "Status refresh sent");
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RequestsFreshStatusInsteadOfMovingWhenSnapshotSynchronizationIsStale()
    {
        var eventHub = new ProxyGameEventHub();
        var situationProvider = new RecordingSituationProvider(
            CreateMovementEligibleSituation() with
            {
                Synchronization = GameSynchronizationState.Stale
            });
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                "Move one hop.",
                3));
        var commandSender = new RecordingCommandSender();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            new RecordingMoveTargetProvider([2, 3]),
            allowGameActions: true);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(DateTimeOffset.UtcNow, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    DateTimeOffset.UtcNow,
                    "ServerPrompt",
                    PromptSurface: "Command")));

            await commandSender.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(0, actionProvider.CallCount);
            Assert.Equal(
                [new GameCommandRequest(GameCommand.RequestShipInformation)],
                commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ProvidesAdjacentKnowledgeWithoutEnablingMovement()
    {
        var eventHub = new ProxyGameEventHub();
        var situationProvider = new RecordingSituationProvider(CreateMovementEligibleSituation());
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                "The sector has not been physically explored.",
                3));
        var commandSender = new RecordingCommandSender();
        var moveTargetProvider = new RecordingMoveTargetProvider(
            [2, 3],
            [
                new AdjacentSectorKnowledge(2, ExplorationStatus.WarpOnly, true, false),
                new AdjacentSectorKnowledge(3, ExplorationStatus.Unknown, false, false)
            ]);
        var relevantKnowledge = new[]
        {
            new AutonomousActionKnowledgeItem(
                "scan-node",
                "Tactical",
                "Sector scanning",
                "A scanner should be used before judging adjacent sectors.",
                0.9m,
                "ExplicitText",
                ["line 12: scan current sector"])
        };
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            moveTargetProvider,
            allowGameActions: false,
            actionKnowledgeProvider: new RecordingActionKnowledgeProvider(relevantKnowledge));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(DateTimeOffset.UtcNow, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    DateTimeOffset.UtcNow,
                    "ServerPrompt",
                    PromptSurface: "Command")));

            await situationProvider.Captured.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(100);

            Assert.False(actionProvider.ActionContext?.GameActionsEnabled);
            Assert.Empty(actionProvider.ActionContext?.AvailableAdjacentSectors ?? []);
            Assert.Equal(
                [
                    new AdjacentSectorKnowledge(2, ExplorationStatus.WarpOnly, true, false),
                    new AdjacentSectorKnowledge(3, ExplorationStatus.Unknown, false, false)
                ],
                actionProvider.ActionContext?.AdjacentSectorKnowledge);
            Assert.Equal(relevantKnowledge, actionProvider.ActionContext?.RelevantKnowledge);
            Assert.Empty(commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DisplaysCurrentSectorBeforeSelectingMovementWhenTheProviderRequiresIt()
    {
        var eventHub = new ProxyGameEventHub();
        var situationProvider = new RecordingSituationProvider(CreateMovementEligibleSituation());
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                "Move one verified hop.",
                3));
        var commandSender = new RecordingCommandSender();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            new RecordingMoveTargetProvider(
                [2, 3],
                requiresCurrentSectorDisplayBeforeMovement: true),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            actionObjectiveProvider: new RecordingActionObjectiveProvider(
                new AutonomousActionObjective(
                    "Reach StarDock via verified adjacent sectors.",
                    "Move one hop at a time.")),
            moveApprovalGate: new ImmediateMoveApprovalGate());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset promptAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));
            await commandSender.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(
                [new GameCommandRequest(GameCommand.DisplayCurrentSector)],
                commandSender.Commands);
            Assert.Equal(0, actionProvider.CallCount);

            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    promptAt.AddMilliseconds(2),
                    "ServerPrompt",
                    PromptSurface: "Command")));
            await WaitForCommandCountAsync(commandSender, 2);

            Assert.Equal(
                [
                    new GameCommandRequest(GameCommand.DisplayCurrentSector),
                    new GameCommandRequest(GameCommand.MoveToSector, 3)
                ],
                commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task LooksUpUnknownStarDockBeforeSelectingGameplayActions()
    {
        var eventHub = new ProxyGameEventHub();
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        GameSituationReport situation = CreateMovementEligibleSituation() with
        {
            CapturedAt = observedAt,
            CurrentPromptObservedAt = observedAt,
            Snapshot = new GameSnapshot(172, 6, 65_434, null, observedAt)
            {
                HoldsEmpty = 19,
                HoldsTotal = 20,
                FuelOre = 1
            }
        };
        var actionProvider = new SequenceActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "Wait for the next prompt."));
        var commandSender = new RecordingCommandSender();
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(situation),
            actionProvider,
            commandSender,
            new RecordingMoveTargetProvider([551]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            gameWorldProvider: new FixedGameWorldProvider(starDockSector: null));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset promptAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));
            await commandSender.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(
                [new GameCommandRequest(GameCommand.FindStarDockSector)],
                commandSender.Commands);
            Assert.Equal(0, actionProvider.CallCount);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task WaitsForMovementToSettleWhenFreshSnapshotStillShowsOrigin()
    {
        static GameSituationReport CreateSituation(int sector, DateTimeOffset observedAt) =>
            CreateMovementEligibleSituation() with
            {
                CapturedAt = observedAt,
                CurrentPromptObservedAt = observedAt,
                Snapshot = new GameSnapshot(sector, 1000, 5, null, observedAt)
            };

        var eventHub = new ProxyGameEventHub();
        var situationProvider = new MutableSituationProvider(
            CreateSituation(1, DateTimeOffset.UtcNow));
        var actionProvider = new SequenceActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                "Move one verified hop.",
                3),
            new AutonomousTestActionDecision(AutonomousTestAction.None, "Wait for the next prompt."));
        var commandSender = new RecordingCommandSender();
        var statusStore = new AutonomousTestWorkerStatusStore();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            new SectorMoveTargetProvider(
                new Dictionary<int, IReadOnlyList<int>>
                {
                    [1] = [2, 3],
                    [3] = [4]
                }),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            actionObjectiveProvider: new RecordingActionObjectiveProvider(
                new AutonomousActionObjective(
                    "Reach StarDock via verified adjacent sectors.",
                    "Move one hop at a time.")),
            moveApprovalGate: new ImmediateMoveApprovalGate(),
            statusStore: statusStore);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset connectedAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(connectedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    connectedAt.AddMilliseconds(1),
                    "ServerPrompt",
                    PromptSurface: "Command")));
            await commandSender.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(20);

            situationProvider.Enqueue(CreateSituation(1, DateTimeOffset.UtcNow));
            DateTimeOffset originPromptAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(originPromptAt, "ServerPrompt", PromptSurface: "Command")));
            await WaitForActivityAsync(
                statusStore,
                "Waiting for movement from sector 1 to sector 3 to settle");

            situationProvider.Enqueue(CreateSituation(3, DateTimeOffset.UtcNow));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    DateTimeOffset.UtcNow.AddMilliseconds(1),
                    "PresenceHeartbeat",
                    PromptSurface: "Command")));
            await WaitForActionCallCountAsync(actionProvider, 2);

            Assert.Equal(
                [new GameCommandRequest(GameCommand.MoveToSector, 3)],
                commandSender.Commands);
            Assert.True(actionProvider.ActionContexts[1].GameActionsEnabled);
            Assert.Equal([4], actionProvider.ActionContexts[1].AvailableAdjacentSectors);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task KeepsLocalTradingAvailableWhenMovementVerificationFails()
    {
        static GameSituationReport CreateSituation(int sector, DateTimeOffset observedAt) =>
            CreateMovementEligibleSituation() with
            {
                CapturedAt = observedAt,
                CurrentPromptObservedAt = observedAt,
                Snapshot = new GameSnapshot(sector, 1000, 5, null, observedAt)
            };

        var eventHub = new ProxyGameEventHub();
        var situationProvider = new MutableSituationProvider(
            CreateSituation(1, DateTimeOffset.UtcNow));
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                "Move one verified hop.",
                3));
        var commandSender = new RecordingCommandSender();
        var statusStore = new AutonomousTestWorkerStatusStore();
        var localTradeOpportunity = new AutonomousPortTradeOpportunity(
            PartnerSector: 5,
            TradeProduct.Organics,
            SellingExistingCargo: false);
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            new RecordingMoveTargetProvider([2, 3, 5]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            actionObjectiveProvider: new RecordingActionObjectiveProvider(
                new AutonomousActionObjective(
                    "Reach StarDock and continue trading.",
                    "Keep making safe local trade actions.")),
            moveApprovalGate: new ImmediateMoveApprovalGate(),
            statusStore: statusStore,
            portTradeOpportunityProvider: new SequencePortTradeOpportunityProvider(
                null,
                localTradeOpportunity));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset connectedAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(connectedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    connectedAt.AddMilliseconds(1),
                    "ServerPrompt",
                    PromptSurface: "Command")));
            await commandSender.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await Task.Delay(20);

            situationProvider.Enqueue(CreateSituation(4, DateTimeOffset.UtcNow));
            DateTimeOffset failedMovePromptAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    failedMovePromptAt,
                    "ServerPrompt",
                    PromptSurface: "Command")));
            await WaitForActivityAsync(statusStore, "Movement verification failed");

            situationProvider.Enqueue(CreateSituation(4, DateTimeOffset.UtcNow));
            situationProvider.Enqueue(CreateSituation(4, DateTimeOffset.UtcNow));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    DateTimeOffset.UtcNow.AddMilliseconds(1),
                    "ServerPrompt",
                    PromptSurface: "Command")));
            await WaitForCommandCountAsync(commandSender, 2);

            Assert.Equal(
                [
                    new GameCommandRequest(GameCommand.MoveToSector, 3),
                    new GameCommandRequest(
                        GameCommand.TradeAtCurrentPort,
                        Product: TradeProduct.Organics)
                ],
                commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RequestsFreshStatusOnceInsteadOfRepeatingMovementBeforePostconditionIsObserved()
    {
        var eventHub = new ProxyGameEventHub();
        var situationProvider = new RecordingSituationProvider(CreateMovementEligibleSituation());
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                "Move one hop.",
                3));
        var commandSender = new RecordingCommandSender();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            new RecordingMoveTargetProvider([2, 3]),
            allowGameActions: true,
            maximumActionsPerSession: 3,
            actionObjectiveProvider: new RecordingActionObjectiveProvider(
                new AutonomousActionObjective(
                    "Explore and map an adjacent sector safely.",
                    "Expand verified local knowledge before trading.")),
            moveApprovalGate: new ImmediateMoveApprovalGate());

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(DateTimeOffset.UtcNow, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    DateTimeOffset.UtcNow,
                    "ServerPrompt",
                    PromptSurface: "Command")));

            await commandSender.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    DateTimeOffset.UtcNow.AddMilliseconds(1),
                    "ServerPrompt",
                    PromptSurface: "Command")));
            await WaitForCommandCountAsync(commandSender, 2);
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    DateTimeOffset.UtcNow.AddMilliseconds(2),
                    "ServerPrompt",
                    PromptSurface: "Command")));
            await commandSender.StatusRequestSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    DateTimeOffset.UtcNow,
                    "SynchronizedShipStatus")));
            var retryPromptAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(retryPromptAt, "ServerPrompt", PromptSurface: "Command")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    retryPromptAt.AddMilliseconds(1),
                    "PresenceHeartbeat",
                    PromptSurface: "Command")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    retryPromptAt.AddMilliseconds(2),
                    "ServerPrompt",
                    PromptSurface: "Command")));
            await Task.Delay(100);

            Assert.Equal(
                [
                    new GameCommandRequest(GameCommand.MoveToSector, 3),
                    new GameCommandRequest(GameCommand.RequestShipInformation)
                ],
                commandSender.Commands);
            Assert.Equal(1, actionProvider.CallCount);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task ScansEachSectorOnlyOnceBeforeAskingForTheNextAction()
    {
        var eventHub = new ProxyGameEventHub();
        var actionProvider = new SequenceActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "Wait for the next prompt."));
        var commandSender = new RecordingCommandSender();
        using var worker = CreateWorker(
            eventHub,
            new SectorSequenceSituationProvider(8, 8, 9, 8),
            actionProvider,
            commandSender,
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset observedAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            for (int index = 0; index < 4; index++)
            {
                eventHub.TryPublish(new ProxySessionEvent(
                    "meridian",
                    new ProxyGameEvent(
                        observedAt.AddMilliseconds(index + 1),
                        "ServerPrompt",
                        PromptSurface: "Command")));
            }

            await WaitForActionCallCountAsync(actionProvider, 2);

            Assert.Equal(
                [
                    new GameCommandRequest(GameCommand.DisplayCurrentSector),
                    new GameCommandRequest(GameCommand.DisplayCurrentSector)
                ],
                commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task WorldTradeModeStartsWithEitherScannerOnlyAtStarDockAndSuppressesConflictingActions(
        bool densityScannerInstalled,
        bool holoScannerInstalled)
    {
        var eventHub = new ProxyGameEventHub();
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        GameSituationReport starDockSituation = CreateMovementEligibleSituation() with
        {
            CurrentPromptObservedAt = observedAt,
            Snapshot = new GameSnapshot(
                551,
                AutonomousTradePolicy.MinimumScannerUpgradeCredits,
                300,
                null,
                observedAt)
            {
                HoloScannerInstalled = holoScannerInstalled,
                DensityScannerInstalled = densityScannerInstalled
            }
        };
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.StartWorldTradeMode,
                "Start the fixed world-trade mode at StarDock."));
        var commandSender = new RecordingCommandSender();
        var scriptRunner = new RecordingGameScriptRunner();
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(starDockSituation),
            actionProvider,
            commandSender,
            moveTargetProvider: new RecordingMoveTargetProvider(
                [550],
                requiresCurrentSectorDisplayBeforeMovement: true),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            gameWorldProvider: new FixedGameWorldProvider(551),
            gameScriptRunner: scriptRunner);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            for (int index = 0; index < 3; index++)
            {
                eventHub.TryPublish(new ProxySessionEvent(
                    "meridian",
                    new ProxyGameEvent(
                        observedAt.AddMilliseconds(index + 1),
                        "ServerPrompt",
                        PromptSurface: "Command")));
            }

            await scriptRunner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await scriptRunner.StatusChecked.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Null(actionProvider.ActionContext);
            Assert.Single(commandSender.Commands);
            Assert.Equal(GameCommand.DisplayCurrentSector, commandSender.Commands.Single().Command);
            Assert.Equal(0, actionProvider.CallCount);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task WorldTradeModeStopsOnlyAfterFreshFundingTargetIsReached()
    {
        var eventHub = new ProxyGameEventHub();
        DateTimeOffset initialObservedAt = DateTimeOffset.UtcNow;
        GameSituationReport initialSituation = CreateMovementEligibleSituation() with
        {
            CurrentPromptObservedAt = initialObservedAt,
            Snapshot = new GameSnapshot(
                551,
                AutonomousTradePolicy.MinimumScannerUpgradeCredits,
                300,
                null,
                initialObservedAt)
            {
                HoloScannerInstalled = true,
                DensityScannerInstalled = true
            }
        };
        var situationProvider = new MutableSituationProvider(initialSituation);
        var actionProvider = new SequenceActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.StopWorldTradeMode,
                "The fresh credit total reached the refurb target."));
        var commandSender = new RecordingCommandSender();
        var scriptRunner = new RecordingGameScriptRunner();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            gameWorldProvider: new FixedGameWorldProvider(551),
            gameScriptRunner: scriptRunner);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(initialObservedAt, "Connected")));
            for (int index = 0; index < 3; index++)
            {
                eventHub.TryPublish(new ProxySessionEvent(
                    "meridian",
                    new ProxyGameEvent(
                        initialObservedAt.AddMilliseconds(index + 1),
                        "ServerPrompt",
                        PromptSurface: "Command")));
            }

            await scriptRunner.Started.Task.WaitAsync(TimeSpan.FromSeconds(5));
            DateTimeOffset fundedAt = DateTimeOffset.UtcNow;
            GameSituationReport fundedSituation = CreateMovementEligibleSituation() with
            {
                CurrentPromptObservedAt = fundedAt,
                Snapshot = new GameSnapshot(
                    551,
                    AutonomousTradePolicy.MinimumWorldTradeUpgradeCredits,
                    299,
                    null,
                    fundedAt)
                {
                    HoloScannerInstalled = true,
                    DensityScannerInstalled = true
                }
            };
            situationProvider.Enqueue(fundedSituation);
            situationProvider.Enqueue(fundedSituation);
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    fundedAt.AddMilliseconds(1),
                    "ServerPrompt",
                    PromptSurface: "Command")));

            await scriptRunner.Stopped.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(1, actionProvider.CallCount);
            Assert.True(actionProvider.ActionContexts[0].WorldTradeModeRunning);
            Assert.True(actionProvider.ActionContexts[0].WorldTradeStopReady);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RefurbPurchaseUsesLiveQuoteAndRequestsFreshComponentVerification()
    {
        var eventHub = new ProxyGameEventHub();
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        var snapshot = new GameSnapshot(
            551,
            AutonomousTradePolicy.MinimumWorldTradeUpgradeCredits,
            300,
            new Ship("Meridian", string.Empty, "Merchant Cruiser", 551, 100, observedAt),
            observedAt)
        {
            HoldsEmpty = 20,
            HoldsTotal = 20,
            Shields = 75,
            HoloScannerInstalled = true
        };
        var situation = new GameSituationReport(
            observedAt,
            GamePresenceState.Connected,
            GameAuthenticationState.Authenticated,
            GameSynchronizationState.Healthy,
            "Command",
            observedAt,
            snapshot,
            []);
        var actionProvider = new SequenceActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.InspectRefurbMenu,
                "Inspect the verified StarDock quote."),
            new AutonomousTestActionDecision(
                AutonomousTestAction.BuyFighters,
                "Use the quoted fighter price while preserving the reserve."));
        var commandSender = new RecordingCommandSender();
        var scriptRunner = new RecordingGameScriptRunner();
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(situation),
            actionProvider,
            commandSender,
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            gameWorldProvider: new FixedGameWorldProvider(551, currentRefurbPortClass: 9),
            gameScriptRunner: scriptRunner);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            for (int index = 0; index < 3; index++)
            {
                eventHub.TryPublish(new ProxySessionEvent(
                    "meridian",
                    new ProxyGameEvent(
                        observedAt.AddMilliseconds(index + 1),
                        "ServerPrompt",
                        PromptSurface: "Command")));
            }

            await scriptRunner.RefurbMenuOpened.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await scriptRunner.RefurbPurchaseExecuted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.Equal(
                [AutonomousRefurbUpgrade.Fighters],
                scriptRunner.RefurbUpgrades.ToArray());
            Assert.Contains(
                commandSender.Commands,
                command => command.Command == GameCommand.RequestShipInformation);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task MovesToAnAdjacentKnownSellerInsteadOfSkippingTheTradePair()
    {
        var eventHub = new ProxyGameEventHub();
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "Use the detected trade route."));
        var commandSender = new RecordingCommandSender();
        var objective = new AutonomousActionObjective(
            "Reach StarDock after building credits through port trades.",
            "Trade opportunities take priority over the StarDock route.");
        var opportunity = new AutonomousPortTradeOpportunity(
            PartnerSector: 1,
            TradeProduct.Organics,
            SellingExistingCargo: false)
        {
            TradeSector = 2,
            RouteSector = 2
        };
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(CreateMovementEligibleSituation()),
            actionProvider,
            commandSender,
            moveTargetProvider: new RecordingMoveTargetProvider([2, 3]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            actionObjectiveProvider: new RecordingActionObjectiveProvider(objective),
            moveApprovalGate: new ImmediateMoveApprovalGate(),
            gameWorldProvider: new FixedGameWorldProvider(551, 3),
            portTradeOpportunityProvider: new RecordingPortTradeOpportunityProvider(opportunity));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset promptAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));

            await WaitForCommandCountAsync(commandSender, 1);

            Assert.Equal(
                new GameCommandRequest(GameCommand.MoveToSector, 2),
                commandSender.Commands.Single());
            Assert.Equal(0, actionProvider.CallCount);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task WaitsForFreshPortCimInsteadOfMovingWithCargoWithoutABuyer()
    {
        var eventHub = new ProxyGameEventHub();
        var statusStore = new AutonomousTestWorkerStatusStore();
        var observedAt = DateTimeOffset.UtcNow;
        GameSituationReport situation = CreateMovementEligibleSituation() with
        {
            Snapshot = new GameSnapshot(1, 730, 5, null, observedAt)
            {
                HoldsEmpty = 0,
                HoldsTotal = 20,
                Organics = 20
            }
        };
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.MoveToAdjacentSector,
                "Try another sector.",
                TargetSector: 2));
        var commandSender = new RecordingCommandSender();
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(situation),
            actionProvider,
            commandSender,
            moveTargetProvider: new RecordingMoveTargetProvider([2, 3]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            statusStore: statusStore,
            portTradeOpportunityProvider: new RecordingPortTradeOpportunityProvider(null));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    observedAt.AddMilliseconds(1),
                    "ServerPrompt",
                    PromptSurface: "Command")));

            await WaitForActivityAsync(
                statusStore,
                "Waiting for port CIM");

            Assert.Empty(commandSender.Commands);
            Assert.Equal(0, actionProvider.CallCount);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RefreshesStalePortCimThenContinuesPlanningWithCarriedCargo()
    {
        var eventHub = new ProxyGameEventHub();
        var statusStore = new AutonomousTestWorkerStatusStore();
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        GameSituationReport situation = CreateMovementEligibleSituation() with
        {
            Snapshot = new GameSnapshot(1, 730, 5, null, observedAt)
            {
                HoldsEmpty = 0,
                HoldsTotal = 20,
                Organics = 20
            }
        };
        var cimProvider = new MutablePortCimOpportunityProvider(isFresh: false);
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "Continue after checking CIM."));
        var commandSender = new RecordingCommandSender();
        var scriptRunner = new RecordingGameScriptRunner(cimProvider.MarkFresh);
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(situation),
            actionProvider,
            commandSender,
            moveTargetProvider: new RecordingMoveTargetProvider([2, 3]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            statusStore: statusStore,
            gameScriptRunner: scriptRunner,
            portTradeOpportunityProvider: cimProvider);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));

            await scriptRunner.CimRefreshed.Task.WaitAsync(TimeSpan.FromSeconds(5));
            await WaitForActivityAsync(statusStore, "Port and warp CIM refreshed");

            Assert.Equal(0, actionProvider.CallCount);
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(DateTimeOffset.UtcNow, "PresenceHeartbeat", PromptSurface: "Command")));

            await WaitForActivityAsync(statusStore, "Holding");

            Assert.True(cimProvider.IsPortCimFresh());
            Assert.Equal(1, scriptRunner.CimRefreshCount);
            Assert.Equal(1, actionProvider.CallCount);
            Assert.Null(actionProvider.ActionContext?.PortTradeOpportunity);
            Assert.Empty(commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task PortCimFailureDoesNotBlockNonTradePlanningWhenCargoHoldsAreEmpty()
    {
        var eventHub = new ProxyGameEventHub();
        var statusStore = new AutonomousTestWorkerStatusStore();
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        GameSituationReport situation = CreateMovementEligibleSituation() with
        {
            Snapshot = new GameSnapshot(1, 730, 5, null, observedAt)
            {
                HoldsEmpty = 20,
                HoldsTotal = 20,
                FuelOre = 0,
                Organics = 0,
                Equipment = 0
            }
        };
        var cimProvider = new MutablePortCimOpportunityProvider(isFresh: false);
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "Continue with verified state."));
        var commandSender = new RecordingCommandSender();
        var scriptRunner = new RecordingGameScriptRunner(
            cimRefreshException: new InvalidOperationException("No fresh port batch was recorded."));
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(situation),
            actionProvider,
            commandSender,
            moveTargetProvider: new RecordingMoveTargetProvider([2, 3]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            statusStore: statusStore,
            gameScriptRunner: scriptRunner,
            portTradeOpportunityProvider: cimProvider);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));

            await WaitForActionCallCountAsync(actionProvider, 1);

            Assert.Equal(1, scriptRunner.CimRefreshCount);
            Assert.False(cimProvider.IsPortCimFresh());
            Assert.Equal(1, actionProvider.CallCount);
            Assert.Null(actionProvider.ActionContext?.PortTradeOpportunity);
            Assert.Empty(commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DefersWorldTradeUntilDensityScannerOwnershipIsVerified()
    {
        var eventHub = new ProxyGameEventHub();
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        GameSituationReport starDockSituation = CreateMovementEligibleSituation() with
        {
            CurrentPromptObservedAt = observedAt,
            Snapshot = new GameSnapshot(551, 300, 300, null, observedAt)
            {
                HoloScannerInstalled = false,
                DensityScannerInstalled = false
            }
        };
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.StartWorldTradeMode,
                "Try World Trade at StarDock."));
        var commandSender = new RecordingCommandSender();
        var scriptRunner = new RecordingGameScriptRunner();
        var statusStore = new AutonomousTestWorkerStatusStore();
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(starDockSituation),
            actionProvider,
            commandSender,
            moveTargetProvider: new RecordingMoveTargetProvider([550]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            statusStore: statusStore,
            gameWorldProvider: new FixedGameWorldProvider(551),
            gameScriptRunner: scriptRunner);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));

            await WaitForActivityAsync(
                statusStore,
                "World Trade deferred until Density or Holo scanning capability is verified");

            Assert.Equal(1, actionProvider.CallCount);
            Assert.Empty(commandSender.Commands);
            Assert.False(scriptRunner.Started.Task.IsCompleted);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BuysAndVerifiesDensityScannerWithoutStartingWorldTrade()
    {
        var eventHub = new ProxyGameEventHub();
        DateTimeOffset observedAt = DateTimeOffset.UtcNow.AddSeconds(-20);
        GameSituationReport starDockSituation = CreateMovementEligibleSituation() with
        {
            CurrentPromptObservedAt = observedAt,
            Snapshot = new GameSnapshot(
                551,
                35_927,
                65_520,
                new Ship("Meridian", string.Empty, "Merchant Cruiser", 551, 27, observedAt),
                observedAt)
            {
                HoloScannerInstalled = false,
                DensityScannerInstalled = false,
                HoldsEmpty = 0,
                HoldsTotal = 18,
                FuelOre = 18,
                Organics = 0,
                Equipment = 0
            }
        };
        var actionProvider = new SequenceActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.RunStarDockShopper,
                "Buy the first-tier Density Scanner."),
            new AutonomousTestActionDecision(
                AutonomousTestAction.RespondToScriptInputPrompt,
                "Refresh the shopper prices.")
            {
                ScriptInputResponse = "Y"
            },
            new AutonomousTestActionDecision(
                AutonomousTestAction.RespondToScriptInputPrompt,
                "Select long-range scanners.")
            {
                ScriptInputResponse = "R"
            },
            new AutonomousTestActionDecision(
                AutonomousTestAction.RespondToScriptInputPrompt,
                "Select the Density Scanner.")
            {
                ScriptInputResponse = "D"
            },
            new AutonomousTestActionDecision(
                AutonomousTestAction.RespondToScriptInputPrompt,
                "Execute the purchase.")
            {
                ScriptInputResponse = "X"
            },
            new AutonomousTestActionDecision(AutonomousTestAction.None, "Continue trading."));
        var scriptRunner = new RecordingGameScriptRunner();
        var statusStore = new AutonomousTestWorkerStatusStore();
        var situationProvider = new MutableSituationProvider(starDockSituation);
        var commandSender = new RecordingCommandSender();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            moveTargetProvider: new RecordingMoveTargetProvider([550]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            statusStore: statusStore,
            gameWorldProvider: new FixedGameWorldProvider(
                551,
                hasOwnedFighter: false,
                isFederationSpace: true),
            gameScriptRunner: scriptRunner);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));
            await scriptRunner.DockShopperStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            string[] scriptPrompts =
            [
                "Item Costs Need To Be Updated, Proceed (Y/N)?",
                "StarDock Shopper menu: <R> Long Range Scanners",
                "Scanner type (Y/N/D)? Holo=Y, Density=D",
                "StarDock Shopper menu: <X> Execute Purchase"
            ];
            for (int inputCount = 1; inputCount <= scriptPrompts.Length; inputCount++)
            {
                DateTimeOffset promptAt = observedAt.AddSeconds(inputCount);
                situationProvider.Enqueue(starDockSituation with
                {
                    CapturedAt = promptAt,
                    ScriptInputPrompt = new GameScriptInputPrompt(
                        "ls-dock-shopper-v33.ts",
                        scriptPrompts[inputCount - 1],
                        SingleKey: true,
                        promptAt)
                });
                eventHub.TryPublish(new ProxySessionEvent(
                    "meridian",
                    new ProxyGameEvent(promptAt, "PresenceHeartbeat")));
                await WaitForDockShopperInputCountAsync(scriptRunner, inputCount);
            }

            Assert.Equal(['Y', 'R', 'D', 'X'], scriptRunner.DockShopperInputs);
            Assert.Equal(ScannerPurchaseTarget.Density, actionProvider.ActionContexts[0].ScannerTarget);
            Assert.False(scriptRunner.Started.Task.IsCompleted);
            Assert.NotEqual(
                nameof(AutonomousTestAction.StartWorldTradeMode),
                statusStore.GetStatus("meridian").LastAction);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BuysHoloScannerAtFederationStarDockAsOptionalUpgradeAfterFunding()
    {
        var eventHub = new ProxyGameEventHub();
        DateTimeOffset observedAt = DateTimeOffset.UtcNow.AddSeconds(-20);
        GameSituationReport starDockSituation = CreateMovementEligibleSituation() with
        {
            CurrentPromptObservedAt = observedAt,
            Snapshot = new GameSnapshot(
                551,
                AutonomousTradePolicy.MinimumWorldTradeUpgradeCredits,
                65_520,
                new Ship("Meridian", string.Empty, "Merchant Cruiser", 551, 27, observedAt),
                observedAt)
            {
                HoloScannerInstalled = false,
                DensityScannerInstalled = true,
                HoldsEmpty = 0,
                HoldsTotal = 18,
                FuelOre = 18,
                Organics = 0,
                Equipment = 0,
                Shields = 10
            }
        };
        var actionProvider = new SequenceActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.None,
                "The Density Scanner is verified, but Fighters and Shields have not both improved yet."),
            new AutonomousTestActionDecision(
                AutonomousTestAction.None,
                "Fighters improved, but Shields still need a verified upgrade."),
            new AutonomousTestActionDecision(
                AutonomousTestAction.RunStarDockShopper,
                "The full Fuel Ore holds and StarDock prerequisites are verified."),
            new AutonomousTestActionDecision(
                AutonomousTestAction.RespondToScriptInputPrompt,
                "The script asks whether it may update its item costs.")
            {
                ScriptInputResponse = "Y"
            },
            new AutonomousTestActionDecision(
                AutonomousTestAction.RespondToScriptInputPrompt,
                "The visible menu offers the Holo Scanner under R.")
            {
                ScriptInputResponse = "R"
            },
            new AutonomousTestActionDecision(
                AutonomousTestAction.RespondToScriptInputPrompt,
                "The prompt asks whether to buy the Holo Scanner.")
            {
                ScriptInputResponse = "Y"
            },
            new AutonomousTestActionDecision(
                AutonomousTestAction.RespondToScriptInputPrompt,
                "The script menu offers X to execute the selected purchase.")
            {
                ScriptInputResponse = "X"
            },
            new AutonomousTestActionDecision(
                AutonomousTestAction.None,
                "The World Trade funding target is already reached.")
            );
        var commandSender = new RecordingCommandSender();
        var scriptRunner = new RecordingGameScriptRunner();
        var statusStore = new AutonomousTestWorkerStatusStore();
        var situationProvider = new MutableSituationProvider(starDockSituation);
        var portCimProvider = new MutablePortCimOpportunityProvider(isFresh: true);
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            moveTargetProvider: new RecordingMoveTargetProvider([550]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            statusStore: statusStore,
            gameWorldProvider: new FixedGameWorldProvider(
                551,
                hasOwnedFighter: false,
                isFederationSpace: true),
            gameScriptRunner: scriptRunner,
            portTradeOpportunityProvider: portCimProvider);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));

            await WaitForActionCallCountAsync(actionProvider, 1);
            Assert.Null(actionProvider.ActionContexts[0].ScannerTarget);
            DateTimeOffset upgradedAt = observedAt.AddSeconds(6);
            situationProvider.Enqueue(starDockSituation with
            {
                CapturedAt = upgradedAt,
                CurrentPromptObservedAt = upgradedAt,
                Snapshot = starDockSituation.Snapshot! with
                {
                    Ship = starDockSituation.Snapshot.Ship! with
                    {
                        Fighters = 28,
                        ObservedAt = upgradedAt
                    },
                    Shields = 10,
                    ObservedAt = upgradedAt
                }
            });
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(upgradedAt, "ServerPrompt", PromptSurface: "Command")));
            await WaitForActionCallCountAsync(actionProvider, 2);
            Assert.Null(actionProvider.ActionContexts[1].ScannerTarget);

            DateTimeOffset shieldedAt = observedAt.AddSeconds(12);
            situationProvider.Enqueue(starDockSituation with
            {
                CapturedAt = shieldedAt,
                CurrentPromptObservedAt = shieldedAt,
                Snapshot = starDockSituation.Snapshot! with
                {
                    Ship = starDockSituation.Snapshot.Ship! with
                    {
                        Fighters = 28,
                        ObservedAt = shieldedAt
                    },
                    Shields = 11,
                    ObservedAt = shieldedAt
                }
            });
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(shieldedAt, "ServerPrompt", PromptSurface: "Command")));
            await WaitForActionCallCountAsync(actionProvider, 3);
            Assert.Equal(ScannerPurchaseTarget.Holo, actionProvider.ActionContexts[2].ScannerTarget);
            Assert.True(actionProvider.ActionContexts[2].StarDockShopperReady);
            await scriptRunner.DockShopperStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            string[] scriptPrompts =
            [
                "Item Costs Need To Be Updated, Proceed (Y/N)?",
                "StarDock Shopper menu: <R> Long Range Scanners",
                "Scanner type (Y/N/D)? Holo=Y, Density=D",
                "StarDock Shopper menu: <X> Execute Purchase"
            ];
            for (int inputCount = 1; inputCount <= scriptPrompts.Length; inputCount++)
            {
                DateTimeOffset promptAt = observedAt.AddSeconds(inputCount + 12);
                situationProvider.Enqueue(starDockSituation with
                {
                    CapturedAt = promptAt,
                    CurrentPromptObservedAt = promptAt,
                    Snapshot = starDockSituation.Snapshot! with
                    {
                        Ship = starDockSituation.Snapshot.Ship! with
                        {
                            Fighters = 28,
                            ObservedAt = promptAt
                        },
                        Shields = 11,
                        ObservedAt = promptAt
                    },
                    ScriptInputPrompt = new GameScriptInputPrompt(
                        "ls-dock-shopper-v33.ts",
                        scriptPrompts[inputCount - 1],
                        SingleKey: true,
                        promptAt)
                });
                eventHub.TryPublish(new ProxySessionEvent(
                    "meridian",
                    new ProxyGameEvent(
                        promptAt,
                        "PresenceHeartbeat")));
                await WaitForDockShopperInputCountAsync(scriptRunner, inputCount);
            }

            Assert.Equal(['Y', 'R', 'Y', 'X'], scriptRunner.DockShopperInputs);
            Assert.Equal(7, actionProvider.CallCount);
            Assert.Null(actionProvider.ActionContexts[0].ScannerTarget);
            Assert.Null(actionProvider.ActionContexts[1].ScannerTarget);
            Assert.Equal(ScannerPurchaseTarget.Holo, actionProvider.ActionContexts[2].ScannerTarget);
            scriptRunner.SetDockShopperRunning(false);
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt.AddSeconds(5), "PresenceHeartbeat")));
            await commandSender.StatusRequestSent.Task.WaitAsync(TimeSpan.FromSeconds(5));
            Assert.Contains(
                commandSender.Commands,
                request => request.Command == GameCommand.RequestShipInformation);

            DateTimeOffset scannerVerifiedAt = DateTimeOffset.UtcNow.AddMilliseconds(50);
            GameSituationReport scannerVerifiedSituation = new(
                scannerVerifiedAt,
                GamePresenceState.Connected,
                GameAuthenticationState.Authenticated,
                GameSynchronizationState.Healthy,
                "Command",
                scannerVerifiedAt,
                new GameSnapshot(
                    551,
                    AutonomousTradePolicy.MinimumWorldTradeUpgradeCredits,
                    65_520,
                    new Ship("Meridian", string.Empty, "Merchant Cruiser", 551, 27, scannerVerifiedAt),
                    scannerVerifiedAt)
                {
                    HoloScannerInstalled = true,
                    DensityScannerInstalled = true,
                    HoldsEmpty = 0,
                    HoldsTotal = 18,
                    FuelOre = 18,
                    Organics = 0,
                    Equipment = 0
                },
                []);
            situationProvider.Enqueue(scannerVerifiedSituation);
            situationProvider.Enqueue(scannerVerifiedSituation);
            await Task.Delay(TimeSpan.FromMilliseconds(60));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(DateTimeOffset.UtcNow, "PresenceHeartbeat")));
            await WaitForActionCallCountAsync(actionProvider, 8);
            AutonomousTestWorkerStatus worldTradeStatus = statusStore.GetStatus("meridian");
            Assert.False(scriptRunner.Started.Task.IsCompleted);

            Assert.Equal(8, actionProvider.CallCount);
            Assert.NotEqual(AutonomousTestAction.StartWorldTradeMode.ToString(), worldTradeStatus.LastAction);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(null)]
    public async Task DoesNotDeployStarDockPrerequisiteFighterInFedSpaceOrWhenSectorSafetyIsUnknown(
        bool? isFederationSpace)
    {
        var eventHub = new ProxyGameEventHub();
        DateTimeOffset firstObservedAt = DateTimeOffset.UtcNow;
        GameSituationReport firstSituation = CreateMovementEligibleSituation() with
        {
            CurrentPromptObservedAt = firstObservedAt,
            Snapshot = new GameSnapshot(
                551,
                AutonomousTradePolicy.MinimumScannerUpgradeCredits,
                300,
                new Ship("Meridian", string.Empty, "Merchant Cruiser", 551, 30, firstObservedAt),
                firstObservedAt)
            {
                HoloScannerInstalled = false,
                DensityScannerInstalled = false,
                HoldsEmpty = 0,
                HoldsTotal = 20,
                FuelOre = 20,
                Organics = 0,
                Equipment = 0
            }
        };
        var situationProvider = new MutableSituationProvider(firstSituation);
        var actionProvider = new SequenceActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.DeployStarDockShopperPrerequisiteFighter,
                "Deploy one fighter so Dock Shopper can start."),
            new AutonomousTestActionDecision(
                AutonomousTestAction.DeployStarDockShopperPrerequisiteFighter,
                "Retry only if the previous deployment was not observed."));
        var commandSender = new RecordingCommandSender();
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            moveTargetProvider: new RecordingMoveTargetProvider([550]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            gameWorldProvider: new FixedGameWorldProvider(
                551,
                hasOwnedFighter: false,
                isFederationSpace: isFederationSpace));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(firstObservedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    firstObservedAt.AddMilliseconds(1),
                    "ServerPrompt",
                    PromptSurface: "Command")));
            await WaitForActionCallCountAsync(actionProvider, 1);

            Assert.Equal(isFederationSpace, actionProvider.ActionContexts[0].CurrentSectorIsFederationSpace);
            Assert.False(actionProvider.ActionContexts[0].StarDockFighterDeploymentAvailable);
            Assert.Empty(commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task AllowsDockShopperAtFederationStarDockWithoutDeployingFighter()
    {
        var eventHub = new ProxyGameEventHub();
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        GameSituationReport starDockSituation = CreateMovementEligibleSituation() with
        {
            CurrentPromptObservedAt = observedAt,
            Snapshot = new GameSnapshot(
                551,
                AutonomousTradePolicy.MinimumScannerUpgradeCredits,
                300,
                null,
                observedAt)
            {
                HoloScannerInstalled = false,
                DensityScannerInstalled = false,
                HoldsEmpty = 0,
                HoldsTotal = 20,
                FuelOre = 20,
                Organics = 0,
                Equipment = 0
            }
        };
        var actionProvider = new SequenceActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.RunStarDockShopper,
                "Start the scanner shopper without deploying a fighter in Federation space."));
        var commandSender = new RecordingCommandSender();
        var scriptRunner = new RecordingGameScriptRunner();
        var statusStore = new AutonomousTestWorkerStatusStore();
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(starDockSituation),
            actionProvider,
            commandSender,
            moveTargetProvider: new RecordingMoveTargetProvider([550]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            statusStore: statusStore,
            gameWorldProvider: new FixedGameWorldProvider(
                551,
                hasOwnedFighter: false,
                isFederationSpace: true),
            gameScriptRunner: scriptRunner);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));

            await WaitForActionCallCountAsync(actionProvider, 1);
            Assert.True(actionProvider.ActionContexts[0].StarDockShopperReady);
            await scriptRunner.DockShopperStarted.Task.WaitAsync(TimeSpan.FromSeconds(5));

            Assert.False(actionProvider.ActionContexts[0].StarDockSectorHasOwnedFighter);
            Assert.True(actionProvider.ActionContexts[0].CurrentSectorIsFederationSpace);
            Assert.Empty(commandSender.Commands);
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt.AddSeconds(1), "PresenceHeartbeat")));
            await WaitForActivityAsync(
                statusStore,
                "Dock Shopper is running; waiting for its next observed script prompt");
            Assert.Empty(scriptRunner.DockShopperInputs);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task DockShopperDecisionReportsWhenStarDockFighterPrerequisiteIsMissing()
    {
        var eventHub = new ProxyGameEventHub();
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        GameSituationReport starDockSituation = CreateMovementEligibleSituation() with
        {
            CurrentPromptObservedAt = observedAt,
            Snapshot = new GameSnapshot(
                551,
                AutonomousTradePolicy.MinimumScannerUpgradeCredits,
                300,
                null,
                observedAt)
            {
                HoloScannerInstalled = false,
                DensityScannerInstalled = false,
                HoldsEmpty = 0,
                HoldsTotal = 20,
                FuelOre = 20,
                Organics = 0,
                Equipment = 0
            }
        };
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(
                AutonomousTestAction.None,
                "The full Fuel Ore holds are in place, but the StarDock fighter prerequisite is unknown."));
        var commandSender = new RecordingCommandSender();
        var scriptRunner = new RecordingGameScriptRunner();
        var statusStore = new AutonomousTestWorkerStatusStore();
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(starDockSituation),
            actionProvider,
            commandSender,
            moveTargetProvider: new RecordingMoveTargetProvider([550]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            statusStore: statusStore,
            gameWorldProvider: new FixedGameWorldProvider(551, hasOwnedFighter: false),
            gameScriptRunner: scriptRunner);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));

            await WaitForActivityAsync(statusStore, "Holding");

            AutonomousTestWorkerStatus status = statusStore.GetStatus("meridian");
            Assert.False(actionProvider.ActionContext!.StarDockShopperReady);
            Assert.False(scriptRunner.DockShopperStarted.Task.IsCompleted);
            Assert.Contains("freshOwnedOrCorporateFighter=False", status.LastRationale);
            Assert.Contains("Dock Shopper readiness: ready=False", status.LastRationale);
            Assert.Empty(commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task WaitsForFullFuelOreHoldsBeforeOfferingDockShopper()
    {
        var eventHub = new ProxyGameEventHub();
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        GameSituationReport starDockSituation = CreateMovementEligibleSituation() with
        {
            CurrentPromptObservedAt = observedAt,
            Snapshot = new GameSnapshot(
                551,
                AutonomousTradePolicy.MinimumScannerUpgradeCredits,
                300,
                null,
                observedAt)
            {
                HoloScannerInstalled = false,
                DensityScannerInstalled = false,
                HoldsEmpty = 20,
                HoldsTotal = 20,
                FuelOre = 0
            }
        };
        var actionProvider = new RecordingActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "Acquire Fuel Ore first."));
        var commandSender = new RecordingCommandSender();
        var scriptRunner = new RecordingGameScriptRunner();
        var statusStore = new AutonomousTestWorkerStatusStore();
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(starDockSituation),
            actionProvider,
            commandSender,
            moveTargetProvider: new RecordingMoveTargetProvider([550]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            statusStore: statusStore,
            gameWorldProvider: new FixedGameWorldProvider(551, hasOwnedFighter: true),
            gameScriptRunner: scriptRunner);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));
            await WaitForActivityAsync(statusStore, "Holding");

            Assert.False(actionProvider.ActionContext!.StarDockShopperReady);
            Assert.Equal(TradeProduct.FuelOre, actionProvider.ActionContext.PreferredTradeProduct);
            Assert.False(scriptRunner.DockShopperStarted.Task.IsCompleted);
            Assert.Empty(commandSender.Commands);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task TradesAtACompatiblePortOnceBeforeResumingMovementDecisions()
    {
        var eventHub = new ProxyGameEventHub();
        var actionProvider = new SequenceActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "Continue after trade."));
        var commandSender = new RecordingCommandSender();
        var statusStore = new AutonomousTestWorkerStatusStore();
        var startingSituation = CreateMovementEligibleSituation();
        startingSituation = startingSituation with
        {
            Snapshot = new GameSnapshot(
                1,
                1000,
                5,
                null,
                startingSituation.Snapshot!.ObservedAt)
            {
                HoldsEmpty = 20,
                HoldsTotal = 20,
                Organics = 0
            }
        };
        var situationProvider = new MutableSituationProvider(startingSituation);
        var opportunity = new AutonomousPortTradeOpportunity(
            PartnerSector: 2,
            TradeProduct.Organics,
            SellingExistingCargo: false);
        using var worker = CreateWorker(
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            statusStore: statusStore,
            portTradeOpportunityProvider: new RecordingPortTradeOpportunityProvider(opportunity));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset promptAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));
            await WaitForCommandCountAsync(commandSender, 1);
            Assert.Equal(
                new GameCommandRequest(GameCommand.DisplayCurrentSector),
                commandSender.Commands.ToArray()[0]);

            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt.AddMilliseconds(2), "ServerPrompt", PromptSurface: "Command")));
            await WaitForCommandCountAsync(commandSender, 2);
            Assert.Equal(
                new GameCommandRequest(
                    GameCommand.TradeAtCurrentPort,
                    Product: TradeProduct.Organics),
                commandSender.Commands.ToArray()[1]);
            Assert.Equal(0, actionProvider.CallCount);
            await WaitForActivityAsync(statusStore, "Port trade sent; awaiting verification");

            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt.AddMilliseconds(3), "PresenceHeartbeat", PromptSurface: "Command")));
            await WaitForCommandCountAsync(commandSender, 3);
            Assert.Equal(
                    new GameCommandRequest(GameCommand.RequestShipInformation),
                    commandSender.Commands.ToArray()[2]);

            var verifiedSituation = CreateMovementEligibleSituation();
            situationProvider.Enqueue(verifiedSituation with
            {
                    Snapshot = new GameSnapshot(
                        1,
                        500,
                        4,
                        null,
                        DateTimeOffset.UtcNow)
                    {
                        HoldsEmpty = 0,
                        HoldsTotal = 20,
                        Organics = 20
                    }
            });
            eventHub.TryPublish(new ProxySessionEvent(
                    "meridian",
                    new ProxyGameEvent(promptAt.AddMilliseconds(4), "PresenceHeartbeat", PromptSurface: "Command")));
            await WaitForActionCallCountAsync(actionProvider, 1);

            Assert.Equal(3, commandSender.Commands.Count);
            Assert.True(actionProvider.ActionContexts.Single().PortTradeAttemptedAtCurrentSector);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task BlocksPortTradeWhenShipHasEscapePodCapacity()
    {
        var eventHub = new ProxyGameEventHub();
        var commandSender = new RecordingCommandSender();
        var statusStore = new AutonomousTestWorkerStatusStore();
        var situation = CreateMovementEligibleSituation();
        DateTimeOffset observedAt = situation.Snapshot!.ObservedAt;
        situation = situation with
        {
            Snapshot = new GameSnapshot(
                situation.Snapshot.CurrentSector,
                5_000,
                100,
                new Ship("Escape Pod", "Meridian", "Escape Pod", 1, 0, observedAt),
                observedAt)
            {
                HoldsEmpty = 1,
                HoldsTotal = 1,
                Organics = 0
            }
        };
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(situation),
            new RecordingActionProvider(
                new AutonomousTestActionDecision(AutonomousTestAction.None, "No model action is required.")),
            commandSender,
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            statusStore: statusStore,
            portTradeOpportunityProvider: new RecordingPortTradeOpportunityProvider(
                new AutonomousPortTradeOpportunity(2, TradeProduct.Organics, SellingExistingCargo: false)));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset promptAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));
            await commandSender.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5));

            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt.AddMilliseconds(2), "ServerPrompt", PromptSurface: "Command")));
            await WaitForActivityAsync(statusStore, "Port trading blocked; recover a viable ship before trading");

            Assert.DoesNotContain(
                commandSender.Commands,
                command => command.Command == GameCommand.TradeAtCurrentPort);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RevalidatesPortTradeWithTheSameFuelOrePreference()
    {
        var eventHub = new ProxyGameEventHub();
        var startingSituation = CreateFuelOreBootstrapSituation();
        var actionProvider = new SequenceActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "Continue after trade."));
        var commandSender = new RecordingCommandSender();
        var opportunityProvider = new PreferenceSensitivePortTradeOpportunityProvider();
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(startingSituation),
            actionProvider,
            commandSender,
            new RecordingMoveTargetProvider([2, 3]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            gameWorldProvider: new FixedGameWorldProvider(starDockSector: 2),
            portTradeOpportunityProvider: opportunityProvider);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset promptAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));
            await WaitForCommandCountAsync(commandSender, 1);

            Assert.Equal(
                new GameCommandRequest(GameCommand.TradeAtCurrentPort, Product: TradeProduct.FuelOre),
                commandSender.Commands.Single());
            Assert.Equal(
                [TradeProduct.FuelOre, TradeProduct.FuelOre],
                opportunityProvider.PreferredProducts.ToArray());
            Assert.Equal(0, actionProvider.CallCount);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task RetriesWhenFreshPortOpportunityChangesInsteadOfGoingIdle()
    {
        var eventHub = new ProxyGameEventHub();
        var startingSituation = CreateFuelOreBootstrapSituation();
        var actionProvider = new SequenceActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "Continue after trade."));
        var commandSender = new RecordingCommandSender();
        var statusStore = new AutonomousTestWorkerStatusStore();
        var opportunityProvider = new SequencePortTradeOpportunityProvider(
            new AutonomousPortTradeOpportunity(2, TradeProduct.FuelOre, SellingExistingCargo: false),
            new AutonomousPortTradeOpportunity(2, TradeProduct.Equipment, SellingExistingCargo: false),
            new AutonomousPortTradeOpportunity(2, TradeProduct.Equipment, SellingExistingCargo: false),
            new AutonomousPortTradeOpportunity(2, TradeProduct.Equipment, SellingExistingCargo: false));
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(startingSituation),
            actionProvider,
            commandSender,
            new RecordingMoveTargetProvider([2, 3]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            gameWorldProvider: new FixedGameWorldProvider(starDockSector: 2),
            statusStore: statusStore,
            portTradeOpportunityProvider: opportunityProvider);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset promptAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));
            await WaitForActivityAsync(statusStore, "Port trade opportunity changed; retrying on the next heartbeat");
            Assert.Empty(commandSender.Commands);

            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt.AddMilliseconds(2), "PresenceHeartbeat", PromptSurface: "Command")));
            await WaitForCommandCountAsync(commandSender, 1);

            Assert.Equal(
                new GameCommandRequest(GameCommand.TradeAtCurrentPort, Product: TradeProduct.Equipment),
                commandSender.Commands.Single());
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task SellsCarriedCargoAtStarDockWithoutWaitingForStalePortCim()
    {
        var eventHub = new ProxyGameEventHub();
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        var startingSituation = CreateMovementEligibleSituation() with
        {
            CurrentPromptObservedAt = observedAt,
            Snapshot = new GameSnapshot(
                551,
                35_927,
                65_520,
                null,
                observedAt)
            {
                HoldsEmpty = 0,
                HoldsTotal = 18,
                FuelOre = 18,
                HoloScannerInstalled = false,
                DensityScannerInstalled = false
            }
        };
        var actionProvider = new SequenceActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "No model decision needed."));
        var commandSender = new RecordingCommandSender();
        var scriptRunner = new RecordingGameScriptRunner();
        var statusStore = new AutonomousTestWorkerStatusStore();
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(startingSituation),
            actionProvider,
            commandSender,
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            gameWorldProvider: new FixedGameWorldProvider(551, currentRefurbPortClass: 9),
            gameScriptRunner: scriptRunner,
            portTradeOpportunityProvider: new MutablePortCimOpportunityProvider(isFresh: false),
            statusStore: statusStore);

        await worker.StartAsync(CancellationToken.None);
        try
        {
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(observedAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    observedAt.AddMilliseconds(1),
                    "ServerPrompt",
                    PromptSurface: "Command")));
            await WaitForCommandCountAsync(commandSender, 1);
            Assert.Equal(
                new GameCommandRequest(GameCommand.DisplayCurrentSector),
                commandSender.Commands.ToArray()[0]);

            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(
                    observedAt.AddMilliseconds(2),
                    "ServerPrompt",
                    PromptSurface: "Command")));
            try
            {
                await WaitForCommandCountAsync(commandSender, 2);
            }
            catch (TaskCanceledException)
            {
                AutonomousTestWorkerStatus status = statusStore.GetStatus("meridian");
                throw new InvalidOperationException(
                    $"Expected StarDock cargo sale, but worker activity was '{status.Activity}' " +
                    $"with last action '{status.LastAction}' and reason '{status.LastRationale}'.");
            }

            Assert.Equal(
                new GameCommandRequest(
                    GameCommand.TradeAtCurrentPort,
                    Product: TradeProduct.FuelOre)
                {
                    SellingExistingCargo = true
                },
                commandSender.Commands.ToArray()[1]);
            Assert.Equal(0, actionProvider.CallCount);
            Assert.False(scriptRunner.CimRefreshed.Task.IsCompleted);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    [Fact]
    public async Task SendsThePlannedFollowUpBuyWithACarriedCargoSale()
    {
        var eventHub = new ProxyGameEventHub();
        var startingSituation = CreateMovementEligibleSituation();
        startingSituation = startingSituation with
        {
            Snapshot = new GameSnapshot(
                1,
                33_475,
                65_483,
                null,
                startingSituation.Snapshot!.ObservedAt)
            {
                HoldsEmpty = 0,
                HoldsTotal = 20,
                FuelOre = 20
            }
        };
        var actionProvider = new SequenceActionProvider(
            new AutonomousTestActionDecision(AutonomousTestAction.None, "Continue after the planned port trade."));
        var commandSender = new RecordingCommandSender();
        var opportunity = new AutonomousPortTradeOpportunity(
            PartnerSector: 2,
            TradeProduct.FuelOre,
            SellingExistingCargo: true)
        {
            FollowUpBuyProduct = TradeProduct.Organics,
            FollowUpPartnerSector = 2
        };
        using var worker = CreateWorker(
            eventHub,
            new RecordingSituationProvider(startingSituation),
            actionProvider,
            commandSender,
            new RecordingMoveTargetProvider([2, 3]),
            allowGameActions: true,
            maximumActionsPerSession: AutonomousTestWorker.UnlimitedActionsPerSession,
            portTradeOpportunityProvider: new RecordingPortTradeOpportunityProvider(opportunity));

        await worker.StartAsync(CancellationToken.None);
        try
        {
            DateTimeOffset promptAt = DateTimeOffset.UtcNow;
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt, "Connected")));
            eventHub.TryPublish(new ProxySessionEvent(
                "meridian",
                new ProxyGameEvent(promptAt.AddMilliseconds(1), "ServerPrompt", PromptSurface: "Command")));
            await WaitForCommandCountAsync(commandSender, 1);

            Assert.Equal(
                new GameCommandRequest(
                    GameCommand.TradeAtCurrentPort,
                    Product: TradeProduct.FuelOre)
                {
                    SellingExistingCargo = true,
                    FollowUpBuyProduct = TradeProduct.Organics
                },
                commandSender.Commands.Single());
            Assert.Equal(0, actionProvider.CallCount);
        }
        finally
        {
            await worker.StopAsync(CancellationToken.None);
        }
    }

    private static AutonomousTestWorker CreateWorker(
        ProxyGameEventHub eventHub,
        IGameSituationProvider situationProvider,
        IAutonomousTestActionProvider actionProvider,
        RecordingCommandSender commandSender,
        IAutonomousMoveTargetProvider? moveTargetProvider = null,
        bool allowGameActions = false,
        int maximumActionsPerSession = 1,
        IAutonomousActionKnowledgeProvider? actionKnowledgeProvider = null,
        IAutonomousActionObjectiveProvider? actionObjectiveProvider = null,
        IAutonomousMoveApprovalGate? moveApprovalGate = null,
        AutonomousTestWorkerStatusStore? statusStore = null,
        string modelName = "qwen3:8b",
        IAutonomousGameWorldProvider? gameWorldProvider = null,
        IAutonomousGameScriptRunner? gameScriptRunner = null,
        IAutonomousPortTradeOpportunityProvider? portTradeOpportunityProvider = null,
        IAgentStore? agentStore = null,
        IReactiveEventStore? reactiveEventStore = null,
        IAutonomousFedSpaceFastStartProvider? fedSpaceFastStartProvider = null) =>
        new(
            "meridian",
            modelName,
            new Uri("http://localhost:11434/"),
            eventHub,
            situationProvider,
            actionProvider,
            commandSender,
            NullLogger<AutonomousTestWorker>.Instance,
            moveTargetProvider,
            allowGameActions,
            maximumActionsPerSession,
            actionKnowledgeProvider,
            actionObjectiveProvider,
            moveApprovalGate,
            statusStore,
            gameWorldProvider: gameWorldProvider,
            gameScriptRunner: gameScriptRunner,
            portTradeOpportunityProvider: portTradeOpportunityProvider,
            agentStore: agentStore,
            reactiveEventStore: reactiveEventStore,
            fedSpaceFastStartProvider: fedSpaceFastStartProvider);

    private sealed class RecordingAgentStore(AgentContainer agent) : IAgentStore
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<AgentContainer>> GetAgentsAsync(
            CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<AgentContainer>>([agent]);

        public Task<AgentContainer?> GetAgentAsync(
            string agentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult<AgentContainer?>(
                string.Equals(agentId, agent.AgentId, StringComparison.OrdinalIgnoreCase)
                    ? agent
                    : null);

        public Task SaveAgentAsync(
            AgentContainer value,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task AppendJournalEntryAsync(
            string agentId,
            AgentJournalEntry entry,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    [Fact]
    public async Task MovementApprovalGatePublishesAndResolvesOnlyTheMatchingRequest()
    {
        var gate = new AutonomousMoveApprovalGate();
        var request = new AutonomousMoveApprovalRequest(
            "approval-1",
            "meridian",
            1,
            3,
            1,
            "Scout one adjacent sector.");

        var decision = gate.RequestApprovalAsync(request);

        Assert.Equal(request, gate.PendingRequest);
        Assert.True(gate.Resolve(request.ApprovalId, approved: true));
        Assert.True(await decision);
        Assert.Null(gate.PendingRequest);
        Assert.False(gate.Resolve(request.ApprovalId, approved: true));
    }

    private static GameSituationReport CreateEligibleSituation()
    {
        var now = DateTimeOffset.UtcNow;
        return new GameSituationReport(
            now,
            GamePresenceState.Connected,
            GameAuthenticationState.Authenticated,
            GameSynchronizationState.Stale,
            "Command",
            now,
            null,
            ["Synchronization health"]);
    }

    private static GameSituationReport CreateMovementEligibleSituation()
    {
        var now = DateTimeOffset.UtcNow;
        return new GameSituationReport(
            now,
            GamePresenceState.Connected,
            GameAuthenticationState.Authenticated,
            GameSynchronizationState.Healthy,
            "Command",
            now,
            new GameSnapshot(1, 1000, 5, null, now),
            []);
    }

    private static GameSituationReport CreateFuelOreBootstrapSituation()
    {
        GameSituationReport situation = CreateMovementEligibleSituation();
        return situation with
        {
            Snapshot = new GameSnapshot(
                1,
                40_000,
                5,
                null,
                situation.Snapshot!.ObservedAt)
            {
                HoldsEmpty = 18,
                HoldsTotal = 18,
                FuelOre = 0,
                HoloScannerInstalled = false,
                DensityScannerInstalled = false
            },
            IntegratedGameClient = new IntegratedGameClientSnapshot(
                DateTimeOffset.UtcNow,
                TerminalConnected: true,
                TwxDatabaseAvailable: true,
                ScriptServiceAvailable: true,
                SectorCount: 1_000,
                CurrentSector: 1,
                StarDockSector: 2,
                LastPortCimAt: null,
                RelevantSectors: [],
                RunningScripts: [],
                ScriptCapabilities: [])
        };
    }

    private sealed class RecordingSituationProvider(GameSituationReport situation) : IGameSituationProvider
    {
        private int _callCount;

        public int CallCount => Volatile.Read(ref _callCount);
        public TaskCompletionSource Captured { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<GameSituationReport> CaptureSituationAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref _callCount);
            Captured.TrySetResult();
            return Task.FromResult(situation);
        }
    }

    private sealed class SequenceSituationProvider(params GameSituationReport[] situations)
        : IGameSituationProvider
    {
        private int _index;

        public int CallCount => Volatile.Read(ref _index);

        public Task<GameSituationReport> CaptureSituationAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            var index = Math.Min(Interlocked.Increment(ref _index) - 1, situations.Length - 1);
            return Task.FromResult(situations[index]);
        }
    }

    private sealed class MutableSituationProvider(GameSituationReport defaultSituation)
        : IGameSituationProvider
    {
        private readonly ConcurrentQueue<GameSituationReport> _queuedSituations = new();

        public void Enqueue(GameSituationReport situation) => _queuedSituations.Enqueue(situation);

        public Task<GameSituationReport> CaptureSituationAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                _queuedSituations.TryDequeue(out GameSituationReport? situation)
                    ? situation
                    : defaultSituation);
        }
    }

    private sealed class SectorSequenceSituationProvider(params int[] sectors)
        : IGameSituationProvider
    {
        private int _index;

        public Task<GameSituationReport> CaptureSituationAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var index = Math.Min(Interlocked.Increment(ref _index) - 1, sectors.Length - 1);
            var observedAt = DateTimeOffset.UtcNow;
            return Task.FromResult(new GameSituationReport(
                observedAt,
                GamePresenceState.Connected,
                GameAuthenticationState.Authenticated,
                GameSynchronizationState.Healthy,
                "Command",
                observedAt,
                new GameSnapshot(sectors[index], 1000, 5, null, observedAt),
                []));
        }
    }

    private sealed class RecordingActionProvider(AutonomousTestActionDecision decision)
        : IAutonomousTestActionProvider
    {
        private int _callCount;

        public string Id => "Ollama";
        public int CallCount => Volatile.Read(ref _callCount);
        public ModelConfiguration? ModelConfiguration { get; private set; }
        public AutonomousTestActionContext? ActionContext { get; private set; }

        public Task<AutonomousTestActionDecision> DecideAsync(
            ModelConfiguration configuration,
            AutonomousTestActionContext context,
            CancellationToken cancellationToken = default)
        {
            ModelConfiguration = configuration;
            ActionContext = context;
            Interlocked.Increment(ref _callCount);
            return Task.FromResult(decision);
        }
    }

    private sealed class RecordingReactiveEventStore(IReadOnlyList<ReactiveEventRecord> records)
        : IReactiveEventStore
    {
        public Task InitializeAsync(CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task SaveAsync(
            ReactiveEventRecord record,
            CancellationToken cancellationToken = default) =>
            Task.CompletedTask;

        public Task<IReadOnlyList<ReactiveEventRecord>> GetRecentAsync(
            string? agentId = null,
            int limit = 100,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<ReactiveEventRecord>>(
                records
                    .Where(record =>
                        agentId is null ||
                        string.Equals(record.Event.AgentId, agentId, StringComparison.OrdinalIgnoreCase))
                    .Take(limit)
                    .ToArray());
        }
    }

    private sealed class SequenceActionProvider(params AutonomousTestActionDecision[] decisions)
        : IAutonomousTestActionProvider
    {
        private int _callCount;
        private readonly ConcurrentQueue<AutonomousTestActionContext> _actionContexts = new();

        public string Id => "Ollama";
        public int CallCount => Volatile.Read(ref _callCount);
        public IReadOnlyList<AutonomousTestActionContext> ActionContexts => _actionContexts.ToArray();

        public Task<AutonomousTestActionDecision> DecideAsync(
            ModelConfiguration configuration,
            AutonomousTestActionContext context,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            _actionContexts.Enqueue(context);
            var index = Math.Min(Interlocked.Increment(ref _callCount) - 1, decisions.Length - 1);
            return Task.FromResult(decisions[index]);
        }
    }

    private sealed class RecordingMoveTargetProvider(
        IReadOnlyList<int> adjacentSectors,
        IReadOnlyList<AdjacentSectorKnowledge>? knowledge = null,
        bool requiresCurrentSectorDisplayBeforeMovement = false)
        : IAutonomousMoveTargetProvider
    {
        public bool RequiresCurrentSectorDisplayBeforeMovement =>
            requiresCurrentSectorDisplayBeforeMovement;

        public Task<IReadOnlyList<int>> GetFreshAdjacentSectorIdsAsync(
            int? currentSector,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(adjacentSectors);

        public Task<IReadOnlyList<AdjacentSectorKnowledge>> GetSectorKnowledgeAsync(
            IReadOnlyList<int> sectorIds,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(knowledge ?? Array.Empty<AdjacentSectorKnowledge>());
    }

    private sealed class SectorMoveTargetProvider(
        IReadOnlyDictionary<int, IReadOnlyList<int>> adjacentSectorsByOrigin)
        : IAutonomousMoveTargetProvider
    {
        public Task<IReadOnlyList<int>> GetFreshAdjacentSectorIdsAsync(
            int? currentSector,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(
                currentSector is { } sectorId &&
                adjacentSectorsByOrigin.TryGetValue(sectorId, out var adjacentSectors)
                    ? adjacentSectors
                    : (IReadOnlyList<int>)Array.Empty<int>());
        }

        public Task<IReadOnlyList<AdjacentSectorKnowledge>> GetSectorKnowledgeAsync(
            IReadOnlyList<int> sectorIds,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<IReadOnlyList<AdjacentSectorKnowledge>>([]);
        }
    }

    private sealed class RecordingActionKnowledgeProvider(
        IReadOnlyList<AutonomousActionKnowledgeItem> knowledge)
        : IAutonomousActionKnowledgeProvider
    {
        public Task<IReadOnlyList<AutonomousActionKnowledgeItem>> GetRelevantKnowledgeAsync(
            string agentId,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(knowledge);
    }

    private sealed class RecordingActionObjectiveProvider(AutonomousActionObjective? objective)
        : IAutonomousActionObjectiveProvider
    {
        public ConcurrentQueue<string> RequestedReasons { get; } = new();

        public Task<AutonomousActionObjective?> GetOrCreateActiveObjectiveAsync(
            string agentId,
            GameSnapshot snapshot,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(objective);

        public void RequestStrategicReview(string agentId, string reason) =>
            RequestedReasons.Enqueue(reason);
    }

    private sealed class RecordingCommandSender :
        IGameCommandSender,
        IAutonomousPortTradePromptRecovery
    {
        public ConcurrentQueue<GameCommandRequest> Commands { get; } = new();
        public ConcurrentQueue<DateTimeOffset> UntrackedPortTradePrompts { get; } = new();
        public ConcurrentQueue<TradeProduct?> UntrackedFollowUpBuyProducts { get; } = new();
        public TaskCompletionSource Sent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StatusRequestSent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource PortTradeRecoveryRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource SecondPortTradeRecoveryRequested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public Task SendAsync(
            string agentId,
            GameCommandRequest request,
            CancellationToken cancellationToken = default)
        {
            Commands.Enqueue(request);
            Sent.TrySetResult();
            if (request.Command == GameCommand.RequestShipInformation)
            {
                StatusRequestSent.TrySetResult();
            }
            return Task.CompletedTask;
        }

        public Task RecoverUntrackedPortTradeAsync(
            string agentId,
            DateTimeOffset promptObservedAt,
            TradeProduct? followUpBuyProduct = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            UntrackedPortTradePrompts.Enqueue(promptObservedAt);
            UntrackedFollowUpBuyProducts.Enqueue(followUpBuyProduct);
            if (UntrackedPortTradePrompts.Count == 1)
            {
                PortTradeRecoveryRequested.TrySetResult();
            }
            else if (UntrackedPortTradePrompts.Count == 2)
            {
                SecondPortTradeRecoveryRequested.TrySetResult();
            }

            return Task.CompletedTask;
        }
    }

    private sealed class FixedGameWorldProvider(
        int? starDockSector,
        int? nextSector = null,
        bool hasOwnedFighter = false,
        int? currentRefurbPortClass = null,
        bool? isFederationSpace = null)
        : IAutonomousGameWorldProvider
    {
        public Task<int?> GetStarDockSectorAsync(CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(starDockSector);
        }

        public Task<int?> GetNextSectorTowardStarDockAsync(
            int currentSector,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(nextSector);
        }

        public Task<int?> GetNextSectorTowardSectorAsync(
            int currentSector,
            int targetSector,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(nextSector);
        }

        public Task<bool> HasFreshOwnedFighterInSectorAsync(
            int sector,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(hasOwnedFighter);
        }

        public Task<bool?> IsFederationSpaceAsync(
            int sector,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(isFederationSpace);
        }

        public Task<int?> GetNextSectorTowardNearestRefurbPortAsync(
            int currentSector,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<int?>(nextSector);
        }

        public Task<int?> GetCurrentRefurbPortClassAsync(
            int sector,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(currentRefurbPortClass);
        }
    }

    private sealed class RecordingPortTradeOpportunityProvider(
        AutonomousPortTradeOpportunity? opportunity)
        : IAutonomousPortTradeOpportunityProvider
    {
        public Task<AutonomousPortTradeOpportunity?> GetOpportunityAsync(
            int currentSector,
            int? previousSector,
            int? nextSector,
            GameSnapshot snapshot,
            CancellationToken cancellationToken = default,
            IReadOnlySet<int>? excludedTradeSectors = null,
            TradeProduct? preferredProduct = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(opportunity);
        }
    }

    private sealed class RecordingFedSpaceFastStartProvider(
        int? nextSweepSector,
        AutonomousPortTradeOpportunity? tradeOpportunity = null)
        : IAutonomousFedSpaceFastStartProvider
    {
        public IReadOnlySet<int> LastVisitedSectors { get; private set; } = new HashSet<int>();
        public int SweepCallCount { get; private set; }

        public Task<int?> GetNextFedSpaceSweepSectorAsync(
            int currentSector,
            IReadOnlySet<int> visitedSectors,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            SweepCallCount++;
            LastVisitedSectors = new HashSet<int>(visitedSectors);
            return Task.FromResult(nextSweepSector);
        }

        public Task<AutonomousPortTradeOpportunity?> GetFedSpaceTradeOpportunityAsync(
            int currentSector,
            int? nextSector,
            GameSnapshot snapshot,
            IReadOnlySet<int> excludedTradeSectors,
            TradeProduct? preferredProduct = null,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(tradeOpportunity);
        }

        public Task<int?> GetNextSectorTowardFedSpaceTradeTargetAsync(
            int currentSector,
            int targetSector,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<int?>(currentSector == targetSector ? null : targetSector);
        }
    }

    private sealed class PreferenceSensitivePortTradeOpportunityProvider
        : IAutonomousPortTradeOpportunityProvider
    {
        public ConcurrentQueue<TradeProduct?> PreferredProducts { get; } = new();

        public Task<AutonomousPortTradeOpportunity?> GetOpportunityAsync(
            int currentSector,
            int? previousSector,
            int? nextSector,
            GameSnapshot snapshot,
            CancellationToken cancellationToken = default,
            IReadOnlySet<int>? excludedTradeSectors = null,
            TradeProduct? preferredProduct = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            PreferredProducts.Enqueue(preferredProduct);
            TradeProduct product = preferredProduct ?? TradeProduct.Equipment;
            return Task.FromResult<AutonomousPortTradeOpportunity?>(
                new AutonomousPortTradeOpportunity(2, product, SellingExistingCargo: false));
        }
    }

    private sealed class SequencePortTradeOpportunityProvider(
        params AutonomousPortTradeOpportunity?[] opportunities)
        : IAutonomousPortTradeOpportunityProvider
    {
        private int _index;

        public Task<AutonomousPortTradeOpportunity?> GetOpportunityAsync(
            int currentSector,
            int? previousSector,
            int? nextSector,
            GameSnapshot snapshot,
            CancellationToken cancellationToken = default,
            IReadOnlySet<int>? excludedTradeSectors = null,
            TradeProduct? preferredProduct = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            int index = Math.Min(Interlocked.Increment(ref _index) - 1, opportunities.Length - 1);
            return Task.FromResult(opportunities[index]);
        }
    }

    private sealed class RecordingGameScriptRunner(
        Action? afterCimRefresh = null,
        Exception? cimRefreshException = null) :
        IAutonomousGameScriptRunner,
        IAutonomousGameCimRefresher
    {
        private int _isRunning;
        private int _isDockShopperRunning;
        private int _cimRefreshCount;

        public TaskCompletionSource Started { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Stopped { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource DockShopperStarted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource StatusChecked { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource CimRefreshed { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public ConcurrentQueue<char> DockShopperInputs { get; } = new();
        public ConcurrentQueue<AutonomousRefurbUpgrade> RefurbUpgrades { get; } = new();
        public TaskCompletionSource RefurbMenuOpened { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource RefurbPurchaseExecuted { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CimRefreshCount => Volatile.Read(ref _cimRefreshCount);

        public Task StartWorldTradeModeAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref _isRunning, 1);
            Started.TrySetResult();
            return Task.CompletedTask;
        }

        public Task StopWorldTradeModeAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref _isRunning, 0);
            Stopped.TrySetResult();
            return Task.CompletedTask;
        }

        public Task<bool> IsWorldTradeModeRunningAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            StatusChecked.TrySetResult();
            return Task.FromResult(Volatile.Read(ref _isRunning) != 0);
        }

        public Task StartStarDockShopperAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Volatile.Write(ref _isDockShopperRunning, 1);
            DockShopperStarted.TrySetResult();
            return Task.CompletedTask;
        }

        public Task<bool> IsStarDockShopperRunningAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult(Volatile.Read(ref _isDockShopperRunning) != 0);
        }

        public Task SendStarDockShopperInputAsync(
            string agentId,
            char input,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            DockShopperInputs.Enqueue(input);
            return Task.CompletedTask;
        }

        public Task<AutonomousRefurbQuote> OpenRefurbMenuAsync(
            string agentId,
            int currentSector,
            int portClass,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefurbMenuOpened.TrySetResult();
            return Task.FromResult(new AutonomousRefurbQuote(
                currentSector,
                portClass,
                100_000,
                5,
                10,
                100,
                10,
                100,
                DateTimeOffset.UtcNow));
        }

        public Task ExecuteRefurbUpgradeAsync(
            string agentId,
            AutonomousRefurbUpgrade upgrade,
            AutonomousRefurbQuote quote,
            long synchronizedCredits,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            RefurbUpgrades.Enqueue(upgrade);
            RefurbPurchaseExecuted.TrySetResult();
            return Task.CompletedTask;
        }

        public Task CloseRefurbMenuAsync(
            string agentId,
            int portClass,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.CompletedTask;
        }

        public void SetDockShopperRunning(bool isRunning) =>
            Volatile.Write(ref _isDockShopperRunning, isRunning ? 1 : 0);

        public Task RefreshCimAsync(
            string agentId,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Interlocked.Increment(ref _cimRefreshCount);
            if (cimRefreshException is not null)
            {
                throw cimRefreshException;
            }

            afterCimRefresh?.Invoke();
            CimRefreshed.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private sealed class MutablePortCimOpportunityProvider(bool isFresh) :
        IAutonomousPortTradeOpportunityProvider,
        IAutonomousPortCimFreshnessProvider
    {
        private int _isFresh = isFresh ? 1 : 0;

        public bool IsPortCimFresh() => Volatile.Read(ref _isFresh) != 0;

        public void MarkFresh() => Volatile.Write(ref _isFresh, 1);

        public Task<AutonomousPortTradeOpportunity?> GetOpportunityAsync(
            int currentSector,
            int? previousSector,
            int? nextSector,
            GameSnapshot snapshot,
            CancellationToken cancellationToken = default,
            IReadOnlySet<int>? excludedTradeSectors = null,
            TradeProduct? preferredProduct = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            return Task.FromResult<AutonomousPortTradeOpportunity?>(null);
        }
    }

    private sealed class FixedLoginCredentialProvider(AutonomousGameLoginCredentials credentials)
        : IAutonomousGameLoginCredentialProvider
    {
        public AutonomousGameLoginCredentials? GetCredentials(string agentId) => credentials;
    }

    private sealed class RecordingLoginInputSender : IAutonomousGameLoginInputSender
    {
        public ConcurrentQueue<string> Inputs { get; } = new();
        public TaskCompletionSource InputSent { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task SendLoginInputAsync(
            string agentId,
            ReadOnlyMemory<byte> input,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Inputs.Enqueue(Encoding.ASCII.GetString(input.Span));
            InputSent.TrySetResult();
            return Task.CompletedTask;
        }
    }

    private static async Task WaitForActivityAsync(
        AutonomousTestWorkerStatusStore statusStore,
        string expectedActivity)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (!string.Equals(
                   statusStore.GetStatus("meridian").Activity,
                   expectedActivity,
                   StringComparison.Ordinal))
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task WaitForLoginInputCountAsync(
        RecordingLoginInputSender loginInputSender,
        int expectedCount)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (loginInputSender.Inputs.Count < expectedCount)
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task WaitForCommandCountAsync(
        RecordingCommandSender commandSender,
        int expectedCount)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (commandSender.Commands.Count < expectedCount)
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task WaitForDockShopperInputCountAsync(
        RecordingGameScriptRunner scriptRunner,
        int expectedCount)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (scriptRunner.DockShopperInputs.Count < expectedCount)
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task WaitForActionCallCountAsync(
        SequenceActionProvider actionProvider,
        int expectedCount)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (actionProvider.CallCount < expectedCount)
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private static async Task WaitForActionCallCountAsync(
        RecordingActionProvider actionProvider,
        int expectedCount)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (actionProvider.CallCount < expectedCount)
        {
            await Task.Delay(10, timeout.Token);
        }
    }

    private sealed class PendingMoveApprovalGate : IAutonomousMoveApprovalGate
    {
        private readonly TaskCompletionSource<bool> _decision =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public AutonomousMoveApprovalRequest? PendingRequest { get; private set; }
        public TaskCompletionSource<AutonomousMoveApprovalRequest> Requested { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<bool> RequestApprovalAsync(
            AutonomousMoveApprovalRequest request,
            CancellationToken cancellationToken = default)
        {
            PendingRequest = request;
            Requested.TrySetResult(request);
            return _decision.Task.WaitAsync(cancellationToken);
        }

        public bool Resolve(string approvalId, bool approved) =>
            PendingRequest is { } request &&
            string.Equals(request.ApprovalId, approvalId, StringComparison.Ordinal) &&
            _decision.TrySetResult(approved);

        public void Resolve(bool approved) => _decision.TrySetResult(approved);
    }

    private sealed class ImmediateMoveApprovalGate : IAutonomousMoveApprovalGate
    {
        public AutonomousMoveApprovalRequest? PendingRequest => null;

        public Task<bool> RequestApprovalAsync(
            AutonomousMoveApprovalRequest request,
            CancellationToken cancellationToken = default) =>
            Task.FromResult(true);

        public bool Resolve(string approvalId, bool approved) => false;
    }
}
