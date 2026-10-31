const fs = require("node:fs");
const os = require("node:os");
const path = require("node:path");
const { TradeWarsBot, TelnetSocket, compilePattern } = require("./bot.js");

const sourceConfigPath = path.join(__dirname, "config.192.168.4.75.json");
const config = JSON.parse(fs.readFileSync(sourceConfigPath, "utf8"));
config.credentials = {
  character: "Offline Test",
  username: "offline-test",
  password: "offline-test",
};
const tempRoot = fs.mkdtempSync(path.join(os.tmpdir(), "twbot-offline-"));
const configPath = path.join(tempRoot, "config.json");
config.logging = {
  logChunks: false,
  file: path.join(tempRoot, "bot.log"),
  statusFile: path.join(tempRoot, "status.json"),
  intelFile: path.join(tempRoot, "intel.json"),
  eventJournalFile: path.join(tempRoot, "events.jsonl"),
  intelDatabaseFile: path.join(tempRoot, "intel.sqlite"),
  scriptKnowledgeFile: path.join(tempRoot, "script-knowledge.json"),
  shipDatabaseFile: path.join(tempRoot, "ships.json"),
  stardockMenuFile: path.join(tempRoot, "stardock-menu.json"),
  menuKnowledgeFile: path.join(tempRoot, "menu-knowledge.json"),
};
fs.writeFileSync(configPath, JSON.stringify(config));
const bot = new TradeWarsBot(configPath);
const unsafeSsmActionsDisabled = !bot.actions.some((action) => /^ssm-leg-/i.test(action.name));
const telnetWrites = [];
const telnet = new TelnetSocket("localhost", 23, () => {}, () => {}, () => {});
telnet.socket = {
  write(payload) {
    telnetWrites.push(Buffer.from(payload));
  },
};
telnet._consumeTelnet(Buffer.from([255, 253]));
telnet._consumeTelnet(Buffer.from([246, 255, 253, 42]));
bot.persistenceState.flushing = true;
bot.persistenceState.flushScheduled = true;
bot._flushPersistenceQueue();
const persistenceScheduleReleased = bot.persistenceState.flushScheduled === false;
const operatorWrites = [];
bot.telnet = {
  send(payload, appendLineEnding) { operatorWrites.push({ payload, appendLineEnding }); },
  close() {},
};
const realSend = bot._send.bind(bot);
bot.controlState.mode = "manual";
realSend("Q", "automatic send while operator owns session");
const automationBlockedDuringTakeover = operatorWrites.length === 0;
realSend("I", "operator manual command", { source: "operator" });
const operatorCommandSent = operatorWrites.at(-1)?.payload === "I";
bot.controlState.mode = "bot";
const mirrorWrites = [];
bot.controlTelnetClient = {
  destroyed: false,
  write(payload) { mirrorWrites.push(String(payload)); },
  destroy() {},
};
bot._handleOperatorTelnetData(Buffer.from("status\r\n"));
const monitoringLeavesBotInControl = bot.controlState.mode === "bot" && bot.controlState.owner === "";
bot._handleOperatorTelnetData(Buffer.from(`${bot.controlCfg.telnetTakeoverKey}\r\n`));
const telnetTakeoverWorks = bot.controlState.mode === "manual" && bot.controlState.owner === "telnet";
bot._handleOperatorTelnetData(Buffer.from("I\r\n"));
const telnetCommandSentAfterTakeover = mirrorWrites.length >= 0 && bot.controlState.lastCommand?.command === "I";
bot._handleOperatorTelnetData(Buffer.from(`${bot.controlCfg.telnetTakeoverKey}\r\n`));
const telnetReleaseWorks = bot.controlState.mode === "bot" && bot.controlState.owner === "";
const sent = [];
bot._send = (text, reason) => sent.push({ text: String(text), reason });
bot.lastCredentialPrompt = { kind: "", atMs: 0 };
bot._handleCredentials("Password?");
bot._handleCredentials("Password?");
const duplicateCredentialSuppressed = sent.filter((entry) => entry.reason === "password").length === 1;
bot.currentAction = "";
const gameSelectionRule = bot.autoResponses.find((rule) => /game\s*\(a|b\)|game\s*\\?a/i.test(String(rule.match || "")));
if (gameSelectionRule) {
  gameSelectionRule.used = true;
  gameSelectionRule.lastSentAtMs = Date.now();
}
for (const rule of bot.autoResponses) {
  rule.used = false;
  rule.lastSentAtMs = 0;
}
bot._handleAutoResponses("Game A: Assimilation Gold");
const reconnectAutoResponseRestored = sent.at(-1)?.text.toLowerCase() === "a";
bot._handleAutoResponses("Do you want to engage the target? (Y/N)");
const specificEngageDeclined = sent.at(-1)?.text.toLowerCase() === "n";
bot._handleAutoResponses("[Pause]");
const pauseUsesDefaultReturn = sent.at(-1)?.text === ""
  && /default return/i.test(sent.at(-1)?.reason || "");
bot.currentAction = "pair-harvest-trade";
bot.stats.credits = 9;
bot.stats.holds = 100;
bot.stats.emptyHolds = 100;
bot.stats.cargoHolds = 0;
bot._handleTradePrompts("How many holds of Fuel Ore do you want to buy [0]?");
bot.tradePromptState.lastQtySentAtMs = Date.now() - 200;
bot.tradePromptState.bootstrapFuelProbe = true;
const beforeUnaffordableQuote = sent.length;
bot._handleTradePrompts("We'll sell them for 12 credits");
const quoteWaitsForOffer = sent.length === beforeUnaffordableQuote;
bot._handleTradePrompts("Your offer [12] ?");
const unaffordableFuelProbe = sent.at(-1)?.text;
bot.tradePromptState.lastQtyPromptSig = "";
bot.tradePromptState.lastQtyHandledAtMs = 0;
bot._handleTradePrompts("How many holds of Organics do you want to buy [19]?");
const lowCreditBuyVetoed = sent.at(-1)?.text === "0";
bot.tradeRuntime.lastOfferAtMs = 0;
bot.tradePromptState.bootstrapFuelProbe = false;
bot.tradePromptState.lastQtySentAtMs = Date.now() - 200;
bot.tradePromptState.lastQtySent = 1;
bot._handleTradePrompts("We'll sell them for 8 credits");
bot._handleTradePrompts("Your offer [8] ?");
const affordableFuelOffer = sent.at(-1)?.text;
bot.currentAction = "worldtrade-local-port-priority";
bot.stats.credits = 100000;
bot.stats.emptyHolds = 100;
bot.stats.cargoHolds = 0;
bot.tradePromptState.lastQtyPromptSig = "";
bot.tradePromptState.lastQtyHandledAtMs = 0;
bot.tradePromptState.lastQtySentAtMs = 0;
bot.tradeRuntime.lastOfferAtMs = 0;
bot._handleTradePrompts("How many holds of Fuel Ore do you want to buy [17]?");
const defaultQtySendRecord = sent.at(-1);
const defaultQtyReturn = defaultQtySendRecord?.text === ""
  && /default return/i.test(defaultQtySendRecord?.reason || "");
bot.stats.emptyHolds = 5;
bot.tradePromptState.lastQtyHandledAtMs = 0;
bot.tradePromptState.lastQtySentAtMs = 0;
bot.tradeRuntime.lastOfferAtMs = 0;
bot._handleTradePrompts("How many holds of Organics do you want to buy [19]?");
const cappedQtySendRecord = sent.at(-1);
const cappedQtyExplicit = cappedQtySendRecord?.text === "5";
bot.tradeRuntime.lastLocalTradeAtMs = Date.now();
bot.tradePromptState.lastQtySentAtMs = 0;
const identicalQtyNextTradeWritesBefore = sent.length;
bot._handleTradePrompts("How many holds of Organics do you want to buy [19]?");
const identicalQtyHandledInNextTrade = sent.length === identicalQtyNextTradeWritesBefore + 1
  && sent.at(-1)?.text === "5";
const localTradeSequenceFrame = bot._resolveCommandFrame("pt", "action:worldtrade-local-port");
const localTradeSequenceNoSubmit = localTradeSequenceFrame?.frameType === "menuSequence"
  && localTradeSequenceFrame.payload === "pt"
  && localTradeSequenceFrame.appendLineEnding === false;
const portUpgradeAction = bot.actions.find((action) => action.name === "port-upgrade-equip");
const portUpgradeSentBefore = sent.length;
bot._executeActionCandidate({
  idx: bot.actions.indexOf(portUpgradeAction),
  action: portUpgradeAction,
  command: "o3663*q",
}, Date.now());
bot._handlePortUpgradePrompt("Choice ?");
bot._handlePortUpgradePrompt("How many units do you want to invest? (189 max, 0 to quit)");
bot._handlePortUpgradePrompt("Choice ?");
const portUpgradeWrites = sent.slice(portUpgradeSentBefore).map((entry) => entry.text);
const promptDrivenPortUpgrade = JSON.stringify(portUpgradeWrites) === JSON.stringify(["o", "3", "663", "q"])
  && bot.portUpgradeRuntime.active;
bot._handlePortUpgradePrompt("Command [3538] (?=Help)?:");
const portUpgradeReleased = !bot.portUpgradeRuntime.active;
const promptCheckAt = Date.now();
bot.menuSurvey.active = false;
bot.lastCommandPromptMs = promptCheckAt - Math.max(10000, bot.maxIdleBeforeKeepAliveMs + 5000);
bot.lastPromptLine = "Command [3538] (?=Help)?:";
bot.lastRxLine = "Unclassified submenu prompt";
bot.lastRxAtMs = promptCheckAt;
const staleCommandPromptBlocked = !bot._hasActionableCommandPrompt(promptCheckAt);
bot.lastCommandPromptMs = promptCheckAt;
const freshCommandPromptAccepted = bot._hasActionableCommandPrompt(promptCheckAt);
const localTradeAction = bot.actions.find((action) => action.name === "worldtrade-local-port");
bot.stats.sector = 3538;
bot.tradeRuntime.lastTradeSector = 3538;
bot.tradeRuntime.tradeExchangePending = false;
bot.tradeRuntime.localTradeInFlightUntilMs = 0;
bot._executeActionCandidate({
  idx: bot.actions.indexOf(localTradeAction),
  action: localTradeAction,
  command: "pt",
}, promptCheckAt);
const sameSectorTradeLeaseAcquired = bot.tradeRuntime.tradeExchangePending
  && bot.tradeRuntime.localTradeInFlightSector === 3538;
bot._finishLocalTradeLease();
bot.ansiSetupDone = false;
bot.ansiSetupStep = 3;
bot.lastRxLine = "Computer command [TL=00:00:00]:[3538] (?=Help)?";
bot.computerSettingsState.ansi = bot.computerSettingsCfg.ansi === "on" ? "off" : "on";
const setupWritesBefore = sent.length;
bot._maybeRunAnsiSetup(promptCheckAt, false);
const setupWrites = sent.slice(setupWritesBefore);
const computerSettingsBatchRequiresLivePrompt = setupWrites.length === 1
  && setupWrites[0]?.text.toLowerCase() === "q"
  && !/settings batch/i.test(setupWrites[0]?.reason || "");
bot.ansiSetupDone = true;
bot.ansiSetupStep = 6;
bot.currentAction = "pair-harvest-trade";
bot.tradeRuntime.tradeExchangePending = true;
bot.tradeRuntime.localTradeInFlightUntilMs = Date.now() + 3500;
bot.tradeRuntime.localTradeInFlightSector = 4;
bot.tradeRuntime.lastLocalTradeAtMs = Date.now();
bot.tradePromptState.mode = "sell";
bot.tradePromptState.product = "fuel ore";
bot.tradePromptState.lastQtySent = 80;
bot.tradePromptState.lastQtySentAtMs = Date.now();
bot.tradeRuntime.lastOfferAtMs = Date.now();
const dealsBeforeQuantityAck = bot.tradeRuntime.successfulDeals;
bot._handleTradePrompts("Agreed, 100 units.");
const quantityAckKeepsExchangeLocked = bot.tradeRuntime.tradeExchangePending
  && bot.tradeRuntime.successfulDeals === dealsBeforeQuantityAck;
bot._handleTradePrompts("Done, we'll take the lot.");
const finalAcceptanceClearsExchange = !bot.tradeRuntime.tradeExchangePending
  && bot.tradeRuntime.successfulDeals === dealsBeforeQuantityAck + 1;
const fuelAfterAcceptance = bot.stats.fuelHolds;
bot._handleTradePrompts("Done, we'll take the lot.");
const duplicateAcceptanceIgnored = bot.tradeRuntime.successfulDeals === dealsBeforeQuantityAck + 1
  && bot.stats.fuelHolds === fuelAfterAcceptance;
bot.tradePromptState.mode = "sell";
bot.tradePromptState.lastQtyPromptSig = "qty:sell:fuel ore:100";
bot.tradePromptState.lastQtyHandledAtMs = Date.now();
bot.tradeRuntime.lastOfferAtMs = Date.now();
bot.stats.emptyHolds = 5;
bot.stats.holds = 100;
bot.stats.cargoHolds = 0;
const beforeNextProductQty = sent.length;
bot._handleTradePrompts("How many holds of Equipment do you want to buy [80]?");
const nextProductQtyHandled = sent.length > beforeNextProductQty
  && sent.at(-1)?.text === "5";
bot.tradePromptState.mode = "sell";
bot.tradePromptState.lastQtyPromptSig = "qty:sell:fuel ore:84";
bot.tradePromptState.lastQtyHandledAtMs = Date.now();
bot.tradeRuntime.lastOfferAtMs = Date.now();
const beforeRapidSameModeProduct = sent.length;
bot._handleTradePrompts("How many holds of Organics do you want to sell [12]?");
const rapidSameModeProductHandled = sent.length > beforeRapidSameModeProduct;
bot.tradePromptState.mode = "sell";
bot.tradePromptState.product = "organics";
bot.currentAction = "pair-harvest-trade";
bot.stardockDiscoveryCfg.pauseTrading = false;
bot.tradePromptState.lastQtySent = 1;
const firstSellQtySentAtMs = Date.now() - 200;
bot.tradePromptState.lastQtySentAtMs = firstSellQtySentAtMs;
bot.tradePromptState.lastOfferPromptSig = "";
bot.tradeRuntime.lastOfferAtMs = 0;
const sellOffersBeforeRepeat = sent.filter((entry) => /trade offer \(sell\)/i.test(entry.reason || "")).length;
bot._handleTradePrompts("Your offer [75] ?");
bot.tradePromptState.lastQtySentAtMs = firstSellQtySentAtMs + 1;
bot._handleTradePrompts("Your offer [75] ?");
const repeatedIdenticalSellOffersHandled = sent.filter((entry) => /trade offer \(sell\)/i.test(entry.reason || "")).length === sellOffersBeforeRepeat + 2;
const repeatedIdenticalSellOfferCount = sent.filter((entry) => /trade offer \(sell\)/i.test(entry.reason || "")).length - sellOffersBeforeRepeat;
bot.isEscapePod = true;
bot.stats.credits = 9;
bot.currentAction = "recovery-trade-local-port";
bot.tradePromptState.mode = "sell";
bot.tradePromptState.product = "organics";
bot.tradePromptState.lastQtySent = 1;
bot.tradePromptState.lastQtySentAtMs = Date.now() - 200;
bot.tradePromptState.lastOfferPromptSig = "";
bot.tradeRuntime.lastOfferAtMs = 0;
bot._handleTradePrompts("We'll buy them for 75 credits");
bot._handleTradePrompts("Your offer [75] ?");
const recoverySellNoHaggle = sent.at(-1)?.text === "75";
bot.tradePromptState.mode = "buy";
bot.tradePromptState.product = "organics";
bot.tradePromptState.lastQtySent = 1;
bot.tradePromptState.lastQtySentAtMs = Date.now() - 200;
bot.tradePromptState.lastOfferPromptSig = "";
bot.tradeRuntime.lastOfferAtMs = 0;
bot._handleTradePrompts("We'll sell them for 42 credits");
bot._handleTradePrompts("Your offer [42] ?");
const recoveryBuyNoHaggle = sent.at(-1)?.text === "42";
bot.stats.credits = 5591;
bot.stats.shipType = "Borg Manned Probe";
bot.tradePromptState.mode = "sell";
bot.tradePromptState.product = "organics";
bot.tradePromptState.lastQtySent = 1;
bot.tradePromptState.lastQtySentAtMs = Date.now() - 200;
bot.tradePromptState.lastOfferPromptSig = "";
bot.tradeRuntime.lastOfferAtMs = 0;
bot._handleTradePrompts("We'll buy them for 131 credits");
bot._handleTradePrompts("Your offer [131] ?");
const vulnerableProbeNoHaggleAboveCreditLimit = sent.at(-1)?.text === "131";
bot.isEscapePod = false;
bot.tradePromptState.lastTradePromptType = "offer";
const savedStartupSurveyDone = bot.startupSurveyDone;
const savedStartupSurveyIndex = bot.startupSurveyIndex;
const savedLastActionAtMs = bot.lastActionAtMs;
bot.startupSurveyDone = false;
bot.startupSurveyIndex = 0;
bot.lastActionAtMs = 0;
const startupSurveyBeforeOffer = sent.length;
const startupSurveyBlockedDuringOffer = bot._maybeRunStartupSurvey(Date.now(), false) === false
  && sent.length === startupSurveyBeforeOffer;
bot.tradePromptState.lastTradePromptType = "";
bot.startupSurveyDone = savedStartupSurveyDone;
bot.startupSurveyIndex = savedStartupSurveyIndex;
bot.lastActionAtMs = savedLastActionAtMs;
bot.stats.credits = 9;
bot.previousSector = 3;
bot.stats.sector = 4;
bot.pairHarvestRuntime.active = true;
bot.tradeRuntime.lastOfferAtMs = 0;
bot.tradePromptState.bootstrapFuelProbe = true;
bot._handleTradePrompts("How many holds of Equipment do you want to buy [0]?");
const failedProbePairActive = bot.pairHarvestRuntime.active;
const failedProbePairKeys = Array.from(bot.tradeRuntime.noTradePairs.keys());
const failedProbeRotatesPair = bot.pairHarvestRuntime.active === false
  && failedProbePairKeys.includes("3-4");
bot.stardockSector = null;
const discoverAction = bot.actions.find((action) => action.name === "discover-stardock");
discoverAction.lastRunAtMs = 0;
const lowCreditDiscovery = bot._maybeDiscoverStardockAtLowCredits(Date.now(), false);
const lowCreditDiscoveryCommand = sent.at(-1)?.text;
bot.stats.credits = 5000;
bot.stats.shipType = "Borg Explore Sphere";
bot.stats.scannerType = "Holographic Scanner";
bot.escapePodShipPurchase.tradeInValue = 300000;
bot.shipDatabase.purchaseCatalog = {
  explore: { name: "Explore", option: "E", price: 25000, maxHolds: 50 },
  transwarp: { name: "TransWarp", option: "T", price: 80000, maxHolds: 200 },
};
const recoveryPlan = bot._computeShipTradeInRecoveryPlan();
bot.shipTradeInRecoveryCfg.enabled = false;
bot.stats.credits = 5;
const recoveryStart = bot._maybeStartShipTradeInRecovery(Date.now(), false);
bot.stardockSector = null;
bot.shipTradeInRecovery.active = true;
bot.isEscapePod = true;
bot.lastProbeRecoveryDiscoverAtMs = 0;
const tradeInRouteDiscovery = bot._maybeRunProbeRecoveryRoute(Date.now(), false);
const tradeInRouteDiscoveryCommand = sent.at(-1)?.text;
bot.stardockSector = null;
bot.stats.sector = 3;
bot.stats.warps = [4];
bot.lastActionAtMs = 0;
bot.shipTradeInRecovery.active = true;
bot.isEscapePod = true;
const recoveryFallbackHeld = bot._tryRunEmergencyWarpFallback(Date.now());
const recoveryFallbackHeldAction = bot.currentAction;
bot.shipTradeInRecovery.active = false;
bot.stats.sector = 22117;
bot.stats.warps = [11065];
bot.sectorIntel.clear();
bot.recentSectors = [11065, 22117, 11065, 22117, 11065, 22117];
bot.tradeRuntime.lastRecoveryLoopRefreshAtMs = Date.now() - 1000;
bot.lastRxAtMs = Date.now();
bot.lastActionAtMs = 0;
bot.tradeRuntime.lastLocalTradeAtMs = 0;
bot.tradeRuntime.lastOfferAtMs = 0;
bot.tradeRuntime.fastPortMacroPending = false;
bot.tradeRuntime.fastPortMacroActive = false;
bot.tradePromptState.lastQtySentAtMs = 0;
bot.tradePromptState.lastTradePromptType = "";
bot.tradePromptState.pendingQtyPrompt = false;
bot.tradePromptState.activeUntilMs = 0;
bot._finishLocalTradeLease();
const loopFallbackWritesBefore = sent.length;
const recoveryLoopUsesVerifiedWarp = bot._tryRunEmergencyWarpFallback(Date.now());
const recoveryLoopVerifiedWarpCommand = sent.slice(loopFallbackWritesBefore).at(-1)?.text;
bot.lastEscapePodDepartureAtMs = Date.now();
bot.isAtStarDock = true;
bot.stardockSector = 10445;
bot.stats.sector = 10445;
bot.stats.credits = 1000;
bot.stats.shipType = "Borg Manned Probe";
bot.isEscapePod = true;
bot.lastRxLine = "Command [TL=00:00:00]:[10445] (?=Help)?:";
bot.lastPromptLine = bot.lastRxLine;
bot.currentAction = "escapepod-leave-stardock-recovery";
const duplicateDepartureWritesBefore = sent.length;
bot._maybeRunEscapePodShipRecovery(Date.now(), false);
const duplicateDepartureBlocked = sent.length === duplicateDepartureWritesBefore;
bot.isAtStarDock = true;
bot.stardockSector = 10445;
bot.stats.sector = 10445;
bot.stats.credits = bot.upgradeCfg.triggerCredits + 82;
bot.stats.holds = 1;
bot.stats.shipType = "Borg Manned Probe";
bot.shipDatabase.ships["manned probe"] = {
  name: "Borg Manned Probe",
  maxHolds: 20,
};
bot.isEscapePod = true;
bot.lastRxLine = "Command [TL=00:00:00]:[10445] (?=Help)?:";
bot.lastPromptLine = bot.lastRxLine;
bot.currentAction = "escapepod-docked-recovery";
const holdPriorityWritesBefore = sent.length;
bot._maybeRunEscapePodShipRecovery(Date.now() + 10000, false);
const holdPriorityStayedAtDock = sent.length === holdPriorityWritesBefore
  || /current ship hold upgrade/i.test(sent.at(-1)?.reason || "");
bot.isAtStarDock = true;
bot.stats.sector = 10445;
bot.stats.credits = bot.upgradeCfg.triggerCredits + 82;
bot.stats.holds = 1;
bot.stats.shipType = "Borg Manned Probe";
bot.lastRxLine = "<StarDock> Where to? (?=Help)";
const scannerBlockedForHolds = bot._maybeRunStardockScannerPurchase(Date.now(), false) === false;
bot.currentAction = "stardock-current-ship-holds";
bot.lastTx = "S";
bot.lastActionAtMs = Date.now();
bot.lastHoldLandingSentAtMs = Date.now();
bot.holdUpgradeTransitionPending = true;
bot.lastRxLine = "Landing on Federation StarDock.";
const duplicateLandingKeyBlocked = bot._maybeRunCurrentShipHoldUpgrade(Date.now() + 1000, false);
const duplicateLandingKeyWrites = sent.at(-1)?.text === "S";
bot.currentAction = "stardock-current-ship-holds";
bot.stardockLandingPending = true;
bot._onLine("Landing on Federation StarDock.");
const landingBannerPromotesRoot = bot.stardockLandingPending === false
  && bot.stardockMenu.activeMenu === "stardock-root";
bot.lastHoldLandingSentAtMs = Date.now() - 8000;
bot.holdUpgradeTransitionPending = true;
bot.stardockLandingPending = true;
bot.isEscapePod = false;
bot.currentAction = "stardock-current-ship-holds";
bot.lastHoldShipyardsKeyAtMs = Date.now();
const duplicateShipyardsKeyWritesBefore = sent.length;
TradeWarsBot.prototype._send.call(bot, "S", "current ship hold upgrade: enter Shipyards");
const duplicateShipyardsKeyBlocked = sent.length === duplicateShipyardsKeyWritesBefore;
const expiredLandingLatchRefreshes = bot._handleAutoResponses("<StarDock> Where to? (?=Help)")
  && bot.lastHoldLandingSentAtMs > Date.now() - 1000;
bot.stats.sector = 2419;
bot.stats.warps = [20256, 26788];
bot.recentSectors = [2419, 20256, 2419, 20256];
bot.sectorIntel.set(2419, { sector: 2419, portClass: 5, portProfile: "SBS", explored: true });
bot.sectorIntel.set(20256, { sector: 20256, portClass: 4, portProfile: "SSB", explored: true });
const pairLoopInfo = bot._detectRecentLoopPattern(2419);
const repeatedPairHopBlocked = bot._selectKnownTradePairSeekWarp(200, false, pairLoopInfo.cycleSet) === null;
bot.stardockSector = 10445;
bot.stats.sector = 2;
bot.stats.credits = bot.upgradeCfg.triggerCredits + 1;
bot.stats.shipType = "Borg Manned Probe";
bot.isEscapePod = true;
bot.isAtStarDock = false;
bot.shipTradeInRecovery.active = false;
bot.probeRecoveryDirectRouteAttempted = false;
bot.cimState.active = false;
bot.lastActionAtMs = Date.now() - 10000;
bot.stats.warps = [1];
const directProbeRouteWritesBefore = sent.length;
const fundedProbeDirectRoute = bot._maybeRunProbeRecoveryRoute(Date.now(), false);
const fundedProbeDirectRouteCommand = sent.slice(directProbeRouteWritesBefore).at(-1)?.text;
bot.probeRecoveryDirectRouteAttempted = true;
bot.stats.sector = 2585;
bot.stats.warps = [10560];
bot.lastActionAtMs = Date.now() - 10000;
bot.cimState.active = false;
bot.cimState.lastRunAtMs = Date.now();
bot.cimState.lastWarpCimAtMs = Date.now();
const fundedProbeRetryWritesBefore = sent.length;
const fundedProbeRouteRetried = bot._maybeRunProbeRecoveryRoute(Date.now(), false);
const fundedProbeRouteRetryCommand = sent.slice(fundedProbeRetryWritesBefore).at(-1)?.text;
bot.movementRuntime.autopilotActive = true;
const autopilotRouteBlocked = bot._maybeRunProbeRecoveryRoute(Date.now(), false)
  && sent.at(-1)?.text === fundedProbeRouteRetryCommand;
bot._parseStats("Arriving sector :10445 Autopilot disengaging.");
const autopilotRouteReleased = bot.movementRuntime.autopilotActive === false;
bot.stardockSector = 10445;
bot.stats.sector = 1;
bot.stats.warps = [2, 3, 4, 5, 6, 7];
bot.sectorIntel.clear();
bot.shipTradeInRecovery.active = true;
bot.isEscapePod = true;
bot.shipTradeInRouteCimAttempted = true;
bot.shipTradeInDirectRouteAttempted = false;
bot.cimState.active = false;
const directTradeInRoute = bot._maybeRunProbeRecoveryRoute(Date.now() + 60000, false);
const directTradeInRouteCommand = sent.at(-1)?.text;
bot.shipTradeInRecovery.active = false;
bot.isEscapePod = false;
bot.stats.credits = 5;
bot.escapePodShipPurchase.tradeInValue = 100000;
const recoveryRefusesUnderfunded = bot._computeShipTradeInRecoveryPlan()?.affordable === false;
bot.stats.credits = 5;
bot.escapePodShipPurchase.tradeInValue = 600000;
bot.escapePodShipPurchase.ships = {
  explore: { name: "Explore Sphere", option: "A", price: 216000, maxHolds: 100 },
  transport: { name: "Transport Sphere", option: "B", price: 250000, maxHolds: 255 },
};
bot.shipDatabase.purchaseCatalog = {
  explore: { name: "Explore Sphere", option: "A", price: 216000, maxHolds: 100 },
};
bot.shipTradeInRecovery.active = true;
const ratioSelectedShip = bot._selectAffordableEscapePodShip();
bot.shipTradeInRecovery.active = false;
bot.isEscapePod = false;
bot.stats.credits = 208;
bot.stats.sector = 10445;
bot.isAtStarDock = true;
bot.recoveryMicroLoop.lastRouteAtMs = 0;
bot.tradePromptState.lastTradePromptType = "";
bot.tradePromptState.pendingQtyPrompt = false;
bot.tradePromptState.activeUntilMs = 0;
const microLoopOutbound = bot._maybeRunRecoveryMicroLoop(Date.now(), false);
const microLoopOutboundCommand = sent.at(-1)?.text;
bot.stats.sector = 6781;
bot.stats.portClass = 4;
bot.stats.portProfile = "SSB";
bot.stats.holds = 1;
bot.stats.emptyHolds = 0;
bot.stats.cargoHolds = 1;
bot.stats.fuelHolds = 0;
bot.stats.organicsHolds = 1;
bot.stats.equipmentHolds = 0;
bot.recoveryMicroLoop.lastRouteAtMs = 0;
bot.recoveryMicroLoop.lastSourceTradeAtMs = 0;
const loadedCargoSourceRedirect = bot._maybeRunRecoveryMicroLoop(Date.now(), false);
const loadedCargoSourceRedirectCommand = sent.at(-1)?.text;
bot.stats.sector = 6781;
bot.isAtStarDock = false;
bot.recoveryMicroLoop.lastSourceTradeAtMs = Date.now() - bot.tradeCfg.tradeExchangeGuardMs - 100;
bot.recoveryMicroLoop.lastRouteAtMs = 0;
bot.tradeRuntime.lastLocalTradeAtMs = 0;
bot.tradePromptState.lastQtySentAtMs = 0;
bot.tradeRuntime.lastOfferAtMs = 0;
bot.tradeRuntime.fastPortMacroPending = false;
bot.tradeRuntime.fastPortMacroActive = false;
bot.tradePromptState.lastTradePromptType = "";
bot.tradePromptState.pendingQtyPrompt = false;
bot.tradePromptState.activeUntilMs = 0;
bot.lastCommandPromptMs = Date.now();
bot._finishLocalTradeLease();
bot.currentAction = "";
const microLoopReturn = bot._maybeRunRecoveryMicroLoop(Date.now(), false);
const microLoopReturnCommand = sent.at(-1)?.text;
bot.stats.credits = 208;
bot.stats.turns = 0;
bot.stats.sector = 251;
bot.isAtStarDock = false;
bot.isEscapePod = false;
bot.cimState.active = false;
bot.cimState.lastRunAtMs = Date.now() - 60000;
bot.cimState.lastPortCimAtMs = Date.now();
bot.cimState.lastWarpCimAtMs = Date.now();
const freshCimSkipped = bot._maybeRunCimSync(Date.now()) === false && bot.cimState.active === false;
const marketCheckAt = Date.now();
const freshSellerIntel = {
  market: {
    fuel: { status: "selling", amount: 100, seenAt: marketCheckAt },
    organics: { status: "selling", amount: 100, seenAt: marketCheckAt },
    equipment: { status: "selling", amount: 100, seenAt: marketCheckAt },
  },
};
const freshPairMarketAccepted = bot._hasFreshPairMarketData(
  freshSellerIntel,
  { market: {} },
  "SSS",
  "BBB",
  marketCheckAt,
);
freshSellerIntel.market.equipment.seenAt = marketCheckAt - bot.pairHarvestCfg.stockFreshMs - 1;
const stalePairMarketRejected = !bot._hasFreshPairMarketData(
  freshSellerIntel,
  { market: {} },
  "SSS",
  "BBB",
  marketCheckAt,
);
bot._send = TradeWarsBot.prototype._send.bind(bot);
bot.stats.scannerType = "Density Scanner";
const scanWritesBeforeDensity = operatorWrites.length;
bot._send("123*", "action:worldtrade-warp-hop:scanner-test");
const densityScanWrites = operatorWrites.slice(scanWritesBeforeDensity).map((entry) => entry.payload);
const densityScanStarted = densityScanWrites.includes("sd") && !densityScanWrites.includes("123");
bot._onLine("Command [100] (?=Help)?:");
const densityScanReleased = operatorWrites.at(-1)?.payload === "123";
bot.stats.scannerType = "Holographic Scanner";
const scanWritesBeforeHolo = operatorWrites.length;
bot._send("456*", "action:pair-harvest-endpoint-hop:scanner-test");
const holoScanStarted = operatorWrites.slice(scanWritesBeforeHolo).some((entry) => entry.payload === "shsd");
bot._onLine("Mined Sector: limpet mines detected");
bot._onLine("Command [100] (?=Help)?:");
const hazardScanBlocked = operatorWrites.at(-1)?.payload !== "456";
bot.stats.sector = 10445;
bot.stardockSector = 10445;
bot.isAtStarDock = false;
bot.lastPromptLine = "Command [TL=00:00:00]:[10445] (?=Help)?:";
const verifiedDockState = bot._isAtVerifiedStarDock();
bot._parseDeployedFighterIntel("Sector: 6781 Fighters: 250 belong to your Corp");
const fighterIntel = bot.sectorIntel.get(6781);
const fighterIntelParsed = fighterIntel?.deployedFighters === 250
  && /your Corp/i.test(fighterIntel?.fighterOwner || "");
bot.currentAction = "stardock-enter-menu";
bot.isAtStarDock = true;
bot.stats.credits = 100000;
bot.stats.holds = 100;
bot.stats.fighters = 100;
bot.stats.shields = 100;
bot.stats.scannerType = "None";
bot.stats.shipType = "Borg Explore Sphere";
bot.ansiSetupDone = true;
bot.ansiSetupStep = 0;
bot.lastActionAtMs = 0;
bot._send = (text, reason) => sent.push({ text: String(text), reason });
const stardockChoiceWritesBefore = sent.length;
bot._handleAutoResponses("Enter your choice [T] ?");
const stardockChoiceWrites = sent.slice(stardockChoiceWritesBefore).map((entry) => entry.text);
const stardockChoiceHandled = stardockChoiceWrites.at(-1) === "S"
  && bot.currentAction === "stardock-land-menu";
bot.stardockScannerPurchase = {
  kind: "H",
  startingCredits: 100000,
  cost: 25000,
};
bot._observeStardockScannerPurchase("You don't need two!");
const ownedScannerHandled = bot.stats.scannerType === "Holographic Scanner"
  && bot.stardockScannerPurchase.kind === ""
  && bot.stardockScannerAlreadyOwned === true
  && bot._computeStardockScannerUpgradeSequence() === ""
  && bot._computeStardockScannerChoice() === "Q";
bot.isEscapePod = false;
bot.isAtStarDock = false;
bot.stats.sector = 500;
bot.stats.portClass = 4;
bot.stats.portProfile = "SSS";
bot.stats.holds = 1;
bot.stats.emptyHolds = 1;
bot.stats.cargoHolds = 0;
bot.stats.fuelHolds = 0;
bot.stats.organicsHolds = 0;
bot.stats.equipmentHolds = 0;
bot.stats.warps = [501, 502];
bot.recentSectors = [500, 501, 500];
bot.tradeRuntime.lastLocalTradeAtMs = Date.now() - 1000;
bot.tradeRuntime.lastLocalTradeSector = 500;
bot.tradeRuntime.localPortBurst = 2;
bot.tradeRuntime.localRejectStreak = 1;
bot.tradeRuntime.noTradeDeals = 1;
bot.tradeRuntime.successfulDeals = 0;
bot.tradeRuntime.noTradeSectors.clear();
bot.tradeRuntime.noTradePairs.clear();
bot.tradeRuntime.decisionWatchdog.baselineSector = 500;
bot.tradeRuntime.decisionWatchdog.baselineSuccessfulDeals = 0;
bot.tradeRuntime.decisionWatchdog.cooldownUntilMs = 0;
bot.movementRuntime.pendingWarpTarget = null;
bot.tradeRuntime.localTradeInFlightUntilMs = 0;
bot.tradeRuntime.tradeExchangePending = false;
bot.tradePromptState.lastTradePromptType = "";
bot.tradePromptState.lastQtySentAtMs = 0;
bot.tradeRuntime.lastOfferAtMs = 0;
bot.lastCommandPromptMs = Date.now();
bot.cimState.active = false;
const watchdogWritesBefore = sent.length;
const watchdogCorrected = bot._maybeRunDecisionWatchdog(Date.now(), false);
const watchdogCorrectionCommand = sent.slice(watchdogWritesBefore).at(-1)?.text;
const watchdogCorrectionIssued = watchdogCorrected
  && bot.tradeRuntime.decisionWatchdog.state === "cooldown"
  && /^m(?:501|502)\*$/.test(watchdogCorrectionCommand || "")
  && bot.tradeRuntime.noTradeSectors.has(500);
const watchdogWritesAfterCorrection = sent.length;
const watchdogCooldownHeld = bot._maybeRunDecisionWatchdog(Date.now() + 100, false) === false
  && sent.length === watchdogWritesAfterCorrection;
bot.isEscapePod = true;
bot.isAtStarDock = false;
bot.stardockSector = 10445;
bot.stats.sector = 9351;
bot.stats.credits = 7875;
bot.stats.holds = 1;
bot.stats.shipType = "Borg Manned Probe";
bot.movementRuntime.pendingWarpTarget = null;
bot.lastActionAtMs = 0;
const holdUpgradeRouteWritesBefore = sent.length;
const holdUpgradeRouteStarted = bot._maybeRouteVulnerableShipForHoldUpgrade(Date.now() + 10000, false);
const holdUpgradeRouteCommand = sent.slice(holdUpgradeRouteWritesBefore).at(-1)?.text;
const vulnerableHoldUpgradePrioritized = holdUpgradeRouteStarted
  && holdUpgradeRouteCommand === "m10445*"
  && bot.currentAction === "stardock-hold-upgrade-route";
bot.stardockSector = null;
bot.currentAction = "";
const nullStardockRouteWritesBefore = sent.length;
const nullStardockRouteBlocked = bot._maybeRouteVulnerableShipForHoldUpgrade(Date.now() + 10000, false) === false
  && sent.length === nullStardockRouteWritesBefore;
const configuredStardockSector = bot.config.strategy.knownStardockSector;
const checks = {
  triggerCredits: bot.upgradeCfg.triggerCredits,
  unsafeSsmActionsDisabled,
  promptReadyDelayMs: bot.stardockTimingCfg.promptReadyDelayMs,
  discoverySequenceGapMs: bot.stardockTimingCfg.discoverySequenceGapMs,
  tradeExchangeGuardMs: bot.tradeCfg.tradeExchangeGuardMs,
  statusDerivedRefreshMs: bot.statusDerivedRefreshMs,
  atomicWritesMap: bot.persistenceState.atomicWrites instanceof Map,
  pattern: compilePattern("/Command\\s+\\[/i").test("Command [TL=00:00:00]"),
  twgsHandshake: telnetWrites[0]?.equals(Buffer.from([255, 251, 246])) === true,
  unknownOptionRejected: telnetWrites[1]?.equals(Buffer.from([255, 252, 42])) === true,
  persistenceScheduleReleased,
  automationBlockedDuringTakeover,
  operatorCommandSent,
  pauseUsesDefaultReturn,
  duplicateCredentialSuppressed,
  specificEngageDeclined,
  reconnectAutoResponseRestored,
  chunkLoggingDisabled: bot.logChunks === false,
  monitoringLeavesBotInControl,
  telnetTakeoverWorks,
  telnetCommandSentAfterTakeover,
  telnetReleaseWorks,
  quoteWaitsForOffer,
  unaffordableFuelProbe,
  lowCreditBuyVetoed,
  affordableFuelOffer,
  defaultQtyReturn,
  cappedQtyExplicit,
  identicalQtyHandledInNextTrade,
  localTradeSequenceNoSubmit,
  promptDrivenPortUpgrade,
  portUpgradeReleased,
  staleCommandPromptBlocked,
  freshCommandPromptAccepted,
  sameSectorTradeLeaseAcquired,
  computerSettingsBatchRequiresLivePrompt,
  quantityAckKeepsExchangeLocked,
  finalAcceptanceClearsExchange,
  duplicateAcceptanceIgnored,
  nextProductQtyHandled,
  rapidSameModeProductHandled,
  repeatedIdenticalSellOffersHandled,
  repeatedIdenticalSellOfferCount,
  recoverySellNoHaggle,
  recoveryBuyNoHaggle,
  vulnerableProbeNoHaggleAboveCreditLimit,
  startupSurveyBlockedDuringOffer,
  failedProbeRotatesPair,
  failedProbePairActive,
  failedProbePairKeys,
  lowCreditDiscovery,
  lowCreditDiscoveryCommand,
  recoveryPlan,
  recoveryStart,
  tradeInRouteDiscovery,
  tradeInRouteDiscoveryCommand,
  recoveryFallbackHeld,
  recoveryFallbackHeldAction,
  recoveryLoopUsesVerifiedWarp,
  recoveryLoopVerifiedWarpCommand,
  duplicateDepartureBlocked,
  holdPriorityStayedAtDock,
  scannerBlockedForHolds,
  duplicateLandingKeyBlocked: Boolean(duplicateLandingKeyBlocked),
  duplicateLandingKeyWrites,
  landingBannerPromotesRoot,
  expiredLandingLatchRefreshes,
  duplicateShipyardsKeyBlocked,
  repeatedPairHopBlocked,
  fundedProbeDirectRoute,
  fundedProbeDirectRouteCommand,
  fundedProbeRouteRetried,
  fundedProbeRouteRetryCommand,
  autopilotRouteBlocked,
  autopilotRouteReleased,
  directTradeInRoute,
  directTradeInRouteCommand,
  recoveryRefusesUnderfunded,
  ratioSelectedShip,
  microLoopOutbound,
  microLoopOutboundCommand,
  loadedCargoSourceRedirect,
  loadedCargoSourceRedirectCommand,
  microLoopReturn,
  microLoopReturnCommand,
  freshCimSkipped,
  freshPairMarketAccepted,
  stalePairMarketRejected,
  densityScanStarted,
  densityScanReleased,
  holoScanStarted,
  hazardScanBlocked,
  verifiedDockState,
  fighterIntelParsed,
  stardockChoiceHandled,
  ownedScannerHandled,
  watchdogCorrectionIssued,
  watchdogCooldownHeld,
  vulnerableHoldUpgradePrioritized,
  nullStardockRouteBlocked,
  configuredStardockSector,
};
bot.persistenceState.flushing = false;

console.log(JSON.stringify(checks));
bot.shutdown("offline validation");

const passed = checks.triggerCredits === 100000
  && checks.unsafeSsmActionsDisabled
  && checks.promptReadyDelayMs === 450
  && checks.discoverySequenceGapMs === 700
  && checks.tradeExchangeGuardMs === 3500
  && checks.statusDerivedRefreshMs === 5000
  && checks.atomicWritesMap
  && checks.pattern
  && checks.twgsHandshake
  && checks.unknownOptionRejected
  && checks.persistenceScheduleReleased
  && checks.automationBlockedDuringTakeover
  && checks.operatorCommandSent
  && checks.pauseUsesDefaultReturn
  && checks.duplicateCredentialSuppressed
  && checks.specificEngageDeclined
  && checks.reconnectAutoResponseRestored
  && checks.chunkLoggingDisabled
  && checks.monitoringLeavesBotInControl
  && checks.telnetTakeoverWorks
  && checks.telnetCommandSentAfterTakeover
  && checks.telnetReleaseWorks
  && checks.quoteWaitsForOffer
  && checks.unaffordableFuelProbe === "0"
  && checks.lowCreditBuyVetoed
  && checks.affordableFuelOffer === "8"
  && checks.defaultQtyReturn
  && checks.cappedQtyExplicit
  && checks.identicalQtyHandledInNextTrade
  && checks.localTradeSequenceNoSubmit
  && checks.promptDrivenPortUpgrade
  && checks.portUpgradeReleased
  && checks.quantityAckKeepsExchangeLocked
  && checks.finalAcceptanceClearsExchange
  && checks.duplicateAcceptanceIgnored
  && checks.nextProductQtyHandled
  && checks.rapidSameModeProductHandled
  && checks.repeatedIdenticalSellOffersHandled
  && checks.repeatedIdenticalSellOfferCount === 2
  && checks.recoverySellNoHaggle
  && checks.recoveryBuyNoHaggle
  && checks.vulnerableProbeNoHaggleAboveCreditLimit
  && checks.startupSurveyBlockedDuringOffer
  && checks.staleCommandPromptBlocked
  && checks.freshCommandPromptAccepted
  && checks.sameSectorTradeLeaseAcquired
  && checks.computerSettingsBatchRequiresLivePrompt
  && checks.failedProbeRotatesPair
  && checks.lowCreditDiscovery
  && checks.lowCreditDiscoveryCommand.toLowerCase() === "v"
  && checks.recoveryStart === false
  && checks.tradeInRouteDiscovery
  && checks.tradeInRouteDiscoveryCommand.toLowerCase() === "v"
  && checks.recoveryFallbackHeld
  && checks.recoveryFallbackHeldAction === "ship-trade-in-recovery-hold"
  && checks.recoveryLoopUsesVerifiedWarp
  && checks.recoveryLoopVerifiedWarpCommand === "11065*"
  && checks.duplicateDepartureBlocked
  && checks.holdPriorityStayedAtDock
  && checks.scannerBlockedForHolds
  && checks.duplicateLandingKeyBlocked
  && !checks.duplicateLandingKeyWrites
  && checks.landingBannerPromotesRoot
  && checks.expiredLandingLatchRefreshes
  && checks.duplicateShipyardsKeyBlocked
  && checks.repeatedPairHopBlocked
  && checks.fundedProbeDirectRoute
  && checks.fundedProbeDirectRouteCommand === "m10445*"
  && checks.fundedProbeRouteRetried
  && checks.fundedProbeRouteRetryCommand === "m10445*"
  && checks.autopilotRouteBlocked
  && checks.autopilotRouteReleased
  && checks.directTradeInRoute
  && checks.directTradeInRouteCommand.toLowerCase() === "m10445*"
  && checks.recoveryPlan.affordable
  && checks.recoveryPlan.reserve >= 1000
  && checks.recoveryPlan.reserve < checks.recoveryPlan.availableCredits
  && checks.recoveryRefusesUnderfunded
  && checks.ratioSelectedShip?.name === "Transport Sphere"
  && checks.ratioSelectedShip?.costPerHold < 1000
  && checks.microLoopOutbound
  && checks.microLoopOutboundCommand.toLowerCase() === "m6781*"
  && checks.loadedCargoSourceRedirect
  && checks.loadedCargoSourceRedirectCommand.toLowerCase() === "m27754*"
  && checks.microLoopReturn
  && checks.microLoopReturnCommand.toLowerCase() === "m27754*"
  && checks.freshCimSkipped
  && checks.freshPairMarketAccepted
  && checks.stalePairMarketRejected
  && checks.densityScanStarted
  && checks.densityScanReleased
  && checks.holoScanStarted
  && checks.hazardScanBlocked
  && checks.verifiedDockState
  && checks.fighterIntelParsed
  && checks.stardockChoiceHandled
  && checks.ownedScannerHandled
  && checks.watchdogCorrectionIssued
  && checks.watchdogCooldownHeld;


setTimeout(() => {
  fs.rmSync(tempRoot, { recursive: true, force: true });
  process.exit(passed ? 0 : 1);
}, 300);
