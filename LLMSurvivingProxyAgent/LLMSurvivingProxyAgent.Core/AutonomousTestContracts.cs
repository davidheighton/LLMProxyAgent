namespace LLMSurvivingProxyAgent.Core;

public static class AutonomousTradePolicy
{
    public const int MinimumScannerUpgradeCredits = 20_000;
    public const int MinimumWorldTradeUpgradeCredits = 1_000_000;
    public const int RefurbCreditReserve = 500_000;
}

public enum AutonomousTestAction
{
    None,
    RequestShipInformation,
    MoveToAdjacentSector,
    TradeAtCurrentPort,
    DeployStarDockShopperPrerequisiteFighter,
    RunStarDockShopper,
    RespondToScriptInputPrompt,
    SelectStarDockHoloScanner,
    ConfirmStarDockHoloScanner,
    ExecuteStarDockShopperPurchase,
    StartWorldTradeMode,
    StopWorldTradeMode,
    InspectRefurbMenu,
    BuyCargoHolds,
    BuyFighters,
    BuyShields,
    CloseRefurbMenu
}

public enum StarDockShopperStage
{
    Inactive,
    SelectHoloScanner,
    ConfirmHoloScanner,
    ExecutePurchase,
    AwaitingCompletion
}

public enum ScannerPurchaseTarget
{
    Density,
    Holo
}

public enum AutonomousRefurbMenuStage
{
    Inactive,
    SelectingUpgrade
}

public enum AutonomousRefurbUpgrade
{
    CargoHolds,
    Fighters,
    Shields
}

public sealed record AutonomousRefurbQuote(
    int Sector,
    int PortClass,
    int CargoHoldUnitCost,
    int CargoHoldsAvailable,
    int FighterUnitCost,
    int FightersAvailable,
    int ShieldUnitCost,
    int ShieldsAvailable,
    DateTimeOffset ObservedAt);

public static class AutonomousRefurbPurchasePolicy
{
    public static int GetMaximumPlannedQuantity(
        AutonomousRefurbQuote quote,
        AutonomousRefurbUpgrade upgrade,
        long synchronizedCredits)
    {
        ArgumentNullException.ThrowIfNull(quote);
        int unitCost;
        int available;
        switch (upgrade)
        {
            case AutonomousRefurbUpgrade.CargoHolds:
                unitCost = quote.CargoHoldUnitCost;
                available = quote.CargoHoldsAvailable;
                break;
            case AutonomousRefurbUpgrade.Fighters:
                unitCost = quote.FighterUnitCost;
                available = quote.FightersAvailable;
                break;
            case AutonomousRefurbUpgrade.Shields:
                unitCost = quote.ShieldUnitCost;
                available = quote.ShieldsAvailable;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(upgrade));
        }

        long perUpgradeBudget = synchronizedCredits > AutonomousTradePolicy.RefurbCreditReserve
            ? (synchronizedCredits - AutonomousTradePolicy.RefurbCreditReserve) / 3
            : 0;
        long conservativeUnitCost = upgrade == AutonomousRefurbUpgrade.Fighters
            ? (long)unitCost * 2
            : unitCost;
        if (conservativeUnitCost <= 0 || available <= 0)
        {
            return 0;
        }

        long plannedQuantity = perUpgradeBudget / conservativeUnitCost;
        if (plannedQuantity > 0)
        {
            plannedQuantity--;
        }

        return (int)Math.Min(available, plannedQuantity);
    }
}

public sealed record AutonomousTestActionDecision(
    AutonomousTestAction Action,
    string Rationale,
    int TargetSector = 0)
{
    public string CitedKnowledgeNodeId { get; init; } = string.Empty;
    public string ScriptInputResponse { get; init; } = string.Empty;
}

public sealed record AutonomousTestActionContext(
    GameSituationReport Situation,
    IReadOnlyList<int> AvailableAdjacentSectors)
{
    public string AgentId { get; init; } = string.Empty;
    public bool GameActionsEnabled { get; init; }
    public AutonomousActionObjective? ActiveObjective { get; init; }
    public int? StarDockSector { get; init; }
    public AutonomousPortTradeOpportunity? PortTradeOpportunity { get; init; }
    public bool PortTradeAttemptedAtCurrentSector { get; init; }
    public bool WorldTradeModeAttemptedAtCurrentSector { get; init; }
    public bool WorldTradeModeRunning { get; init; }
    public bool WorldTradeStopReady { get; init; }
    public int? CurrentRefurbPortClass { get; init; }
    public AutonomousRefurbMenuStage RefurbMenuStage { get; init; }
    public AutonomousRefurbQuote? RefurbQuote { get; init; }
    public IReadOnlySet<AutonomousRefurbUpgrade> RefurbUpgradesAttemptedAtCurrentSector { get; init; } =
        new HashSet<AutonomousRefurbUpgrade>();
    public bool StarDockShopperReady { get; init; }
    public bool StarDockShopperAttemptedAtCurrentSector { get; init; }
    public bool StarDockSectorHasOwnedFighter { get; init; }
    public bool? CurrentSectorIsFederationSpace { get; init; }
    public bool StarDockFighterDeploymentAvailable { get; init; }
    public StarDockShopperStage StarDockShopperStage { get; init; }
    public ScannerPurchaseTarget? ScannerTarget { get; init; }
    public TradeProduct? PreferredTradeProduct { get; init; }
    public IReadOnlyList<ReactivePilotSignal> RecentReactiveSignals { get; init; } = [];
    public IReadOnlyList<AdjacentSectorKnowledge> AdjacentSectorKnowledge { get; init; } = [];
    public IReadOnlyList<AutonomousActionKnowledgeItem> RelevantKnowledge { get; init; } = [];
}

public sealed record AutonomousActionObjective(
    string Goal,
    string Rationale)
{
    public IReadOnlyList<AutonomousScriptPlan> PlannedScripts { get; init; } = [];
}

public sealed record ReactivePilotSignal(
    DateTimeOffset ProcessedAt,
    ReactiveEventType Type,
    ReactiveEventSource Source,
    int? SectorId,
    ReactiveClassificationCategory? Category,
    ReactiveUrgency? Urgency,
    decimal? Confidence,
    string Summary,
    IReadOnlyList<ReactiveActionIntent> ActionIntents);

public enum AutonomousScriptId
{
    StarDockShopper,
    WorldTrade
}

public sealed record AutonomousScriptPlan(
    AutonomousScriptId Script,
    string Purpose);

public sealed record AutonomousPortTradeOpportunity(
    int PartnerSector,
    TradeProduct Product,
    bool SellingExistingCargo)
{
    public int? TradeSector { get; init; }
    public int? RouteSector { get; init; }
    public TradeProduct? FollowUpBuyProduct { get; init; }
    public int? FollowUpPartnerSector { get; init; }
}

public sealed record AutonomousActionKnowledgeItem(
    string NodeId,
    string Layer,
    string Title,
    string Summary,
    decimal Confidence,
    string ExtractionBasis,
    IReadOnlyList<string> Evidence);

public enum GameCommand
{
    RequestShipInformation,
    MoveToSector,
    DisplayCurrentSector,
    FindStarDockSector,
    StopAtCurrentSector,
    DeclineAutopilotEngagement,
    ExitNavPointSelection,
    DeployOneFighterAtCurrentSector,
    TradeAtCurrentPort
}

public sealed record GameCommandRequest(
    GameCommand Command,
    int? TargetSector = null,
    TradeProduct? Product = null)
{
    public bool SellingExistingCargo { get; init; }
    public TradeProduct? FollowUpBuyProduct { get; init; }
    public int? ExpectedShipFighterCount { get; init; }
    public bool? CurrentSectorIsFederationSpace { get; init; }
}

public interface IAutonomousTestActionProvider
{
    string Id { get; }

    Task<AutonomousTestActionDecision> DecideAsync(
        ModelConfiguration configuration,
        AutonomousTestActionContext context,
        CancellationToken cancellationToken = default);
}

public interface IGameCommandSender
{
    Task SendAsync(
        string agentId,
        GameCommandRequest request,
        CancellationToken cancellationToken = default);
}

public interface IAutonomousPortTradePromptRecovery
{
    Task RecoverUntrackedPortTradeAsync(
        string agentId,
        DateTimeOffset promptObservedAt,
        TradeProduct? followUpBuyProduct = null,
        CancellationToken cancellationToken = default);
}

public interface IAutonomousGameWorldProvider
{
    Task<int?> GetStarDockSectorAsync(CancellationToken cancellationToken = default);

    Task<int?> GetNextSectorTowardStarDockAsync(
        int currentSector,
        CancellationToken cancellationToken = default);

    Task<int?> GetNextSectorTowardSectorAsync(
        int currentSector,
        int targetSector,
        CancellationToken cancellationToken = default);

    Task<bool> HasFreshOwnedFighterInSectorAsync(
        int sector,
        CancellationToken cancellationToken = default);

    Task<bool?> IsFederationSpaceAsync(
        int sector,
        CancellationToken cancellationToken = default);

    Task<int?> GetCurrentRefurbPortClassAsync(
        int sector,
        CancellationToken cancellationToken = default);

    Task<int?> GetNextSectorTowardNearestRefurbPortAsync(
        int currentSector,
        CancellationToken cancellationToken = default);
}

public interface IAutonomousPortTradeOpportunityProvider
{
    Task<AutonomousPortTradeOpportunity?> GetOpportunityAsync(
        int currentSector,
        int? previousSector,
        int? nextSector,
        GameSnapshot snapshot,
        CancellationToken cancellationToken = default,
        IReadOnlySet<int>? excludedTradeSectors = null,
        TradeProduct? preferredProduct = null);
}

public interface IAutonomousPortCimFreshnessProvider
{
    bool IsPortCimFresh();
}

public interface IAutonomousFedSpaceFastStartProvider
{
    Task<int?> GetNextFedSpaceSweepSectorAsync(
        int currentSector,
        IReadOnlySet<int> visitedSectors,
        CancellationToken cancellationToken = default);

    Task<AutonomousPortTradeOpportunity?> GetFedSpaceTradeOpportunityAsync(
        int currentSector,
        int? nextSector,
        GameSnapshot snapshot,
        IReadOnlySet<int> excludedTradeSectors,
        TradeProduct? preferredProduct = null,
        CancellationToken cancellationToken = default);

    Task<int?> GetNextSectorTowardFedSpaceTradeTargetAsync(
        int currentSector,
        int targetSector,
        CancellationToken cancellationToken = default);
}

public interface IAutonomousGameScriptRunner
{
    Task StartWorldTradeModeAsync(string agentId, CancellationToken cancellationToken = default);

    Task StopWorldTradeModeAsync(string agentId, CancellationToken cancellationToken = default);

    Task<bool> IsWorldTradeModeRunningAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    Task StartStarDockShopperAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    Task<bool> IsStarDockShopperRunningAsync(
        string agentId,
        CancellationToken cancellationToken = default);

    Task SendStarDockShopperInputAsync(
        string agentId,
        char input,
        CancellationToken cancellationToken = default);

    Task<bool> IsApprovedScriptRunningAsync(
        string agentId,
        string scriptName,
        CancellationToken cancellationToken = default) =>
        Path.GetFileName(scriptName).Equals(
            "ls-dock-shopper-v33.ts",
            StringComparison.OrdinalIgnoreCase)
            ? IsStarDockShopperRunningAsync(agentId, cancellationToken)
            : Path.GetFileName(scriptName).Equals(
                "2-worldtrade.ts",
                StringComparison.OrdinalIgnoreCase)
                ? IsWorldTradeModeRunningAsync(agentId, cancellationToken)
                : Task.FromResult(false);

    Task SendScriptInputResponseAsync(
        string agentId,
        GameScriptInputPrompt prompt,
        string response,
        CancellationToken cancellationToken = default)
    {
        if (!ScriptInputPromptPolicy.IsSafeResponse(prompt, response) ||
            !prompt.SingleKey ||
            !Path.GetFileName(prompt.ScriptName).Equals(
                "ls-dock-shopper-v33.ts",
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "This script runner cannot safely send the requested script prompt response.");
        }

        return SendStarDockShopperInputAsync(
            agentId,
            char.ToUpperInvariant(response[0]),
            cancellationToken);
    }

    Task<AutonomousRefurbQuote> OpenRefurbMenuAsync(
        string agentId,
        int currentSector,
        int portClass,
        CancellationToken cancellationToken = default);

    Task ExecuteRefurbUpgradeAsync(
        string agentId,
        AutonomousRefurbUpgrade upgrade,
        AutonomousRefurbQuote quote,
        long synchronizedCredits,
        CancellationToken cancellationToken = default);

    Task CloseRefurbMenuAsync(
        string agentId,
        int portClass,
        CancellationToken cancellationToken = default);
}

public interface IAutonomousGameCimRefresher
{
    Task RefreshCimAsync(string agentId, CancellationToken cancellationToken = default);
}

public sealed record AutonomousLoginIdentityContext(
    string ServerHost,
    int ServerPort,
    string GameProfile,
    string GameLetter,
    string? ServerLoginName = null,
    string? TraderAlias = null);

public interface IAutonomousLoginIdentityProvider
{
    Task<string> ChooseServerLoginNameAsync(
        ModelConfiguration configuration,
        AutonomousLoginIdentityContext context,
        CancellationToken cancellationToken = default);

    Task<string> ChooseTraderAliasAsync(
        ModelConfiguration configuration,
        AutonomousLoginIdentityContext context,
        CancellationToken cancellationToken = default);

    Task<string> GenerateGamePasswordAsync(
        ModelConfiguration configuration,
        AutonomousLoginIdentityContext context,
        CancellationToken cancellationToken = default);
}
