using System.Net;
using System.Text;
using System.Text.Json;
using LLMSurvivingProxyAgent.Core;
using LLMSurvivingProxyAgent.Models;
using LLMSurvivingProxyAgent.Runtime;

namespace LLMSurvivingProxyAgent.TradeWars.Tests;

public sealed class AutonomousTestActionProviderTests
{
    [Theory]
    [InlineData(4, 3, TradeProduct.Organics)]
    [InlineData(1, 4, TradeProduct.Equipment)]
    [InlineData(7, 8, TradeProduct.Equipment)]
    [InlineData(7, 6, TradeProduct.FuelOre)]
    public void PortClassesIdentifyCompatibleTradeProducts(
        int sellerClass,
        int buyerClass,
        TradeProduct expectedProduct)
    {
        Assert.Equal(
            expectedProduct,
            PortTradeCompatibility.FindTradeProduct(sellerClass, buyerClass));
    }

    [Theory]
    [InlineData(900L, 20, TradeProduct.Equipment)]
    [InlineData(300L, 20, TradeProduct.Organics)]
    [InlineData(5_000L, 20, TradeProduct.Equipment)]
    [InlineData(5_000L, 3, TradeProduct.Equipment)]
    public void TradeProductChoiceFollowsAffordableProfit(long credits, int emptyHolds, TradeProduct expectedProduct)
    {
        // A class 7 port sells everything and a class 8 port buys everything, so only profit decides.
        Assert.Equal(
            expectedProduct,
            PortTradeCompatibility.FindMostProfitableProduct(7, 8, null, credits, emptyHolds));
    }

    [Fact]
    public void PreferredProductWinsWhenCompatibleEvenIfLessProfitable()
    {
        Assert.Equal(
            TradeProduct.FuelOre,
            PortTradeCompatibility.FindMostProfitableProduct(7, 8, TradeProduct.FuelOre, 5_000, 20));
        Assert.True(
            PortTradeCompatibility.EstimateTripProfit(TradeProduct.Equipment, 5_000, 20) >
            PortTradeCompatibility.EstimateTripProfit(TradeProduct.Organics, 5_000, 20));
        Assert.True(
            PortTradeCompatibility.EstimateTripProfit(TradeProduct.Organics, 5_000, 20) >
            PortTradeCompatibility.EstimateTripProfit(TradeProduct.FuelOre, 5_000, 20));
    }

    [Fact]
    public void PortClassesWithoutMatchingBuyAndSellProductsAreNotCompatible()
    {
        Assert.Null(PortTradeCompatibility.FindTradeProduct(1, 6));
    }

    [Fact]
    public void RefurbPurchasePlanPreservesReserveAndBlocksUnknownCargoHoldPrice()
    {
        var quote = new AutonomousRefurbQuote(
            551,
            9,
            CargoHoldUnitCost: 0,
            CargoHoldsAvailable: 100,
            FighterUnitCost: 100,
            FightersAvailable: 10_000,
            ShieldUnitCost: 200,
            ShieldsAvailable: 10_000,
            DateTimeOffset.UtcNow);

        Assert.Equal(
            0,
            AutonomousRefurbPurchasePolicy.GetMaximumPlannedQuantity(
                quote,
                AutonomousRefurbUpgrade.CargoHolds,
                800_000));
        var pricedQuote = quote with { CargoHoldUnitCost = 1_000 };
        Assert.Equal(
            99,
            AutonomousRefurbPurchasePolicy.GetMaximumPlannedQuantity(
                pricedQuote,
                AutonomousRefurbUpgrade.CargoHolds,
                800_000));
        Assert.Equal(
            499,
            AutonomousRefurbPurchasePolicy.GetMaximumPlannedQuantity(
                quote,
                AutonomousRefurbUpgrade.Fighters,
                800_000));
        Assert.Equal(
            499,
            AutonomousRefurbPurchasePolicy.GetMaximumPlannedQuantity(
                quote,
                AutonomousRefurbUpgrade.Shields,
                800_000));
        Assert.Equal(
            0,
            AutonomousRefurbPurchasePolicy.GetMaximumPlannedQuantity(
                quote,
                AutonomousRefurbUpgrade.Fighters,
                500_000));
    }

    [Fact]
    public void ExistingFuelOreCanBeSoldAtAnyPortThatBuysIt()
    {
        var snapshot = new GameSnapshot(67, 50, 100, null, DateTimeOffset.UtcNow)
        {
            HoldsEmpty = 0,
            HoldsTotal = 20,
            FuelOre = 20,
            Organics = 0,
            Equipment = 0
        };

        Assert.Equal(TradeProduct.FuelOre, PortTradeCompatibility.FindSellableCargo(2, snapshot));
        Assert.Null(PortTradeCompatibility.FindSellableCargo(3, snapshot));
        Assert.Equal(TradeProduct.FuelOre, PortTradeCompatibility.FindSellableCargo(9, snapshot));
    }

    [Fact]
    public async Task OllamaProviderReturnsOnlyTheTypedAllowlistedDecision()
    {
        var decisionJson = JsonSerializer.Serialize(new
        {
            action = "requestShipInformation",
            targetSector = 0,
            rationale = "The session is authenticated at the Command prompt.",
            citedKnowledgeNodeId = ""
        });
        var handler = new RecordingHttpHandler(JsonSerializer.Serialize(new
        {
            model = "qwen3:8b",
            created_at = "2026-10-25T00:11:14Z",
            message = new { role = "assistant", content = decisionJson },
            done = true,
            done_reason = "stop",
            total_duration = 1234,
            prompt_eval_count = 32,
            eval_count = 18
        }));
        using var client = new HttpClient(handler);
        var interactionHistory = new LlmInteractionHistoryStore();
        var provider = new OllamaAutonomousTestActionProvider(client, interactionHistory);
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        var situation = new GameSituationReport(
            observedAt,
            GamePresenceState.Connected,
            GameAuthenticationState.Authenticated,
            GameSynchronizationState.Stale,
            "Command",
            observedAt,
            null,
            ["Synchronization health"])
        {
            IntegratedGameClient = new IntegratedGameClientSnapshot(
                observedAt,
                TerminalConnected: true,
                TwxDatabaseAvailable: true,
                ScriptServiceAvailable: true,
                SectorCount: 5_000,
                CurrentSector: 551,
                StarDockSector: 1,
                LastPortCimAt: observedAt,
                [
                    new IntegratedGameSectorSnapshot(
                        551,
                        "Explored",
                        "Federation",
                        "FedSpace",
                        0,
                        [1, 552],
                        "BBB",
                        [],
                        [],
                        [],
                        string.Empty,
                        string.Empty,
                        string.Empty)
                ],
                [
                    new IntegratedGameScriptSnapshot(
                        "2-worldtrade",
                        "trading-cashing/2-worldtrade.ts",
                        Paused: false,
                        IsSystemScript: false,
                        IsBot: false)
                ],
                [
                    new IntegratedGameScriptCapability(
                        "worldTrade",
                        "trading-cashing/2-worldtrade.ts",
                        "Earn credits through approved World Trade.",
                        MayStart: true)
                ])
            {
                ScriptCatalogStatus = "Loaded 2 safe relative TWX script references.",
                ScriptCatalogTotalEntries = 2,
                ScriptCatalogEntries =
                [
                    new IntegratedGameScriptCatalogEntry("trading-cashing/2-worldtrade.ts", "Trading"),
                    new IntegratedGameScriptCatalogEntry(
                        "combat-defense/unreviewed-action.ts",
                        "combat-defense")
                ]
            }
        };

        var result = await provider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
            new AutonomousTestActionContext(situation, [2, 3])
            {
                AgentId = "meridian",
                RecentReactiveSignals =
                [
                    new ReactivePilotSignal(
                        observedAt,
                        ReactiveEventType.TraderEnteredSector,
                        ReactiveEventSource.Cim,
                        2,
                        ReactiveClassificationCategory.Opportunity,
                        ReactiveUrgency.Routine,
                        0.9m,
                        "A compatible buyer was reported nearby.",
                        [new ReactiveActionIntent(ReactiveActionKind.RaiseAlert, "Review the verified trade route.")])
                ],
                AdjacentSectorKnowledge =
                [
                    new AdjacentSectorKnowledge(2, ExplorationStatus.WarpOnly, true, false),
                    new AdjacentSectorKnowledge(3, ExplorationStatus.Unknown, false, false)
                ],
                RelevantKnowledge =
                [
                    new AutonomousActionKnowledgeItem(
                        "scan-node",
                        "Tactical",
                        "Sector scanning",
                        "Use the scanner before assessing adjacent sectors.",
                        0.9m,
                        "ExplicitText",
                        ["line 12: scan command"])
                ]
            });

        Assert.Equal("http://localhost:11434/api/chat", handler.RequestUri?.ToString());
        Assert.Equal(AutonomousTestAction.RequestShipInformation, result.Action);
        using var request = JsonDocument.Parse(handler.RequestBody);
        Assert.True(request.RootElement.GetProperty("think").GetBoolean());
        Assert.Equal(
            2048,
            request.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32());
        var format = request.RootElement.GetProperty("format");
        Assert.Equal("object", format.GetProperty("type").GetString());
        Assert.False(format.GetProperty("additionalProperties").GetBoolean());
        Assert.Equal(
            new[]
            {
                "none",
                "requestShipInformation"
            },
            format.GetProperty("properties")
                .GetProperty("action")
                .GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.Equal(
            new[] { "action", "targetSector", "rationale", "citedKnowledgeNodeId" },
            format.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        Assert.DoesNotContain(
            "runStarDockShopper",
            format.GetProperty("properties").GetProperty("action").GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()));
        Assert.False(format.GetProperty("properties").TryGetProperty("scriptInputResponse", out _));
        Assert.Equal(
            new[] { string.Empty, "scan-node" },
            format.GetProperty("properties").GetProperty("citedKnowledgeNodeId")
                .GetProperty("enum").EnumerateArray().Select(value => value.GetString()));
        Assert.Equal(
            "integer",
            format.GetProperty("properties").GetProperty("targetSector").GetProperty("type").GetString());
        Assert.Equal(
            new[] { 0 },
            format.GetProperty("properties").GetProperty("targetSector").GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetInt32()));
        Assert.Contains("availableAdjacentSectors", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("adjacentSectorKnowledge", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("relevantKnowledge", handler.RequestBody, StringComparison.Ordinal);
        var userContext = request.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();
        Assert.NotNull(userContext);
        using var context = JsonDocument.Parse(userContext);
        Assert.Equal(
            "warpOnly",
            context.RootElement.GetProperty("adjacentSectorKnowledge")[0]
                .GetProperty("exploration")
                .GetString());
        Assert.Equal(
            "scan-node",
            context.RootElement.GetProperty("relevantKnowledge")[0].GetProperty("nodeId").GetString());
        Assert.Equal(
            "opportunity",
            context.RootElement.GetProperty("recentReactiveSignals")[0].GetProperty("category").GetString());
        Assert.Contains("Execute its next safe, allowlisted game action", handler.RequestBody, StringComparison.Ordinal);
        JsonElement integratedClient = context.RootElement.GetProperty("integratedGameClient");
        Assert.True(integratedClient.GetProperty("twxDatabaseAvailable").GetBoolean());
        Assert.Equal(
            "worldTrade",
            integratedClient.GetProperty("scriptCapabilities")[0].GetProperty("id").GetString());
        Assert.Single(integratedClient.GetProperty("scriptCatalogEntries").EnumerateArray());
        Assert.Equal(
            "trading-cashing/2-worldtrade.ts",
            integratedClient.GetProperty("scriptCatalogEntries")[0].GetProperty("file").GetString());
        Assert.Equal(
            "Trading",
            integratedClient.GetProperty("scriptCatalogEntries")[0].GetProperty("category").GetString());
        Assert.DoesNotContain("combat-defense/unreviewed-action.ts", userContext, StringComparison.Ordinal);
        Assert.False(context.RootElement.GetProperty("gameActionsEnabled").GetBoolean());
        Assert.False(context.RootElement.TryGetProperty("activeObjective", out _));
        Assert.Contains("CIM and plotted routes do not prove exploration", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("citedKnowledgeNodeId", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains(
            "a fresh, server-derived route and is sufficient evidence for a one-hop path",
            handler.RequestBody,
            StringComparison.Ordinal);
        Assert.Contains("do not move solely to claim", handler.RequestBody, StringComparison.Ordinal);
        Assert.False(format.GetProperty("properties").GetProperty("action").TryGetProperty("maxLength", out _));
        Assert.False(format.GetProperty("properties").GetProperty("rationale").TryGetProperty("enum", out _));
        Assert.Contains("read-only", handler.RequestBody, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("Movement consumes a turn", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("fixed TWX World Trade script", handler.RequestBody, StringComparison.Ordinal);
        var interaction = Assert.Single(interactionHistory.GetRecent("meridian"));
        Assert.Equal("Pilot", interaction.Role);
        Assert.Equal("qwen3:8b", interaction.Model);
        Assert.True(interaction.Succeeded);
        Assert.Contains("[system]", interaction.Prompt, StringComparison.Ordinal);
        Assert.Contains("[user]", interaction.Prompt, StringComparison.Ordinal);
        Assert.Contains("adjacentSectorKnowledge", interaction.Prompt, StringComparison.Ordinal);
        Assert.Contains("requestShipInformation", interaction.Response, StringComparison.Ordinal);
    }

    [Fact]
    public async Task QwenEmptyThinkingResponseRetriesWithoutThinkingAndUsesOnlyFinalContent()
    {
        string decisionJson = JsonSerializer.Serialize(new
        {
            action = "requestShipInformation",
            targetSector = 0,
            rationale = "The ship snapshot is missing.",
            citedKnowledgeNodeId = ""
        });
        var handler = new RecordingHttpHandler(
            JsonSerializer.Serialize(new
            {
                message = new { role = "assistant", content = "", thinking = "not a decision" },
                done = true,
                done_reason = "length",
                eval_count = 512
            }),
            JsonSerializer.Serialize(new
            {
                message = new { role = "assistant", content = decisionJson, thinking = "" },
                done = true,
                done_reason = "stop",
                eval_count = 24
            }));
        using var client = new HttpClient(handler);
        var history = new LlmInteractionHistoryStore();
        var provider = new OllamaAutonomousTestActionProvider(client, history);

        AutonomousTestActionDecision result = await provider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
            new AutonomousTestActionContext(CreateEligibleSituation(), [])
            {
                AgentId = "meridian"
            });

        Assert.Equal(AutonomousTestAction.RequestShipInformation, result.Action);
        Assert.Equal(2, handler.RequestBodies.Count);
        using var firstRequest = JsonDocument.Parse(handler.RequestBodies[0]);
        using var retryRequest = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.True(firstRequest.RootElement.GetProperty("think").GetBoolean());
        Assert.False(retryRequest.RootElement.GetProperty("think").GetBoolean());
        IReadOnlyList<LlmInteractionRecord> interactions = history.GetRecentByRole("meridian", "Pilot", 2);
        Assert.Equal(2, interactions.Count);
        Assert.True(interactions[0].Succeeded);
        Assert.NotNull(interactions[1].Error);
        Assert.Contains("thinkingLength=", interactions[1].Error, StringComparison.Ordinal);
        Assert.DoesNotContain("not a decision", interactions[1].Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OllamaProviderAcceptsOnlyAnAdjacentTargetFromTheSuppliedRoute()
    {
        var handler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new
            {
                message = new
                {
                    content = """{"action":"moveToAdjacentSector","targetSector":3,"rationale":"Take one verified hop.","citedKnowledgeNodeId":"route-node"}"""
                }
            }));
        using var client = new HttpClient(handler);
        var provider = new OllamaAutonomousTestActionProvider(client);

        var result = await provider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
            new AutonomousTestActionContext(CreateTradeSituation(551), [2, 3])
            {
                GameActionsEnabled = true,
                ActiveObjective = new AutonomousActionObjective(
                    "Explore and map an adjacent sector safely.",
                    "Expand verified local knowledge before trading."),
                RelevantKnowledge =
                [
                    new AutonomousActionKnowledgeItem(
                        "route-node",
                        "Tactical",
                        "Verified movement",
                        "Move only over a fresh directed warp.",
                        0.8m,
                        "ExplicitText",
                        [])
                ]
            });

        Assert.Equal(AutonomousTestAction.MoveToAdjacentSector, result.Action);
        Assert.Equal(3, result.TargetSector);
        using var request = JsonDocument.Parse(handler.RequestBody);
        JsonElement nonMovementBranch = Assert.Single(
            request.RootElement.GetProperty("format").GetProperty("oneOf").EnumerateArray(),
            branch => !branch.GetProperty("properties").GetProperty("action")
                .GetProperty("enum").EnumerateArray()
                .Any(value => value.GetString() == "moveToAdjacentSector"));
        Assert.DoesNotContain(
            "requestShipInformation",
            nonMovementBranch.GetProperty("properties").GetProperty("action")
                .GetProperty("enum").EnumerateArray()
                .Select(value => value.GetString()));
    }

    [Fact]
    public async Task OllamaProviderRetriesOnceWithTheValidationReason()
    {
        var handler = new RecordingHttpHandler(
            JsonSerializer.Serialize(new
            {
                message = new
                {
                    content = """{"action":"tradeAtCurrentPort","targetSector":0,"rationale":"Trade here again.","citedKnowledgeNodeId":""}"""
                }
            }),
            JsonSerializer.Serialize(new
            {
                message = new
                {
                    content = """{"action":"moveToAdjacentSector","targetSector":155,"rationale":"Continue to the verified buyer.","citedKnowledgeNodeId":""}"""
                }
            }));
        using var client = new HttpClient(handler);
        var provider = new OllamaAutonomousTestActionProvider(client);

        var result = await provider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:1.7b", new Uri("http://localhost:11434/")),
            new AutonomousTestActionContext(CreateTradeSituation(583), [155, 894, 35])
            {
                GameActionsEnabled = true,
                ActiveObjective = new AutonomousActionObjective(
                    "Carry cargo to a compatible buyer.",
                    "Continue the verified trading route."),
                PortTradeOpportunity = new AutonomousPortTradeOpportunity(
                    155,
                    TradeProduct.Equipment,
                    SellingExistingCargo: true)
                {
                    TradeSector = 155,
                    RouteSector = 155
                },
                PortTradeAttemptedAtCurrentSector = true
            });

        Assert.Equal(AutonomousTestAction.MoveToAdjacentSector, result.Action);
        Assert.Equal(155, result.TargetSector);
        Assert.Equal(2, handler.RequestBodies.Count);
        using var firstRequest = JsonDocument.Parse(handler.RequestBodies[0]);
        using var retryRequest = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.True(firstRequest.RootElement.GetProperty("think").GetBoolean());
        Assert.False(retryRequest.RootElement.GetProperty("think").GetBoolean());
        Assert.Contains(
            "a port trade was already attempted in this sector",
            retryRequest.RootElement.GetProperty("messages")[3].GetProperty("content").GetString(),
            StringComparison.Ordinal);
        Assert.Contains(
            "brief non-empty rationale",
            retryRequest.RootElement.GetProperty("messages")[3].GetProperty("content").GetString(),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task QwenMissingRationaleRetriesWithoutThinkingAndKeepsValidationFailClosed()
    {
        var handler = new RecordingHttpHandler(
            JsonSerializer.Serialize(new
            {
                message = new
                {
                    content = """{"action":"requestShipInformation","targetSector":0}"""
                }
            }),
            JsonSerializer.Serialize(new
            {
                message = new
                {
                    content = """{"action":"requestShipInformation","targetSector":0,"rationale":"The ship snapshot is stale.","citedKnowledgeNodeId":""}"""
                }
            }));
        using var client = new HttpClient(handler);
        var provider = new OllamaAutonomousTestActionProvider(client);

        AutonomousTestActionDecision result = await provider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:1.7b", new Uri("http://localhost:11434/")),
            new AutonomousTestActionContext(CreateEligibleSituation(), [])
            {
                AgentId = "meridian"
            });

        Assert.Equal(AutonomousTestAction.RequestShipInformation, result.Action);
        Assert.Equal(2, handler.RequestBodies.Count);
        using var firstRequest = JsonDocument.Parse(handler.RequestBodies[0]);
        using var retryRequest = JsonDocument.Parse(handler.RequestBodies[1]);
        Assert.True(firstRequest.RootElement.GetProperty("think").GetBoolean());
        Assert.False(retryRequest.RootElement.GetProperty("think").GetBoolean());
        Assert.Contains(
            "brief non-empty rationale",
            handler.RequestBodies[1],
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task OllamaProviderRejectsUnrecognizedActionsAndExtraFields()
    {
        foreach (var decisionJson in new[]
                 {
                     """{"action":"move","rationale":"move to a new sector"}""",
                     """{"action":"none","targetSector":0,"rationale":"stop","rawCommand":"m 2"}""",
                     """{"action":"none","targetSector":3,"rationale":"stop","citedKnowledgeNodeId":""}""",
                     """{"action":"moveToAdjacentSector","targetSector":3,"rationale":"move","citedKnowledgeNodeId":""}""",
                     """{"action":"moveToAdjacentSector","targetSector":99,"rationale":"move","citedKnowledgeNodeId":""}""",
                     """{"action":"none","targetSector":0,"rationale":"stop","citedKnowledgeNodeId":"made-up"}"""
                 })
        {
            var handler = new RecordingHttpHandler(JsonSerializer.Serialize(
                new { message = new { content = decisionJson } }));
            using var client = new HttpClient(handler);
            var provider = new OllamaAutonomousTestActionProvider(client);

            await Assert.ThrowsAsync<InvalidDataException>(() => provider.DecideAsync(
                new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
                new AutonomousTestActionContext(CreateEligibleSituation(), [2, 3])));
        }
    }

    [Fact]
    public async Task OllamaProviderExplainsMovementValidationFailures()
    {
        var handler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new
            {
                message = new
                {
                    content = """{"action":"moveToAdjacentSector","targetSector":99,"rationale":"Take one hop.","citedKnowledgeNodeId":""}"""
                }
            }));
        using var client = new HttpClient(handler);
        var provider = new OllamaAutonomousTestActionProvider(client);

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => provider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
            new AutonomousTestActionContext(CreateEligibleSituation(), [2, 3])
            {
                GameActionsEnabled = true,
                ActiveObjective = new AutonomousActionObjective(
                    "Explore an adjacent sector.",
                    "Test a bounded, verified move.")
            }));

        Assert.Contains("target sector is not an available adjacent sector", exception.Message);
    }

    [Fact]
    public async Task OllamaProviderAcceptsFreshAdjacentRouteWithoutKnowledgeCitation()
    {
        var handler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new
            {
                message = new
                {
                    content = """{"action":"moveToAdjacentSector","targetSector":3,"rationale":"Use the fresh adjacent route to advance the active movement objective.","citedKnowledgeNodeId":""}"""
                }
            }));
        using var client = new HttpClient(handler);
        var provider = new OllamaAutonomousTestActionProvider(client);

        var result = await provider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
            new AutonomousTestActionContext(CreateEligibleSituation(), [2, 3])
            {
                GameActionsEnabled = true,
                ActiveObjective = new AutonomousActionObjective(
                    "Establish movement patterns with a single verified adjacent hop.",
                    "Validate bounded movement using a fresh server-derived route.")
            });

        Assert.Equal(AutonomousTestAction.MoveToAdjacentSector, result.Action);
        Assert.Equal(3, result.TargetSector);
        Assert.Empty(result.CitedKnowledgeNodeId);
        using var request = JsonDocument.Parse(handler.RequestBody);
        Assert.Equal(
            new[] { 0, 2, 3 },
            request.RootElement.GetProperty("format").GetProperty("properties")
                .GetProperty("targetSector").GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetInt32()));
        Assert.Equal(
            new[] { string.Empty },
            request.RootElement.GetProperty("format").GetProperty("properties")
                .GetProperty("citedKnowledgeNodeId").GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()));
    }

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public async Task OllamaProviderAllowsFixedWorldTradeModeWithEitherScannerAtVerifiedStarDock(
        bool densityScannerInstalled,
        bool holoScannerInstalled)
    {
        var handler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new
            {
                message = new
                {
                    content = """{"action":"startWorldTradeMode","targetSector":0,"rationale":"Start the fixed trade mode at StarDock for the active trade objective.","citedKnowledgeNodeId":""}"""
                }
            }));
        using var client = new HttpClient(handler);
        var provider = new OllamaAutonomousTestActionProvider(client);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var situation = new GameSituationReport(
            now,
            GamePresenceState.Connected,
            GameAuthenticationState.Authenticated,
            GameSynchronizationState.Healthy,
            "Command",
            now,
            new GameSnapshot(551, 6_403, 300, null, now)
            {
                HoloScannerInstalled = holoScannerInstalled,
                DensityScannerInstalled = densityScannerInstalled
            },
            [])
        {
            RecentServerText =
            [
                new ObservedGameText(now, "A  Cargo holds     : 20"),
                new ObservedGameText(now, "B  Fighters        : 500")
            ]
        };

        var result = await provider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
            new AutonomousTestActionContext(situation, [])
            {
                GameActionsEnabled = true,
                ActiveObjective = new AutonomousActionObjective(
                    "Reach StarDock and begin world trading.",
                    "Use the fixed TWX trade mode after reaching the known StarDock sector."),
                StarDockSector = 551
            });

        Assert.Equal(AutonomousTestAction.StartWorldTradeMode, result.Action);
        using var request = JsonDocument.Parse(handler.RequestBody);
        var actions = request.RootElement.GetProperty("format").GetProperty("properties")
            .GetProperty("action").GetProperty("enum").EnumerateArray()
            .Select(value => value.GetString());
        Assert.Contains("startWorldTradeMode", actions);
        string? systemPrompt = request.RootElement.GetProperty("messages")[0].GetProperty("content").GetString();
        Assert.NotNull(systemPrompt);
        Assert.Contains("World Trade is the approved earnings path", systemPrompt, StringComparison.Ordinal);
        Assert.Contains(
            "either DensityScannerInstalled or HoloScannerInstalled is true",
            systemPrompt,
            StringComparison.Ordinal);
        Assert.Contains("Holds, Fighters, and Shields", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("500,000 credits", systemPrompt, StringComparison.Ordinal);
        Assert.Contains("Distinguish the `$ S S` script-selection menu", systemPrompt, StringComparison.Ordinal);
        var userContext = request.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();
        Assert.NotNull(userContext);
        using var context = JsonDocument.Parse(userContext);
        Assert.Equal(551, context.RootElement.GetProperty("starDockSector").GetInt32());
        Assert.False(context.RootElement.GetProperty("worldTradeModeAttemptedAtCurrentSector").GetBoolean());
        Assert.Equal(
            "A  Cargo holds     : 20",
            context.RootElement.GetProperty("recentServerText")[0].GetProperty("text").GetString());
    }

    [Fact]
    public async Task OllamaProviderAllowsWorldTradeStopOnlyWithFundingReadyContext()
    {
        var handler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new
            {
                message = new
                {
                    content = """{"action":"stopWorldTradeMode","targetSector":0,"rationale":"The verified funding target has been reached.","citedKnowledgeNodeId":""}"""
                }
            }));
        using var client = new HttpClient(handler);
        var provider = new OllamaAutonomousTestActionProvider(client);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var situation = new GameSituationReport(
            now,
            GamePresenceState.Connected,
            GameAuthenticationState.Authenticated,
            GameSynchronizationState.Healthy,
            "Command",
            now,
            new GameSnapshot(551, AutonomousTradePolicy.MinimumWorldTradeUpgradeCredits, 100, null, now),
            []);

        AutonomousTestActionDecision result = await provider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
            new AutonomousTestActionContext(situation, [])
            {
                GameActionsEnabled = true,
                WorldTradeModeRunning = true,
                WorldTradeStopReady = true
            });

        Assert.Equal(AutonomousTestAction.StopWorldTradeMode, result.Action);
    }

    [Fact]
    public async Task OllamaProviderAllowsDockShopperOnlyWithVerifiedScriptPrerequisites()
    {
        var handler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new
            {
                message = new
                {
                    content = """{"action":"runStarDockShopper","targetSector":0,"rationale":"The scanner budget, full Fuel Ore holds, StarDock location, and owned fighter are verified.","citedKnowledgeNodeId":""}"""
                }
            }));
        using var client = new HttpClient(handler);
        var provider = new OllamaAutonomousTestActionProvider(client);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var situation = CreateTradeSituation(551) with
        {
            Snapshot = new GameSnapshot(
                551,
                AutonomousTradePolicy.MinimumScannerUpgradeCredits,
                300,
                null,
                now)
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
        var actionContext = new AutonomousTestActionContext(situation, [])
        {
            GameActionsEnabled = true,
            StarDockSector = 551,
            StarDockShopperReady = true,
            ScannerTarget = ScannerPurchaseTarget.Density,
            StarDockSectorHasOwnedFighter = true
        };

        var result = await provider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
            actionContext);

        Assert.Equal(AutonomousTestAction.RunStarDockShopper, result.Action);
        using var request = JsonDocument.Parse(handler.RequestBody);
        Assert.Contains(
            "runStarDockShopper",
            request.RootElement.GetProperty("format").GetProperty("properties")
                .GetProperty("action").GetProperty("enum")
                .EnumerateArray()
                .Select(value => value.GetString()));
        var contextContent = request.RootElement.GetProperty("messages")[1]
            .GetProperty("content").GetString();
        Assert.NotNull(contextContent);
        using var context = JsonDocument.Parse(contextContent);
        Assert.True(context.RootElement.GetProperty("starDockShopperReady").GetBoolean());
        Assert.True(context.RootElement.GetProperty("starDockSectorHasOwnedFighter").GetBoolean());

        var invalidHandler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new
            {
                message = new
                {
                    content = """{"action":"runStarDockShopper","targetSector":0,"rationale":"Try the Dock Shopper.","citedKnowledgeNodeId":""}"""
                }
            }));
        using var invalidClient = new HttpClient(invalidHandler);
        var invalidProvider = new OllamaAutonomousTestActionProvider(invalidClient);
        await Assert.ThrowsAsync<InvalidDataException>(() => invalidProvider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
            actionContext with
            {
                StarDockShopperReady = false,
                StarDockSectorHasOwnedFighter = false
            }));
    }

    [Fact]
    public async Task OllamaProviderAllowsDockShopperWithoutFighterAtVerifiedFederationStarDock()
    {
        var handler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new
            {
                message = new
                {
                    content = """{"action":"runStarDockShopper","targetSector":0,"rationale":"Use the verified scanner workflow at the Federation StarDock without deploying a fighter.","citedKnowledgeNodeId":""}"""
                }
            }));
        using var client = new HttpClient(handler);
        var provider = new OllamaAutonomousTestActionProvider(client);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var situation = CreateTradeSituation(551) with
        {
            Snapshot = new GameSnapshot(
                551,
                AutonomousTradePolicy.MinimumScannerUpgradeCredits,
                300,
                null,
                now)
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
        var actionContext = new AutonomousTestActionContext(situation, [])
        {
            GameActionsEnabled = true,
            StarDockSector = 551,
            StarDockShopperReady = true,
            ScannerTarget = ScannerPurchaseTarget.Density,
            StarDockSectorHasOwnedFighter = false,
            CurrentSectorIsFederationSpace = true
        };

        var result = await provider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
            actionContext);

        Assert.Equal(AutonomousTestAction.RunStarDockShopper, result.Action);
    }

    [Fact]
    public async Task OllamaProviderRejectsNonMovementActionWithTargetSector()
    {
        var handler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new
            {
                message = new
                {
                    content = """{"action":"none","targetSector":172,"rationale":"Hold position until prerequisites are ready.","citedKnowledgeNodeId":""}"""
                }
            }));
        using var client = new HttpClient(handler);
        var provider = new OllamaAutonomousTestActionProvider(client);
        var actionContext = new AutonomousTestActionContext(CreateTradeSituation(551), [172, 552])
        {
            GameActionsEnabled = true,
            ActiveObjective = new AutonomousActionObjective(
                "Reach StarDock safely.",
                "Use only a verified adjacent sector when moving.")
        };

        var exception = await Assert.ThrowsAsync<InvalidDataException>(() => provider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
            actionContext));

        Assert.Contains("non-movement action specified a target sector", exception.Message);
        Assert.Equal(2, handler.RequestBodies.Count);
        using var request = JsonDocument.Parse(handler.RequestBody);
        JsonElement schema = request.RootElement.GetProperty("format");
        JsonElement[] branches = schema.GetProperty("oneOf").EnumerateArray().ToArray();
        Assert.Equal(2, branches.Length);
        foreach (JsonElement branch in branches)
        {
            Assert.Equal(
                new[] { "action", "targetSector", "rationale", "citedKnowledgeNodeId" },
                branch.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
            Assert.Equal(
                1,
                branch.GetProperty("properties").GetProperty("rationale")
                    .GetProperty("minLength").GetInt32());
        }

        JsonElement movementBranch = Assert.Single(
            branches,
            branch => branch.GetProperty("properties").GetProperty("action")
                .GetProperty("enum").EnumerateArray()
                .Any(value => value.GetString() == "moveToAdjacentSector"));
        Assert.Equal(
            new[] { 172, 552 },
            movementBranch.GetProperty("properties").GetProperty("targetSector")
                .GetProperty("enum").EnumerateArray().Select(value => value.GetInt32()));
        JsonElement nonMovementBranch = Assert.Single(
            branches,
            branch => !branch.GetProperty("properties").GetProperty("action")
                .GetProperty("enum").EnumerateArray()
                .Any(value => value.GetString() == "moveToAdjacentSector"));
        Assert.Equal(
            new[] { 0 },
            nonMovementBranch.GetProperty("properties").GetProperty("targetSector")
                .GetProperty("enum").EnumerateArray().Select(value => value.GetInt32()));
    }

    [Fact]
    public async Task OllamaProviderRejectsLegacyScannerActionsWithoutObservedPrompts()
    {
        var handler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new
            {
                message = new
                {
                    content = """{"action":"selectStarDockHoloScanner","targetSector":0,"rationale":"Select the scanner option in the active script.","citedKnowledgeNodeId":""}"""
                }
            }));
        using var client = new HttpClient(handler);
        var provider = new OllamaAutonomousTestActionProvider(client);
        var actionContext = new AutonomousTestActionContext(CreateTradeSituation(551), [])
        {
            GameActionsEnabled = true,
            StarDockShopperStage = StarDockShopperStage.SelectHoloScanner
        };

        await Assert.ThrowsAsync<InvalidDataException>(() => provider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
            actionContext));

        var invalidHandler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new
            {
                message = new
                {
                    content = """{"action":"selectStarDockHoloScanner","targetSector":0,"rationale":"Select the scanner option.","citedKnowledgeNodeId":""}"""
                }
            }));
        using var invalidClient = new HttpClient(invalidHandler);
        var invalidProvider = new OllamaAutonomousTestActionProvider(invalidClient);
        await Assert.ThrowsAsync<InvalidDataException>(() => invalidProvider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
            actionContext with { StarDockShopperStage = StarDockShopperStage.ConfirmHoloScanner }));
    }

    [Fact]
    public async Task OllamaProviderCanAnswerAnUnfamiliarApprovedScriptPrompt()
    {
        var handler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new
            {
                message = new
                {
                    content = """{"action":"respondToScriptInputPrompt","targetSector":0,"rationale":"The prompt asks whether to proceed and visibly offers Y or N.","citedKnowledgeNodeId":"","scriptInputResponse":"Y"}"""
                }
            }));
        using var client = new HttpClient(handler);
        var provider = new OllamaAutonomousTestActionProvider(client);
        DateTimeOffset promptAt = DateTimeOffset.UtcNow;
        var situation = CreateTradeSituation(551) with
        {
            ScriptInputPrompt = new GameScriptInputPrompt(
                "ls-dock-shopper-v33.ts",
                "Item Costs Need To Be Updated, Proceed (Y/N)?",
                SingleKey: true,
                promptAt)
        };
        var actionContext = new AutonomousTestActionContext(situation, [])
        {
            GameActionsEnabled = true,
            StarDockShopperStage = StarDockShopperStage.SelectHoloScanner,
            ActiveObjective = new AutonomousActionObjective(
                "Learn how the approved Dock Shopper works.",
                "Continue only through observed script prompts.")
            {
                PlannedScripts =
                [
                    new AutonomousScriptPlan(
                        AutonomousScriptId.StarDockShopper,
                        "Learn scanner purchase flow.")
                ]
            }
        };

        AutonomousTestActionDecision result = await provider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
            actionContext);

        Assert.Equal(AutonomousTestAction.RespondToScriptInputPrompt, result.Action);
        Assert.Equal("Y", result.ScriptInputResponse);
        using var request = JsonDocument.Parse(handler.RequestBody);
        JsonElement format = request.RootElement.GetProperty("format");
        Assert.True(format.GetProperty("properties").TryGetProperty("scriptInputResponse", out _));
        Assert.DoesNotContain(
            "scriptInputResponse",
            format.GetProperty("required").EnumerateArray().Select(value => value.GetString()));
        JsonElement[] branches = format.GetProperty("oneOf").EnumerateArray().ToArray();
        JsonElement scriptInputBranch = Assert.Single(
            branches,
            branch => branch.GetProperty("properties").GetProperty("action")
                .GetProperty("enum").EnumerateArray()
                .Any(value => value.GetString() == "respondToScriptInputPrompt"));
        Assert.True(scriptInputBranch.GetProperty("properties").TryGetProperty("scriptInputResponse", out _));
        Assert.Contains(
            "scriptInputResponse",
            scriptInputBranch.GetProperty("required").EnumerateArray()
                .Select(value => value.GetString()));
        Assert.False(scriptInputBranch.GetProperty("additionalProperties").GetBoolean());
        foreach (JsonElement branch in branches.Where(branch =>
                     !branch.GetProperty("properties").GetProperty("action")
                         .GetProperty("enum").EnumerateArray()
                         .Any(value => value.GetString() == "respondToScriptInputPrompt")))
        {
            Assert.False(branch.GetProperty("properties").TryGetProperty("scriptInputResponse", out _));
            Assert.False(branch.GetProperty("additionalProperties").GetBoolean());
        }

        Assert.Contains("Item Costs Need To Be Updated", handler.RequestBody, StringComparison.Ordinal);
        Assert.Contains("plannedScripts", handler.RequestBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OllamaProviderOmitsScriptResponseForStaleScriptPrompt()
    {
        var handler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new
            {
                message = new
                {
                    content = """{"action":"none","targetSector":0,"rationale":"Wait for a fresh approved script prompt.","citedKnowledgeNodeId":""}"""
                }
            }));
        using var client = new HttpClient(handler);
        var provider = new OllamaAutonomousTestActionProvider(client);
        var situation = CreateTradeSituation(551) with
        {
            ScriptInputPrompt = new GameScriptInputPrompt(
                "ls-dock-shopper-v33.ts",
                "Scanner type (Y/N/D)? Holo=Y, Density=D",
                SingleKey: true,
                DateTimeOffset.UtcNow.AddMinutes(-2))
        };

        AutonomousTestActionDecision result = await provider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
            new AutonomousTestActionContext(situation, [])
            {
                GameActionsEnabled = true
            });

        Assert.Equal(AutonomousTestAction.None, result.Action);
        using var request = JsonDocument.Parse(handler.RequestBody);
        JsonElement format = request.RootElement.GetProperty("format");
        Assert.False(format.GetProperty("properties").TryGetProperty("scriptInputResponse", out _));
        Assert.DoesNotContain(
            "respondToScriptInputPrompt",
            format.GetProperty("properties").GetProperty("action").GetProperty("enum")
                .EnumerateArray().Select(value => value.GetString()));
        foreach (JsonElement branch in format.GetProperty("oneOf").EnumerateArray())
        {
            Assert.False(branch.GetProperty("properties").TryGetProperty("scriptInputResponse", out _));
        }
    }

    [Fact]
    public void ScriptPromptPolicyAcceptsVisibleChoicesAndRejectsCredentialOrInventedResponses()
    {
        var prompt = new GameScriptInputPrompt(
            "ls-dock-shopper-v33.ts",
            "Unknown confirmation (Y/N)?",
            SingleKey: true,
            DateTimeOffset.UtcNow);
        var credentialPrompt = prompt with { Text = "Enter password:" };

        Assert.True(ScriptInputPromptPolicy.IsSafeResponse(prompt, "Y"));
        Assert.True(ScriptInputPromptPolicy.IsSafeResponse(prompt, "N"));
        Assert.False(ScriptInputPromptPolicy.IsSafeResponse(prompt, "X"));
        Assert.False(ScriptInputPromptPolicy.IsSafeResponse(credentialPrompt, "Y"));
        Assert.True(ScriptInputPromptPolicy.IsSafeResponse(
            prompt with { Text = "Quantity to buy:" , SingleKey = false },
            "12"));
        Assert.False(ScriptInputPromptPolicy.IsSafeResponse(
            prompt with { Text = "Quantity to buy:", SingleKey = false },
            "buy 12"));
    }

    [Fact]
    public async Task OllamaProviderAcceptsPortTradeOnlyWithVerifiedOpportunity()
    {
        var handler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new
            {
                message = new
                {
                    content = """{"action":"tradeAtCurrentPort","targetSector":0,"rationale":"Trade the compatible route port before moving.","citedKnowledgeNodeId":""}"""
                }
            }));
        using var client = new HttpClient(handler);
        var provider = new OllamaAutonomousTestActionProvider(client);
        var opportunity = new AutonomousPortTradeOpportunity(
            9,
            TradeProduct.Equipment,
            SellingExistingCargo: false);
        var actionContext = new AutonomousTestActionContext(CreateTradeSituation(8), [])
        {
            GameActionsEnabled = true,
            PortTradeOpportunity = opportunity,
            ScannerFundingTargetCredits = 20_000,
            ScannerFundingShortfallCredits = 19_500
        };

        var result = await provider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
            actionContext);

        Assert.Equal(AutonomousTestAction.TradeAtCurrentPort, result.Action);
        using var request = JsonDocument.Parse(handler.RequestBody);
        Assert.Contains(
            "tradeAtCurrentPort",
            request.RootElement.GetProperty("format").GetProperty("properties")
                .GetProperty("action").GetProperty("enum").EnumerateArray()
                .Select(value => value.GetString()));
        string? systemPrompt = request.RootElement.GetProperty("messages")[0]
            .GetProperty("content").GetString();
        Assert.NotNull(systemPrompt);
        Assert.Contains(
            "buy its product at the current sector's",
            systemPrompt,
            StringComparison.Ordinal);
        Assert.Contains(
            "selling port and treat partnerSector as the buyer",
            systemPrompt,
            StringComparison.Ordinal);
        var contextContent = request.RootElement.GetProperty("messages")[1].GetProperty("content").GetString();
        Assert.NotNull(contextContent);
        using var context = JsonDocument.Parse(contextContent);
        Assert.Equal(
            9,
            context.RootElement.GetProperty("portTradeOpportunity").GetProperty("partnerSector").GetInt32());
        Assert.False(context.RootElement.GetProperty("portTradeAttemptedAtCurrentSector").GetBoolean());
        Assert.Equal(20_000, context.RootElement.GetProperty("scannerFundingTargetCredits").GetInt64());
        Assert.Equal(19_500, context.RootElement.GetProperty("scannerFundingShortfallCredits").GetInt64());

        var invalidHandler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new
            {
                message = new
                {
                    content = """{"action":"tradeAtCurrentPort","targetSector":0,"rationale":"Trade now.","citedKnowledgeNodeId":""}"""
                }
            }));
        using var invalidClient = new HttpClient(invalidHandler);
        var invalidProvider = new OllamaAutonomousTestActionProvider(invalidClient);
        await Assert.ThrowsAsync<InvalidDataException>(() => invalidProvider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
            new AutonomousTestActionContext(CreateTradeSituation(8), [])
            {
                GameActionsEnabled = true
            }));
    }

    [Fact]
    public async Task OllamaProviderOmitsUnavailableTradeAndRefurbActionsFromSchema()
    {
        var handler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new
            {
                message = new
                {
                    content = """{"action":"none","targetSector":0,"rationale":"No verified trade or upgrade action is available.","citedKnowledgeNodeId":""}"""
                }
            }));
        using var client = new HttpClient(handler);
        var provider = new OllamaAutonomousTestActionProvider(client);
        var actionContext = new AutonomousTestActionContext(CreateTradeSituation(933), [])
        {
            GameActionsEnabled = true,
            ActiveObjective = new AutonomousActionObjective(
                "Reach StarDock safely.",
                "Do not trade without a verified compatible port opportunity.")
        };

        _ = await provider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:1.7b", new Uri("http://localhost:11434/")),
            actionContext);

        using var request = JsonDocument.Parse(handler.RequestBody);
        string?[] allowedActions = request.RootElement.GetProperty("format")
            .GetProperty("properties")
            .GetProperty("action")
            .GetProperty("enum")
            .EnumerateArray()
            .Select(value => value.GetString())
            .ToArray();
        Assert.DoesNotContain("tradeAtCurrentPort", allowedActions);
        Assert.DoesNotContain("inspectRefurbMenu", allowedActions);
        Assert.DoesNotContain("buyCargoHolds", allowedActions);
        Assert.DoesNotContain("buyFighters", allowedActions);
        Assert.DoesNotContain("buyShields", allowedActions);
    }

    [Fact]
    public async Task OllamaProviderRejectsWorldTradeModeOutsideStarDockOrAfterAnAttempt()
    {
        foreach (var actionContext in new[]
                 {
                     new AutonomousTestActionContext(CreateTradeSituation(8), [])
                     {
                         GameActionsEnabled = true,
                         StarDockSector = 551
                     },
                     new AutonomousTestActionContext(CreateTradeSituation(551), [])
                     {
                         GameActionsEnabled = true,
                         StarDockSector = 551,
                         WorldTradeModeAttemptedAtCurrentSector = true
                     },
                     new AutonomousTestActionContext(
                         CreateTradeSituation(551) with
                         {
                             Snapshot = new GameSnapshot(551, 300, 300, null, DateTimeOffset.UtcNow)
                             {
                                 HoloScannerInstalled = false,
                                 DensityScannerInstalled = false
                             }
                         },
                         [])
                     {
                         GameActionsEnabled = true,
                         StarDockSector = 551
                     },
                     new AutonomousTestActionContext(
                         CreateTradeSituation(551) with
                         {
                             Snapshot = new GameSnapshot(551, 300, 300, null, DateTimeOffset.UtcNow)
                         },
                         [])
                     {
                         GameActionsEnabled = true,
                         StarDockSector = 551
                     }
                 })
        {
            var handler = new RecordingHttpHandler(JsonSerializer.Serialize(
                new
                {
                    message = new
                    {
                        content = """{"action":"startWorldTradeMode","targetSector":0,"rationale":"Start world trading.","citedKnowledgeNodeId":""}"""
                    }
                }));
            using var client = new HttpClient(handler);
            var provider = new OllamaAutonomousTestActionProvider(client);

            await Assert.ThrowsAsync<InvalidDataException>(() => provider.DecideAsync(
                new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
                actionContext));
        }
    }

    [Fact]
    public async Task OllamaProviderRejectsPortTradingFromAnEscapePodOrSingleHoldShip()
    {
        var handler = new RecordingHttpHandler(JsonSerializer.Serialize(
            new
            {
                message = new
                {
                    content = """{"action":"tradeAtCurrentPort","targetSector":0,"rationale":"Trade this cargo.","citedKnowledgeNodeId":""}"""
                }
            }));
        using var client = new HttpClient(handler);
        var provider = new OllamaAutonomousTestActionProvider(client);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        var snapshot = new GameSnapshot(
            8,
            5_000,
            100,
            new Ship("Escape Pod", "Meridian", "Escape Pod", 8, 0, now),
            now)
        {
            HoldsEmpty = 1,
            HoldsTotal = 1
        };
        var situation = CreateTradeSituation(8) with { Snapshot = snapshot };

        await Assert.ThrowsAsync<InvalidDataException>(() => provider.DecideAsync(
            new ModelConfiguration("Ollama", "qwen3:8b", new Uri("http://localhost:11434/")),
            new AutonomousTestActionContext(situation, [])
            {
                GameActionsEnabled = true,
                PortTradeOpportunity = new AutonomousPortTradeOpportunity(
                    9,
                    TradeProduct.Organics,
                    SellingExistingCargo: false)
            }));
    }

    private static GameSituationReport CreateTradeSituation(int currentSector)
    {
        DateTimeOffset now = DateTimeOffset.UtcNow;
        return new GameSituationReport(
            now,
            GamePresenceState.Connected,
            GameAuthenticationState.Authenticated,
            GameSynchronizationState.Healthy,
            "Command",
            now,
            new GameSnapshot(currentSector, 300, 100, null, now),
            []);
    }

    private static GameSituationReport CreateEligibleSituation() =>
        new(
            DateTimeOffset.UtcNow,
            GamePresenceState.Connected,
            GameAuthenticationState.Authenticated,
            GameSynchronizationState.Stale,
            "Command",
            DateTimeOffset.UtcNow,
            null,
            []);

    private sealed class RecordingHttpHandler(params string[] responseBodies) : HttpMessageHandler
    {
        private readonly Queue<string> _responses = new(responseBodies);

        public Uri? RequestUri { get; private set; }
        public string RequestBody { get; private set; } = string.Empty;
        public List<string> RequestBodies { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;
            RequestBody = request.Content is null
                ? string.Empty
                : await request.Content.ReadAsStringAsync(cancellationToken);
            RequestBodies.Add(RequestBody);
            string responseBody = _responses.Count > 1 ? _responses.Dequeue() : _responses.Peek();
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(responseBody, Encoding.UTF8, "application/json")
            };
        }
    }
}
