using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using LLMSurvivingProxyAgent.Core;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Data.Sqlite;
using MTC;
using Core = TWXProxy.Core;

int failures = 0;

void Check(bool condition, string name)
{
    if (condition)
    {
        Console.WriteLine($"PASS {name}");
        return;
    }

    Console.WriteLine($"FAIL {name}");
    failures++;
}

Core.ModDatabase CreateFastStartDatabase(int sellerSector, int buyerSector)
{
    var database = new Core.ModDatabase();
    database.ReplaceHeader(new Core.DataHeader
    {
        Sectors = 10,
        LastPortCIM = DateTime.Now
    });

    for (int sectorNumber = 1; sectorNumber <= 10; sectorNumber++)
    {
        ushort[] warps = sectorNumber < 10 ? [(ushort)(sectorNumber + 1)] : [];
        Core.Port? port = sectorNumber == sellerSector
            ? new Core.Port { ClassIndex = 1 }
            : sectorNumber == buyerSector
                ? new Core.Port { ClassIndex = 4 }
                : null;
        database.SaveSector(new Core.SectorData
        {
            Number = sectorNumber,
            Warp = warps,
            SectorPort = port
        });
    }

    return database;
}

Core.ModDatabase CreateMultiHopFundingDatabase()
{
    var database = new Core.ModDatabase();
    database.ReplaceHeader(new Core.DataHeader
    {
        Sectors = 750,
        StarDock = 551,
        LastPortCIM = DateTime.Now
    });

    foreach (Core.SectorData sector in new[]
    {
        new Core.SectorData
        {
            Number = 172,
            Warp = [(ushort)551],
            SectorPort = new Core.Port { ClassIndex = 3 },
            Update = DateTime.Now,
            Explored = Core.ExploreType.Yes
        },
        new Core.SectorData
        {
            Number = 551,
            Warp = [(ushort)172, (ushort)600],
            SectorPort = new Core.Port { ClassIndex = 9 },
            Update = DateTime.Now
        },
        new Core.SectorData
        {
            Number = 600,
            Warp = [(ushort)750],
            Update = DateTime.Now
        },
        new Core.SectorData
        {
            Number = 750,
            Warp = [(ushort)600],
            SectorPort = new Core.Port { ClassIndex = 8 },
            Update = DateTime.Now,
            Explored = Core.ExploreType.Yes
        }
    })
    {
        database.SaveSector(sector);
    }

    return database;
}

string quickLoadCatalogRoot = Path.Combine(
    Path.GetTempPath(),
    $"mtc-quickload-catalog-{Guid.NewGuid():N}");
try
{
    string utilitiesDirectory = Path.Combine(quickLoadCatalogRoot, "utilities");
    string includesDirectory = Path.Combine(quickLoadCatalogRoot, "includes");
    Directory.CreateDirectory(utilitiesDirectory);
    Directory.CreateDirectory(includesDirectory);
    File.WriteAllText(Path.Combine(utilitiesDirectory, "tool-old.ts"), "old");
    File.WriteAllText(Path.Combine(utilitiesDirectory, "tool-new.ts"), "new");
    File.WriteAllText(Path.Combine(utilitiesDirectory, "custom.ts"), "custom");
    File.WriteAllText(Path.Combine(includesDirectory, "helper.ts"), "helper");
    File.WriteAllText(Path.Combine(quickLoadCatalogRoot, "root-script.ts"), "root");
    File.WriteAllText(
        Path.Combine(quickLoadCatalogRoot, "catalog.json"),
        """
        {
          "library": "LLMProxyScripts",
          "entries": [
            {
              "file": "utilities/tool-old.ts",
              "variant_group": "tool",
              "preferred_variant": false
            },
            {
              "file": "utilities/tool-new.ts",
              "variant_group": "tool",
              "preferred_variant": true
            }
          ]
        }
        """);

    IReadOnlyList<Core.QuickLoadGroup> quickLoadGroups =
        Core.ProxyMenuCatalog.BuildQuickLoadGroups(quickLoadCatalogRoot, quickLoadCatalogRoot);
    var scriptMenuMetadata =
        Core.ProxyMenuCatalog.ReadScriptMenuMetadata(quickLoadCatalogRoot);
    Core.QuickLoadEntry[] quickLoadEntries = quickLoadGroups
        .SelectMany(group => group.Entries)
        .ToArray();
    Check(
        !quickLoadGroups.Any(group => group.Name.Equals("includes", StringComparison.OrdinalIgnoreCase)) &&
        quickLoadEntries.All(entry => !entry.RelativePath.Equals(
            "utilities/tool-old.ts",
            StringComparison.OrdinalIgnoreCase)) &&
        quickLoadEntries.Any(entry =>
            entry.RelativePath.Equals("utilities/tool-new.ts", StringComparison.OrdinalIgnoreCase) &&
            entry.DisplayName == "tool") &&
        scriptMenuMetadata.TryGetValue("utilities/tool-old.ts", out var oldScriptMetadata) &&
        oldScriptMetadata.IsNonPreferredVariant &&
        scriptMenuMetadata.TryGetValue("utilities/tool-new.ts", out var newScriptMetadata) &&
        !newScriptMetadata.IsNonPreferredVariant &&
        newScriptMetadata.DisplayName == "tool" &&
        quickLoadEntries.Any(entry => entry.RelativePath == "utilities/custom.ts") &&
        quickLoadEntries.Any(entry => entry.RelativePath == "root-script.ts"),
        "script menus hide nonpreferred variants and retain local additions");
}
finally
{
    if (Directory.Exists(quickLoadCatalogRoot))
        Directory.Delete(quickLoadCatalogRoot, recursive: true);
}

DateTimeOffset now = DateTimeOffset.UtcNow;

var threeHopPairProvider = new MtcFreshAdjacentSectorProvider(
    () => CreateFastStartDatabase(sellerSector: 2, buyerSector: 5));
int? nextSweepSector = await threeHopPairProvider.GetNextFedSpaceSweepSectorAsync(
    1,
    new HashSet<int> { 1 });
Check(nextSweepSector == 2, "selects the next unvisited FedSpace sector by a one-hop route");

var partiallyMappedFedSpace = new Core.ModDatabase();
partiallyMappedFedSpace.ReplaceHeader(new Core.DataHeader
{
    Sectors = 10,
    LastPortCIM = DateTime.Now
});
for (int sectorNumber = 1; sectorNumber <= 10; sectorNumber++)
{
    var sector = new Core.SectorData
    {
        Number = sectorNumber,
        Warp = sectorNumber == 1 ? [(ushort)2] : [(ushort)Math.Min(sectorNumber + 1, 10)],
        Update = DateTime.Now
    };
    partiallyMappedFedSpace.SaveSector(sector);
    if (sectorNumber != 1)
    {
        sector.Update = DateTime.MinValue;
    }
}

var partiallyMappedSweepProvider = new MtcFreshAdjacentSectorProvider(
    () => partiallyMappedFedSpace);
int? nextUnknownNeighbor = await partiallyMappedSweepProvider.GetNextFedSpaceSweepSectorAsync(
    1,
    new HashSet<int> { 1 });
Check(
    nextUnknownNeighbor == 2,
    "allows a fresh current-sector warp to lead into an unmapped FedSpace neighbor");
int? routeThroughUnmappedSector =
    await partiallyMappedSweepProvider.GetNextFedSpaceSweepSectorAsync(
        1,
        new HashSet<int> { 1, 2 });
Check(
    routeThroughUnmappedSector is null,
    "does not route through an unmapped FedSpace sector before its own warps are refreshed");

var fastStartSnapshot = new GameSnapshot(2, 50_000, 20, null, now)
{
    HoldsEmpty = 4,
    HoldsTotal = 4
};
AutonomousPortTradeOpportunity? threeHopPair =
    await threeHopPairProvider.GetFedSpaceTradeOpportunityAsync(
        2,
        null,
        fastStartSnapshot,
        new HashSet<int>());
Check(
    threeHopPair is
    {
        PartnerSector: 5,
        Product: TradeProduct.Equipment,
        SellingExistingCargo: false,
        TradeSector: null
    },
    "plans a compatible three-hop FedSpace port pair");
Core.ModDatabase followUpDatabase = new();
followUpDatabase.ReplaceHeader(new Core.DataHeader { Sectors = 30, StarDock = 30, LastPortCIM = DateTime.Now });
foreach (Core.SectorData sector in new[]
{
    // Sector 20 buys Fuel Ore and sells Equipment; the only Equipment buyer is two hops away in sector 22.
    new Core.SectorData { Number = 20, Warp = [(ushort)21], SectorPort = new Core.Port { ClassIndex = 1 }, Update = DateTime.Now, Explored = Core.ExploreType.Yes },
    new Core.SectorData { Number = 21, Warp = [(ushort)20, (ushort)22], Update = DateTime.Now, Explored = Core.ExploreType.Yes },
    new Core.SectorData { Number = 22, Warp = [(ushort)21], SectorPort = new Core.Port { ClassIndex = 4 }, Update = DateTime.Now, Explored = Core.ExploreType.Yes }
})
{
    followUpDatabase.SaveSector(sector);
}
AutonomousPortTradeOpportunity? saleWithFollowUp =
    await new MtcFreshAdjacentSectorProvider(() => followUpDatabase).GetOpportunityAsync(
        20,
        null,
        null,
        new GameSnapshot(20, 900, 20, null, now)
        {
            HoldsEmpty = 0,
            HoldsTotal = 20,
            FuelOre = 20,
            Organics = 0,
            Equipment = 0,
            DensityScannerInstalled = true
        });
Check(
    saleWithFollowUp is
    {
        Product: TradeProduct.FuelOre,
        SellingExistingCargo: true,
        FollowUpBuyProduct: TradeProduct.Equipment,
        FollowUpPartnerSector: 22
    },
    "after selling Fuel Ore, plans an Equipment buy for a buyer port two hops away");

Core.ModDatabase roundTripDatabase = new();
roundTripDatabase.ReplaceHeader(new Core.DataHeader { Sectors = 40, StarDock = 40, LastPortCIM = DateTime.Now });
foreach (Core.SectorData sector in new[]
{
    // 30 (class 5, SBS) and 32 (class 2, BSB): Equipment one way, Organics back - the bidirectional pair.
    new Core.SectorData { Number = 30, Warp = [(ushort)31], SectorPort = new Core.Port { ClassIndex = 5 }, Update = DateTime.Now, Explored = Core.ExploreType.Yes },
    new Core.SectorData { Number = 31, Warp = [(ushort)30, (ushort)32], Update = DateTime.Now, Explored = Core.ExploreType.Yes },
    new Core.SectorData { Number = 32, Warp = [(ushort)31], SectorPort = new Core.Port { ClassIndex = 2 }, Update = DateTime.Now, Explored = Core.ExploreType.Yes }
})
{
    roundTripDatabase.SaveSector(sector);
}
AutonomousPortTradeOpportunity? organicsSaleFollowUp =
    await new MtcFreshAdjacentSectorProvider(() => roundTripDatabase).GetOpportunityAsync(
        30,
        null,
        null,
        new GameSnapshot(30, 1_500, 20, null, now)
        {
            HoldsEmpty = 0,
            HoldsTotal = 20,
            FuelOre = 0,
            Organics = 20,
            Equipment = 0,
            DensityScannerInstalled = true
        },
        excludedTradeSectors: new HashSet<int> { 32 });
Check(
    organicsSaleFollowUp is
    {
        Product: TradeProduct.Organics,
        SellingExistingCargo: true,
        FollowUpBuyProduct: TradeProduct.Equipment,
        FollowUpPartnerSector: 32
    },
    "after selling Organics, buys Equipment for the port it just traded at (bidirectional pair)");

var multiHopFundingProvider = new MtcFreshAdjacentSectorProvider(
    CreateMultiHopFundingDatabase);
AutonomousPortTradeOpportunity? multiHopFundingPlan =
    await multiHopFundingProvider.GetOpportunityAsync(
        551,
        null,
        null,
        new GameSnapshot(551, 1, 20, null, now)
        {
            HoldsEmpty = 20,
            HoldsTotal = 20,
            DensityScannerInstalled = true
        });
Check(
    multiHopFundingPlan is
    {
        PartnerSector: 750,
        Product: TradeProduct.FuelOre,
        TradeSector: 172,
        RouteSector: 172,
        SellingExistingCargo: false
    },
    "finds a fresh three-hop non-FedSpace funding pair from StarDock");
AutonomousPortTradeOpportunity? scannerlessUnsafePair =
    await multiHopFundingProvider.GetOpportunityAsync(
        551,
        null,
        null,
        new GameSnapshot(551, 1, 20, null, now)
        {
            HoldsEmpty = 20,
            HoldsTotal = 20
        });
Check(
    scannerlessUnsafePair is null,
    "rejects a scannerless pair whose route crosses an unexplored intermediate sector");
Check(
    MtcGameCommandSender.GetAutonomousPortTradeQuantity(20) == 20,
    "fills the available holds when buying an autonomous port-trade product");
Check(
    MtcGameCommandSender.GetAutonomousPortTradeQuantity(0) == 0,
    "does not request cargo when the port reports no available hold capacity");
Check(
    MtcGameCommandSender.GetAutonomousPortTradeQuantity(13, isSale: false, emptyHolds: 20) == 11,
    "buys fewer units than a credit-limited default so the negotiated quote stays affordable");
Check(
    MtcGameCommandSender.GetAutonomousPortTradeQuantity(20, isSale: false, emptyHolds: 20) == 20,
    "fills every empty hold when holds, not credits, limit the purchase");
Check(
    MtcGameCommandSender.GetAutonomousPortTradeQuantity(20, isSale: true, emptyHolds: null) == 20,
    "sells the full quantity offered");
Check(
    MtcGameCommandSender.GetAutonomousPortTradeQuantity(1, isSale: false, emptyHolds: 20) == 1,
    "still buys a single affordable unit");
Check(
    MtcGameCommandSender.GetAutonomousPortTradeQuantity(15, isSale: false, emptyHolds: null) == 15,
    "keeps the server default when the empty-hold count was not observed");
AutonomousPortTradeOpportunity? routedThreeHopPair =
    await threeHopPairProvider.GetFedSpaceTradeOpportunityAsync(
        1,
        null,
        fastStartSnapshot with { CurrentSector = 1 },
        new HashSet<int>());
Check(
    routedThreeHopPair is
    {
        PartnerSector: 5,
        TradeSector: 2,
        RouteSector: 2
    },
    "routes to a FedSpace pair seller without leaving sectors 1-10");

var fourHopPairProvider = new MtcFreshAdjacentSectorProvider(
    () => CreateFastStartDatabase(sellerSector: 2, buyerSector: 6));
AutonomousPortTradeOpportunity? fourHopPair =
    await fourHopPairProvider.GetFedSpaceTradeOpportunityAsync(
        2,
        null,
        fastStartSnapshot,
        new HashSet<int>());
Check(fourHopPair is null, "rejects FedSpace port pairs more than three hops apart");

var outOfRangePairProvider = new MtcFreshAdjacentSectorProvider(
    () => CreateFastStartDatabase(sellerSector: 1, buyerSector: 4));
AutonomousPortTradeOpportunity? outOfRangePair =
    await outOfRangePairProvider.GetFedSpaceTradeOpportunityAsync(
        1,
        null,
        fastStartSnapshot with { CurrentSector = 1 },
        new HashSet<int>());
Check(outOfRangePair is null, "excludes port pairs with endpoints outside sectors 2-10");

bool worldTradeToolParsed = GameAgentToolRegistry.TryParseModelAction(
    """{"tool_call":{"name":"run_script","arguments":{"script":"trading-cashing/2-worldtrade.ts"}}}""",
    out GameAgentToolAction? worldTradeTool,
    out _);
Check(
    worldTradeToolParsed &&
    worldTradeTool?.Kind == GameAgentToolActionKind.RunScript &&
    worldTradeTool.ScriptReference == "trading-cashing/2-worldtrade.ts",
    "parses the reviewed World Trade script action");
var worldTradeMenu = new Core.MenuManager();
var worldTradeMenuOwner = new object();
worldTradeMenu.AddCustomMenu(
    "",
    "WorldTrade",
    "World Trade Settings",
    "",
    "Main",
    '.',
    false,
    worldTradeMenuOwner);
worldTradeMenu.AddCustomMenu(
    "WorldTrade",
    "GO",
    "GO!",
    ":MENU_GO",
    "",
    'G',
    true,
    worldTradeMenuOwner);
worldTradeMenu.OpenMenu("WorldTrade", 0);
Check(
    !worldTradeMenu.TryHandleMenuOption(new object(), "WORLDTRADE", "GO") &&
    worldTradeMenu.IsMenuOpen(),
    "rejects script-menu selection from a different script owner");
Check(
    !worldTradeMenu.TryHandleMenuOption(worldTradeMenuOwner, "OTHER", "GO") &&
    worldTradeMenu.IsMenuOpen(),
    "rejects script-menu selection outside the expected current menu");
Check(
    worldTradeMenu.TryHandleMenuOption(worldTradeMenuOwner, "WORLDTRADE", "GO"),
    "selects an exact script-owned WorldTrade GO menu option");
Check(
    WorldTradeScriptStartPolicy.IsWorldTradeScriptReference(
        "trading-cashing/2-worldtrade.ts") &&
    !WorldTradeScriptStartPolicy.IsWorldTradeScriptReference(
        "unreviewed/2-worldtrade.ts"),
    "limits autonomous menu startup to the approved WorldTrade script path");
Check(
    WorldTradeScriptStartPolicy.IsDensityOnlyScanner(
        CreateWorldTradeContext(551, "Dens", now),
        now) &&
    !WorldTradeScriptStartPolicy.IsDensityOnlyScanner(
        CreateWorldTradeContext(551, "Holo", now),
        now),
    "identifies Density-only scanner ownership for WorldTrade command selection");
Check(
    GameAgentScanPolicy.TrySelectScanType(
        "H",
        "Density Scanner",
        out string densityFallback,
        out bool densityNeedsChoice) &&
    densityFallback == "D" &&
    !densityNeedsChoice,
    "uses S directly for Density-only scans instead of expecting a scanner-choice prompt");
Check(
    !GameAgentScanPolicy.TrySelectScanType("H", "None", out _, out _),
    "blocks holo-scan selection when no scanner is verified");
Check(
    GameAgentScanPolicy.TrySelectScanType(
        "H",
        "Holo Scanner",
        out string holoSelection,
        out bool holoNeedsChoice) &&
    holoSelection == "H" &&
    holoNeedsChoice,
    "opens the scanner-choice prompt when Holo Scanner ownership is verified");
Check(
    GameAgentScanPolicy.TrySelectScanType(
        "D",
        "Holo Scanner",
        out string densityWithHolo,
        out bool holoNeedsDensityChoice) &&
    densityWithHolo == "D" &&
    holoNeedsDensityChoice,
    "selects D from the scanner-choice prompt when a Holo Scanner is installed");
GameAgentContextSnapshot CreateWorldTradeContext(
    int currentSector,
    string scannerType,
    DateTimeOffset statusAt) =>
    new()
{
    Connected = true,
    CurrentPrompt = "Command",
    CurrentSector = currentSector,
    RecentEvents =
    [
        new GameAgentEvent
        {
            Timestamp = statusAt,
            Kind = GameAgentEventKind.SynchronizedShipStatus,
            CurrentSector = currentSector,
            Metadata = new Dictionary<string, string> { ["scannerType"] = scannerType }
        }
    ]
};
Check(
    WorldTradeScriptStartPolicy.GetBlockReason(
        "trading-cashing/2-worldtrade.ts",
        CreateWorldTradeContext(172, "Density", now),
        551,
        now)?.Contains("StarDock", StringComparison.OrdinalIgnoreCase) == true,
    "blocks World Trade outside the verified StarDock sector");
Check(
    WorldTradeScriptStartPolicy.GetBlockReason(
        "trading-cashing/2-worldtrade.ts",
        CreateWorldTradeContext(551, "None", now),
        551,
        now)?.Contains("Scanner", StringComparison.OrdinalIgnoreCase) == true,
    "blocks World Trade until scanner ownership is verified");
Check(
    WorldTradeScriptStartPolicy.GetBlockReason(
        "trading-cashing/2-worldtrade.ts",
        CreateWorldTradeContext(551, "Density", now - TimeSpan.FromMinutes(1)),
        551,
        now)?.Contains("fresh", StringComparison.OrdinalIgnoreCase) == true,
    "blocks World Trade when synchronized scanner status is stale");
Check(
    WorldTradeScriptStartPolicy.GetBlockReason(
        "trading-cashing/2-worldtrade.ts",
        CreateWorldTradeContext(551, "Density", now),
        551,
        now) is null,
    "allows World Trade at StarDock with fresh Density Scanner status");
Check(
    WorldTradeScriptStartPolicy.GetBlockReason(
        "trading-cashing/2-worldtrade.ts",
        CreateWorldTradeContext(551, "Dens", now),
        551,
        now) is null,
    "allows World Trade at StarDock with abbreviated Density Scanner status");
Check(
    WorldTradeScriptStartPolicy.GetBlockReason(
        "trading-cashing/2-worldtrade.ts",
        CreateWorldTradeContext(551, "Holo", now),
        551,
        now) is null,
    "allows World Trade with a verified Holo Scanner");
Check(
    !GameAgentToolRegistry.TryParseModelAction(
        """{"tool_call":{"name":"run_script","arguments":{"script":"../unreviewed.ts"}}}""",
        out _,
        out string rejectedScriptError) &&
    !string.IsNullOrWhiteSpace(rejectedScriptError),
    "rejects model requests to start unreviewed scripts");
Check(
    GameAgentToolRegistry.TryParseModelAction(
        """{"tool_call":{"name":"stop_script","arguments":{"id":7}}}""",
        out GameAgentToolAction? stopTool,
        out _) &&
    stopTool?.ScriptId == 7,
    "parses stop requests by interpreter script id");
Check(
    GameAgentToolRegistry.IsApprovedRunningScriptReference(
        @"C:\Games\TWX\scripts\trading-cashing\2-worldtrade.ts") &&
    !GameAgentToolRegistry.IsApprovedRunningScriptReference(
        @"C:\Games\TWX\scripts\unsafe\other.ts"),
    "matches only reviewed script references in the active interpreter");
Check(
    GameAgentToolRegistry.IsAllowedMombotCommand("status", out string normalizedMombot) &&
    normalizedMombot == "status" &&
    !GameAgentToolRegistry.IsAllowedMombotCommand("stopall", out _) &&
    !GameAgentToolRegistry.IsAllowedMombotCommand("status | relog", out _),
    "limits model Mombot commands to single local non-combat allowlisted commands");

var itemCostPrompt = new GameScriptInputPrompt(
    "ls-dock-shopper-v33.ts",
    "Item Costs Need To Be Updated, Proceed (Y/N)?",
    SingleKey: true,
    ObservedAt: now);
bool promptResponseParsed = GameAgentToolRegistry.TryParseModelAction(
    """{"tool_call":{"name":"respond_to_prompt","arguments":{"script_name":"ls-dock-shopper-v33.ts","expected_prompt":"Item Costs Need To Be Updated, Proceed (Y/N)?","response":"Y"}}}""",
    out GameAgentToolAction? promptTool,
    out _);
Check(
    promptResponseParsed &&
    promptTool?.Command == "Y" &&
    ScriptInputPromptPolicy.IsSafeResponse(itemCostPrompt, promptTool.Command),
    "accepts a visible Y/N response to the current approved script prompt");
Check(
    !ScriptInputPromptPolicy.IsSafeResponse(itemCostPrompt, "X") &&
    !ScriptInputPromptPolicy.IsSafeResponse(itemCostPrompt with { Text = "Enter password:" }, "Y"),
    "blocks invisible script choices and sensitive script prompts");
var shopperExecutePrompt = new GameScriptInputPrompt(
    "ls-dock-shopper-v33.ts",
    "A - Holds\r\nX - Execute\r\nQ - Quit",
    SingleKey: true,
    ObservedAt: now);
Check(
    ScriptInputPromptPolicy.IsSafeResponse(shopperExecutePrompt, "X") &&
    !ScriptInputPromptPolicy.IsSafeResponse(shopperExecutePrompt, "Z"),
    "allows the visible execute choice while keeping max-out blocked");

var scannerParser = new TWXProxy.Core.ShipInfoParser();
TWXProxy.Core.ShipStatus? latestScannerStatus = null;
scannerParser.SynchronizedUpdated += status => latestScannerStatus = status;
void FeedScannerInfo(string shipName)
{
    scannerParser.FeedLine("<Info>");
    scannerParser.FeedLine($"Ship Name : {shipName}");
    scannerParser.FeedLine("Ship Info : Merchant Cruiser");
    scannerParser.FeedLine("Current Sector : 551");
    scannerParser.FeedLine("Turns left : 65501");
    scannerParser.FeedLine("Total Holds : 50");
    scannerParser.FeedLine("Fighters : 12");
    scannerParser.FeedLine("Credits : 25000");
    scannerParser.FeedLine("Command [TL=00:00:00]:[1]");
}

FeedScannerInfo("Meridian");
scannerParser.FeedLine("You don't have a long range scanner.");
FeedScannerInfo("Meridian");
Check(
    latestScannerStatus is not null &&
    latestScannerStatus.ObservedFields.HasFlag(TWXProxy.Core.ShipStatusFields.LongRangeScanner) &&
    string.IsNullOrEmpty(latestScannerStatus.LRSType),
    "retains explicit scanner absence through a fresh ship-info response");

scannerParser.ApplyDelta(new TWXProxy.Core.ShipStatusDelta { LRSType = "Holo" });
FeedScannerInfo("Meridian");
Check(
    latestScannerStatus is not null &&
    latestScannerStatus.ObservedFields.HasFlag(TWXProxy.Core.ShipStatusFields.LongRangeScanner) &&
    latestScannerStatus.LRSType == "Holo",
    "retains a purchased Holo Scanner through a fresh ship-info response");

FeedScannerInfo("Replacement");
Check(
    latestScannerStatus is not null &&
    !latestScannerStatus.ObservedFields.HasFlag(TWXProxy.Core.ShipStatusFields.LongRangeScanner),
    "does not transfer scanner ownership to a different ship");

Check(
    GameAgentRuntime.IsSafeOnlineRefreshPrompt("Command"),
    "allows online refresh only at the authenticated TradeWars command prompt");
Check(
    !GameAgentRuntime.IsSafeOnlineRefreshPrompt("BBSName") &&
    !GameAgentRuntime.IsSafeOnlineRefreshPrompt("Please enter your name (ENTER for none):"),
    "blocks online refresh during BBS login prompts");
Check(
    !GameAgentRuntime.IsSafeOnlineRefreshPrompt("NewCharacter") &&
    !GameAgentRuntime.IsSafeOnlineRefreshPrompt("ServerRegistration"),
    "blocks online refresh during server and character selection");

DateTimeOffset cimSubmittedAt = DateTimeOffset.UtcNow;
Check(
    GameAgentRuntime.HasCimCompletionMarker(
        [
            new GameAgentEvent
            {
                Timestamp = cimSubmittedAt.AddMilliseconds(1),
                Kind = GameAgentEventKind.ServerLine,
                PlainText = ": ENDINTERROG",
            },
        ],
        cimSubmittedAt),
    "recognizes a fresh TWX CIM completion marker");
Check(
    !GameAgentRuntime.HasCimCompletionMarker(
        [
            new GameAgentEvent
            {
                Timestamp = cimSubmittedAt.AddSeconds(-1),
                Kind = GameAgentEventKind.ServerLine,
                PlainText = ": ENDINTERROG",
            },
            new GameAgentEvent
            {
                Timestamp = cimSubmittedAt.AddMilliseconds(1),
                Kind = GameAgentEventKind.PresenceHeartbeat,
                PlainText = ": ENDINTERROG",
            },
        ],
        cimSubmittedAt),
    "ignores stale or non-server CIM completion markers");
Check(
    MainWindow.RequiresTradeWarsCommandPrompt(@"utilities\MTC_PortCIM.ts") &&
    MainWindow.RequiresTradeWarsCommandPrompt("MTC_WarpCIM.ts") &&
    !MainWindow.RequiresTradeWarsCommandPrompt("trading-cashing/2-worldtrade.ts"),
    "requires the authenticated TradeWars command prompt for CIM scripts only");
string cimScriptsRoot = Path.GetFullPath(
    Path.Combine(Path.GetTempPath(), "twx-script-root"));
Check(
    MainWindow.ResolveCimScriptPath(
        @"trading-cashing\utilities\MTC_PortCIM.ts",
        cimScriptsRoot) ==
    Path.Combine(cimScriptsRoot, "utilities", "MTC_PortCIM.ts") &&
    MainWindow.ResolveCimScriptPath("MTC_WarpCIM.ts", cimScriptsRoot) ==
    Path.Combine(cimScriptsRoot, "utilities", "MTC_WarpCIM.ts"),
    "resolves fixed CIM scripts from the configured scripts root after nested script execution");
string worldTradeResolutionRoot = Path.Combine(
    Path.GetTempPath(),
    $"mtc-worldtrade-resolution-{Guid.NewGuid():N}");
try
{
    string categoryScriptsRoot = Path.Combine(worldTradeResolutionRoot, "LLMProxyScripts");
    string worldTradeFromRoot = Path.Combine(
        categoryScriptsRoot,
        "trading-cashing",
        "2-worldtrade.ts");
    Directory.CreateDirectory(Path.GetDirectoryName(worldTradeFromRoot)!);
    File.WriteAllText(worldTradeFromRoot, "root");
    Check(
        MainWindow.ResolveWorldTradeScriptPath(
            "trading-cashing/2-worldtrade.ts",
            categoryScriptsRoot) == worldTradeFromRoot,
        "resolves WorldTrade from the configured scripts root");

    string categoryDirectory = Path.Combine(categoryScriptsRoot, "trading-cashing");
    string worldTradeFromCategory = Path.Combine(categoryDirectory, "2-worldtrade.ts");
    Check(
        MainWindow.ResolveWorldTradeScriptPath(
            "trading-cashing/2-worldtrade.ts",
            categoryDirectory) == worldTradeFromCategory,
        "does not duplicate the WorldTrade category when the configured root is already that category");
}
finally
{
    if (Directory.Exists(worldTradeResolutionRoot))
    {
        Directory.Delete(worldTradeResolutionRoot, recursive: true);
    }
}
Check(
    MainWindow.IsRequestedScriptActive(
        [
            new TWXProxy.Core.RunningScriptInfo(
                3,
                "MTC_PortCIM.ts",
                @"C:\Projects\TWBot\LLMProxyScripts\utilities\MTC_PortCIM.ts",
                false,
                false,
                false),
        ],
        "utilities/MTC_PortCIM.ts") &&
    !MainWindow.IsRequestedScriptActive(
        [
            new TWXProxy.Core.RunningScriptInfo(
                4,
                "MTC_WarpCIM.ts",
                @"C:\TWXProxy\Scripts\MTC_WarpCIM.ts",
                false,
                false,
                false),
        ],
        "utilities/MTC_PortCIM.ts"),
    "confirms the requested CIM script is active and rejects unrelated scripts");

var cimScriptCalls = new List<string>();
var cimCompletionCalls = new List<DateTimeOffset>();
DateTime lastPortCim = DateTime.MinValue;
var cimCommandSender = new MtcGameCommandSender(
    "meridian",
    (_, _) => Task.FromResult(MtcRpcActionResult.Ok("command sent")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    script =>
    {
        cimScriptCalls.Add(script);
        return Task.FromResult(MtcRpcActionResult.Ok("script loaded"));
    },
    () => Task.FromResult(false),
    () => true,
    () => lastPortCim,
    (submittedAt, _) =>
    {
        cimCompletionCalls.Add(submittedAt);
        if (cimScriptCalls[^1] == "utilities/MTC_PortCIM.ts")
            lastPortCim = DateTime.Now;
        return Task.CompletedTask;
    },
    () => [],
    () => true,
    commandsApproved: true);
await cimCommandSender.RefreshCimAsync("meridian");
Check(
    cimScriptCalls.SequenceEqual(["utilities/MTC_PortCIM.ts", "utilities/MTC_WarpCIM.ts"]) &&
    cimCompletionCalls.Count == 2,
    "waits for port and warp CIM script completion before reporting refresh complete");

var dockShopperScripts = new List<string>();
var dockShopperGameCommands = new List<(string Input, bool AppendEnter)>();
var dockShopperInputs = new List<string>();
GameScriptInputPrompt dockShopperPrompt = new(
    "ls-dock-shopper-v33.ts",
    "R - Long Range Scanners",
    SingleKey: true,
    ObservedAt: now);
var dockShopperCommandSender = new MtcGameCommandSender(
    "meridian",
    (input, appendEnter) =>
    {
        dockShopperGameCommands.Add((input, appendEnter));
        return Task.FromResult(MtcRpcActionResult.Ok("game command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => true,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => [],
    () => true,
    commandsApproved: true,
    runDockShopperScript: script =>
    {
        dockShopperScripts.Add(script);
        return Task.FromResult(MtcRpcActionResult.Ok("Dock Shopper started"));
    },
    isDockShopperRunning: () => Task.FromResult(true),
    getCurrentScriptInputPrompt: () => dockShopperPrompt,
    submitScriptInput: (prompt, response) =>
    {
        dockShopperInputs.Add(response);
        dockShopperPrompt = response switch
        {
            "R" => prompt with { Text = "D - Density", ObservedAt = now.AddTicks(1) },
            "D" => prompt with { Text = "X - Execute", ObservedAt = now.AddTicks(2) },
            _ => prompt with { Text = "Purchase submitted", ObservedAt = now.AddTicks(3) }
        };
        return Task.FromResult(MtcRpcActionResult.Ok("input delivered to script"));
    });
await dockShopperCommandSender.StartStarDockShopperAsync("meridian");
foreach (char input in new[] { 'R', 'D', 'X' })
{
    await dockShopperCommandSender.SendStarDockShopperInputAsync("meridian", input);
}
string? invalidDockShopperInputError = null;
try
{
    await dockShopperCommandSender.SendStarDockShopperInputAsync("meridian", 'Z');
}
catch (ArgumentOutOfRangeException exception)
{
    invalidDockShopperInputError = exception.Message;
}
Check(
    dockShopperScripts.SequenceEqual(["ship-upgrades/ls-dock-shopper-v33.ts"]) &&
    dockShopperInputs.SequenceEqual(["R", "D", "X"]) &&
    dockShopperGameCommands.Count == 0 &&
    invalidDockShopperInputError is not null,
    "routes only current, visible Dock Shopper choices directly to the script without sending game commands");

var genericPrompt = new GameScriptInputPrompt(
    "ls-dock-shopper-v33.ts",
    "X - Execute",
    SingleKey: true,
    ObservedAt: now);
var genericScriptInputs = new List<string>();
var genericGameCommands = new List<(string Input, bool AppendEnter)>();
var genericScriptInputSender = new MtcGameCommandSender(
    "meridian",
    (input, appendEnter) =>
    {
        genericGameCommands.Add((input, appendEnter));
        return Task.FromResult(MtcRpcActionResult.Ok("game command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => true,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => [],
    () => true,
    commandsApproved: true,
    isDockShopperRunning: () => Task.FromResult(true),
    getCurrentScriptInputPrompt: () => genericPrompt,
    submitScriptInput: (_, response) =>
    {
        genericScriptInputs.Add(response);
        return Task.FromResult(MtcRpcActionResult.Ok("input delivered to script"));
    });
await genericScriptInputSender.SendScriptInputResponseAsync(
    "meridian",
    genericPrompt,
    "X");
Check(
    genericScriptInputs.SequenceEqual(["X"]) &&
    genericGameCommands.Count == 0,
    "routes a validated generic script-prompt response directly to its script");

var incompleteCimScriptCalls = new List<string>();
var incompleteCimCommandSender = new MtcGameCommandSender(
    "meridian",
    (_, _) => Task.FromResult(MtcRpcActionResult.Ok("command sent")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    script =>
    {
        incompleteCimScriptCalls.Add(script);
        return Task.FromResult(MtcRpcActionResult.Ok("script loaded"));
    },
    () => Task.FromResult(false),
    () => true,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => [],
    () => true,
    commandsApproved: true);
string? incompleteCimError = null;
try
{
    await incompleteCimCommandSender.RefreshCimAsync("meridian");
}
catch (InvalidOperationException ex)
{
    incompleteCimError = ex.Message;
}
Check(
    incompleteCimError?.Contains("fresh port-data batch", StringComparison.Ordinal) == true &&
    incompleteCimScriptCalls.SequenceEqual(["utilities/MTC_PortCIM.ts"]),
    "does not run warp CIM when the port data batch was not recorded");

Check(
    MtcGameCommandSender.TryParsePortTradeQuantityPrompt(
        "How many holds of Organics do you want to buy [15]?",
        out var parsedProduct,
        out var parsedSale,
        out var parsedQuantity) &&
    parsedProduct == TradeProduct.Organics &&
    !parsedSale &&
    parsedQuantity == 15,
    "parses a bounded port purchase prompt");

var shipInfoCommands = new List<(string Command, bool AppendEnter)>();
var shipInfoPrompts = new List<MtcServerPrompt>();
var shipInfoLines = new List<MtcServerPrompt>();
var shipInfoSender = new MtcGameCommandSender(
    "meridian",
    (command, appendEnter) =>
    {
        shipInfoCommands.Add((command, appendEnter));
        DateTimeOffset timestamp = DateTimeOffset.UtcNow;
        shipInfoPrompts.Add(new MtcServerPrompt(
            timestamp,
            "Command [TL=00:00:00]:[35]"));
        if (command == "\x11/")
        {
            shipInfoLines.Add(new MtcServerPrompt(
                timestamp,
                "\u00B3 Ship status"));
        }

        return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => true,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => shipInfoPrompts.ToArray(),
    () => true,
    commandsApproved: true,
    getRecentServerLines: () => shipInfoLines.ToArray());
await shipInfoSender.SendAsync(
    "meridian",
    new GameCommandRequest(GameCommand.RequestShipInformation));
Check(
    shipInfoCommands.SequenceEqual([("I", false), ("\x11/", false), ("I", false)]),
    "refreshes ship information and quick stats without appending Enter");

var portTradeCommands = new List<(string Command, bool AppendEnter)>();
var portTradeHaggleStates = new List<(string Command, bool HaggleEnabled)>();
var portTradePrompts = new List<MtcServerPrompt>();
bool portTradeHaggleEnabled = true;
portTradePrompts.Add(new MtcServerPrompt(
    DateTimeOffset.UtcNow.AddSeconds(-1),
    "Command [TL=00:00:00]:[35]"));
var unrelatedTradeQuantityResponses = 0;
var portTradeSender = new MtcGameCommandSender(
    "meridian",
    (command, appendEnter) =>
    {
        portTradeCommands.Add((command, appendEnter));
        portTradeHaggleStates.Add((command, portTradeHaggleEnabled));
        DateTimeOffset timestamp = DateTimeOffset.UtcNow.AddTicks(portTradePrompts.Count + 1);
        if (command == "P")
        {
            portTradePrompts.Add(new MtcServerPrompt(
                timestamp,
                "Enter your choice [T] ?"));
        }
        else if (command == "T")
        {
            portTradePrompts.Add(new MtcServerPrompt(
                timestamp,
                "How many holds of Fuel Ore do you want to buy [20]?"));
        }
        else if (command == "0")
        {
            unrelatedTradeQuantityResponses++;
            portTradePrompts.Add(unrelatedTradeQuantityResponses == 1
                ? new MtcServerPrompt(
                    timestamp,
                    "How many holds of Organics do you want to buy [15]?")
                : new MtcServerPrompt(
                    timestamp,
                    "Command [TL=00:00:00]:[35]"));
        }
        else if (command == "15")
        {
            portTradePrompts.Add(new MtcServerPrompt(timestamp, "Your offer [542] ?"));
            portTradePrompts.Add(new MtcServerPrompt(
                timestamp.AddMilliseconds(10),
                "PortTradeOffer",
                IsPresenceHeartbeat: true,
                PromptObservedAt: timestamp,
                IsCurrentPromptConfirmed: true));
        }
        else if (command == "542")
        {
            portTradePrompts.Add(new MtcServerPrompt(
                timestamp,
                "How many holds of Equipment do you want to buy [2]?"));
        }

        return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => true,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => portTradePrompts.ToArray(),
    () => portTradeHaggleEnabled,
    commandsApproved: true,
    setNativeHaggleEnabled: enabled => portTradeHaggleEnabled = enabled);
await portTradeSender.SendAsync(
    "meridian",
    new GameCommandRequest(GameCommand.TradeAtCurrentPort, Product: TradeProduct.Organics)
    {
        AvailableCredits = 600
    });
Check(
    portTradeCommands.SequenceEqual(
    [
        ("P", false),
        ("T", false),
        ("0", true),
        ("15", true),
        ("542", true),
        ("0", true),
    ]),
    "fills all available holds for the requested product, accepts its quote, and skips unrelated products");
Check(
    portTradeHaggleStates.All(state => !state.HaggleEnabled) && portTradeHaggleEnabled,
    "restores native haggling after the port trade completes");

var creditLimitedCommands = new List<string>();
var creditLimitedPrompts = new List<MtcServerPrompt> { new(DateTimeOffset.UtcNow.AddSeconds(-1), "Command [TL=00:00:00]:[950]") };
var creditLimitedLines = new List<MtcServerPrompt>();
bool creditLimitedHaggle = false;
var creditLimitedSender = new MtcGameCommandSender(
    "meridian",
    (command, _) =>
    {
        creditLimitedCommands.Add(command);
        DateTimeOffset timestamp = DateTimeOffset.UtcNow.AddTicks(creditLimitedPrompts.Count + 1);
        if (command == "P")
        {
            creditLimitedPrompts.Add(new MtcServerPrompt(timestamp, "Enter your choice [T] ?"));
        }
        else if (command == "T")
        {
            creditLimitedLines.Add(new MtcServerPrompt(timestamp, "You have 900 credits and 20 empty cargo holds."));
            creditLimitedPrompts.Add(new MtcServerPrompt(timestamp, "How many holds of Equipment do you want to buy [13]?"));
        }
        else if (command == "11")
        {
            creditLimitedPrompts.Add(new MtcServerPrompt(timestamp, "Your offer [850] ?"));
        }
        else if (command == "850")
        {
            creditLimitedPrompts.Add(new MtcServerPrompt(timestamp, "Command [TL=00:00:00]:[950]"));
        }

        return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => true,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => creditLimitedPrompts.ToArray(),
    () => creditLimitedHaggle,
    commandsApproved: true,
    getRecentServerLines: () => creditLimitedLines.ToArray(),
    setNativeHaggleEnabled: enabled => creditLimitedHaggle = enabled);
await creditLimitedSender.SendAsync(
    "meridian",
    new GameCommandRequest(GameCommand.TradeAtCurrentPort, Product: TradeProduct.Equipment)
    {
        AvailableCredits = 900
    });
Check(
    creditLimitedCommands.SequenceEqual(["P", "T", "11", "850"]),
    "buys below a credit-limited default and accepts the affordable quote");

var nativeHaggleEngine = new Core.NativeHaggleEngine();
nativeHaggleEngine.SetEnabled(true);
nativeHaggleEngine.SetEnabled(false);
Check(
    !nativeHaggleEngine.Enabled &&
    nativeHaggleEngine.HandleLine("We'll sell them for 7 credits.") is null,
    "native haggling stays suppressed after the trade worker disables it");

var failedSuppressionCommands = new List<(string Command, bool AppendEnter)>();
var failedSuppressionPrompts = new List<MtcServerPrompt>
{
    new(DateTimeOffset.UtcNow, "Command [TL=00:00:00]:[35]")
};
var failedSuppressionSender = new MtcGameCommandSender(
    "meridian",
    (command, appendEnter) =>
    {
        failedSuppressionCommands.Add((command, appendEnter));
        return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => true,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => failedSuppressionPrompts,
    () => true,
    commandsApproved: true,
    setNativeHaggleEnabled: _ => { });
bool failedSuppressionRejected = false;
try
{
    await failedSuppressionSender.SendAsync(
        "meridian",
        new GameCommandRequest(GameCommand.TradeAtCurrentPort, Product: TradeProduct.Organics));
}
catch (InvalidOperationException exception) when (
    exception.Message.Contains("native haggling could not be disabled", StringComparison.Ordinal))
{
    failedSuppressionRejected = true;
}
Check(
    failedSuppressionRejected && failedSuppressionCommands.Count == 0,
    "blocks a port trade when native haggling remains enabled");

var staleQuoteCommands = new List<(string Command, bool AppendEnter)>();
var staleQuotePrompts = new List<MtcServerPrompt>
{
    new(DateTimeOffset.UtcNow.AddSeconds(-1), "Command [TL=00:00:00]:[35]")
};
var staleQuoteSender = new MtcGameCommandSender(
    "meridian",
    (command, appendEnter) =>
    {
        staleQuoteCommands.Add((command, appendEnter));
        DateTimeOffset timestamp = DateTimeOffset.UtcNow.AddTicks(staleQuotePrompts.Count + 1);
        if (command == "P")
        {
            staleQuotePrompts.Add(new MtcServerPrompt(timestamp, "Enter your choice [T] ?"));
        }
        else if (command == "T")
        {
            staleQuotePrompts.Add(new MtcServerPrompt(
                timestamp,
                "How many holds of Fuel Ore do you want to buy [20]?"));
        }
        else if (command == "20")
        {
            staleQuotePrompts.Add(new MtcServerPrompt(timestamp, "Your offer [542] ?"));
            staleQuotePrompts.Add(new MtcServerPrompt(
                timestamp.AddTicks(1),
                "Command [TL=00:00:00]:[35]"));
        }

        return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => true,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => staleQuotePrompts.ToArray(),
    () => false,
    commandsApproved: true);
bool staleQuoteRejected = false;
try
{
    await staleQuoteSender.SendAsync(
        "meridian",
        new GameCommandRequest(GameCommand.TradeAtCurrentPort, Product: TradeProduct.FuelOre));
}
catch (InvalidDataException exception) when (
    exception.Message.Contains("multiple new port-trade prompts", StringComparison.Ordinal))
{
    staleQuoteRejected = true;
}
Check(
    staleQuoteRejected &&
    staleQuoteCommands.SequenceEqual([("P", false), ("T", false), ("20", true)]),
    "does not send a stale quoted price after a newer Command prompt has arrived");

async Task<List<(string Command, bool AppendEnter)>> RunUnaffordableQuoteScenarioAsync(
    long availableCredits,
    string? quoteAfterCounterOffer)
{
    var commands = new List<(string Command, bool AppendEnter)>();
    var prompts = new List<MtcServerPrompt>
    {
        new(DateTimeOffset.UtcNow.AddSeconds(-1), "Command [TL=00:00:00]:[35]")
    };
    var sender = new MtcGameCommandSender(
        "meridian",
        (command, appendEnter) =>
        {
            commands.Add((command, appendEnter));
            DateTimeOffset timestamp = DateTimeOffset.UtcNow.AddTicks(prompts.Count + 1);
            if (command == "P")
            {
                prompts.Add(new MtcServerPrompt(timestamp, "Enter your choice [T] ?"));
            }
            else if (command == "T")
            {
                prompts.Add(new MtcServerPrompt(timestamp, "How many holds of Fuel Ore do you want to buy [20]?"));
            }
            else if (command == "20")
            {
                prompts.Add(new MtcServerPrompt(timestamp, "Your offer [501] ?"));
            }
            else if (command == availableCredits.ToString(System.Globalization.CultureInfo.InvariantCulture) && quoteAfterCounterOffer is not null)
            {
                prompts.Add(new MtcServerPrompt(timestamp, quoteAfterCounterOffer));
            }
            else
            {
                prompts.Add(new MtcServerPrompt(timestamp, "Command [TL=00:00:00]:[35]"));
            }

            return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
        },
        _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
        _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
        () => Task.FromResult(false),
        () => true,
        () => DateTime.MinValue,
        (_, _) => Task.CompletedTask,
        () => prompts.ToArray(),
        () => false,
        commandsApproved: true);
    await sender.SendAsync(
        "meridian",
        new GameCommandRequest(GameCommand.TradeAtCurrentPort, Product: TradeProduct.FuelOre)
        {
            AvailableCredits = availableCredits
        });
    return commands;
}

Check(
    (await RunUnaffordableQuoteScenarioAsync(500, quoteAfterCounterOffer: null)).SequenceEqual(
        [("P", false), ("T", false), ("20", true), ("500", true)]),
    "counter-offers every verified credit when a quote is slightly above the balance instead of offering 0");
Check(
    (await RunUnaffordableQuoteScenarioAsync(500, "Your offer [505] ?")).SequenceEqual(
        [("P", false), ("T", false), ("20", true), ("500", true), ("0", true)]),
    "declines a second unaffordable quote after one counter-offer");
Check(
    (await RunUnaffordableQuoteScenarioAsync(400, quoteAfterCounterOffer: null)).SequenceEqual(
        [("P", false), ("T", false), ("20", true), ("0", true)]),
    "declines a quote far above the balance without submitting the quoted price");

var coursePlotCommands = new List<(string Command, bool AppendEnter)>();
var coursePlotPrompts = new List<MtcServerPrompt>
{
    new(DateTimeOffset.UtcNow.AddSeconds(-1), "Command [TL=00:00:00]:[35] (?=Help)? :")
};
var coursePlotLines = new List<MtcServerPrompt>();
var coursePlotSender = new MtcGameCommandSender(
    "meridian",
    (command, appendEnter) =>
    {
        coursePlotCommands.Add((command, appendEnter));
        DateTimeOffset timestamp = DateTimeOffset.UtcNow.AddTicks(coursePlotPrompts.Count + 1);
        switch (command)
        {
            case "C":
                coursePlotPrompts.Add(new MtcServerPrompt(timestamp, "Computer command [TL=00:00:00]:[35] (?=Help)? :"));
                break;
            case "F":
                coursePlotPrompts.Add(new MtcServerPrompt(timestamp, "What is the starting sector ? [35]"));
                break;
            case "":
                coursePlotPrompts.Add(new MtcServerPrompt(timestamp, "What is the destination sector ?"));
                break;
            case "2909":
                coursePlotLines.Add(new MtcServerPrompt(timestamp, "The shortest path (2 hops, 6 turns) from sector 35 to sector 2909 is:"));
                coursePlotLines.Add(new MtcServerPrompt(timestamp.AddTicks(1), "35 > (400) > 2909"));
                coursePlotPrompts.Add(new MtcServerPrompt(timestamp.AddTicks(2), "Computer command [TL=00:00:00]:[35] (?=Help)? :"));
                break;
            case "Q":
                coursePlotPrompts.Add(new MtcServerPrompt(timestamp, "Command [TL=00:00:00]:[35] (?=Help)? :"));
                break;
        }

        return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => true,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => coursePlotPrompts.ToArray(),
    () => false,
    commandsApproved: true,
    getRecentServerLines: () => coursePlotLines.ToArray());
await coursePlotSender.SendAsync(
    "meridian",
    new GameCommandRequest(GameCommand.PlotCourseToSector, 2909));
Check(
    coursePlotCommands.SequenceEqual(
        [("C", false), ("F", false), ("", true), ("2909", true), ("Q", false)]),
    "plots a course with the ship computer and returns to the Command prompt");
async Task<(List<(string Command, bool AppendEnter)> Commands, bool Rejected)> RunContinuePauseScenarioAsync(string latestPrompt)
{
    var commands = new List<(string Command, bool AppendEnter)>();
    var prompts = new List<MtcServerPrompt> { new(DateTimeOffset.UtcNow.AddSeconds(-1), latestPrompt) };
    var sender = new MtcGameCommandSender(
        "meridian",
        (command, appendEnter) =>
        {
            commands.Add((command, appendEnter));
            return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
        },
        _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
        _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
        () => Task.FromResult(false),
        () => false,
        () => DateTime.MinValue,
        (_, _) => Task.CompletedTask,
        () => prompts.ToArray(),
        () => false,
        commandsApproved: true);
    try
    {
        await sender.SendAsync("meridian", new GameCommandRequest(GameCommand.ContinuePausePrompt));
        return (commands, false);
    }
    catch (InvalidOperationException)
    {
        return (commands, true);
    }
}

var pauseContinued = await RunContinuePauseScenarioAsync("[Pause] - [Press Space or Enter to continue]");
Check(
    !pauseContinued.Rejected && pauseContinued.Commands.SequenceEqual([("", true)]),
    "continues a live pause prompt with Enter");
var nonPauseRejected = await RunContinuePauseScenarioAsync("Command [TL=00:00:00]:[2909] (?=Help)? :");
Check(
    nonPauseRejected.Rejected && nonPauseRejected.Commands.Count == 0,
    "refuses to send a pause continuation when the latest prompt is not a pause");
async Task<List<(string Command, bool AppendEnter)>> RunFighterEncounterScenarioAsync(bool tollFighters)
{
    var commands = new List<(string Command, bool AppendEnter)>();
    DateTimeOffset encounterAt = DateTimeOffset.UtcNow.AddMilliseconds(-50);
    string optionPrompt = tollFighters ? "Option? (A,D,I,R,P,S,?):? " : "Option? (A,D,I,R,S,?):? ";
    var prompts = new List<MtcServerPrompt> { new(encounterAt, optionPrompt) };
    var lines = new List<MtcServerPrompt>
    {
        new(encounterAt.AddMilliseconds(-1), tollFighters
            ? "You have to destroy the fighters or pay their toll to remain in this sector."
            : "You have to destroy the fighters to remain in this sector.")
    };
    var sender = new MtcGameCommandSender(
        "meridian",
        (command, appendEnter) =>
        {
            commands.Add((command, appendEnter));
            DateTimeOffset timestamp = DateTimeOffset.UtcNow.AddTicks(prompts.Count + 1);
            prompts.Add(command == "P"
                ? new MtcServerPrompt(timestamp, "Do you want to pay it?")
                : new MtcServerPrompt(timestamp, "Command [TL=00:00:00]:[2627] (?=Help)? :"));
            return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
        },
        _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
        _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
        () => Task.FromResult(false),
        () => false,
        () => DateTime.MinValue,
        (_, _) => Task.CompletedTask,
        () => prompts.ToArray(),
        () => false,
        commandsApproved: true,
        getRecentServerLines: () => lines.ToArray());
    await sender.SendAsync("meridian", new GameCommandRequest(GameCommand.ResolveFighterEncounter));
    return commands;
}

Check(
    (await RunFighterEncounterScenarioAsync(tollFighters: true)).SequenceEqual([("P", false), ("Y", false)]),
    "pays a toll-fighter toll and confirms it instead of attacking");
Check(
    (await RunFighterEncounterScenarioAsync(tollFighters: false)).SequenceEqual([("R", false)]),
    "retreats from non-toll hostile fighters instead of attacking");
var autopilotTradeCommands = new List<(string Command, bool AppendEnter)>();
var autopilotTradePrompts = new List<MtcServerPrompt>
{
    new(DateTimeOffset.UtcNow, "Engage the Autopilot? (Y/N/Single step/Express) [Y]")
};
var autopilotTradeSender = new MtcGameCommandSender(
    "meridian",
    (command, appendEnter) =>
    {
        autopilotTradeCommands.Add((command, appendEnter));
        return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => true,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => autopilotTradePrompts.ToArray(),
    () => true,
    commandsApproved: true);
bool autopilotTradeRejected = false;
try
{
    await autopilotTradeSender.SendAsync(
        "meridian",
        new GameCommandRequest(GameCommand.TradeAtCurrentPort, Product: TradeProduct.Organics));
}
catch (InvalidOperationException ex)
{
    autopilotTradeRejected = ex.Message.Contains(
        "fresh TradeWars Command prompt",
        StringComparison.Ordinal);
}
Check(
    autopilotTradeRejected && autopilotTradeCommands.Count == 0,
    "does not start a port trade when a fresh AutopilotConfirmation is the latest prompt");

var stalePromptTradeCommands = new List<(string Command, bool AppendEnter)>();
var stalePromptTradeSender = new MtcGameCommandSender(
    "meridian",
    (command, appendEnter) =>
    {
        stalePromptTradeCommands.Add((command, appendEnter));
        return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => true,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () =>
    [
        new MtcServerPrompt(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            "Command [TL=00:00:00]:[35]")
    ],
    () => true,
    commandsApproved: true);
bool staleCommandPromptTradeRejected = false;
try
{
    await stalePromptTradeSender.SendAsync(
        "meridian",
        new GameCommandRequest(GameCommand.TradeAtCurrentPort, Product: TradeProduct.Organics));
}
catch (InvalidOperationException ex)
{
    staleCommandPromptTradeRejected = ex.Message.Contains(
        "fresh TradeWars Command prompt",
        StringComparison.Ordinal);
}
Check(
    staleCommandPromptTradeRejected && stalePromptTradeCommands.Count == 0,
    "does not start a port trade from a stale Command prompt");

var saleTradeCommands = new List<(string Command, bool AppendEnter)>();
var saleTradePrompts = new List<MtcServerPrompt>();
bool saleTradeHaggleEnabled = true;
saleTradePrompts.Add(new MtcServerPrompt(
    DateTimeOffset.UtcNow.AddSeconds(-1),
    "Command [TL=00:00:00]:[35]"));
var saleTradeSender = new MtcGameCommandSender(
    "meridian",
    (command, appendEnter) =>
    {
        saleTradeCommands.Add((command, appendEnter));
        DateTimeOffset timestamp = DateTimeOffset.UtcNow.AddTicks(saleTradePrompts.Count + 1);
        if (command == "P")
        {
            saleTradePrompts.Add(new MtcServerPrompt(
                timestamp,
                "Enter your choice [T] ?"));
        }
        else if (command == "T")
        {
            saleTradePrompts.Add(new MtcServerPrompt(
                timestamp,
                "How many holds of Organics do you want to buy [20]?"));
        }
        else if (command == "0")
        {
            saleTradePrompts.Add(new MtcServerPrompt(
                timestamp,
                "How many holds of Organics do you want to sell [5]?"));
        }
        else if (command == "5")
        {
            saleTradePrompts.Add(new MtcServerPrompt(
                timestamp,
                "Command [TL=00:00:00]:[35]"));
        }

        return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => true,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => saleTradePrompts.ToArray(),
    () => saleTradeHaggleEnabled,
    commandsApproved: true,
    setNativeHaggleEnabled: enabled => saleTradeHaggleEnabled = enabled);
await saleTradeSender.SendAsync(
    "meridian",
    new GameCommandRequest(GameCommand.TradeAtCurrentPort, Product: TradeProduct.Organics)
    {
        SellingExistingCargo = true,
    });
Check(
    saleTradeCommands.SequenceEqual(
    [
        ("P", false),
        ("T", false),
        ("0", true),
        ("5", true),
    ]),
    "skips port purchases when the selected opportunity is to sell existing cargo");

var recoveryTradeCommands = new List<(string Command, bool AppendEnter)>();
var recoveryTradePrompts = new List<MtcServerPrompt>();
bool recoveryTradeHaggleEnabled = true;
DateTimeOffset recoveryPromptAt = DateTimeOffset.UtcNow;
recoveryTradePrompts.Add(new MtcServerPrompt(
    recoveryPromptAt,
    "How many holds of Fuel Ore do you want to buy [20]?"));
var recoveryTradeSender = new MtcGameCommandSender(
    "meridian",
    (command, appendEnter) =>
    {
        recoveryTradeCommands.Add((command, appendEnter));
        DateTimeOffset timestamp = DateTimeOffset.UtcNow.AddTicks(recoveryTradePrompts.Count + 1);
        if (command == "0" && recoveryTradePrompts.Count == 1)
        {
            recoveryTradePrompts.Add(new MtcServerPrompt(
                timestamp,
                "How many holds of Organics do you want to sell [5]?"));
        }
        else if (command == "0")
        {
            recoveryTradePrompts.Add(new MtcServerPrompt(
                timestamp,
                "Command [TL=00:00:00]:[35]"));
        }

        return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => false,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => recoveryTradePrompts.ToArray(),
    () => recoveryTradeHaggleEnabled,
    commandsApproved: true,
    setNativeHaggleEnabled: enabled => recoveryTradeHaggleEnabled = enabled);
await recoveryTradeSender.RecoverUntrackedPortTradeAsync("meridian", recoveryPromptAt);
Check(
    recoveryTradeCommands.SequenceEqual([("0", true), ("0", true)]),
    "declines each quantity in an untracked port trade and waits for Command");

var affordableRecoveryCommands = new List<(string Command, bool AppendEnter)>();
var affordableRecoveryPrompts = new List<MtcServerPrompt>();
DateTimeOffset affordableRecoveryPromptAt = DateTimeOffset.UtcNow;
affordableRecoveryPrompts.Add(new MtcServerPrompt(
    affordableRecoveryPromptAt,
    "How many holds of Fuel Ore do you want to buy [20]?"));
var affordableRecoverySender = new MtcGameCommandSender(
    "meridian",
    (command, appendEnter) =>
    {
        affordableRecoveryCommands.Add((command, appendEnter));
        DateTimeOffset timestamp = DateTimeOffset.UtcNow.AddTicks(affordableRecoveryPrompts.Count + 1);
        if (command == "20")
        {
            affordableRecoveryPrompts.Add(new MtcServerPrompt(timestamp, "Your offer [500] ?"));
        }
        else if (command == "500")
        {
            affordableRecoveryPrompts.Add(new MtcServerPrompt(
                timestamp,
                "Command [TL=00:00:00]:[35]"));
        }

        return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => false,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => affordableRecoveryPrompts.ToArray(),
    () => false,
    commandsApproved: true);
await affordableRecoverySender.RecoverUntrackedPortTradeAsync(
    "meridian",
    affordableRecoveryPromptAt,
    followUpBuyProduct: TradeProduct.FuelOre,
    availableCredits: 500);
Check(
    affordableRecoveryCommands.SequenceEqual([("20", true), ("500", true)]),
    "uses verified credits to fill affordable holds, including an exact-balance quote");

var requestedRecoveryCommands = new List<(string Command, bool AppendEnter)>();
var requestedRecoveryPrompts = new List<MtcServerPrompt>();
DateTimeOffset requestedRecoveryPromptAt = DateTimeOffset.UtcNow;
requestedRecoveryPrompts.Add(new MtcServerPrompt(
    requestedRecoveryPromptAt,
    "How many holds of Organics do you want to buy [20]?"));
var requestedRecoverySender = new MtcGameCommandSender(
    "meridian",
    (command, appendEnter) =>
    {
        requestedRecoveryCommands.Add((command, appendEnter));
        DateTimeOffset timestamp = DateTimeOffset.UtcNow.AddTicks(requestedRecoveryPrompts.Count + 1);
        if (command == "20")
        {
            requestedRecoveryPrompts.Add(new MtcServerPrompt(timestamp, "Your offer [500] ?"));
        }
        else if (command == "500")
        {
            requestedRecoveryPrompts.Add(new MtcServerPrompt(
                timestamp,
                "Command [TL=00:00:00]:[35]"));
        }

        return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => false,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => requestedRecoveryPrompts.ToArray(),
    () => false,
    commandsApproved: true);
await requestedRecoverySender.RecoverUntrackedPortTradeAsync(
    "meridian",
    requestedRecoveryPromptAt,
    availableCredits: 500,
    requestedProduct: TradeProduct.Organics);
Check(
    requestedRecoveryCommands.SequenceEqual([("20", true), ("500", true)]),
    "preserves the requested purchase product and fills all available holds during recovery");

var correlatedOfferCommands = new List<(string Command, bool AppendEnter)>();
DateTimeOffset correlatedQuantityPromptAt = DateTimeOffset.UtcNow.AddSeconds(-35);
DateTimeOffset correlatedOfferPromptAt = correlatedQuantityPromptAt.AddSeconds(1);
var correlatedOfferPrompts = new List<MtcServerPrompt>
{
    new(
        correlatedQuantityPromptAt,
        "How many holds of Fuel Ore do you want to buy [20]?"),
    new(
        correlatedOfferPromptAt,
        "Your offer [107] ?"),
    new(
        correlatedOfferPromptAt.AddMilliseconds(10),
        "PortTradeOffer",
        IsPresenceHeartbeat: true,
        PromptObservedAt: correlatedOfferPromptAt,
        IsCurrentPromptConfirmed: true)
};
var correlatedOfferSender = new MtcGameCommandSender(
    "meridian",
    (command, appendEnter) =>
    {
        correlatedOfferCommands.Add((command, appendEnter));
        if (command == "107")
        {
            correlatedOfferPrompts.Add(new MtcServerPrompt(
                DateTimeOffset.UtcNow,
                "Command [TL=00:00:00]:[35]"));
        }

        return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => true,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => correlatedOfferPrompts.ToArray(),
    () => false,
    commandsApproved: true);
await correlatedOfferSender.RecoverUntrackedPortTradeAsync(
    "meridian",
    correlatedQuantityPromptAt,
    followUpBuyProduct: TradeProduct.FuelOre,
    availableCredits: 500);
Check(
    correlatedOfferCommands.SequenceEqual([("107", true)]),
    "recovers an affordable quote across a presence heartbeat within the freshness window");

var echoedTradeCommands = new List<(string Command, bool AppendEnter)>();
DateTimeOffset echoedTradePromptAt = DateTimeOffset.UtcNow;
var echoedTradePrompts = new List<MtcServerPrompt>
{
    new(echoedTradePromptAt, "How many holds of Organics do you want to sell [20]?")
};
var echoedTradeSender = new MtcGameCommandSender(
    "meridian",
    (command, appendEnter) =>
    {
        echoedTradeCommands.Add((command, appendEnter));
        DateTimeOffset timestamp = DateTimeOffset.UtcNow.AddTicks(echoedTradePrompts.Count + 1);
        if (command == "20")
        {
            echoedTradePrompts.Add(new MtcServerPrompt(
                timestamp,
                "How many holds of Organics do you want to sell [20]? 20"));
            echoedTradePrompts.Add(new MtcServerPrompt(timestamp.AddTicks(1), "Your offer [1,690] ?"));
        }
        else if (command == "1690")
        {
            echoedTradePrompts.Add(new MtcServerPrompt(
                timestamp,
                "Your offer [1,690] ? 1690"));
            echoedTradePrompts.Add(new MtcServerPrompt(
                timestamp.AddTicks(1),
                "Command [TL=00:00:00]:[35]"));
        }

        return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => false,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => echoedTradePrompts.ToArray(),
    () => false,
    commandsApproved: true);
await echoedTradeSender.RecoverUntrackedPortTradeAsync(
    "meridian",
    echoedTradePromptAt,
    availableCredits: 1_325,
    requestedProduct: TradeProduct.Organics,
    sellingExistingCargo: true);
Check(
    echoedTradeCommands.SequenceEqual([("20", true), ("1690", true)]),
    "ignores echoed quantity and quote inputs while completing a bounded sale autonomously");

var unmatchedOfferCommands = new List<(string Command, bool AppendEnter)>();
var unmatchedOfferPrompts = new List<MtcServerPrompt>
{
    new(DateTimeOffset.UtcNow, "Your offer [107] ?")
};
var unmatchedOfferSender = new MtcGameCommandSender(
    "meridian",
    (command, appendEnter) =>
    {
        unmatchedOfferCommands.Add((command, appendEnter));
        return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => false,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => unmatchedOfferPrompts.ToArray(),
    () => false,
    commandsApproved: true);
bool unmatchedOfferRejected = false;
try
{
    await unmatchedOfferSender.RecoverUntrackedPortTradeAsync(
        "meridian",
        unmatchedOfferPrompts[0].Timestamp,
        availableCredits: 500);
}
catch (InvalidOperationException exception) when (
    exception.Message.Contains("no response was sent", StringComparison.Ordinal))
{
    unmatchedOfferRejected = true;
}
Check(
    unmatchedOfferRejected && unmatchedOfferCommands.Count == 0,
    "leaves an unmatched untracked quote unanswered instead of sending zero");

var autopilotDeclineCommands = new List<(string Command, bool AppendEnter)>();
var autopilotDeclinePrompts = new List<MtcServerPrompt>
{
    new(
        DateTimeOffset.UtcNow,
        "Engage the Autopilot? (Y/N/Single step/Express) [Y]")
};
var autopilotDeclineSender = new MtcGameCommandSender(
    "meridian",
    (command, appendEnter) =>
    {
        autopilotDeclineCommands.Add((command, appendEnter));
        autopilotDeclinePrompts.Add(new MtcServerPrompt(
            DateTimeOffset.UtcNow.AddMilliseconds(1),
            "Command [TL=00:00:00]:[35]"));
        return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => true,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => autopilotDeclinePrompts.ToArray(),
    () => true,
    commandsApproved: true);
await autopilotDeclineSender.SendAsync(
    "meridian",
    new GameCommandRequest(GameCommand.DeclineAutopilotEngagement));
Check(
    autopilotDeclineCommands.SequenceEqual([("N", false)]),
    "declines the current autopilot prompt with a single N key and waits for Command");

var moveEchoCommands = new List<(string Command, bool AppendEnter)>();
var moveEchoPrompts = new List<MtcServerPrompt>
{
    new(DateTimeOffset.UtcNow, "Command [TL=00:00:00]:[551]")
};
var moveEchoSender = new MtcGameCommandSender(
    "meridian",
    (command, appendEnter) =>
    {
        moveEchoCommands.Add((command, appendEnter));
        DateTimeOffset observedAt = DateTimeOffset.UtcNow;
        if (command == "M")
        {
            moveEchoPrompts.Add(new MtcServerPrompt(
                observedAt.AddMilliseconds(1),
                "To which Sector [551] ?"));
        }
        else if (command == "860")
        {
            moveEchoPrompts.Add(new MtcServerPrompt(
                observedAt.AddMilliseconds(1),
                "To which Sector [551] ? 860"));
            moveEchoPrompts.Add(new MtcServerPrompt(
                observedAt.AddMilliseconds(2),
                "Command [TL=00:00:00]:[206]"));
        }

        return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => true,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => moveEchoPrompts.ToArray(),
    () => true,
    commandsApproved: true);
await moveEchoSender.SendAsync(
    "meridian",
    new GameCommandRequest(GameCommand.MoveToSector, 860));
Check(
    moveEchoCommands.SequenceEqual([("M", false), ("860", true)]),
    "waits through the echoed MoveSector prompt for a fresh movement result");

var navPointExitCommands = new List<(string Command, bool AppendEnter)>();
var navPointExitPrompts = new List<MtcServerPrompt>
{
    new(DateTimeOffset.UtcNow, "Choose NavPoint (?=Help) [Q] :")
};
var navPointExitSender = new MtcGameCommandSender(
    "meridian",
    (command, appendEnter) =>
    {
        navPointExitCommands.Add((command, appendEnter));
        navPointExitPrompts.Add(new MtcServerPrompt(
            DateTimeOffset.UtcNow.AddMilliseconds(1),
            "Command [TL=00:00:00]:[35]"));
        return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => false,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () => navPointExitPrompts.ToArray(),
    () => true,
    commandsApproved: true);
await navPointExitSender.SendAsync(
    "meridian",
    new GameCommandRequest(GameCommand.ExitNavPointSelection));
Check(
    navPointExitCommands.SequenceEqual([("Q", true)]),
    "exits a fresh NavPoint selection with the non-moving default choice and waits for Command");

var staleNavPointCommands = new List<(string Command, bool AppendEnter)>();
var staleNavPointExitSender = new MtcGameCommandSender(
    "meridian",
    (command, appendEnter) =>
    {
        staleNavPointCommands.Add((command, appendEnter));
        return Task.FromResult(MtcRpcActionResult.Ok("command sent"));
    },
    _ => Task.FromResult(MtcRpcActionResult.Ok("world trade started")),
    _ => Task.FromResult(MtcRpcActionResult.Ok("script loaded")),
    () => Task.FromResult(false),
    () => false,
    () => DateTime.MinValue,
    (_, _) => Task.CompletedTask,
    () =>
    [
        new MtcServerPrompt(
            DateTimeOffset.UtcNow.AddMinutes(-1),
            "Choose NavPoint (?=Help) [Q] :")
    ],
    () => true,
    commandsApproved: true);
bool staleNavPointPromptRejected = false;
try
{
    await staleNavPointExitSender.SendAsync(
        "meridian",
        new GameCommandRequest(GameCommand.ExitNavPointSelection));
}
catch (InvalidOperationException)
{
    staleNavPointPromptRejected = true;
}
Check(
    staleNavPointPromptRejected && staleNavPointCommands.Count == 0,
    "does not exit a stale NavPoint prompt");

string gameAgentSystemPrompt = GameAgentProviders.BuildSystemPrompt();
Check(
    gameAgentSystemPrompt.Contains("X lists current avoids", StringComparison.Ordinal) &&
    gameAgentSystemPrompt.Contains("V opens avoid management and is state-changing", StringComparison.Ordinal),
    "grounds avoid-list advice in the Computer prompt's read-only and state-changing actions");
Check(
    gameAgentSystemPrompt.Contains("recommend refreshing ship information before any further movement", StringComparison.Ordinal),
    "instructs advice model to resolve stale sector snapshots before movement");

DateTimeOffset promptObservedAt = DateTimeOffset.UtcNow;
GameAgentEvent? heartbeatPrompt = MainWindow.FindCurrentAgentPrompt(
    [
        new GameAgentEvent
        {
            Timestamp = promptObservedAt.AddMinutes(-20),
            Kind = GameAgentEventKind.Connected,
        },
        new GameAgentEvent
        {
            Timestamp = promptObservedAt.AddSeconds(-5),
            Kind = GameAgentEventKind.PresenceHeartbeat,
            PromptSurface = "Command",
            Metadata = new Dictionary<string, string>
            {
                ["promptObservedAt"] = promptObservedAt.AddSeconds(-5).ToString("O"),
                ["promptIsCurrent"] = "true"
            }
        },
    ],
    promptObservedAt);
Check(
    heartbeatPrompt?.Kind == GameAgentEventKind.PresenceHeartbeat &&
    heartbeatPrompt.PromptSurface == "Command",
    "recovers a fresh Command prompt from heartbeat when the connection event aged out");
Check(
    MainWindow.FindCurrentAgentPrompt(
        [
            new GameAgentEvent
            {
                Timestamp = promptObservedAt.AddSeconds(-30),
                Kind = GameAgentEventKind.PresenceHeartbeat,
                PromptSurface = "Command",
            },
        ],
        promptObservedAt) is null,
    "rejects stale presence heartbeat when recovering startup prompt");

using (JsonDocument ollamaPayload = JsonDocument.Parse(JsonSerializer.Serialize(new OllamaChatRequest
{
    Model = GameAgentProviders.DefaultOllamaModel,
    Think = false,
    Options = new OllamaGenerationOptions { Temperature = 0.2, NumPredict = 384 },
})))
{
    Check(
        ollamaPayload.RootElement.GetProperty("think").ValueKind == JsonValueKind.False &&
        ollamaPayload.RootElement.GetProperty("options").GetProperty("num_predict").GetInt32() == 384,
        "keeps Ollama reasoning out of user output and bounds response generation");
}

string scriptCatalogTestDirectory = Path.Combine(Path.GetTempPath(), $"MtcScriptCatalogTests-{Guid.NewGuid():N}");
Directory.CreateDirectory(scriptCatalogTestDirectory);
try
{
    File.WriteAllText(
        Path.Combine(scriptCatalogTestDirectory, "catalog.json"),
        """{"entries":[{"file":"intel-data/psii-avoidmgr.ts","category":"intel-data"},{"file":"combat-defense/test.ts","category":"combat-defense"}]}""");
    GameAgentToolCallResult scriptSearch = GameAgentToolRegistry.SearchScriptCatalog(
        scriptCatalogTestDirectory,
        "find a TWX script for avoid sectors");
    Check(
        scriptSearch.Success &&
        scriptSearch.Data.Values.Any(value => value.Contains("intel-data/psii-avoidmgr.ts", StringComparison.Ordinal)),
        "searches catalog filenames for relevant TWX scripts");
    Check(
        scriptSearch.Message.Contains("do not verify behavior or safety", StringComparison.Ordinal),
        "labels script matches as unreviewed and never executes them");

    GameAgentToolCallResult catalogOverview = GameAgentToolRegistry.SearchScriptCatalog(
        scriptCatalogTestDirectory,
        "show me available TWX scripts");
    Check(
        catalogOverview.Data.ContainsKey("category_intel-data") &&
        catalogOverview.Data.ContainsKey("category_combat-defense"),
        "returns category counts for broad script-library discovery");

    string modelPrompt = GameAgentProviders.BuildUserPrompt(new GameAgentModelRequest
    {
        Prompt = "find a TWX script for avoid sectors",
        ScriptCatalogDirectory = scriptCatalogTestDirectory,
        Context = new GameAgentContextSnapshot(),
    });
    Check(
        modelPrompt.Contains("intel-data/psii-avoidmgr.ts", StringComparison.Ordinal),
        "includes matching local catalog entries in model context");
}
finally
{
    Directory.Delete(scriptCatalogTestDirectory, recursive: true);
}

byte[] initialPassword = Encoding.ASCII.GetBytes("SAMEVALUE1");
byte[] samePassword = Encoding.ASCII.GetBytes("SAMEVALUE1");
byte[] differentPassword = Encoding.ASCII.GetBytes("DIFFERENT1");
using (var confirmationGuard = new MtcRpcPasswordConfirmationGuard())
{
    confirmationGuard.Begin(initialPassword);
    Check(confirmationGuard.Matches(samePassword), "confirmation guard accepts the same password bytes");
    Check(!confirmationGuard.Matches(differentPassword), "confirmation guard rejects different password bytes");
    confirmationGuard.Clear();
    Check(!confirmationGuard.Matches(samePassword), "confirmation guard clears its process-local fingerprint");
}
System.Security.Cryptography.CryptographicOperations.ZeroMemory(initialPassword);
System.Security.Cryptography.CryptographicOperations.ZeroMemory(samePassword);
System.Security.Cryptography.CryptographicOperations.ZeroMemory(differentPassword);

IReadOnlyList<GameAgentEvent> ValidPasswordFlow(DateTimeOffset currentTime, string prompt = "Enter your new password:")
    =>
    [
        new GameAgentEvent
        {
            Timestamp = currentTime.AddSeconds(-10),
            GameName = "Meridian Game D",
            Kind = GameAgentEventKind.ServerPrompt,
            PlainText = "Would you like to start a new character?",
        },
        new GameAgentEvent
        {
            Timestamp = currentTime,
            GameName = "Meridian Game D",
            Kind = GameAgentEventKind.ServerPrompt,
            PlainText = prompt,
        },
    ];

MtcRpcSensitivePrompt? valid = MtcRpcSecretInputPolicy.FindCurrentAccountSetupPasswordPrompt(
    ValidPasswordFlow(now), true, "Meridian Game D", "192.168.4.75", 2002, now);
Check(valid?.Stage == "password", "recognizes fresh Game D account-setup password prompt");

Check(
    MtcRpcSecretInputPolicy.ClassifyPasswordPrompt("Please confirm your password:") == "password-confirmation",
    "classifies explicit password-confirmation wording");
IReadOnlyList<GameAgentEvent> repeatedPasswordFlow =
[
    new GameAgentEvent
    {
        Timestamp = now.AddSeconds(-10),
        GameName = "Meridian Game D",
        Kind = GameAgentEventKind.ServerPrompt,
        PlainText = "Would you like to start a new character?",
    },
    new GameAgentEvent
    {
        Timestamp = now.AddSeconds(-8),
        GameName = "Meridian Game D",
        Kind = GameAgentEventKind.ServerLine,
        PlainText = "Please enter a password for this game account. Repeat password to verify.",
    },
    new GameAgentEvent
    {
        Timestamp = now.AddSeconds(-6),
        GameName = "Meridian Game D",
        Kind = GameAgentEventKind.ServerPrompt,
        PlainText = "[redacted account-setup password prompt]",
        Metadata = new Dictionary<string, string>
        {
            ["protectedSetupPromptStage"] = "password",
        },
    },
    new GameAgentEvent
    {
        Timestamp = now.AddSeconds(-1),
        GameName = "Meridian Game D",
        Kind = GameAgentEventKind.ClientInput,
        PlainText = "[redacted account-setup credential input]",
        Metadata = new Dictionary<string, string>
        {
            ["inputType"] = "account-setup-credential",
        },
    },
    new GameAgentEvent
    {
        Timestamp = now,
        GameName = "Meridian Game D",
        Kind = GameAgentEventKind.ServerPrompt,
        PlainText = "[redacted account-setup password prompt]",
        Metadata = new Dictionary<string, string>
        {
            ["protectedSetupPromptStage"] = "password",
        },
    },
];
MtcRpcSensitivePrompt? confirmation = MtcRpcSecretInputPolicy.FindCurrentAccountSetupPasswordPrompt(
    repeatedPasswordFlow, true, "Meridian Game D", "192.168.4.75", 2002, now);
Check(confirmation?.Stage == "password-confirmation", "recognizes generic prompt as confirmation after explicit repeat instruction and one submission");

IReadOnlyList<GameAgentEvent> unconfirmedRepeatFlow =
[
    repeatedPasswordFlow[0],
    repeatedPasswordFlow[2],
    repeatedPasswordFlow[3],
    repeatedPasswordFlow[4],
];
Check(
    MtcRpcSecretInputPolicy.FindCurrentAccountSetupPasswordPrompt(
        unconfirmedRepeatFlow, true, "Meridian Game D", "192.168.4.75", 2002, now) == null,
    "rejects a second generic password prompt without an explicit repeat instruction");

IReadOnlyList<GameAgentEvent> completedPasswordFlow =
[
    .. repeatedPasswordFlow,
    new GameAgentEvent
    {
        Timestamp = now.AddMilliseconds(10),
        GameName = "Meridian Game D",
        Kind = GameAgentEventKind.ClientInput,
        PlainText = "[redacted account-setup credential input]",
        Metadata = new Dictionary<string, string>
        {
            ["inputType"] = "account-setup-credential",
        },
    },
    new GameAgentEvent
    {
        Timestamp = now.AddMilliseconds(20),
        GameName = "Meridian Game D",
        Kind = GameAgentEventKind.ServerPrompt,
        PlainText = "[redacted account-setup password prompt]",
        Metadata = new Dictionary<string, string>
        {
            ["protectedSetupPromptStage"] = "password",
        },
    },
];
Check(
    MtcRpcSecretInputPolicy.FindCurrentAccountSetupPasswordPrompt(
        completedPasswordFlow, true, "Meridian Game D", "192.168.4.75", 2002, now) == null,
    "rejects additional password input after the confirmation submission");
IReadOnlyList<GameAgentEvent> approvalDelayFlow =
[
    new GameAgentEvent
    {
        Timestamp = now.AddMinutes(-4).AddSeconds(-10),
        GameName = "Meridian Game D",
        Kind = GameAgentEventKind.ServerPrompt,
        PlainText = "Would you like to start a new character?",
    },
    new GameAgentEvent
    {
        Timestamp = now.AddMinutes(-4),
        GameName = "Meridian Game D",
        Kind = GameAgentEventKind.ServerPrompt,
        PlainText = "Enter your new password:",
    },
];
Check(
    MtcRpcSecretInputPolicy.FindCurrentAccountSetupPasswordPrompt(
        approvalDelayFlow, true, "Meridian Game D", "192.168.4.75", 2002, now)?.Stage == "password",
    "allows a still-current setup prompt during a delayed approval round-trip");
Check(
    MtcRpcSecretInputPolicy.ClassifyProtectedSetupPrompt("Enter the name of your trader:") == "trader-alias",
    "classifies trader alias without retaining prompt text");
Check(
    MtcRpcSecretInputPolicy.ClassifyProtectedSetupPrompt("What is the name of your ship?") == "ship-name",
    "classifies ship-name prompt without retaining prompt text");
Check(
    MtcRpcSecretInputPolicy.ClassifyProtectedSetupPrompt("Please enter a planet name:") == "planet",
    "marks planet prompt as a stop boundary");
Check(
    MtcRpcSecretInputPolicy.ClassifyProtectedSetupPrompt("Unexpected setup question") == "unknown",
    "fails closed on unknown protected setup prompt");
Check(
    MtcRpcSecretInputPolicy.ClassifyProtectedSetupNotice("The passwords do not match.") == "password-mismatch",
    "classifies a password mismatch without retaining server text");
Check(
    MtcRpcSecretInputPolicy.ClassifyProtectedSetupNotice("Invalid password, try again.") == "password-rejected",
    "classifies a rejected password without retaining server text");
Check(
    MtcRpcSecretInputPolicy.ClassifyProtectedSetupNotice("Welcome to the game.") == null,
    "ignores unclassified setup output while redaction is active");

const string approvedPausePrompt = "]D[Pause] - [Press Space or Enter to continue]";
Check(
    MtcRpcApprovedPauseKeyPolicy.IsAllowed(
        true, true, true, "Meridian Game D", "192.168.4.75", 2002, approvedPausePrompt, "Enter"),
    "allows Enter only at the verified Game D pause");
Check(
    MtcRpcApprovedPauseKeyPolicy.IsAllowed(
        true, true, true, "Meridian Game D", "192.168.4.75", 2002, approvedPausePrompt, "Space"),
    "allows Space only at the verified Game D pause");
Check(
    !MtcRpcApprovedPauseKeyPolicy.IsAllowed(
        true, true, true, "Meridian Game D", "192.168.4.75", 2002, approvedPausePrompt, "D"),
    "rejects non-pause key input");
Check(
    !MtcRpcApprovedPauseKeyPolicy.IsAllowed(
        true, true, true, "Meridian Game E", "192.168.4.75", 2002, approvedPausePrompt, "Enter"),
    "rejects Game E pause-key input");
Check(
    !MtcRpcApprovedPauseKeyPolicy.IsAllowed(
        true, true, true, "Meridian Game D", "192.168.4.76", 2002, approvedPausePrompt, "Enter"),
    "rejects unapproved pause-key host");
Check(
    !MtcRpcApprovedPauseKeyPolicy.IsAllowed(
        true, true, true, "Meridian Game D", "192.168.4.75", 2002, "Command [TL=00:00:00]", "Enter"),
    "rejects Enter outside the explicit pause prompt");
Check(
    !MtcRpcApprovedPauseKeyPolicy.IsAllowed(
        false, true, true, "Meridian Game D", "192.168.4.75", 2002, approvedPausePrompt, "Enter"),
    "rejects non-loopback pause-key input");
Check(
    MtcRpcApprovedPauseKeyPolicy.GetInputByte("Enter") == "\r" &&
    MtcRpcApprovedPauseKeyPolicy.GetInputByte("Space") == " ",
    "maps approved pause keys to exactly one terminal byte");

string? originalProcessOnly = Environment.GetEnvironmentVariable("MTC_RPC_PROCESS_ONLY");
string? originalEventLogOverride = Environment.GetEnvironmentVariable("MTC_GAME_AGENT_EVENT_LOG_PATH");
string isolatedLogDirectory = Path.Combine(Path.GetTempPath(), "mtc-rpc-safety-" + Guid.NewGuid().ToString("N"));
string isolatedLogPath = Path.Combine(isolatedLogDirectory, "events.jsonl");
try
{
    Environment.SetEnvironmentVariable("MTC_RPC_PROCESS_ONLY", "1");
    Environment.SetEnvironmentVariable("MTC_GAME_AGENT_EVENT_LOG_PATH", isolatedLogPath);
    using var isolatedRuntime = new GameAgentRuntime();
    isolatedRuntime.SetGameName("Meridian Game D");
    isolatedRuntime.Record(new GameAgentEvent
    {
        GameName = "Meridian Game D",
        Kind = GameAgentEventKind.Connected,
        PlainText = "connected",
    });
    bool isolatedPathUsed = System.Threading.SpinWait.SpinUntil(
        () => string.Equals(isolatedRuntime.EventLogPath, isolatedLogPath, StringComparison.OrdinalIgnoreCase),
        TimeSpan.FromSeconds(2));
    Check(isolatedPathUsed, "process-only event log override isolates runtime writes");
}
finally
{
    Environment.SetEnvironmentVariable("MTC_RPC_PROCESS_ONLY", originalProcessOnly);
    Environment.SetEnvironmentVariable("MTC_GAME_AGENT_EVENT_LOG_PATH", originalEventLogOverride);
    try
    {
        if (Directory.Exists(isolatedLogDirectory))
            Directory.Delete(isolatedLogDirectory, recursive: true);
    }
    catch
    {
    }
}

Check(
    MtcRpcSecretInputPolicy.FindCurrentAccountSetupPasswordPrompt(
        ValidPasswordFlow(now), true, "Meridian Game D", "192.168.4.76", 2002, now) == null,
    "rejects non-approved host");
Check(
    MtcRpcSecretInputPolicy.FindCurrentAccountSetupPasswordPrompt(
        ValidPasswordFlow(now), true, "Meridian Game D", "192.168.4.75", 2003, now) == null,
    "rejects non-approved port");
Check(
    MtcRpcSecretInputPolicy.FindCurrentAccountSetupPasswordPrompt(
        ValidPasswordFlow(now), true, "Meridian Game E", "192.168.4.75", 2002, now) == null,
    "rejects Game E identity");
Check(
    MtcRpcSecretInputPolicy.FindCurrentAccountSetupPasswordPrompt(
        ValidPasswordFlow(now), false, "Meridian Game D", "192.168.4.75", 2002, now) == null,
    "rejects disconnected session");

IReadOnlyList<GameAgentEvent> staleFlow =
[
    new GameAgentEvent
    {
        Timestamp = now.AddMinutes(-6),
        GameName = "Meridian Game D",
        Kind = GameAgentEventKind.ServerPrompt,
        PlainText = "Would you like to start a new character?",
    },
    new GameAgentEvent
    {
        Timestamp = now,
        GameName = "Meridian Game D",
        Kind = GameAgentEventKind.ServerPrompt,
        PlainText = "Enter your password:",
    },
];
Check(
    MtcRpcSecretInputPolicy.FindCurrentAccountSetupPasswordPrompt(
        staleFlow, true, "Meridian Game D", "192.168.4.75", 2002, now) == null,
    "rejects stale account-setup context");

IReadOnlyList<GameAgentEvent> ordinaryLogin =
[
    new GameAgentEvent
    {
        Timestamp = now.AddSeconds(-10),
        GameName = "Meridian Game D",
        Kind = GameAgentEventKind.ServerPrompt,
        PlainText = "Command [TL=00:00:00]:",
    },
    new GameAgentEvent
    {
        Timestamp = now,
        GameName = "Meridian Game D",
        Kind = GameAgentEventKind.ServerPrompt,
        PlainText = "Password:",
    },
];
Check(
    MtcRpcSecretInputPolicy.FindCurrentAccountSetupPasswordPrompt(
        ordinaryLogin, true, "Meridian Game D", "192.168.4.75", 2002, now) == null,
    "rejects ordinary login password prompt without new-character flow");

IReadOnlyList<GameAgentEvent> consumedPrompt =
[
    new GameAgentEvent
    {
        Timestamp = now.AddSeconds(-10),
        GameName = "Meridian Game D",
        Kind = GameAgentEventKind.ServerPrompt,
        PlainText = "Would you like to start a new character?",
    },
    new GameAgentEvent
    {
        Timestamp = now.AddSeconds(-1),
        GameName = "Meridian Game D",
        Kind = GameAgentEventKind.ServerPrompt,
        PlainText = "Enter your password:",
    },
    new GameAgentEvent
    {
        Timestamp = now,
        GameName = "Meridian Game D",
        Kind = GameAgentEventKind.ClientInput,
        PlainText = "[redacted account-setup credential input]",
    },
];
Check(
    MtcRpcSecretInputPolicy.FindCurrentAccountSetupPasswordPrompt(
        consumedPrompt, true, "Meridian Game D", "192.168.4.75", 2002, now) == null,
    "rejects a password prompt after an input was already submitted");

Check(
    MtcRpcSecretInputPolicy.IsRequestAllowed(true, true, true, true),
    "allows protected input only when loopback, process-only, opt-in, and target are all set");
Check(
    !MtcRpcSecretInputPolicy.IsRequestAllowed(false, true, true, true) &&
    !MtcRpcSecretInputPolicy.IsRequestAllowed(true, false, true, true) &&
    !MtcRpcSecretInputPolicy.IsRequestAllowed(true, true, false, true) &&
    !MtcRpcSecretInputPolicy.IsRequestAllowed(true, true, true, false),
    "rejects every missing protected-input gate");
Check(MtcRpcSecretInputPolicy.IsApprovalModeAllowed("approve-actions"), "requires player approval for protected input");
Check(
    !MtcRpcSecretInputPolicy.IsApprovalModeAllowed("read-only") &&
    !MtcRpcSecretInputPolicy.IsApprovalModeAllowed("full-automation"),
    "rejects read-only and full-automation modes for protected input");
Check(
    !MtcRpcSecretInputPolicy.ShouldAllowServerLineToRecorders(true, "EchoedSetupCredential") &&
    MtcRpcSecretInputPolicy.ShouldAllowServerLineToRecorders(true, "Command [TL=00:00:00]:[N]") &&
    MtcRpcSecretInputPolicy.ShouldAllowServerLineToRecorders(false, "Ordinary server line"),
    "blocks sensitive setup output from non-agent recorders until the command prompt");

await RunCredentialLineMockServer(Check);

string[] environmentNames =
[
    "MTC_RPC_PROCESS_ONLY",
    "MTC_RPC_PROCESS_TOKEN",
    "MTC_RPC_PROCESS_PORT",
    "MTC_RPC_SECRET_INPUT_ENABLED",
    "MTC_RPC_SECRET_CREDENTIAL_TARGET",
];
var originalEnvironment = new Dictionary<string, string?>(StringComparer.Ordinal);
foreach (string name in environmentNames)
    originalEnvironment[name] = Environment.GetEnvironmentVariable(name);

try
{
    foreach (string name in environmentNames)
        Environment.SetEnvironmentVariable(name, null);
    Check(!MtcRpcProcessConfiguration.ReadFromEnvironment().Requested, "RPC process-only mode is disabled by default");

    Environment.SetEnvironmentVariable("MTC_RPC_PROCESS_ONLY", "1");
    Environment.SetEnvironmentVariable("MTC_RPC_PROCESS_TOKEN", new string('a', 48));
    Environment.SetEnvironmentVariable("MTC_RPC_SECRET_INPUT_ENABLED", "1");
    Environment.SetEnvironmentVariable("MTC_RPC_SECRET_CREDENTIAL_TARGET", "test-credential-target");
    MtcRpcProcessConfiguration configured = MtcRpcProcessConfiguration.ReadFromEnvironment();
    Check(configured.Requested && configured.Valid, "accepts explicit process-only RPC opt-in and valid token");
    Check(configured.SecretInputEnabled, "enables secret input only with explicit opt-in and a credential target");
    Check(configured.Port == 7623, "uses the loopback RPC default port when no process port is specified");

    Environment.SetEnvironmentVariable("MTC_RPC_PROCESS_TOKEN", string.Empty);
    MtcRpcProcessConfiguration missingToken = MtcRpcProcessConfiguration.ReadFromEnvironment();
    Check(missingToken.Requested && !missingToken.Valid && !missingToken.SecretInputEnabled, "fails closed without a process token");
}
finally
{
    foreach ((string name, string? value) in originalEnvironment)
        Environment.SetEnvironmentVariable(name, value);
}

await RunRpcServerRegressions(Check);
await RunEmbeddedDashboardRegression(Check);

// The two reviewed autonomous scripts must compile with the live TWX compiler, so a script edit cannot break them
// silently until a live run.
string repositoryRoot = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "..", "..", ".."));
foreach (string reviewedScript in new[] { @"trading-cashing\2-worldtrade.ts", @"ship-upgrades\ls-dock-shopper-v33.ts" })
{
    string scriptPath = Path.Combine(repositoryRoot, "LLMProxyScripts", reviewedScript);
    string? compileError = null;
    try
    {
        using var compiler = new Core.ScriptCmp(null, Path.GetDirectoryName(scriptPath)!);
        compiler.CompileFromFile(scriptPath, string.Empty);
    }
    catch (Exception exception)
    {
        compileError = exception.Message;
    }

    Check(
        File.Exists(scriptPath) && compileError is null,
        $"reviewed script {reviewedScript} compiles{(compileError is null ? string.Empty : $": {compileError}")}");
}

Console.WriteLine(failures == 0 ? "All MTC RPC safety and dashboard regressions passed." : $"{failures} MTC RPC safety/dashboard regression(s) failed.");
return failures == 0 ? 0 : 1;

async Task RunEmbeddedDashboardRegression(Action<bool, string> check)
{
    string testDirectory = Path.Combine(
        Path.GetTempPath(),
        $"mtc-dashboard-test-{Guid.NewGuid():N}");
    Directory.CreateDirectory(testDirectory);
    var appOptions = new Microsoft.AspNetCore.Builder.WebApplicationOptions
    {
        Args = [],
        EnvironmentName = "Production",
    };
    Microsoft.AspNetCore.Builder.WebApplication? dashboard = null;
    try
    {
        dashboard = await DashboardApplication.BuildAsync(
            [],
            configureBuilder: builder =>
            {
                builder.Configuration["LLMControlCenter:EmbeddedHost"] = "true";
                builder.Configuration["Dashboard:HttpsRedirection"] = "false";
                builder.Configuration["AgentDatabasePath"] = Path.Combine(testDirectory, "agents.db");
                builder.Configuration["Knowledge:SeedDatabasePath"] = Path.Combine(testDirectory, "knowledge.seed.db");
                builder.Configuration["Knowledge:RuntimeDatabasePath"] = Path.Combine(testDirectory, "knowledge.runtime.db");
                builder.WebHost.ConfigureKestrel(server => server.Listen(IPAddress.Loopback, 0));
            },
            environmentName: appOptions.EnvironmentName);
        await dashboard.StartAsync();

        string? address = dashboard.Services
            .GetRequiredService<IServer>()
            .Features
            .Get<IServerAddressesFeature>()?
            .Addresses
            .SingleOrDefault(value => value.StartsWith("http://127.0.0.1:", StringComparison.Ordinal));
        check(address is not null, "embedded dashboard binds to a loopback-only address");
        if (address is null)
            return;

        using var client = new HttpClient();
        using HttpResponseMessage response = await client.GetAsync(address);
        string html = await response.Content.ReadAsStringAsync();
        check(response.IsSuccessStatusCode, "embedded dashboard serves its Blazor page");
        check(html.Contains("_framework/blazor.web.js", StringComparison.Ordinal), "embedded dashboard emits interactive Blazor boot markup");
        check(html.Contains("\"type\":\"server\"", StringComparison.Ordinal),
            "embedded dashboard attaches routes with an interactive server circuit");
        foreach (var (path, expectedText) in new[]
                 {
                     ("/", "Fleet overview"),
                     ("/agents", "Agent registry"),
                     ("/controller-settings", "Controller settings"),
                 })
        {
            using HttpResponseMessage routeResponse = await client.GetAsync(new Uri(new Uri(address), path));
            string routeHtml = await routeResponse.Content.ReadAsStringAsync();
            check(routeResponse.IsSuccessStatusCode, $"embedded dashboard serves {path}");
            check(routeHtml.Contains(expectedText, StringComparison.OrdinalIgnoreCase),
                $"embedded dashboard renders {expectedText} at {path}");
        }

        Match stylesheet = Regex.Match(
            html,
            "href=\"(?<path>[^\"]*app[^\"]*\\.css[^\"]*)\"",
            RegexOptions.IgnoreCase);
        check(stylesheet.Success, "embedded dashboard emits its stylesheet link");
        if (stylesheet.Success)
        {
            Uri stylesheetUri = new(new Uri(address), stylesheet.Groups["path"].Value);
            using HttpResponseMessage stylesheetResponse = await client.GetAsync(stylesheetUri);
            check(stylesheetResponse.IsSuccessStatusCode, "embedded dashboard serves its stylesheet asset");
            string stylesheetContent = await stylesheetResponse.Content.ReadAsStringAsync();
            check(stylesheetContent.Contains("--cc-window", StringComparison.Ordinal),
                "embedded dashboard serves the unified Control Center theme tokens");
        }
    }
    finally
    {
        try
        {
            if (dashboard is not null)
            {
                await dashboard.StopAsync();
                await dashboard.DisposeAsync();
            }
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            Directory.Delete(testDirectory, recursive: true);
        }
    }
}

async Task RunRpcServerRegressions(Action<bool, string> check)
{
    using var portProbe = new TcpListener(IPAddress.Loopback, 0);
    portProbe.Start();
    int port = ((IPEndPoint)portProbe.LocalEndpoint).Port;
    portProbe.Stop();

    int promptReads = 0;
    int approvalCalls = 0;
    int credentialReads = 0;
    int sensitiveSends = 0;
    int ordinarySends = 0;
    string? approvalDetails = null;
    string? approvalAction = null;
    string? lastSentInput = null;
    bool lastAppendEnter = true;
    bool changePausePromptOnApproval = false;
    GameAgentContextSnapshot activeContext = new();
    byte[]? credentialBytes = null;
    byte[]? deliveredSecret = null;
    byte[]? deliveredPasswordLine = null;

    using var gameAgent = new GameAgentRuntime();
    var bridge = new MtcRpcBridge
    {
        GetContextAsync = _ => Task.FromResult(activeContext),
        GetRecentEventsAsync = (_, _) =>
            Task.FromResult<IReadOnlyList<GameAgentEvent>>(Array.Empty<GameAgentEvent>()),
        QuerySectorAsync = _ => Task.FromResult<GameAgentSectorSnapshot?>(null),
        ListScriptsAsync = () =>
            Task.FromResult<IReadOnlyList<GameAgentRunningScriptSnapshot>>(Array.Empty<GameAgentRunningScriptSnapshot>()),
        SendCommandAsync = (input, appendEnter) =>
        {
            ordinarySends++;
            lastSentInput = input;
            lastAppendEnter = appendEnter;
            return Task.FromResult(MtcRpcActionResult.Ok("ordinary command sent"));
        },
        RunMombotCommandAsync = _ => Task.FromResult(MtcRpcActionResult.Ok("mombot command sent")),
        RunScriptAsync = _ => Task.FromResult(MtcRpcActionResult.Ok("script requested")),
        StopScriptAsync = (_, _) => Task.FromResult(MtcRpcActionResult.Ok("script stopped")),
        ApproveActionAsync = (action, details) =>
        {
            approvalCalls++;
            approvalAction = action;
            approvalDetails = details;
            if (changePausePromptOnApproval)
            {
                activeContext = new GameAgentContextSnapshot
                {
                    Connected = true,
                    GameName = "Meridian Game D",
                    Host = "192.168.4.75",
                    Port = 2002,
                    CurrentPrompt = "Command [TL=00:00:00]",
                };
            }
            return Task.FromResult(true);
        },
        GetSensitivePasswordPromptAsync = () =>
        {
            promptReads++;
            return Task.FromResult<MtcRpcSensitivePrompt?>(new MtcRpcSensitivePrompt("prompt-1", "password"));
        },
        SendSensitivePasswordAsync = (_, secret) =>
        {
            sensitiveSends++;
            deliveredSecret = secret.ToArray();
            byte[] payload = MtcRpcSecretInputPolicy.CreateCredentialPasswordLine(secret);
            deliveredPasswordLine = payload.ToArray();
            System.Security.Cryptography.CryptographicOperations.ZeroMemory(payload);
            return Task.FromResult(MtcRpcActionResult.Ok("Credential input submitted to the verified account-setup prompt."));
        },
        ReadSensitiveCredentialAsync = _ =>
        {
            credentialReads++;
            credentialBytes = [(byte)'A', (byte)'B'];
            return credentialBytes;
        },
    };

    const string token = "0123456789abcdef0123456789abcdef";
    using var server = new MtcJsonRpcServer(gameAgent, bridge);
    activeContext = new GameAgentContextSnapshot
    {
        Connected = true,
        GameName = "Meridian Game D",
        Host = "192.168.4.75",
        Port = 2002,
        CurrentPrompt = approvedPausePrompt,
    };
    server.ApplyOptions(new MtcJsonRpcServerOptions
    {
        Enabled = true,
        BindAddress = "127.0.0.1",
        Port = port,
        AuthToken = token,
        ApprovalLevel = MtcRpcApprovalLevel.ReadOnly,
        ProcessOnly = true,
        SecretInputEnabled = true,
        CredentialTarget = "test-target",
    });

    using var client = new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    string endpoint = $"http://127.0.0.1:{port}/";

    async Task<(HttpStatusCode Status, string Body)> PostAsync(
        string json,
        bool bearerToken,
        bool queryToken = false)
    {
        string uri = queryToken ? $"{endpoint}?token={token}" : endpoint;
        using var request = new HttpRequestMessage(HttpMethod.Post, uri)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        };
        if (bearerToken)
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);

        using HttpResponseMessage response = await client.SendAsync(request);
        return (response.StatusCode, await response.Content.ReadAsStringAsync());
    }

    (HttpStatusCode Status, string Body) noAuth = await PostAsync(
        """{"jsonrpc":"2.0","id":1,"method":"rpc.discover"}""",
        bearerToken: false);
    check(noAuth.Status == HttpStatusCode.Unauthorized, "process-only RPC requires bearer authorization");

    (HttpStatusCode Status, string Body) queryAuth = await PostAsync(
        """{"jsonrpc":"2.0","id":1,"method":"rpc.discover"}""",
        bearerToken: false,
        queryToken: true);
    check(queryAuth.Status == HttpStatusCode.Unauthorized, "process-only RPC rejects query-string tokens");

    (HttpStatusCode Status, string Body) rawParams = await PostAsync(
        """{"jsonrpc":"2.0","id":1,"method":"mtc.submitCredentialPassword","params":{"value":"not-accepted"}}""",
        bearerToken: true);
    check(rawParams.Status == HttpStatusCode.OK && ReadErrorCode(rawParams.Body) == -32602,
        "protected password method rejects all JSON parameters");
    check(credentialReads == 0 && sensitiveSends == 0, "rejected JSON parameters never reach Credential Manager or terminal input");

    (HttpStatusCode Status, string Body) readOnly = await PostAsync(
        """{"jsonrpc":"2.0","id":2,"method":"mtc.submitCredentialPassword"}""",
        bearerToken: true);
    check(readOnly.Status == HttpStatusCode.OK && ReadErrorCode(readOnly.Body) == -32002,
        "read-only mode cannot submit a protected password");
    check(promptReads == 0 && approvalCalls == 0 && credentialReads == 0,
        "read-only rejection occurs before prompt, approval, or secret access");
    (HttpStatusCode Status, string Body) readOnlyPauseKey = await PostAsync(
        """{"jsonrpc":"2.0","id":6,"method":"mtc.pressKey","params":{"key":"Enter"}}""",
        bearerToken: true);
    check(readOnlyPauseKey.Status == HttpStatusCode.OK && ReadErrorCode(readOnlyPauseKey.Body) == -32002,
        "read-only mode cannot send an approved pause key");
    check(approvalCalls == 0 && ordinarySends == 0,
        "read-only pause-key rejection occurs before approval or terminal input");

    server.ApplyOptions(new MtcJsonRpcServerOptions
    {
        Enabled = true,
        BindAddress = "127.0.0.1",
        Port = port,
        AuthToken = token,
        ApprovalLevel = MtcRpcApprovalLevel.ApproveActions,
        ProcessOnly = true,
        SecretInputEnabled = true,
        CredentialTarget = "test-target",
    });

    (HttpStatusCode Status, string Body) protectedSend = await PostAsync(
        """{"jsonrpc":"2.0","id":3,"method":"mtc.submitCredentialPassword"}""",
        bearerToken: true);
    check(protectedSend.Status == HttpStatusCode.OK &&
          JsonDocument.Parse(protectedSend.Body).RootElement.GetProperty("result").GetProperty("redacted").GetBoolean(),
        "approved protected input reports only a redacted result");
    check(promptReads == 2 && approvalCalls == 1 && credentialReads == 1 && sensitiveSends == 1,
        "protected input rechecks the prompt after approval and reads Credential Manager once");
    check(approvalDetails?.Contains("redacted", StringComparison.OrdinalIgnoreCase) == true &&
          approvalDetails.Contains("AB", StringComparison.Ordinal) == false,
        "approval details do not contain the credential value");
    check(deliveredSecret?.SequenceEqual([(byte)'A', (byte)'B']) == true,
        "only the Credential Manager value reaches the sensitive terminal callback");
    check(deliveredPasswordLine?.SequenceEqual([(byte)'A', (byte)'B', (byte)'\r']) == true,
        "protected RPC constructs one complete password line with one CR terminator");
    check(credentialBytes?.All(value => value == 0) == true,
        "the retrieved credential buffer is cleared after the RPC action");
    check(!protectedSend.Body.Contains("AB", StringComparison.Ordinal),
        "the RPC response does not contain the credential value");

    (HttpStatusCode Status, string Body) ordinaryCommand = await PostAsync(
        """{"jsonrpc":"2.0","id":4,"method":"mtc.sendCommand","params":{"command":"look","appendEnter":true}}""",
        bearerToken: true);
    check(ordinaryCommand.Status == HttpStatusCode.OK && ordinarySends == 1 && approvalCalls == 2,
        "ordinary action still follows the existing approval callback");
    check(approvalDetails == "look", "ordinary action approval details remain unchanged");

    (HttpStatusCode Status, string Body) invalidPauseKey = await PostAsync(
        """{"jsonrpc":"2.0","id":7,"method":"mtc.pressKey","params":{"key":"D"}}""",
        bearerToken: true);
    check(invalidPauseKey.Status == HttpStatusCode.OK && ReadErrorCode(invalidPauseKey.Body) == -32010,
        "process-only pause-key method rejects keys outside Enter and Space");
    check(approvalCalls == 2 && ordinarySends == 1,
        "invalid pause key is rejected before approval and terminal input");

    (HttpStatusCode Status, string Body) extraPauseKeyParam = await PostAsync(
        """{"jsonrpc":"2.0","id":8,"method":"mtc.pressKey","params":{"key":"Enter","command":"x"}}""",
        bearerToken: true);
    check(extraPauseKeyParam.Status == HttpStatusCode.OK && ReadErrorCode(extraPauseKeyParam.Body) == -32602,
        "pause-key method rejects extra JSON parameters");

    (HttpStatusCode Status, string Body) approvedPauseKey = await PostAsync(
        """{"jsonrpc":"2.0","id":9,"method":"mtc.pressKey","params":{"key":"Enter"}}""",
        bearerToken: true);
    check(approvedPauseKey.Status == HttpStatusCode.OK &&
          JsonDocument.Parse(approvedPauseKey.Body).RootElement.GetProperty("result").GetProperty("success").GetBoolean(),
        "approved pause-key input succeeds through the existing approval callback");
    check(approvalCalls == 3 &&
          approvalAction == "Press approved Game D pause key" &&
          approvalDetails == "Game D pause prompt; key=Enter." &&
          ordinarySends == 2 &&
          lastSentInput == "\r" &&
          !lastAppendEnter,
        "approved Enter is audited by name and sends exactly one carriage return");

    activeContext = new GameAgentContextSnapshot
    {
        Connected = true,
        GameName = "Meridian Game D",
        Host = "192.168.4.75",
        Port = 2002,
        CurrentPrompt = approvedPausePrompt,
    };
    changePausePromptOnApproval = true;
    (HttpStatusCode Status, string Body) stalePauseKey = await PostAsync(
        """{"jsonrpc":"2.0","id":10,"method":"mtc.pressKey","params":{"key":"Space"}}""",
        bearerToken: true);
    changePausePromptOnApproval = false;
    check(stalePauseKey.Status == HttpStatusCode.OK && ReadErrorCode(stalePauseKey.Body) == -32010,
        "pause-key method rechecks the live prompt after approval");
    check(ordinarySends == 2,
        "stale pause prompt is rejected before terminal input");

    server.ApplyOptions(new MtcJsonRpcServerOptions
    {
        Enabled = true,
        BindAddress = "127.0.0.1",
        Port = port,
        AuthToken = token,
        ApprovalLevel = MtcRpcApprovalLevel.FullAutomation,
        ProcessOnly = true,
        SecretInputEnabled = true,
        CredentialTarget = "test-target",
    });
    (HttpStatusCode Status, string Body) fullAutomation = await PostAsync(
        """{"jsonrpc":"2.0","id":5,"method":"mtc.submitCredentialPassword"}""",
        bearerToken: true);
    check(fullAutomation.Status == HttpStatusCode.OK && ReadErrorCode(fullAutomation.Body) == -32002,
        "protected input refuses full-automation approval mode");
    check(credentialReads == 1 && sensitiveSends == 1,
        "full-automation rejection never reads or submits a secret");
    (HttpStatusCode Status, string Body) fullAutomationPauseKey = await PostAsync(
        """{"jsonrpc":"2.0","id":11,"method":"mtc.pressKey","params":{"key":"Enter"}}""",
        bearerToken: true);
    check(fullAutomationPauseKey.Status == HttpStatusCode.OK && ReadErrorCode(fullAutomationPauseKey.Body) == -32002,
        "pause-key method refuses full-automation approval mode");

    static int? ReadErrorCode(string body)
    {
        using JsonDocument document = JsonDocument.Parse(body);
        return document.RootElement.TryGetProperty("error", out JsonElement error) &&
               error.TryGetProperty("code", out JsonElement code) &&
               code.TryGetInt32(out int value)
            ? value
            : null;
    }
}

async Task RunCredentialLineMockServer(Action<bool, string> check)
{
    const string initialPrompt = "Password?";
    const string confirmationPrompt = "Repeat password to verify:";
    byte[] dummySecret = Encoding.ASCII.GetBytes("MockOnlySecret9");
    byte[] expectedLine = MtcRpcSecretInputPolicy.CreateCredentialPasswordLine(dummySecret);

    using var listener = new TcpListener(IPAddress.Loopback, 0);
    listener.Start();
    int port = ((IPEndPoint)listener.LocalEndpoint).Port;
    using var timeout = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(5));

    Task<bool> mockServer = Task.Run(async () =>
    {
        using TcpClient accepted = await listener.AcceptTcpClientAsync(timeout.Token);
        using NetworkStream stream = accepted.GetStream();
        byte[] promptBytes = Encoding.ASCII.GetBytes(initialPrompt);
        await stream.WriteAsync(promptBytes, timeout.Token);

        var received = new List<byte>();
        byte[] oneByte = new byte[1];
        while (true)
        {
            int count = await stream.ReadAsync(oneByte, timeout.Token);
            if (count == 0)
                break;

            received.Add(oneByte[0]);
            if (oneByte[0] == (byte)'\r')
                break;
        }

        bool receivedCompleteLine = received.SequenceEqual(expectedLine);
        byte[] confirmationBytes = Encoding.ASCII.GetBytes(confirmationPrompt);
        await stream.WriteAsync(confirmationBytes, timeout.Token);
        return receivedCompleteLine;
    }, timeout.Token);

    try
    {
        using var client = new TcpClient();
        await client.ConnectAsync(IPAddress.Loopback, port, timeout.Token);
        using NetworkStream clientStream = client.GetStream();

        byte[] promptBuffer = new byte[initialPrompt.Length];
        await clientStream.ReadExactlyAsync(promptBuffer, timeout.Token);
        string observedInitialPrompt = Encoding.ASCII.GetString(promptBuffer);

        byte[] payload = MtcRpcSecretInputPolicy.CreateCredentialPasswordLine(dummySecret);
        await clientStream.WriteAsync(payload, timeout.Token);

        byte[] confirmationBuffer = new byte[confirmationPrompt.Length];
        await clientStream.ReadExactlyAsync(confirmationBuffer, timeout.Token);
        string observedConfirmationPrompt = Encoding.ASCII.GetString(confirmationBuffer);
        bool serverReceivedCompleteLine = await mockServer;

        check(observedInitialPrompt == initialPrompt,
            "mock server presents the initial password prompt");
        check(serverReceivedCompleteLine &&
              payload.Length == dummySecret.Length + 1 &&
              payload[^1] == (byte)'\r' &&
              payload.Count(value => value == (byte)'\r') == 1 &&
              !payload.Contains((byte)'\n'),
            "mock server receives the full protected password followed by exactly one CR");
        check(observedConfirmationPrompt == confirmationPrompt &&
              MtcRpcSecretInputPolicy.ClassifyPasswordPrompt(observedConfirmationPrompt) == "password-confirmation",
            "mock server advances to the password-confirmation prompt");

        System.Security.Cryptography.CryptographicOperations.ZeroMemory(payload);
    }
    finally
    {
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(dummySecret);
        System.Security.Cryptography.CryptographicOperations.ZeroMemory(expectedLine);
        listener.Stop();
    }
}
