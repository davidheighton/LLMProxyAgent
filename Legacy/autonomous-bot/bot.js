const fs = require("node:fs");
const path = require("node:path");
const net = require("node:net");

function compilePattern(pattern, flags = "i") {
  if (!pattern || typeof pattern !== "string") {
    return null;
  }

  if (pattern.startsWith("/") && pattern.lastIndexOf("/") > 0) {
    const lastSlash = pattern.lastIndexOf("/");
    const source = pattern.slice(1, lastSlash);
    const parsedFlags = pattern.slice(lastSlash + 1) || flags;
    return new RegExp(source, parsedFlags);
  }

  return new RegExp(pattern, flags);
}

function nowIso() {
  return new Date().toISOString();
}

function randomInt(min, max) {
  const low = Math.floor(min);
  const high = Math.floor(max);
  if (high <= low) {
    return low;
  }
  return low + Math.floor(Math.random() * (high - low + 1));
}

class TelnetSocket {
  constructor(host, port, onTextChunk, onRawLine) {
    this.host = host;
    this.port = port;
    this.onTextChunk = onTextChunk;
    this.onRawLine = onRawLine;
    this.socket = null;
    this.partial = "";
  }

  connect() {
    return new Promise((resolve, reject) => {
      const sock = net.createConnection({ host: this.host, port: this.port }, () => {
        this.socket = sock;
        resolve();
      });

      sock.on("data", (chunk) => {
        const text = this._consumeTelnet(chunk);
        if (!text) {
          return;
        }

        this.onTextChunk(text);
        this._emitLines(text);
      });

      sock.on("error", (err) => {
        reject(err);
      });
    });
  }

  send(text) {
    if (!this.socket) {
      return;
    }

    this.socket.write(text + "\r\n", "latin1");
  }

  close() {
    if (this.socket) {
      this.socket.end();
    }
  }

  _consumeTelnet(buffer) {
    const out = [];

    for (let i = 0; i < buffer.length; i += 1) {
      const b = buffer[i];

      if (b !== 255) {
        out.push(b);
        continue;
      }

      const cmd = buffer[i + 1];
      if (cmd === undefined) {
        break;
      }

      if (cmd === 255) {
        out.push(255);
        i += 1;
        continue;
      }

      const opt = buffer[i + 2];
      if (opt === undefined) {
        break;
      }

      // Refuse all options by default for predictable automation.
      if (this.socket) {
        if (cmd === 251 || cmd === 252) {
          this.socket.write(Buffer.from([255, 254, opt])); // DONT
        } else if (cmd === 253 || cmd === 254) {
          this.socket.write(Buffer.from([255, 252, opt])); // WONT
        }
      }

      i += 2;
    }

    return Buffer.from(out).toString("latin1");
  }

  _emitLines(text) {
    const normalized = (this.partial + text).replace(/\r/g, "");
    const lines = normalized.split("\n");
    this.partial = lines.pop() || "";

    for (const line of lines) {
      this.onRawLine(line);
    }
  }
}

class TradeWarsBot {
  constructor(configPath) {
    this.configPath = configPath;
    this.config = this._loadConfig(configPath);
    this.logFilePath = this._buildLogPath();
    this.statusFilePath = this._buildStatusPath();
    this.intelFilePath = this._buildIntelPath();
    this.telnet = null;

    this.lastCommandPromptMs = 0;
    this.lastInteractivePromptMs = 0;
    this.lastActionAtMs = 0;
    this.lastInfoRefreshAtMs = Date.now();
    this.lastThreatEvadeAtMs = 0;
    this.lastThreatSignalAtMs = 0;
    this.lastRxAtMs = Date.now();
    this.lastNudgeAtMs = 0;
    this.actionIndex = 0;
    this.stats = {
      turns: null,
      credits: null,
      sector: null,
      exp: null,
      alignment: null,
      colonists: null,
      holds: null,
      emptyHolds: null,
      cargoHolds: null,
      fuelHolds: 0,
      organicsHolds: 0,
      equipmentHolds: 0,
      shipId: null,
      portEquipBuying: null,
      warps: [],
      portClass: null,
      portProfile: "",
      explored: null,
      shields: null,
      scannerType: "",
    };
    this.previousSector = null;
    this.stardockSector = null;
    this.isEscapePod = false;
    this.isAtStarDock = false;
    this.pendingRouteTarget = null;
    this.sectorIntel = new Map();
    this.recentSectors = [];
    this.bustedSectors = new Set();
    this.evilBustedSectors = new Map();
    this.failedWarpTargets = new Map();
    this.lastAttemptedWarpTarget = null;
    this.tradePromptState = {
      mode: "",
      product: "",
      lastAskPrice: null,
      haggleRound: 0,
      lastPromptSig: "",
      lastHandledAtMs: 0,
      lastModelKey: "",
    };
    this.tradeRuntime = {
      baselineCredits: null,
      baselineExp: null,
      bestNetCredits: 0,
      lastProfitAtMs: Date.now(),
      lastProfitQuestionAtMs: 0,
      successfulDeals: 0,
      rejectedDeals: 0,
      noTradeDeals: 0,
      forcedWarpHops: 0,
      forcedStuckWarps: 0,
      forcedProfitWarps: 0,
      optimizerIteration: 0,
      optimizerTargetIterations: 0,
      localRejectStreak: 0,
      localPortBurst: 0,
      lastTradeSector: null,
      lastOutcome: "",
      lastOfferSent: null,
      lastOfferAtMs: 0,
      lastModelOffer: null,
      lastModelMode: "",
      noTradeSectors: new Map(),
      recentOutcomes: [],
      priceModels: new Map(),
    };

    this.movementProgress = {
      lastSector: null,
      lastSectorChangeAtMs: Date.now(),
    };

    this.cimCfg = this._buildCimConfig();
    this.cimState = {
      active: false,
      mode: "",
      queuedAtMs: 0,
      startedAtMs: 0,
      completedAtMs: 0,
      lastRunAtMs: 0,
      lastPortCimAtMs: 0,
      lastWarpCimAtMs: 0,
      nextMode: "ports",
      parsedPortLines: 0,
      parsedWarpLines: 0,
      updatedPortSectors: 0,
      updatedWarpSectors: 0,
      runSawPortData: false,
      runSawWarpData: false,
      lastSummary: "",
    };
    this.lastIntelPersistAtMs = 0;

    this.identity = this._initIdentity();
    this.aliasState = {
      current: "",
      attempts: 0,
      complete: false,
    };
    this.aliasState.current = this._generateAliasCandidate();

    this.patterns = {
      characterPrompt: compilePattern(this.config.prompts.characterPrompt),
      usernamePrompt: compilePattern(this.config.prompts.usernamePrompt),
      passwordPrompt: compilePattern(this.config.prompts.passwordPrompt),
      commandPrompt: compilePattern(this.config.prompts.commandPrompt),
      sectorPrompt: compilePattern(this.config.prompts.sectorPrompt),
    };

    this.autoResponses = (this.config.autoResponses || []).map((rule) => ({
      match: compilePattern(rule.match),
      reply: rule.reply,
      once: Boolean(rule.once),
      allowEmpty: Boolean(rule.allowEmpty),
      sensitive: Boolean(rule.sensitive),
      onlyWhenAtStarDock: Boolean(rule.onlyWhenAtStarDock),
      onlyWhenEscapePod: Boolean(rule.onlyWhenEscapePod),
      skipWhenCimActive: Boolean(rule.skipWhenCimActive),
      minIntervalMs: Number.isFinite(rule.minIntervalMs) ? rule.minIntervalMs : 1200,
      used: false,
      lastSentAtMs: 0,
    }));

    this.actions = (this.config.strategy.actions || []).map((action) => ({
      name: action.name || "unnamed",
      command: action.command || "",
      enabled: action.enabled !== false,
      minTurns: Number.isFinite(action.minTurns) ? action.minTurns : 0,
      maxTurns: Number.isFinite(action.maxTurns) ? action.maxTurns : Number.MAX_SAFE_INTEGER,
      minCredits: Number.isFinite(action.minCredits) ? action.minCredits : Number.MIN_SAFE_INTEGER,
      maxCredits: Number.isFinite(action.maxCredits) ? action.maxCredits : Number.MAX_SAFE_INTEGER,
      minExp: Number.isFinite(action.minExp) ? action.minExp : Number.MIN_SAFE_INTEGER,
      maxExp: Number.isFinite(action.maxExp) ? action.maxExp : Number.MAX_SAFE_INTEGER,
      minAlignment: Number.isFinite(action.minAlignment) ? action.minAlignment : Number.MIN_SAFE_INTEGER,
      maxAlignment: Number.isFinite(action.maxAlignment) ? action.maxAlignment : Number.MAX_SAFE_INTEGER,
      minHolds: Number.isFinite(action.minHolds) ? action.minHolds : Number.MIN_SAFE_INTEGER,
      maxHolds: Number.isFinite(action.maxHolds) ? action.maxHolds : Number.MAX_SAFE_INTEGER,
      cooldownMs: Number.isFinite(action.cooldownMs) ? action.cooldownMs : 0,
      once: Boolean(action.once),
      whenPromptMatches: compilePattern(action.whenPromptMatches || ""),
      onlyWhenHasPort: Boolean(action.onlyWhenHasPort),
      onlyWhenEscapePod: Boolean(action.onlyWhenEscapePod),
      onlyWhenNotEscapePod: Boolean(action.onlyWhenNotEscapePod),
      onlyWhenAtStarDock: Boolean(action.onlyWhenAtStarDock),
      onlyWhenNotAtStarDock: Boolean(action.onlyWhenNotAtStarDock),
      onlyWhenStardockKnown: Boolean(action.onlyWhenStardockKnown),
      onlyWhenStardockUnknown: Boolean(action.onlyWhenStardockUnknown),
      onlyWhenRecoveryMode: Boolean(action.onlyWhenRecoveryMode),
      onlyWhenNotRecoveryMode: Boolean(action.onlyWhenNotRecoveryMode),
      onlyWhenCargoLoaded: Boolean(action.onlyWhenCargoLoaded),
      used: false,
      lastRunAtMs: 0,
    })).filter((a) => a.enabled && a.command);

    this.minActionDelayMs = Math.max(1000, Number(this.config.strategy.minActionDelayMs || 4500));
    this.maxIdleBeforeKeepAliveMs = Math.max(5000, Number(this.config.strategy.maxIdleBeforeKeepAliveMs || 20000));
    const infoRefreshCfg = this.config.strategy.infoRefresh || {};
    this.infoRefreshCfg = {
      enabled: infoRefreshCfg.enabled !== false,
      command: String(infoRefreshCfg.command || "I"),
      intervalMs: Math.max(10000, Number(infoRefreshCfg.intervalMs || 90000)),
      minGapAfterActionMs: Math.max(0, Number(infoRefreshCfg.minGapAfterActionMs || 0)),
      skipInStarDockSubmenu: infoRefreshCfg.skipInStarDockSubmenu !== false,
    };
    this.keepAliveCommand = String(this.config.strategy.keepAliveCommand || "/");
    this.randomSectorMin = Number(this.config.strategy.randomSectorMin || 1);
    this.randomSectorMax = Number(this.config.strategy.randomSectorMax || 5000);
    this.stealHoldsExpDivisor = Math.max(1, Number(this.config.strategy.stealHoldsExpDivisor ?? this.config.strategy.stealFactor ?? 20));
    this.stealCreditsMultiplier = Math.max(1, Number(this.config.strategy.stealCreditsMultiplier ?? 10));
    this.stealRequiresAlignment = this.config.strategy.stealRequiresAlignment !== false;
    this.stealAlignmentMax = Number.isFinite(Number(this.config.strategy.stealAlignmentMax))
      ? Number(this.config.strategy.stealAlignmentMax)
      : -100;
    this.minStealHolds = Math.max(1, Number(this.config.strategy.minStealHolds || 1));
    this.maxStealHolds = Math.max(this.minStealHolds, Number(this.config.strategy.maxStealHolds || 255));
    this.safeProgressionMinExp = Math.max(0, Number(this.config.strategy.safeProgressionMinExp ?? 0));
    this.preferCommodityPairsMinCredits = Math.max(0, Number(this.config.strategy.preferCommodityPairsMinCredits ?? 10000));
    this.organicPairBonus = Math.max(0, Number(this.config.strategy.organicPairBonus ?? 70));
    this.equipmentPairBonus = Math.max(0, Number(this.config.strategy.equipmentPairBonus ?? 80));
    this.fuelPairOpportunisticFactor = Math.min(1, Math.max(0, Number(this.config.strategy.fuelPairOpportunisticFactor ?? 0.45)));
    const tradeCfg = this.config.strategy.trade || {};
    this.tradeCfg = {
      enabled: tradeCfg.enabled !== false,
      forcePromptHaggle: tradeCfg.forcePromptHaggle !== false,
      desiredCargo: String(tradeCfg.desiredCargo || "equipment").toLowerCase(),
      autoDesiredCargoByNeed: tradeCfg.autoDesiredCargoByNeed !== false,
      buyOfferPct: Math.min(1.05, Math.max(0.7, Number(tradeCfg.buyOfferPct ?? 0.985))),
      sellOfferPct: Math.min(1.35, Math.max(0.95, Number(tradeCfg.sellOfferPct ?? 1.015))),
      maxHaggleRounds: Math.max(0, Number(tradeCfg.maxHaggleRounds ?? 2)),
      driftTolerance: Math.max(0, Number(tradeCfg.driftTolerance ?? 20)),
      uncertainThreshold: Math.max(0, Number(tradeCfg.uncertainThreshold ?? 28)),
      overbidWhenUncertain: tradeCfg.overbidWhenUncertain === true,
      correctionPct: Math.min(1, Math.max(0, Number(tradeCfg.correctionPct ?? 0.15))),
      enableHoldStepModel: tradeCfg.enableHoldStepModel !== false,
      localPortBurstMin: Math.max(0, Number(tradeCfg.localPortBurstMin ?? 3)),
      moveOnRejectStreak: Math.max(1, Number(tradeCfg.moveOnRejectStreak ?? 2)),
      noTradeSectorCooldownMs: Math.max(2000, Number(tradeCfg.noTradeSectorCooldownMs ?? 30000)),
      profitQuestionEnabled: tradeCfg.profitQuestionEnabled !== false,
      profitQuestionMs: Math.max(30000, Number(tradeCfg.profitQuestionMs ?? 180000)),
      profitQuestionMinNetCredits: Math.max(0, Number(tradeCfg.profitQuestionMinNetCredits ?? 150)),
      stuckSectorWarpEnabled: tradeCfg.stuckSectorWarpEnabled !== false,
      stuckSectorWarpMs: Math.max(8000, Number(tradeCfg.stuckSectorWarpMs ?? 22000)),
      adaptiveHaggleEnabled: tradeCfg.adaptiveHaggleEnabled !== false,
      adaptiveHaggleWindow: Math.max(6, Number(tradeCfg.adaptiveHaggleWindow ?? 16)),
      adaptiveHaggleMinSamples: Math.max(4, Number(tradeCfg.adaptiveHaggleMinSamples ?? 8)),
      adaptiveRejectRateThreshold: Math.min(0.95, Math.max(0.1, Number(tradeCfg.adaptiveRejectRateThreshold ?? 0.45))),
      adaptiveBuyPctMax: Math.min(1.05, Math.max(0.8, Number(tradeCfg.adaptiveBuyPctMax ?? 0.997))),
      adaptiveSellPctMin: Math.min(1.2, Math.max(1.0, Number(tradeCfg.adaptiveSellPctMin ?? 1.005))),
      adaptiveUncertainThresholdBoost: Math.max(0, Number(tradeCfg.adaptiveUncertainThresholdBoost ?? 10)),
      adaptiveCorrectionScale: Math.min(1, Math.max(0.1, Number(tradeCfg.adaptiveCorrectionScale ?? 0.6))),
      iterationTuningEnabled: tradeCfg.iterationTuningEnabled !== false,
      iterationTargetIterations: Math.max(1, Number(tradeCfg.iterationTargetIterations ?? 100)),
      iterationProfitQuestionStepMs: Math.max(250, Number(tradeCfg.iterationProfitQuestionStepMs ?? 1000)),
      iterationMinProfitQuestionMs: Math.max(10000, Number(tradeCfg.iterationMinProfitQuestionMs ?? 30000)),
      iterationStuckWarpStepMs: Math.max(100, Number(tradeCfg.iterationStuckWarpStepMs ?? 250)),
      iterationMinStuckSectorWarpMs: Math.max(8000, Number(tradeCfg.iterationMinStuckSectorWarpMs ?? 8000)),
      iterationRouteBonusStep: Math.max(1, Number(tradeCfg.iterationRouteBonusStep ?? 1)),
      iterationMaxProfitablePairBonus: Math.max(10, Number(tradeCfg.iterationMaxProfitablePairBonus ?? 60)),
      initialNoHaggleDeals: Math.max(0, Number(tradeCfg.initialNoHaggleDeals ?? 6)),
    };
    this.tradeRuntime.optimizerTargetIterations = this.tradeCfg.iterationTargetIterations;
    this.staticPlaceholders = this.config.strategy.placeholders || {};
    this.stardockUpgradeSequence = String(this.config.strategy.stardockUpgradeSequence || "");
    this.stardockShipBuySequence = String(this.config.strategy.stardockShipBuySequence || "");
    this.stardockBuyShield = String(this.config.strategy.stardockBuyShield || "0");
    this.stardockBuyFighters = String(this.config.strategy.stardockBuyFighters || "0");
    this.stardockBuyScanner = String(this.config.strategy.stardockBuyScanner || "H");
    this.stardockBuyShipConfirm = String(this.config.strategy.stardockBuyShipConfirm || "y");
    const upgradeCfg = this.config.strategy.upgrades || {};
    this.upgradeCfg = {
      enabled: upgradeCfg.enabled !== false,
      triggerCredits: Math.max(0, Number(upgradeCfg.triggerCredits ?? 90000)),
      holoScannerPriority: upgradeCfg.holoScannerPriority !== false,
      holoScannerMinCredits: Math.max(0, Number(upgradeCfg.holoScannerMinCredits ?? 20000)),
      minCashReserve: Math.max(0, Number(upgradeCfg.minCashReserve ?? 30000)),
      maxSpendPerVisit: Math.max(0, Number(upgradeCfg.maxSpendPerVisit ?? 60000)),
      minSpendToUpgrade: Math.max(0, Number(upgradeCfg.minSpendToUpgrade ?? 5000)),
      autoPurchase: upgradeCfg.autoPurchase !== false,
      shieldUnitCost: Math.max(1, Number(upgradeCfg.shieldUnitCost ?? 100)),
      fighterUnitCost: Math.max(1, Number(upgradeCfg.fighterUnitCost ?? 1)),
      shieldSpendRatio: Math.min(1, Math.max(0, Number(upgradeCfg.shieldSpendRatio ?? 0.35))),
      fighterSpendRatio: Math.min(1, Math.max(0, Number(upgradeCfg.fighterSpendRatio ?? 0.65))),
      maxShieldBuy: Math.max(0, Number(upgradeCfg.maxShieldBuy ?? 300)),
      maxFighterBuy: Math.max(0, Number(upgradeCfg.maxFighterBuy ?? 1500)),
    };
    this.noEligibleFallbackAction = String(this.config.strategy.noEligibleFallbackAction || "");
    this.startupTuningSequence = String(this.config.strategy.startupTuningSequence || "");
    this.startupTuningDone = !this.startupTuningSequence;
    const startupSurveyCfg = this.config.strategy.startupSurvey || {};
    const startupSurveyCommands = Array.isArray(startupSurveyCfg.commands)
      ? startupSurveyCfg.commands.map((cmd) => String(cmd || "").trim()).filter(Boolean)
      : ["I", "v"];
    this.startupSurveyCfg = {
      enabled: startupSurveyCfg.enabled !== false,
      minGapMs: Math.max(800, Number(startupSurveyCfg.minGapMs || 1200)),
      cimMinAgeMs: Math.max(0, Number(startupSurveyCfg.cimMinAgeMs || (7 * 24 * 60 * 60 * 1000))),
    };
    this.startupSurveyCommands = startupSurveyCommands;
    this.startupSurveyIndex = 0;
    this.startupSurveyDone = !this.startupSurveyCfg.enabled || this.startupSurveyCommands.length === 0;
    this.preCommandNudgeMs = Math.max(3000, Number(this.config.strategy.preCommandNudgeMs || 9000));
    this.recoveryModeMaxCredits = Math.max(0, Number(this.config.strategy.recoveryModeMaxCredits ?? 150));
    this.recoveryOnlyWhenEscapePod = this.config.strategy.recoveryOnlyWhenEscapePod !== false;
    this.recoveryLiquidationSequence = String(this.config.strategy.recoveryLiquidationSequence || "");
    this.recoveryLiquidationDone = !this.recoveryLiquidationSequence;
    this.recoveryLiquidationMaxCredits = Math.max(0, Number(this.config.strategy.recoveryLiquidationMaxCredits ?? this.recoveryModeMaxCredits));
    this.recoveryLiquidationStagnationTicks = Math.max(2, Number(this.config.strategy.recoveryLiquidationStagnationTicks ?? 10));
    this.recoveryStagnationTicks = 0;
    this.recoveryLastCredits = null;
    const supervisorCfg = this.config.strategy.supervisor || {};
    this.supervisorCfg = {
      enabled: supervisorCfg.enabled !== false,
      freezeMs: Math.max(15000, Number(supervisorCfg.freezeMs || 100000)),
      recoveryCooldownMs: Math.max(3000, Number(supervisorCfg.recoveryCooldownMs || 12000)),
      escalationKeepAliveAfter: Math.max(1, Number(supervisorCfg.escalationKeepAliveAfter || 2)),
    };
    this.supervisorState = {
      phase: "boot",
      phaseEnteredAtMs: Date.now(),
      lastProgressAtMs: Date.now(),
      lastRecoveryAtMs: 0,
      lastRecoveryReason: "",
      recoveriesTotal: 0,
      consecutiveRecoveries: 0,
      freezeDetected: false,
    };
    const eventCfg = this.config.strategy.events || {};
    this.eventCfg = {
      enabled: eventCfg.enabled !== false,
      maxRecent: Math.max(20, Number(eventCfg.maxRecent || 80)),
      maxCounters: Math.max(20, Number(eventCfg.maxCounters || 300)),
      maxPayloadChars: Math.max(60, Number(eventCfg.maxPayloadChars || 280)),
    };
    this.eventState = {
      seq: 0,
      counters: {},
      recent: [],
    };
    this.modeState = {
      current: "Bootstrap",
      changedAtMs: Date.now(),
      reason: "init",
    };
    const alignmentCfg = this.config.strategy.alignmentShift || {};
    this.alignmentShiftCfg = {
      enabled: alignmentCfg.enabled === true,
      targetAlignmentMax: Number.isFinite(alignmentCfg.targetAlignmentMax) ? Number(alignmentCfg.targetAlignmentMax) : -100,
      sourceSector: Number.isFinite(alignmentCfg.sourceSector) ? Number(alignmentCfg.sourceSector) : 1,
      infoCommand: String(alignmentCfg.infoCommand || "I"),
      moveToSourceCommand: String(alignmentCfg.moveToSourceCommand || "m{sourceSector}*"),
      loadColonistsCommand: String(alignmentCfg.loadColonistsCommand || "l1*t*"),
      leaveFedSpaceCommand: String(alignmentCfg.leaveFedSpaceCommand || "m{nextWarp}*"),
      jettisonCommand: String(alignmentCfg.jettisonCommand || "jy"),
      preferDirectMoveToSource: alignmentCfg.preferDirectMoveToSource !== false,
      requireKnownAlignment: alignmentCfg.requireKnownAlignment !== false,
      minColonistsToJettison: Math.max(1, Number(alignmentCfg.minColonistsToJettison || 1)),
      suspendInRecovery: alignmentCfg.suspendInRecovery !== false,
      suspendInEscapePod: alignmentCfg.suspendInEscapePod !== false,
      prioritizeInRecoveryUntilTarget: alignmentCfg.prioritizeInRecoveryUntilTarget !== false,
      enableSelfDestructReset: alignmentCfg.enableSelfDestructReset === true,
      selfDestructCommand: String(alignmentCfg.selfDestructCommand || "cby"),
      selfDestructOnlyInRecovery: alignmentCfg.selfDestructOnlyInRecovery !== false,
      selfDestructOnlyWhenNotEscapePod: alignmentCfg.selfDestructOnlyWhenNotEscapePod !== false,
      selfDestructWhenColonistPathInsufficient: alignmentCfg.selfDestructWhenColonistPathInsufficient !== false,
      skipJettisonWhenInsufficientCapacity: alignmentCfg.skipJettisonWhenInsufficientCapacity !== false,
      selfDestructNoEffectMaxAttempts: Math.max(1, Number(alignmentCfg.selfDestructNoEffectMaxAttempts || 2)),
      selfDestructNoEffectBackoffMs: Math.max(10000, Number(alignmentCfg.selfDestructNoEffectBackoffMs || 300000)),
      maxLoadAttemptsBeforeSelfDestruct: Math.max(1, Number(alignmentCfg.maxLoadAttemptsBeforeSelfDestruct || 3)),
      maxJettisonAttemptsBeforeSelfDestruct: Math.max(1, Number(alignmentCfg.maxJettisonAttemptsBeforeSelfDestruct || 3)),
      maxSourceSeekAttemptsBeforeSelfDestruct: Math.max(1, Number(alignmentCfg.maxSourceSeekAttemptsBeforeSelfDestruct || 8)),
      selfDestructCooldownMs: Math.max(10000, Number(alignmentCfg.selfDestructCooldownMs || 3600000)),
      verifyAfterJettison: alignmentCfg.verifyAfterJettison !== false,
      verifyAfterSelfDestruct: alignmentCfg.verifyAfterSelfDestruct !== false,
      verificationMinGapMs: Math.max(1500, Number(alignmentCfg.verificationMinGapMs || 6000)),
      fedSpaceSectors: Array.isArray(alignmentCfg.fedSpaceSectors)
        ? alignmentCfg.fedSpaceSectors.map((n) => Number(n)).filter((n) => Number.isFinite(n) && n > 0)
        : [1, 2, 3, 4, 5, 6, 7, 8, 9, 10],
    };
    this.lastAlignmentSelfDestructAtMs = 0;
    this.alignmentCheckState = {
      pending: false,
      requestedAtMs: 0,
      lastObservedAlignment: null,
    };
    this.alignmentLoadState = {
      attempts: 0,
      lastColonists: null,
      blocked: false,
      blockedReason: "",
      lastBlockedAtMs: 0,
    };
    this.alignmentJettisonState = {
      attempts: 0,
      lastAttemptAlignment: null,
    };
    this.alignmentSourceSeekState = {
      attempts: 0,
      lastSector: null,
    };
    this.alignmentSelfDestructState = {
      awaitingResult: false,
      lastAttemptAlignment: null,
      noEffectAttempts: 0,
      suppressUntilMs: 0,
      lastFailureReason: "",
    };
    const routeCfg = this.config.strategy.route || {};
    this.routeCfg = {
      recentMemorySize: Math.max(4, Number(routeCfg.recentMemorySize || 20)),
      unknownSectorBonus: Number(routeCfg.unknownSectorBonus || 30),
      unexploredSectorBonus: Number(routeCfg.unexploredSectorBonus || 25),
      anyPortBonus: Number(routeCfg.anyPortBonus || 20),
      doubleBuyPortBonus: Number(routeCfg.doubleBuyPortBonus || 12),
      singleBuyPortBonus: Number(routeCfg.singleBuyPortBonus || 6),
      profitablePairBonus: Number(routeCfg.profitablePairBonus || 14),
      fuelPairBonus: Number(routeCfg.fuelPairBonus || 40),
      extraCommodityPairBonus: Number(routeCfg.extraCommodityPairBonus || 15),
      backtrackPenalty: Number(routeCfg.backtrackPenalty || 20),
      recentSectorPenalty: Number(routeCfg.recentSectorPenalty || 35),
      bustedSectorPenalty: Number(routeCfg.bustedSectorPenalty || 1000),
      hostileSectorPenalty: Number(routeCfg.hostileSectorPenalty || 1500),
      failedRoutePenalty: Number(routeCfg.failedRoutePenalty || 250),
      failedRoutePenaltyStep: Number(routeCfg.failedRoutePenaltyStep || 120),
      failedRouteForgetMs: Math.max(20000, Number(routeCfg.failedRouteForgetMs || 180000)),
      minTradeProductAmount: Math.max(0, Number(routeCfg.minTradeProductAmount || 250)),
      randomJitterMax: Math.max(0, Number(routeCfg.randomJitterMax || 4)),
    };
    const movementSafetyCfg = this.config.strategy.movementSafety || {};
    this.movementSafetyCfg = {
      enabled: movementSafetyCfg.enabled !== false,
      layFightersOnTradeWarpHop: movementSafetyCfg.layFightersOnTradeWarpHop !== false,
      fightersPerDrop: Math.max(0, Number(movementSafetyCfg.fightersPerDrop ?? 1)),
      layOnlyOnGoodPairs: movementSafetyCfg.layOnlyOnGoodPairs !== false,
      layOnAdjacentGoodPairs: movementSafetyCfg.layOnAdjacentGoodPairs !== false,
      maxNavHazToLay: Math.max(0, Number(movementSafetyCfg.maxNavHazToLay ?? 2)),
      figDropCommandPrefix: String(movementSafetyCfg.figDropCommandPrefix || "f"),
      figDropCommandSuffix: String(movementSafetyCfg.figDropCommandSuffix || "*ct"),
    };
    const evilCfg = this.config.strategy.evil || {};
    const defaultBustForgetMs = Math.max(30000, Number(evilCfg.bustForgetMs || 900000));
    this.evilCfg = {
      bustForgetMs: defaultBustForgetMs,
      bustForgetMsSteal: Math.max(30000, Number(evilCfg.bustForgetMsSteal || defaultBustForgetMs)),
      bustForgetMsRob: Math.max(30000, Number(evilCfg.bustForgetMsRob || defaultBustForgetMs)),
      bustPenalty: Math.max(0, Number(evilCfg.bustPenalty || 3000)),
      enforceBustBlacklist: evilCfg.enforceBustBlacklist !== false,
    };
    this.lastPromptLine = "";
    this.lastSectorLine = "";
    this.lastRxLine = "";
    this.lastStarDockPromptLine = "";
    this.lastTx = "";
    this.currentAction = "";
    this.statusState = {
      phase: "init",
      action: "",
      note: "",
      updatedAt: nowIso(),
    };

    this._publishEvent("runtime.init", { configPath: this.configPath, host: this.config.connection.host });

    this._loadIntelSnapshot();
  }

  _initIdentity() {
    const creds = this.config.credentials || {};
    const autoCfg = this.config.autoCreateAccount || {};

    const identity = {
      character: creds.character || "",
      username: creds.username || "",
      password: creds.password || "",
      generated: false,
    };

    const reuseGenerated = autoCfg.reuseGeneratedAccount !== false;
    if (reuseGenerated && (!identity.username || !identity.password)) {
      const reused = this._loadGeneratedIdentity(autoCfg);
      if (reused) {
        return {
          ...identity,
          username: identity.username || reused.username,
          password: identity.password || reused.password,
          character: identity.character || reused.character || "",
          generated: false,
        };
      }
    }

    const shouldGenerate = Boolean(autoCfg.enabled) && (!identity.username || !identity.password);
    if (!shouldGenerate) {
      return identity;
    }

    const usernamePrefix = String(autoCfg.usernamePrefix || "twbot");
    const passwordPrefix = String(autoCfg.passwordPrefix || "tw");
    const minSuffix = Number.isFinite(autoCfg.minSuffix) ? autoCfg.minSuffix : 1000;
    const maxSuffix = Number.isFinite(autoCfg.maxSuffix) ? autoCfg.maxSuffix : 99999;
    const suffix = randomInt(minSuffix, maxSuffix);
    const extra = randomInt(10, 99);

    if (!identity.username) {
      identity.username = `${usernamePrefix}${suffix}`;
    }

    if (!identity.password) {
      identity.password = `${passwordPrefix}${suffix}${extra}`;
    }

    identity.generated = true;
    this._persistGeneratedIdentity(identity, autoCfg);
    return identity;
  }

  _loadGeneratedIdentity(autoCfg) {
    const target = autoCfg.saveGeneratedTo || "./generated-account.json";
    const inputPath = path.resolve(path.dirname(this.configPath), target);
    if (!fs.existsSync(inputPath)) {
      return null;
    }

    try {
      const raw = fs.readFileSync(inputPath, "utf8");
      const parsed = JSON.parse(raw);
      if (!parsed.username || !parsed.password) {
        return null;
      }
      return parsed;
    } catch {
      return null;
    }
  }

  _persistGeneratedIdentity(identity, autoCfg) {
    const target = autoCfg.saveGeneratedTo || "./generated-account.json";
    const outputPath = path.resolve(path.dirname(this.configPath), target);

    const payload = {
      createdAt: nowIso(),
      host: this.config.connection.host,
      port: this.config.connection.port,
      username: identity.username,
      password: identity.password,
      character: identity.character || "",
    };

    fs.mkdirSync(path.dirname(outputPath), { recursive: true });
    fs.writeFileSync(outputPath, JSON.stringify(payload, null, 2));
  }

  async start() {
    this._log(`Starting bot with config: ${this.configPath}`);
    this._log(`Connecting to ${this.config.connection.host}:${this.config.connection.port}`);
    this._setStatus({ phase: "connecting", note: `Connecting to ${this.config.connection.host}:${this.config.connection.port}` });

    this.telnet = new TelnetSocket(
      this.config.connection.host,
      Number(this.config.connection.port),
      (chunk) => this._onTextChunk(chunk),
      (line) => this._onLine(line)
    );

    await this.telnet.connect();
    this._log("Connected.");
    this._setStatus({ phase: "connected", note: "Connected to server" });
    if (this.identity.generated) {
      this._log(`Generated credentials for auto-create. Username: ${this.identity.username}`);
    }

    setInterval(() => {
      this._tick();
    }, 1000);
  }

  _loadConfig(configPath) {
    const raw = fs.readFileSync(configPath, "utf8");
    const cfg = JSON.parse(raw);

    if (!cfg.connection || !cfg.connection.host || !cfg.connection.port) {
      throw new Error("Invalid config: connection.host and connection.port are required.");
    }

    if (!cfg.prompts) {
      throw new Error("Invalid config: prompts object is required.");
    }

    if (!cfg.strategy) {
      cfg.strategy = {};
    }

    if (!cfg.credentials) {
      cfg.credentials = {};
    }

    return cfg;
  }

  _buildLogPath() {
    const baseName = `session-${new Date().toISOString().replace(/[:.]/g, "-")}.log`;
    const explicit = this.config.logging && this.config.logging.file;
    const filePath = explicit ? path.resolve(path.dirname(this.configPath), explicit) : path.resolve(path.dirname(this.configPath), "logs", baseName);

    const dir = path.dirname(filePath);
    fs.mkdirSync(dir, { recursive: true });

    return filePath;
  }

  _buildStatusPath() {
    const explicit = this.config.logging && this.config.logging.statusFile;
    const filePath = explicit
      ? path.resolve(path.dirname(this.configPath), explicit)
      : path.resolve(path.dirname(this.configPath), "logs", "status.json");

    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    return filePath;
  }

  _buildIntelPath() {
    const explicit = this.config.logging && this.config.logging.intelFile;
    const filePath = explicit
      ? path.resolve(path.dirname(this.configPath), explicit)
      : path.resolve(path.dirname(this.configPath), "logs", "sector-intel.json");

    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    return filePath;
  }

  _buildCimConfig() {
    const strategy = this.config.strategy || {};
    const raw = strategy.cim || {};
    return {
      enabled: raw.enabled !== false,
      intervalMs: Math.max(30000, Number(raw.intervalMs || 240000)),
      includeWarpData: raw.includeWarpData !== false,
      bootstrapIfMissing: raw.bootstrapIfMissing !== false,
      prioritizeStale: raw.prioritizeStale !== false,
      fallbackOnlyWhenPairsExhausted: raw.fallbackOnlyWhenPairsExhausted !== false,
      knownPairThreshold: Math.max(0, Number(raw.knownPairThreshold || 0)),
      skipInEscapePod: raw.skipInEscapePod !== false,
      requireSafeSector: raw.requireSafeSector !== false,
      maxNavHazForCim: Math.max(0, Number(raw.maxNavHazForCim || 2)),
      deferAfterThreatMs: Math.max(0, Number(raw.deferAfterThreatMs || 60000)),
      portFreshMs: Math.max(30000, Number(raw.portFreshMs || 1800000)),
      warpFreshMs: Math.max(30000, Number(raw.warpFreshMs || 3600000)),
      portCommand: String(raw.portCommand || "crq"),
      warpCommand: String(raw.warpCommand || "ciq"),
      completionPattern: compilePattern(raw.completionPattern || "ENDINTERROG"),
      timeoutMs: Math.max(10000, Number(raw.timeoutMs || 90000)),
      minTurns: Math.max(0, Number(raw.minTurns || 0)),
      minCredits: Math.max(0, Number(raw.minCredits || 0)),
      persistEveryMs: Math.max(5000, Number(raw.persistEveryMs || 15000)),
    };
  }

  _loadIntelSnapshot() {
    if (!fs.existsSync(this.intelFilePath)) {
      return;
    }

    try {
      const parsed = JSON.parse(fs.readFileSync(this.intelFilePath, "utf8"));
      if (Array.isArray(parsed.sectors)) {
        for (const item of parsed.sectors) {
          if (!item || !Number.isFinite(item.sector)) {
            continue;
          }
          this._upsertSectorIntel(Number(item.sector), {
            explored: item.explored === false ? false : null,
            portClass: Number.isFinite(item.portClass) ? item.portClass : null,
            portProfile: item.portProfile ? String(item.portProfile).toUpperCase() : "",
            warps: Array.isArray(item.warps) ? item.warps : [],
            navHaz: Number.isFinite(item.navHaz) ? item.navHaz : null,
            market: item.market && typeof item.market === "object" ? item.market : {},
            busted: Boolean(item.busted),
            hostile: Boolean(item.hostile),
          });
        }
      }

      if (Array.isArray(parsed.bustedSectors)) {
        for (const sector of parsed.bustedSectors) {
          if (Number.isFinite(sector)) {
            this.bustedSectors.add(Number(sector));
          }
        }
      }

      if (parsed.cimMeta && typeof parsed.cimMeta === "object") {
        this.cimState.lastRunAtMs = Number(parsed.cimMeta.lastRunAtMs || 0);
        this.cimState.completedAtMs = Number(parsed.cimMeta.completedAtMs || 0);
        this.cimState.lastPortCimAtMs = Number(parsed.cimMeta.lastPortCimAtMs || 0);
        this.cimState.lastWarpCimAtMs = Number(parsed.cimMeta.lastWarpCimAtMs || 0);
        this.cimState.nextMode = parsed.cimMeta.nextMode === "warps" ? "warps" : "ports";
      }
    } catch (err) {
      this._log(`Intel snapshot load failed: ${err.message || err}`);
    }
  }

  _writeIntelSnapshot(force = false) {
    const now = Date.now();
    const last = this.lastIntelPersistAtMs || 0;
    if (!force && now - last < this.cimCfg.persistEveryMs) {
      return;
    }

    const sectors = Array.from(this.sectorIntel.values())
      .map((intel) => ({
        sector: intel.sector,
        explored: intel.explored,
        portClass: intel.portClass,
        portProfile: intel.portProfile,
        warps: Array.isArray(intel.warps) ? intel.warps : [],
        navHaz: intel.navHaz,
        market: intel.market || {},
        busted: Boolean(intel.busted),
        hostile: Boolean(intel.hostile),
        lastSeenAt: intel.lastSeenAt || 0,
      }))
      .sort((a, b) => a.sector - b.sector);

    const payload = {
      updatedAt: nowIso(),
      host: this.config.connection.host,
      port: this.config.connection.port,
      cimMeta: {
        lastRunAtMs: this.cimState.lastRunAtMs,
        completedAtMs: this.cimState.completedAtMs,
        lastPortCimAtMs: this.cimState.lastPortCimAtMs,
        lastWarpCimAtMs: this.cimState.lastWarpCimAtMs,
        nextMode: this.cimState.nextMode,
      },
      sectors,
      bustedSectors: Array.from(this.bustedSectors).sort((a, b) => a - b),
      stats: {
        totalKnownSectors: sectors.length,
        sectorsWithPort: sectors.filter((s) => Number.isFinite(s.portClass) && s.portClass > 0).length,
        sectorsWithWarps: sectors.filter((s) => Array.isArray(s.warps) && s.warps.length > 0).length,
      },
    };

    fs.writeFileSync(this.intelFilePath, JSON.stringify(payload, null, 2));
    this.lastIntelPersistAtMs = now;
  }

  _sanitize(text) {
    // Remove ANSI escape sequences to keep parser stable.
    return text.replace(/\x1B\[[0-9;?]*[ -/]*[@-~]/g, "");
  }

  _onTextChunk(chunk) {
    const clean = this._sanitize(chunk);
    this.lastRxAtMs = Date.now();
    this._markSupervisorProgress("rx-chunk");
    this._appendLog(`[RX-CHUNK ${nowIso()}] ${clean}`);
    this._setStatus({ phase: "receiving", note: "Received server chunk" });

    // Some telnet prompts are rendered without newlines, so parse chunk-level text too.
    const inline = clean.replace(/\r/g, "\n").split("\n").map((s) => s.trim()).filter(Boolean);
    for (const snippet of inline) {
      this._handleCredentials(snippet);
      const handledTradePrompt = this._handleTradePrompts(snippet);
      if (!handledTradePrompt) {
        this._handleAutoResponses(snippet);
      }
      this._handlePrompts(snippet);
    }
  }

  _onLine(line) {
    const clean = this._sanitize(line).trimEnd();
    if (!clean) {
      return;
    }

    this.lastRxAtMs = Date.now();
    this._markSupervisorProgress("rx-line");

    this._appendLog(`[RX-LINE  ${nowIso()}] ${clean}`);
    this.lastRxLine = clean;
    if (/<StarDock>\s*Where to\?/i.test(clean)) {
      this.lastStarDockPromptLine = clean;
    }
    if (/<StarDock>/i.test(clean)) {
      this.isAtStarDock = true;
    }
    if (this._isInteractiveSubPrompt(clean)) {
      this.lastInteractivePromptMs = Date.now();
    }
    this._setStatus({ phase: "processing", note: clean.slice(0, 160) });
    this._handleAliasFlow(clean);
    this._handleCredentials(clean);
    const handledTradePrompt = this._handleTradePrompts(clean);
    if (!handledTradePrompt) {
      this._handleAutoResponses(clean);
    }
    this._handleAlignmentSelfDestructFeedback(clean);
    this._parseStats(clean);
    this._handleAlignmentLoadPrompts(clean);
    this._handleCimData(clean);
    this._handleRouteFailures(clean);
    this._handleComputedRouteLine(clean);
    this._handleThreatAlerts(clean);
    this._handlePrompts(clean);
  }

  _handleAlignmentLoadPrompts(line) {
    if (this.currentAction !== "alignment-load-colonists") {
      return;
    }

    const now = Date.now();
    const noHoldsPrompt = /How many groups of Colonists do you want to take \(\[0\] empty holds\)/i.test(line);
    const takeLeavePrompt = /Do you wish to \(L\)eave or \(T\)ake Colonists\?/i.test(line);

    if (!noHoldsPrompt && !takeLeavePrompt) {
      return;
    }

    this.alignmentLoadState.blocked = true;
    this.alignmentLoadState.blockedReason = noHoldsPrompt ? "terra-no-empty-holds" : "terra-load-prompt-loop";
    this.alignmentLoadState.attempts = Math.max(
      this.alignmentLoadState.attempts,
      this.alignmentShiftCfg.maxLoadAttemptsBeforeSelfDestruct,
    );
    this.alignmentLoadState.lastBlockedAtMs = now;
  }

  _handleAlignmentSelfDestructFeedback(line) {
    if (!this.alignmentSelfDestructState.awaitingResult) {
      return;
    }

    if (!/Long Range Scan|Relative Density Scan|<Set NavPoint>/i.test(line)) {
      return;
    }

    const now = Date.now();
    this.alignmentSelfDestructState.awaitingResult = false;
    this.alignmentSelfDestructState.noEffectAttempts = this.alignmentShiftCfg.selfDestructNoEffectMaxAttempts;
    this.alignmentSelfDestructState.suppressUntilMs = now + this.alignmentShiftCfg.selfDestructNoEffectBackoffMs;
    this.alignmentSelfDestructState.lastFailureReason = "self-destruct-command-mapped-to-scan";
    this.alignmentCheckState.pending = false;
  }

  _handleRouteFailures(line) {
    const target = this.lastAttemptedWarpTarget;
    if (!Number.isFinite(target) || target <= 0) {
      return;
    }

    if (/no route available to that sector|do you really want to warp there\?/i.test(line)) {
      this._recordWarpFailure(target, "no-route");
      return;
    }

    if (/you do not have enough fuel ore/i.test(line)) {
      this._recordWarpFailure(target, "no-fuel");
      return;
    }

    if (/no locating beam found/i.test(line)) {
      this._recordWarpFailure(target, "no-lock");
      return;
    }

    if (/interdictor generator in this sector holds you fast/i.test(line)) {
      this._recordWarpFailure(target, "igd-lock");
    }

    if (/He arrives shortly|for getting caught your alignment went down by|guards saw you last time/i.test(line)) {
      this._recordEvilBust(this.stats.sector, "steal-busted");
    }

    if (/suddenly you're Busted!|Federal agents have traced your computer/i.test(line)) {
      this._recordEvilBust(this.stats.sector, "rob-busted");
    }
  }

  _recordEvilBust(sector, reason) {
    if (!Number.isFinite(sector) || sector <= 0) {
      return;
    }

    const current = this.evilBustedSectors.get(sector) || {
      count: 0,
      stealCount: 0,
      robCount: 0,
      lastStealAt: 0,
      lastRobAt: 0,
      lastAt: 0,
      lastReason: "",
    };
    const now = Date.now();
    current.count += 1;
    if (/rob/i.test(reason || "")) {
      current.robCount += 1;
      current.lastRobAt = now;
    } else if (/steal/i.test(reason || "")) {
      current.stealCount += 1;
      current.lastStealAt = now;
    }
    current.lastAt = now;
    current.lastReason = reason;
    this.evilBustedSectors.set(sector, current);
  }

  _getEvilBustForgetMs(reason) {
    if (/rob/i.test(reason || "")) {
      return this.evilCfg.bustForgetMsRob;
    }

    if (/steal/i.test(reason || "")) {
      return this.evilCfg.bustForgetMsSteal;
    }

    return this.evilCfg.bustForgetMs;
  }

  _isEvilBustActive(sector, actionType = "any") {
    if (!this.evilCfg.enforceBustBlacklist) {
      return false;
    }

    if (!Number.isFinite(sector) || sector <= 0) {
      return false;
    }

    const entry = this.evilBustedSectors.get(sector);
    if (!entry) {
      return false;
    }

    const now = Date.now();
    const stealActive = Number.isFinite(entry.lastStealAt) && entry.lastStealAt > 0
      ? (now - entry.lastStealAt) <= this.evilCfg.bustForgetMsSteal
      : false;
    const robActive = Number.isFinite(entry.lastRobAt) && entry.lastRobAt > 0
      ? (now - entry.lastRobAt) <= this.evilCfg.bustForgetMsRob
      : false;

    if (!stealActive && !robActive) {
      this.evilBustedSectors.delete(sector);
      return false;
    }

    if (/steal/i.test(actionType)) {
      return stealActive;
    }

    if (/rob/i.test(actionType)) {
      return robActive;
    }

    return stealActive || robActive;
  }

  _recordWarpFailure(targetSector, reason) {
    const current = this.failedWarpTargets.get(targetSector) || {
      count: 0,
      lastReason: "",
      lastAt: 0,
    };
    current.count += 1;
    current.lastReason = reason;
    current.lastAt = Date.now();
    this.failedWarpTargets.set(targetSector, current);

    if (Number.isFinite(this.pendingRouteTarget) && this.pendingRouteTarget === targetSector) {
      this.pendingRouteTarget = null;
    }
    this._setStatus({ phase: "routing", action: "route-failure", note: `Route failure (${reason}) for ${targetSector}` });
  }

  _getWarpFailurePenalty(targetSector) {
    const failure = this.failedWarpTargets.get(targetSector);
    if (!failure) {
      return 0;
    }

    const ageMs = Date.now() - failure.lastAt;
    if (ageMs > this.routeCfg.failedRouteForgetMs) {
      this.failedWarpTargets.delete(targetSector);
      return 0;
    }

    return this.routeCfg.failedRoutePenalty + ((failure.count - 1) * this.routeCfg.failedRoutePenaltyStep);
  }

  _handleComputedRouteLine(line) {
    if (!this.isEscapePod || !Number.isFinite(this.pendingRouteTarget) || this.stats.sector == null) {
      return;
    }

    if (!line.includes(">")) {
      return;
    }

    const hops = [...line.matchAll(/\d+/g)].map((m) => Number(m[0])).filter((n) => Number.isFinite(n));
    if (hops.length < 2) {
      return;
    }

    if (hops[0] !== this.stats.sector) {
      return;
    }

    if (!hops.includes(this.pendingRouteTarget)) {
      return;
    }

    const nextHop = hops[1];
    if (!Number.isFinite(nextHop) || nextHop <= 0) {
      return;
    }

    this.currentAction = "escapepod-follow-route";
    this._setStatus({ phase: "acting", action: this.currentAction, note: `Following computed path to ${this.pendingRouteTarget} via ${nextHop}` });
    this._send(`m${nextHop}*`, "escape-pod shortest-path next-hop");
    this.lastActionAtMs = Date.now();
  }

  _handleAliasFlow(line) {
    if (this.aliasState.complete) {
      return;
    }

    if (/you\s+must\s+now\s+christen\s+your\s+new/i.test(line)) {
      this.aliasState.complete = true;
      return;
    }

    if (new RegExp(`${this.aliasState.current}\s+is\s+what\s+you\s+want\?\s*\(y\/n\)\s*yes`, "i").test(line)) {
      this.aliasState.complete = true;
      return;
    }

    if (/sorry,\s*you\s*cannot\s*use\s*the\s*name/i.test(line)) {
      this.aliasState.current = this._generateAliasCandidate();
      this._send(this.aliasState.current, "alias retry");
      return;
    }

    if (/what\s+alias\s+do\s+you\s+want\s+to\s+use\?\s*$/i.test(line)) {
      if (!this.aliasState.current) {
        this.aliasState.current = this._generateAliasCandidate();
      }
      this._send(this.aliasState.current, "alias prompt");
    }
  }

  _generateAliasCandidate() {
    this.aliasState.attempts += 1;
    const letters = "abcdefghijklmnopqrstuvwxyz";
    const len = randomInt(6, 8);
    let alias = "";
    for (let i = 0; i < len; i += 1) {
      alias += letters[randomInt(0, letters.length - 1)];
    }
    return `vx${alias}`;
  }

  _handleCredentials(line) {
    const creds = this.identity;

    if (this.patterns.characterPrompt && this.patterns.characterPrompt.test(line) && creds.character) {
      this._send(creds.character, "character selection");
      return;
    }

    if (this.patterns.usernamePrompt && this.patterns.usernamePrompt.test(line) && creds.username) {
      this._send(creds.username, "username");
      return;
    }

    if (this.patterns.passwordPrompt && this.patterns.passwordPrompt.test(line) && creds.password) {
      this._send(creds.password, "password", { sensitive: true });
    }
  }

  _isTradeActionActive() {
    const action = String(this.currentAction || "").toLowerCase();
    return action.includes("trade") || action.includes("worldtrade") || action.includes("port");
  }

  _getTradeModelKey(mode, product) {
    const sector = Number(this.stats.sector || 0);
    const holds = Math.max(1, Number(this.stats.holds || 1));
    return `${sector}:${String(mode || "")}:${String(product || "")}::${holds}`;
  }

  _getOrCreateTradeModel(mode, product) {
    const key = this._getTradeModelKey(mode, product);
    let model = this.tradeRuntime.priceModels.get(key);
    if (!model) {
      model = {
        key,
        mode,
        product,
        holds: Math.max(1, Number(this.stats.holds || 1)),
        baselineAsk: null,
        baselineBest: null,
        lastAsk: null,
        lastOffer: null,
      };
      this.tradeRuntime.priceModels.set(key, model);
    }
    return model;
  }

  _computeTradeModelOffer(model, ask) {
    const askNum = Math.max(1, Number(ask || 1));
    const mode = String(model.mode || "buy");
    const step = Math.max(1, Number(model.holds || this.stats.holds || 1));
    const tuning = this._getAdaptiveHaggleTuning();
    const tolerance = this.tradeCfg.driftTolerance;
    const uncertainThreshold = tuning.uncertainThreshold;

    if (!Number.isFinite(model.baselineAsk) || !Number.isFinite(model.baselineBest)) {
      const seeded = mode === "sell"
        ? Math.ceil(askNum * tuning.sellOfferPct)
        : Math.floor(askNum * tuning.buyOfferPct);
      model.baselineAsk = askNum;
      model.baselineBest = Math.max(1, seeded);
      model.lastAsk = askNum;
      return model.baselineBest;
    }

    const deltaAsk = askNum - model.baselineAsk;
    const stepUnits = Math.round(deltaAsk / step);
    const snappedDelta = stepUnits * step;
    const residual = Math.abs(deltaAsk - snappedDelta);

    let target = model.baselineBest;
    if (Math.abs(deltaAsk) > tolerance) {
      target = model.baselineBest + snappedDelta;
      if (tuning.overbidWhenUncertain && residual >= uncertainThreshold && mode === "sell") {
        target += step;
      }
    }

    if (Number.isFinite(model.lastOffer) && Number.isFinite(model.lastAsk)
      && model.lastOffer > target && mode === "sell") {
      const overbid = model.lastOffer - target;
      if (overbid > 0) {
        target += Math.round(overbid * tuning.correctionPct);
      }
    }

    model.lastAsk = askNum;
    return Math.max(1, Math.round(target));
  }

  _recordTradeOutcome(outcome) {
    const key = String(outcome || "").toLowerCase();
    if (key !== "agreed" && key !== "rejected") {
      return;
    }

    this.tradeRuntime.recentOutcomes.push(key);
    const window = this.tradeCfg.adaptiveHaggleWindow;
    if (this.tradeRuntime.recentOutcomes.length > window) {
      this.tradeRuntime.recentOutcomes.splice(0, this.tradeRuntime.recentOutcomes.length - window);
    }
  }

  _getAdaptiveHaggleTuning() {
    const base = {
      buyOfferPct: this.tradeCfg.buyOfferPct,
      sellOfferPct: this.tradeCfg.sellOfferPct,
      uncertainThreshold: this.tradeCfg.uncertainThreshold,
      overbidWhenUncertain: this.tradeCfg.overbidWhenUncertain,
      correctionPct: this.tradeCfg.correctionPct,
      noHaggleMode: false,
      active: false,
      rejectRate: 0,
      samples: 0,
    };

    if (!this.tradeCfg.adaptiveHaggleEnabled) {
      return base;
    }

    const outcomes = this.tradeRuntime.recentOutcomes;
    const samples = outcomes.length;
    if (samples < this.tradeCfg.adaptiveHaggleMinSamples) {
      return { ...base, samples };
    }

    const rejects = outcomes.filter((o) => o === "rejected").length;
    const rejectRate = rejects / Math.max(1, samples);
    const threshold = this.tradeCfg.adaptiveRejectRateThreshold;
    if (rejectRate < threshold) {
      return { ...base, rejectRate, samples };
    }

    // Under heavy rejection pressure, temporarily stop haggling and transact at market.
    if (rejectRate >= Math.min(0.95, threshold + 0.10)) {
      return {
        ...base,
        noHaggleMode: true,
        active: true,
        rejectRate,
        samples,
      };
    }

    const severity = Math.min(1, Math.max(0, (rejectRate - threshold) / Math.max(0.01, 1 - threshold)));
    const softBuy = this.tradeCfg.buyOfferPct + (this.tradeCfg.adaptiveBuyPctMax - this.tradeCfg.buyOfferPct) * severity;
    const softSell = this.tradeCfg.sellOfferPct - (this.tradeCfg.sellOfferPct - this.tradeCfg.adaptiveSellPctMin) * severity;
    return {
      buyOfferPct: Math.min(this.tradeCfg.adaptiveBuyPctMax, Math.max(this.tradeCfg.buyOfferPct, softBuy)),
      sellOfferPct: Math.max(this.tradeCfg.adaptiveSellPctMin, Math.min(this.tradeCfg.sellOfferPct, softSell)),
      uncertainThreshold: this.tradeCfg.uncertainThreshold + Math.round(this.tradeCfg.adaptiveUncertainThresholdBoost * severity),
      overbidWhenUncertain: false,
      correctionPct: Math.max(0.01, this.tradeCfg.correctionPct * this.tradeCfg.adaptiveCorrectionScale),
      active: true,
      rejectRate,
      samples,
    };
  }

  _handleTradePrompts(line) {
    if (!this.tradeCfg.enabled) {
      return false;
    }

    const text = String(line || "");
    const tradeContext = this._isTradeActionActive() || this._isInteractiveSubPrompt(text);
    if (!tradeContext) {
      return false;
    }

    if (/Agreed,\s*[\d,]+\s*units\./i.test(text)) {
      this.tradeRuntime.successfulDeals += 1;
      this.tradeRuntime.localRejectStreak = 0;
      this.tradeRuntime.lastOutcome = "agreed";
      this._recordTradeOutcome("agreed");
      if (Number.isFinite(this.stats.sector)) {
        this.tradeRuntime.noTradeSectors.delete(this.stats.sector);
      }
      this.tradePromptState.haggleRound = 0;
    }

    if (/Nothing\s+in\s+this\s+universe\s+is\s+free|We're\s+not\s+interested\./i.test(text)) {
      this.tradeRuntime.rejectedDeals += 1;
      this.tradeRuntime.localRejectStreak += 1;
      this.tradeRuntime.lastOutcome = "rejected";
      this._recordTradeOutcome("rejected");
    }

    if (/You\s+don't\s+have\s+anything\s+they\s+want,\s+and\s+they\s+don't\s+have\s+anything\s+you\s+can\s+buy\./i.test(text)) {
      this.tradeRuntime.noTradeDeals += 1;
      this.tradeRuntime.lastOutcome = "no-trade-opportunity";
      this.tradeRuntime.localRejectStreak = Math.max(this.tradeRuntime.localRejectStreak, this.tradeCfg.moveOnRejectStreak);
      const sector = Number(this.stats.sector);
      if (Number.isFinite(sector) && sector > 0) {
        this.tradeRuntime.noTradeSectors.set(sector, Date.now());
      }
    }

    const qtyPrompt = text.match(/How\s+many\s+holds\s+of\s+(Fuel Ore|Organics|Equipment)\s+do\s+you\s+want\s+to\s+(buy|sell)\s*\[([\d,]+)\]\?/i);
    if (qtyPrompt) {
      const product = String(qtyPrompt[1] || "").toLowerCase();
      const mode = String(qtyPrompt[2] || "").toLowerCase();
      const suggested = Number(String(qtyPrompt[3] || "0").replace(/,/g, ""));
      const promptSig = `qty:${mode}:${product}:${suggested}`;
      const now = Date.now();
      if (this.tradePromptState.lastPromptSig === promptSig && now - this.tradePromptState.lastHandledAtMs < 1200) {
        return true;
      }

      let amount = Number.isFinite(suggested) ? suggested : 0;
      if (mode === "buy") {
        const empty = Number(this.stats.emptyHolds);
        if (Number.isFinite(empty)) {
          amount = Math.min(amount, Math.max(0, empty));
        }
      } else {
        const cargo = Number(this.stats.cargoHolds);
        if (Number.isFinite(cargo)) {
          amount = Math.min(amount, Math.max(0, cargo));
        }
      }

      this.tradePromptState.mode = mode;
      this.tradePromptState.product = product;
      this.tradePromptState.lastPromptSig = promptSig;
      this.tradePromptState.lastHandledAtMs = now;
      this.tradePromptState.lastModelKey = this._getTradeModelKey(mode, product);
      this._send(String(Math.max(0, amount)), `trade qty (${product}/${mode})`);
      return true;
    }

    const buyQuote = text.match(/We'll\s+sell\s+them\s+for\s*([\d,]+)\s*credits/i);
    if (buyQuote) {
      this.tradePromptState.mode = "buy";
      this.tradePromptState.lastAskPrice = Number(String(buyQuote[1]).replace(/,/g, ""));
      this.tradePromptState.haggleRound = 0;
    }

    const sellQuote = text.match(/We'll\s+buy\s+them\s+for\s*([\d,]+)\s*credits/i);
    if (sellQuote) {
      this.tradePromptState.mode = "sell";
      this.tradePromptState.lastAskPrice = Number(String(sellQuote[1]).replace(/,/g, ""));
      this.tradePromptState.haggleRound = 0;
    }

    const offerPrompt = text.match(/Your\s+offer\s*\[([\d,]+)\]\s*\?/i);
    if (offerPrompt) {
      const ask = Number(String(offerPrompt[1] || "0").replace(/,/g, ""));
      const mode = this.tradePromptState.mode || "buy";
      const product = this.tradePromptState.product || "";
      const isFinalOffer = /Our\s+final\s+offer/i.test(text);
      const tuning = this._getAdaptiveHaggleTuning();
      const totalDeals = Number(this.tradeRuntime.successfulDeals || 0) + Number(this.tradeRuntime.rejectedDeals || 0);
      const warmupNoHaggle = totalDeals < this.tradeCfg.initialNoHaggleDeals;
      let offer = ask;
      const model = this._getOrCreateTradeModel(mode, product);
      const rejectPressure = warmupNoHaggle || Number(this.tradeRuntime.localRejectStreak || 0) > 0 || tuning.noHaggleMode;
      if (!rejectPressure && !isFinalOffer && this.tradePromptState.haggleRound < this.tradeCfg.maxHaggleRounds) {
        if (this.tradeCfg.enableHoldStepModel) {
          offer = this._computeTradeModelOffer(model, ask);
        } else if (mode === "buy") {
          offer = Math.max(1, Math.floor(ask * tuning.buyOfferPct));
        } else {
          offer = Math.max(1, Math.ceil(ask * tuning.sellOfferPct));
        }
      } else {
        offer = ask;
      }

      if (!Number.isFinite(offer) || offer <= 0) {
        offer = ask;
      }

      this.tradePromptState.haggleRound += 1;
      this.tradeRuntime.lastOfferSent = offer;
      this.tradeRuntime.lastOfferAtMs = Date.now();
      this.tradeRuntime.lastModelOffer = offer;
      this.tradeRuntime.lastModelMode = mode;
      model.lastOffer = offer;
      this._send(String(offer), `trade offer (${mode})`);
      return true;
    }

    return false;
  }

  _handleAutoResponses(line) {
    const now = Date.now();
    for (const rule of this.autoResponses) {
      if (this.currentAction === "alignment-self-destruct"
        && String(rule.match || "").toLowerCase().includes("computer\\s+command")) {
        continue;
      }

      if (rule.once && rule.used) {
        continue;
      }

      if (rule.onlyWhenAtStarDock && !this.isAtStarDock) {
        continue;
      }

      if (rule.onlyWhenEscapePod && !this.isEscapePod) {
        continue;
      }

      if (rule.skipWhenCimActive && this.cimState.active) {
        continue;
      }

      if (now - rule.lastSentAtMs < rule.minIntervalMs) {
        continue;
      }

      if (rule.match && rule.match.test(line)) {
        const renderedReply = this._renderTemplate(rule.reply);
        this._send(renderedReply, `auto-response (${rule.match})`, { allowEmpty: rule.allowEmpty, sensitive: rule.sensitive });
        rule.used = true;
        rule.lastSentAtMs = now;
        break;
      }
    }
  }

  _handlePrompts(line) {
    const compactCommandPrompt = /^\[\d+\]\s*\(\?=Help\)\?\s*:/i.test(String(line || "").trim());
    const isCommandPrompt = (this.patterns.commandPrompt && this.patterns.commandPrompt.test(line)) || compactCommandPrompt;
    const isComputerPrompt = /^\s*computer\s+command\s*\[/i.test(String(line || "").trim());

    if (isCommandPrompt) {
      if (isComputerPrompt) {
        this.lastCommandPromptMs = 0;
        this.lastInteractivePromptMs = Date.now();
        return;
      }

      if (!this._isStableCommandPrompt(line)) {
        return;
      }

      this.lastCommandPromptMs = Date.now();
      this.lastPromptLine = line;

      // Defer action execution briefly after interactive sub-prompts,
      // but keep the command prompt latched so _tick can act next pass.
      if (Date.now() - this.lastInteractivePromptMs < 2000) {
        return;
      }

      this._executeNextAction();
      return;
    }

    if (this.patterns.sectorPrompt && this.patterns.sectorPrompt.test(line)) {
      // Sector lines are informational; keep parsing state but don't issue actions here.
      this.lastSectorLine = line;
    }
  }

  _parseStats(line) {
    const cargoStatus = line.match(/You\s+have\s+([\d,]+)\s+credits\s+and\s+([\d,]+)\s+empty\s+cargo\s+holds?/i);
    if (cargoStatus) {
      this.stats.credits = Number(cargoStatus[1].replace(/,/g, ""));
      this._trackRecoveryCredits(this.stats.credits);
      this.stats.emptyHolds = Number(cargoStatus[2].replace(/,/g, ""));
      if (Number.isFinite(this.stats.holds)) {
        this.stats.cargoHolds = Math.max(0, this.stats.holds - this.stats.emptyHolds);
      }
    }

    const turns = line.match(/\bTL\s*=\s*(\d+)\b/i);
    if (turns) {
      this.stats.turns = Number(turns[1]);
    }

    const credits = line.match(/\bCreds\b[^\d]*([\d,]+)/i);
    if (credits) {
      this.stats.credits = Number(credits[1].replace(/,/g, ""));
      this._trackRecoveryCredits(this.stats.credits);
    }

    const creditsLong = line.match(/^\s*Credits\s*:\s*([\d,]+)/i);
    if (creditsLong) {
      this.stats.credits = Number(creditsLong[1].replace(/,/g, ""));
      this._trackRecoveryCredits(this.stats.credits);
    }

    const exp = line.match(/\bExp\b[^\d]*([\d,]+)/i);
    if (exp) {
      this.stats.exp = Number(exp[1].replace(/,/g, ""));
    }

    const align = line.match(/\b(?:Align|Alignment)\b[^\d-]*(-?[\d,]+)/i);
    if (align) {
      this.stats.alignment = Number(align[1].replace(/,/g, ""));
      if (Number.isFinite(this.stats.alignment)) {
        if (this.alignmentSelfDestructState.awaitingResult) {
          const prior = this.alignmentSelfDestructState.lastAttemptAlignment;
          this.alignmentSelfDestructState.awaitingResult = false;
          if (Number.isFinite(prior) && this.stats.alignment < prior) {
            this.alignmentSelfDestructState.noEffectAttempts = 0;
            this.alignmentSelfDestructState.suppressUntilMs = 0;
            this.alignmentSelfDestructState.lastFailureReason = "";
          } else {
            this.alignmentSelfDestructState.noEffectAttempts += 1;
            this.alignmentSelfDestructState.lastFailureReason = "no-alignment-change-after-self-destruct";
            if (this.alignmentSelfDestructState.noEffectAttempts >= this.alignmentShiftCfg.selfDestructNoEffectMaxAttempts) {
              this.alignmentSelfDestructState.suppressUntilMs = Date.now() + this.alignmentShiftCfg.selfDestructNoEffectBackoffMs;
              this.alignmentCheckState.pending = false;
            }
          }
        }

        if (Number.isFinite(this.alignmentJettisonState.lastAttemptAlignment)
          && this.stats.alignment < this.alignmentJettisonState.lastAttemptAlignment) {
          this.alignmentJettisonState.attempts = 0;
        }
        this.alignmentCheckState.lastObservedAlignment = this.stats.alignment;
        if (this.alignmentCheckState.pending) {
          this.alignmentCheckState.pending = false;
        }
      }
    }

    const colonists = line.match(/\bColonists\b[^\d]*([\d,]+)/i);
    if (colonists) {
      this.stats.colonists = Number(colonists[1].replace(/,/g, ""));
      if (Number.isFinite(this.stats.colonists)) {
        const prevColonists = this.alignmentLoadState.lastColonists;
        if (Number.isFinite(prevColonists) && this.stats.colonists > prevColonists) {
          this.alignmentLoadState.attempts = 0;
        }
        if (this.stats.colonists > 0) {
          this.alignmentLoadState.attempts = 0;
        }
        this.alignmentLoadState.lastColonists = this.stats.colonists;
      }
    }

    const alignDown = line.match(/alignment\s+went\s+down\s+by\s+([\d,]+)/i);
    if (alignDown && Number.isFinite(this.stats.alignment)) {
      const delta = Number(alignDown[1].replace(/,/g, ""));
      if (Number.isFinite(delta)) {
        this.stats.alignment -= delta;
      }
    }

    const holds = line.match(/\bHlds?\b[^\d]*([\d,]+)/i);
    if (holds) {
      this.stats.holds = Number(holds[1].replace(/,/g, ""));
      if (Number.isFinite(this.stats.emptyHolds)) {
        this.stats.cargoHolds = Math.max(0, this.stats.holds - this.stats.emptyHolds);
      }
    }

    const infoHolds = line.match(/^\s*Total\s+Holds\s*:\s*([\d,]+)\s*-\s*Empty\s*=\s*([\d,]+)/i);
    if (infoHolds) {
      this.stats.holds = Number(infoHolds[1].replace(/,/g, ""));
      this.stats.emptyHolds = Number(infoHolds[2].replace(/,/g, ""));
      if (Number.isFinite(this.stats.holds) && Number.isFinite(this.stats.emptyHolds)) {
        this.stats.cargoHolds = Math.max(0, this.stats.holds - this.stats.emptyHolds);
      }
    }

    const infoSector = line.match(/^\s*Current\s+Sector\s*:\s*(\d+)/i);
    if (infoSector) {
      const nextSector = Number(infoSector[1]);
      if (Number.isFinite(nextSector) && nextSector > 0) {
        if (this.stats.sector != null && this.stats.sector !== nextSector) {
          this.previousSector = this.stats.sector;
        }
        this.stats.sector = nextSector;
        this._recordRecentSector(nextSector);
      }
    }

    const shipId = line.match(/\bShip\b[^\d]*([\d,]+)/i);
    if (shipId) {
      this.stats.shipId = Number(shipId[1].replace(/,/g, ""));
    }

    const portEquip = line.match(/\bEquipment\b\s+Buying\s+([\d,]+)/i);
    if (portEquip) {
      this.stats.portEquipBuying = Number(portEquip[1].replace(/,/g, ""));
    }

    const productLine = line.match(/^\s*(Fuel Ore|Organics|Equipment)\s+(Selling|Buying)\s+([\d,]+)\s+(\d+)%/i);
    if (productLine && this.stats.sector != null) {
      const product = productLine[1].toLowerCase().startsWith("fuel")
        ? "fuel"
        : productLine[1].toLowerCase().startsWith("organ")
          ? "organics"
          : "equipment";
      const amount = Number(String(productLine[3]).replace(/,/g, ""));
      const percent = Number(productLine[4]);
      const status = String(productLine[2]).toLowerCase();
      const intel = this.sectorIntel.get(this.stats.sector);
      const market = {
        ...(intel && intel.market ? intel.market : {}),
        [product]: {
          status,
          amount,
          percent,
          seenAt: Date.now(),
        },
      };
      this._upsertSectorIntel(this.stats.sector, { market });
    }

    if (Number.isFinite(this.stats.credits) && this.tradeRuntime.baselineCredits == null && this.stats.credits > 0) {
      this.tradeRuntime.baselineCredits = this.stats.credits;
    }

    this._trackTradeProfitProgress(this.stats.credits);

    if (Number.isFinite(this.stats.exp) && this.tradeRuntime.baselineExp == null) {
      this.tradeRuntime.baselineExp = this.stats.exp;
    }

    const sector = line.match(/\bSector\s*[:#=]\s*(\d+)\b/i);
    if (sector) {
      const nextSector = Number(sector[1]);
      if (this.stats.sector != null && this.stats.sector !== Number(sector[1])) {
        this.previousSector = this.stats.sector;
      }
      this.stats.sector = nextSector;
      this.isAtStarDock = Number.isFinite(this.stardockSector) && nextSector === this.stardockSector;
      if (Number.isFinite(this.pendingRouteTarget) && nextSector === this.pendingRouteTarget) {
        this.pendingRouteTarget = null;
      }
      // Reset per-sector state so no-port sectors cannot reuse previous port intel.
      this.stats.portClass = null;
      this.stats.portProfile = "";
      this.stats.warps = [];
      this._recordRecentSector(nextSector);

      const explored = /\bunexplored\b/i.test(line) ? false : null;
      this.stats.explored = explored;
      this._upsertSectorIntel(nextSector, { explored });
    }

    const portClassMatch = line.match(/\bPorts\s*:\s*.+\bClass\s+(\d+)\b/i);
    if (portClassMatch) {
      const portClass = Number(portClassMatch[1]);
      const profileMatch = line.match(/\(([SB]{3})\)/i);
      const portProfile = String(profileMatch ? profileMatch[1] : "").toUpperCase();
      this.stats.portClass = portClass;
      this.stats.portProfile = portProfile;
      if (portClass === 9 || /\(StarDock\)/i.test(line)) {
        this.isAtStarDock = true;
        if (this.stats.sector != null) {
          this.stardockSector = this.stats.sector;
        }
      }
      if (this.stats.sector != null) {
        this._upsertSectorIntel(this.stats.sector, { portClass, portProfile });
      }
    }

    if (/Warps\s+to\s+Sector/i.test(line)) {
      const warpNumbers = [...line.matchAll(/\b(\d+)\b/g)]
        .map((m) => Number(m[1]))
        .filter((n) => Number.isFinite(n) && n > 0 && n !== this.stats.sector);
      if (warpNumbers.length > 0) {
        this.stats.warps = [...new Set(warpNumbers)];
        if (this.stats.sector != null) {
          this._upsertSectorIntel(this.stats.sector, { warps: this.stats.warps });
        }
      }
    }

    if (/\bBusted!\b/i.test(line) && this.stats.sector != null) {
      this.bustedSectors.add(this.stats.sector);
      this._upsertSectorIntel(this.stats.sector, { busted: true });
    }

    if (/for getting caught your alignment went down by|guards saw you last time/i.test(line) && this.stats.sector != null) {
      this.bustedSectors.add(this.stats.sector);
      this._upsertSectorIntel(this.stats.sector, { busted: true });
    }

    const navHaz = line.match(/\bNavHaz\b\s*:\s*(\d+)%/i);
    if (navHaz && this.stats.sector != null) {
      this._upsertSectorIntel(this.stats.sector, { navHaz: Number(navHaz[1]) });
    }

    const shields = line.match(/\bShlds?\b[^\d]*([\d,]+)/i);
    if (shields) {
      this.stats.shields = Number(shields[1].replace(/,/g, ""));
    }

    const scanner = line.match(/LongRange\s+Scan\s*:\s*(.+)$/i);
    if (scanner) {
      this.stats.scannerType = String(scanner[1]).trim();
    }

    const stardock = line.match(/The\s+StarDock\s+is\s+located\s+in\s+sector\s+(\d+)\.?/i);
    if (stardock) {
      this.stardockSector = Number(stardock[1]);
    }

    const escPod = line.match(/\bShip\b[^\n]*\bEscPod\b/i);
    if (escPod) {
      this.isEscapePod = true;
    } else if (/\bShip\b[^\n]*\b(Scout|Merchant|Transport|Cruiser|Interdictor|Fury|Marauder|Frigate)\b/i.test(line)) {
      this.isEscapePod = false;
    }

    if (/You\s+fled\s+in\s+your\s+Escape\s+Pod/i.test(line)) {
      this.isEscapePod = true;
    }

    const fuelHolds = line.match(/Fuel\s*Ore\s*=\s*([\d,]+)/i);
    if (fuelHolds) {
      this.stats.fuelHolds = Number(String(fuelHolds[1]).replace(/,/g, ""));
    }

    const organicHolds = line.match(/Organics\s*=\s*([\d,]+)/i);
    if (organicHolds) {
      this.stats.organicsHolds = Number(String(organicHolds[1]).replace(/,/g, ""));
    }

    const equipmentHolds = line.match(/Equipment\s*=\s*([\d,]+)/i);
    if (equipmentHolds) {
      this.stats.equipmentHolds = Number(String(equipmentHolds[1]).replace(/,/g, ""));
    }

    if (Number.isFinite(this.stats.holds)) {
      const sumCargo = Math.max(0, Number(this.stats.fuelHolds || 0))
        + Math.max(0, Number(this.stats.organicsHolds || 0))
        + Math.max(0, Number(this.stats.equipmentHolds || 0));
      if (sumCargo > 0) {
        this.stats.cargoHolds = Math.min(this.stats.holds, sumCargo);
        this.stats.emptyHolds = Math.max(0, this.stats.holds - this.stats.cargoHolds);
      }
    }
  }

  _handleCimData(line) {
    if (!line) {
      return;
    }

    if (this.cimState.active && this.cimCfg.completionPattern && this.cimCfg.completionPattern.test(line)) {
      this._markCimComplete("completion-marker");
      return;
    }

    // Parse passively even when a sync is not currently active.
    const portParsed = this._parseCimPortLine(line);
    if (portParsed) {
      if (this.cimState.active) {
        this.cimState.parsedPortLines += 1;
        this.cimState.updatedPortSectors += 1;
        this.cimState.runSawPortData = true;
      }
      return;
    }

    const warpParsed = this._parseCimWarpLine(line);
    if (warpParsed) {
      if (this.cimState.active) {
        this.cimState.parsedWarpLines += 1;
        this.cimState.updatedWarpSectors += 1;
        this.cimState.runSawWarpData = true;
      }
    }
  }

  _parseCimPortLine(line) {
    const profileInParen = line.match(/^\s*(\d{1,6})\s*\(([SB]{3})\)/i);
    const profilePlain = line.match(/^\s*(\d{1,6})\s+([SB]{3})\b/i);
    const pick = profileInParen || profilePlain;
    if (!pick) {
      return false;
    }

    const sector = Number(pick[1]);
    const portProfile = String(pick[2] || "").toUpperCase();
    if (!Number.isFinite(sector) || portProfile.length !== 3) {
      return false;
    }

    const classMatch = line.match(/\bclass\s+(\d+)\b/i);
    const portClass = classMatch ? Number(classMatch[1]) : this._derivePortClassFromProfile(portProfile);
    this._upsertSectorIntel(sector, {
      portProfile,
      portClass,
      explored: true,
    });
    return true;
  }

  _parseCimWarpLine(line) {
    if (/Warps\s+to\s+Sector\(s\)/i.test(line)) {
      const pivot = line.search(/Warps\s+to\s+Sector\(s\)/i);
      const before = line.slice(0, pivot);
      const after = line.slice(pivot);
      const src = before.match(/(\d+)\D*$/);
      if (!src) {
        return false;
      }
      const sector = Number(src[1]);
      const warps = [...after.matchAll(/\b(\d+)\b/g)]
        .map((m) => Number(m[1]))
        .filter((n) => Number.isFinite(n) && n > 0 && n !== sector);
      if (!Number.isFinite(sector) || warps.length === 0) {
        return false;
      }
      this._upsertSectorIntel(sector, { warps, explored: true });
      return true;
    }

    // Fallback parser for terse map lines like: 1234: 44 55 66
    const terse = line.match(/^\s*(\d{1,6})\s*[:>-]\s*((?:\d+[\s,-]+){1,8}\d+)\s*$/);
    if (!terse) {
      return false;
    }
    const sector = Number(terse[1]);
    const warps = [...String(terse[2]).matchAll(/\b(\d+)\b/g)]
      .map((m) => Number(m[1]))
      .filter((n) => Number.isFinite(n) && n > 0 && n !== sector);
    if (!Number.isFinite(sector) || warps.length === 0) {
      return false;
    }
    this._upsertSectorIntel(sector, { warps, explored: true });
    return true;
  }

  _derivePortClassFromProfile(profile) {
    const p = String(profile || "").toUpperCase();
    const map = {
      BBS: 1,
      BSB: 2,
      SBB: 3,
      SSB: 4,
      SBS: 5,
      BSS: 6,
      SSS: 7,
      BBB: 8,
    };
    return map[p] || null;
  }

  _startCimSync(mode) {
    const selectedMode = mode === "warps" ? "warps" : "ports";
    const rawCommand = selectedMode === "warps" ? this.cimCfg.warpCommand : this.cimCfg.portCommand;
    const command = this._normalizeCimCommand(rawCommand);
    if (!command.trim()) {
      return false;
    }

    this.cimState.active = true;
    this.cimState.mode = selectedMode;
    this.cimState.startedAtMs = Date.now();
    this.cimState.lastRunAtMs = Date.now();
    this.cimState.parsedPortLines = 0;
    this.cimState.parsedWarpLines = 0;
    this.cimState.updatedPortSectors = 0;
    this.cimState.updatedWarpSectors = 0;
    this.cimState.runSawPortData = false;
    this.cimState.runSawWarpData = false;
    this.cimState.lastSummary = `running-${selectedMode}`;

    this.currentAction = `cim-${selectedMode}`;
    this._setStatus({ phase: "sync", action: this.currentAction, note: `Starting CIM ${selectedMode} sync` });
    this._publishEvent("cim.start", { mode: selectedMode });
    this._send(command, `cim-${selectedMode}`);
    return true;
  }

  _normalizeCimCommand(command) {
    return String(command || "")
      .replace(/\^/g, "c")
      .replace(/\s+/g, "")
      .trim();
  }

  _markCimComplete(reason) {
    const now = Date.now();
    const duration = this.cimState.startedAtMs > 0 ? now - this.cimState.startedAtMs : 0;
    const summary = `done-${this.cimState.mode}:${this.cimState.parsedPortLines}p/${this.cimState.parsedWarpLines}w in ${duration}ms (${reason})`;
    this.cimState.active = false;
    this.cimState.completedAtMs = now;

    if (this.cimState.mode === "ports" && this.cimState.runSawPortData) {
      this.cimState.lastPortCimAtMs = now;
    }

    if (this.cimState.mode === "warps" && this.cimState.runSawWarpData) {
      this.cimState.lastWarpCimAtMs = now;
    }

    this.cimState.lastSummary = summary;
    this._writeIntelSnapshot();
    this._setStatus({ phase: "sync", action: "cim-complete", note: summary });
    this._publishEvent("cim.complete", {
      mode: this.cimState.mode,
      reason,
      parsedPorts: this.cimState.parsedPortLines,
      parsedWarps: this.cimState.parsedWarpLines,
    });

    if (this.cimCfg.includeWarpData) {
      this.cimState.nextMode = this.cimState.mode === "ports" ? "warps" : "ports";
    } else {
      this.cimState.nextMode = "ports";
    }
  }

  _getIntelFreshness(now = Date.now()) {
    const sectors = Array.from(this.sectorIntel.values());
    const sectorsWithPortIntel = sectors.filter((s) => typeof s.portProfile === "string" && s.portProfile.length === 3).length;
    const sectorsWithWarpIntel = sectors.filter((s) => Array.isArray(s.warps) && s.warps.length > 0).length;

    const missingPortCim = this.cimState.lastPortCimAtMs <= 0;
    const missingWarpCim = this.cimCfg.includeWarpData && this.cimState.lastWarpCimAtMs <= 0;
    const missingCimData = missingPortCim || missingWarpCim;

    const portStale = missingPortCim || (now - this.cimState.lastPortCimAtMs >= this.cimCfg.portFreshMs);
    const warpStale = this.cimCfg.includeWarpData
      ? (missingWarpCim || (now - this.cimState.lastWarpCimAtMs >= this.cimCfg.warpFreshMs))
      : false;

    return {
      sectorsWithPortIntel,
      sectorsWithWarpIntel,
      missingCimData,
      portStale,
      warpStale,
      stale: portStale || warpStale,
    };
  }

  _pickNextCimMode(now = Date.now()) {
    const freshness = this._getIntelFreshness(now);
    if (freshness.portStale) {
      return "ports";
    }
    if (this.cimCfg.includeWarpData && freshness.warpStale) {
      return "warps";
    }
    return this.cimState.nextMode || "ports";
  }

  _countKnownFuelPairs(limit = 200) {
    let count = 0;
    const seen = new Set();
    for (const [sector, intel] of this.sectorIntel.entries()) {
      if (!intel || !intel.portProfile || intel.portProfile.length !== 3) {
        continue;
      }

      const warps = Array.isArray(intel.warps) ? intel.warps : [];
      for (const warp of warps) {
        if (!Number.isFinite(warp) || warp <= 0) {
          continue;
        }

        const key = sector < warp ? `${sector}-${warp}` : `${warp}-${sector}`;
        if (seen.has(key)) {
          continue;
        }
        seen.add(key);

        const other = this.sectorIntel.get(warp);
        if (!other || !other.portProfile || other.portProfile.length !== 3) {
          continue;
        }

        if ([0, 9].includes(Number(intel.portClass)) || [0, 9].includes(Number(other.portClass))) {
          continue;
        }

        if (!this._isCandidatePairSafe(intel, other, warp)) {
          continue;
        }

        const summary = this._summarizePairProfiles(intel.portProfile, other.portProfile);
        if (summary.valid && summary.fuelComplement) {
          count += 1;
          if (count >= limit) {
            return count;
          }
        }
      }
    }
    return count;
  }

  _isCimSafeNow(now = Date.now()) {
    if (this.isAtStarDock) {
      return false;
    }

    if (this.cimCfg.skipInEscapePod && this.isEscapePod) {
      return false;
    }

    if (this.cimCfg.requireSafeSector && this.stats.sector != null) {
      const intel = this.sectorIntel.get(this.stats.sector);
      if ((intel && intel.hostile) || (intel && intel.busted) || this.bustedSectors.has(this.stats.sector)) {
        return false;
      }

      if (intel && Number.isFinite(intel.navHaz) && intel.navHaz > this.cimCfg.maxNavHazForCim) {
        return false;
      }
    }

    if (this.cimCfg.deferAfterThreatMs > 0 && this.lastThreatSignalAtMs > 0 && now - this.lastThreatSignalAtMs < this.cimCfg.deferAfterThreatMs) {
      return false;
    }

    return true;
  }

  _getCimRunGuards(now = Date.now()) {
    const localFuelPairs = this._getFuelPairCandidates(1).length;
    const knownFuelPairs = this._countKnownFuelPairs(300);
    const pairExhausted = localFuelPairs <= 0 && knownFuelPairs <= this.cimCfg.knownPairThreshold;
    return {
      localFuelPairs,
      knownFuelPairs,
      pairExhausted,
      safeNow: this._isCimSafeNow(now),
    };
  }

  _maybeRunCimSync(now) {
    if (!this.cimCfg.enabled) {
      return false;
    }

    if (this.cimState.active) {
      if (now - this.cimState.startedAtMs > this.cimCfg.timeoutMs) {
        this._markCimComplete("timeout");
      }
      return false;
    }

    const turns = Number(this.stats.turns || 0);
    const credits = Number(this.stats.credits || 0);
    if (turns < this.cimCfg.minTurns || credits < this.cimCfg.minCredits) {
      return false;
    }

    const guards = this._getCimRunGuards(now);
    if (!guards.safeNow) {
      return false;
    }

    const freshness = this._getIntelFreshness(now);
    const pairGateAllows = !this.cimCfg.fallbackOnlyWhenPairsExhausted || guards.pairExhausted;
    const bootstrapDue = this.cimCfg.bootstrapIfMissing && freshness.missingCimData && pairGateAllows;
    const staleDue = this.cimCfg.prioritizeStale && freshness.stale && pairGateAllows;
    const intervalDue = now - this.cimState.lastRunAtMs >= this.cimCfg.intervalMs;

    if (!bootstrapDue && !staleDue) {
      if (turns < this.cimCfg.minTurns || credits < this.cimCfg.minCredits) {
        return false;
      }
    }

    const intervalAllowed = intervalDue && pairGateAllows;
    if (!bootstrapDue && !staleDue && !intervalAllowed) {
      return false;
    }

    const mode = this._pickNextCimMode(now);
    return this._startCimSync(mode);
  }

  _handleThreatAlerts(line) {
    const threatSignal = /(is\s+powering\s+up\s+weapons\s+systems|launches\s+a\s+wave\s+of\s+fighters|is\s+attacking\s+with)/i;
    const hostilePresence = /\bBorg\s*:/i;
    if (!threatSignal.test(line) && !hostilePresence.test(line)) {
      return;
    }

    this.lastThreatSignalAtMs = Date.now();

    if (this.stats.sector != null) {
      this._upsertSectorIntel(this.stats.sector, { hostile: true });
    }

    const now = Date.now();
    if (now - this.lastThreatEvadeAtMs < 2500) {
      return;
    }

    const escapeWarp = this._selectEscapeWarp();
    if (escapeWarp == null) {
      this._setStatus({ phase: "threat", action: "threat-detected", note: "Hostile detected, no warp available" });
      return;
    }

    this.currentAction = "threat-evade";
    this._setStatus({ phase: "threat", action: this.currentAction, note: `Hostile detected, evading to ${escapeWarp}` });
    this._send(`m${escapeWarp}*`, "threat evade move");
    this.lastThreatEvadeAtMs = now;
    this.lastActionAtMs = now;
  }

  _tick() {
    const now = Date.now();
    const commandPromptFreshMs = Math.max(2500, this.minActionDelayMs + 1500);
    const hasFreshCommandPrompt = this.lastCommandPromptMs > 0 && (now - this.lastCommandPromptMs) <= commandPromptFreshMs;
    const inStarDockSubmenu = /<StarDock>\s*Where to\?/i.test(this.lastRxLine || this.lastStarDockPromptLine || "");
    this._writeStatus();

    if (this.cimState.active) {
      if (now - this.cimState.startedAtMs > this.cimCfg.timeoutMs) {
        this._markCimComplete("tick-timeout");
      }
      return;
    }

    if (this._maybeRecoverFrozenRuntime(now)) {
      return;
    }

    if (this.lastCommandPromptMs === 0 && now - this.lastRxAtMs > this.preCommandNudgeMs && now - this.lastNudgeAtMs > this.preCommandNudgeMs) {
      this.lastNudgeAtMs = now;
      this._setStatus({ phase: "nudging", note: "No prompt detected, sending Enter nudge" });
      this._send("", "pre-command nudge", { allowEmpty: true });
      return;
    }

    if (hasFreshCommandPrompt && now - this.lastActionAtMs > this.minActionDelayMs) {
      this._executeNextAction();
      return;
    }

    if (!inStarDockSubmenu && hasFreshCommandPrompt && now - this.lastActionAtMs > this.maxIdleBeforeKeepAliveMs) {
      this._send(this.keepAliveCommand, "keepalive");
      this.lastActionAtMs = now;
    }
  }

  _executeNextAction() {
    const now = Date.now();
    const inStarDockSubmenu = /<StarDock>\s*Where to\?/i.test(this.lastRxLine || this.lastStarDockPromptLine || "");
    this._updateMovementProgress(now);
    if (now - this.lastActionAtMs < this.minActionDelayMs) {
      return;
    }

    if (this.cimState.active) {
      return;
    }

    if (this.actions.length === 0) {
      this._log("No actions configured. Sending keepalive.");
      this.currentAction = "fallback-keepalive";
      this._setStatus({ phase: "acting", action: this.currentAction, note: "No actions configured" });
      this._send(this.keepAliveCommand, "fallback keepalive");
      this.lastActionAtMs = now;
      return;
    }

    if (!this.startupTuningDone && this.startupTuningSequence) {
      this.currentAction = "startup-tuning";
      this._setStatus({ phase: "acting", action: this.currentAction, note: "Applying startup tuning sequence" });
      this._send(this.startupTuningSequence, "startup tuning");
      this.lastActionAtMs = now;
      this.startupTuningDone = true;
      return;
    }

    if (this._shouldRunRecoveryLiquidation()) {
      this.currentAction = "recovery-liquidation";
      this._setStatus({ phase: "acting", action: this.currentAction, note: "Recovery stalled at low credits; running liquidation sequence" });
      this._send(this.recoveryLiquidationSequence, "recovery liquidation");
      this.lastActionAtMs = now;
      this.recoveryLiquidationDone = true;
      return;
    }

    if (this._maybeRunAlignmentVerification(now, inStarDockSubmenu)) {
      return;
    }

    if (this._maybeRunAlignmentShift(now, inStarDockSubmenu)) {
      return;
    }

    if (this._maybeRunStartupSurvey(now, inStarDockSubmenu)) {
      return;
    }

    if (this._maybeRunCimSync(now)) {
      this.lastActionAtMs = now;
      return;
    }

    if (this._maybeRefreshInfo(now, inStarDockSubmenu)) {
      return;
    }

    if (this._maybeRouteForHoloUpgrade(now, inStarDockSubmenu)) {
      return;
    }

    if (this._shouldForceStuckWarp(now, inStarDockSubmenu)) {
      return;
    }

    if (this._maybeQuestionProfit(now, inStarDockSubmenu)) {
      return;
    }

    const localTradeAction = this.actions.find((a) => a.name === "worldtrade-local-port" && a.enabled);
    if (localTradeAction && this._localPortCanTradeNow() && !inStarDockSubmenu && !this.isEscapePod && !this._isRecoveryMode()) {
      const forcedLocal = this._renderActionCommand(localTradeAction.command, localTradeAction.name).trim();
      if (forcedLocal) {
        this.currentAction = "worldtrade-local-port-priority";
        this._setStatus({ phase: "acting", action: this.currentAction, note: "Immediate local trade opportunity; prioritizing pt" });
        this._send(forcedLocal, "action:worldtrade-local-port:priority");
        localTradeAction.lastRunAtMs = now;
        this.lastActionAtMs = now;
        return;
      }
    }

    const localTradeEligible = localTradeAction ? this._isActionEligible(localTradeAction, now) : false;
    const shouldStickLocalTrade = this._shouldStickToLocalTrade(localTradeEligible, inStarDockSubmenu);

    for (let i = 0; i < this.actions.length; i += 1) {
      const idx = (this.actionIndex + i) % this.actions.length;
      const action = this.actions[idx];
      if (!this._isActionEligible(action, now)) {
        continue;
      }

      if (shouldStickLocalTrade && action.name === "worldtrade-warp-hop") {
        continue;
      }

      const command = this._renderActionCommand(action.command, action.name);
      if (!command.trim()) {
        continue;
      }
      this.currentAction = action.name;
      if (action.name === "escapepod-return-stardock" && Number.isFinite(this.stardockSector)) {
        this.pendingRouteTarget = this.stardockSector;
      }
      this._setStatus({ phase: "acting", action: action.name, note: `Executing action ${action.name}` });
      this._send(command, `action:${action.name}`);

      if (action.name === "worldtrade-local-port") {
        const sector = Number(this.stats.sector);
        if (Number.isFinite(sector) && sector > 0) {
          if (this.tradeRuntime.lastTradeSector !== sector) {
            this.tradeRuntime.lastTradeSector = sector;
            this.tradeRuntime.localPortBurst = 1;
            this.tradeRuntime.localRejectStreak = 0;
          } else {
            this.tradeRuntime.localPortBurst += 1;
          }
        }
      } else if (action.name === "worldtrade-warp-hop") {
        this.tradeRuntime.localPortBurst = 0;
      }

      this.actionIndex = (idx + 1) % this.actions.length;
      this.lastActionAtMs = now;
      action.lastRunAtMs = now;
      if (action.once) {
        action.used = true;
      }
      return;
    }

    this._log("No eligible primary action found. Attempting fallback.");
    if (this._tryRunNoEligibleFallbackAction(now, inStarDockSubmenu)) {
      return;
    }

    this.currentAction = "no-eligible-action";
    if (inStarDockSubmenu) {
      this._setStatus({ phase: "idle", action: this.currentAction, note: "No eligible action at StarDock submenu; holding" });
      this.lastActionAtMs = now;
      return;
    }
    this._log("No eligible fallback available. Sending keepalive.");
    this._setStatus({ phase: "idle", action: this.currentAction, note: "No eligible action; sending keepalive" });
    this._send(this.keepAliveCommand, "no eligible action keepalive");
    this.lastActionAtMs = now;
  }

  _tryRunNoEligibleFallbackAction(now, inStarDockSubmenu) {
    if (inStarDockSubmenu) {
      return false;
    }

    const preferredName = String(this.noEligibleFallbackAction || "").trim() || "worldtrade-warp-hop";
    const fallback = this.actions.find((a) => a.name === preferredName && a.enabled);
    if (!fallback) {
      return false;
    }

    if (fallback.name === "worldtrade-warp-hop" && (this.isEscapePod || this._isRecoveryMode())) {
      return false;
    }

    const command = this._renderActionCommand(fallback.command, fallback.name).trim();
    if (!command) {
      return false;
    }

    this.currentAction = `${fallback.name}-forced`;
    this._setStatus({ phase: "acting", action: this.currentAction, note: "Forced no-eligible fallback action" });
    this._send(command, `action:${fallback.name}:forced`);
    fallback.lastRunAtMs = now;
    this.lastActionAtMs = now;
    if (fallback.name === "worldtrade-warp-hop") {
      this.tradeRuntime.forcedWarpHops += 1;
      this.tradeRuntime.localPortBurst = 0;
    }
    return true;
  }

  _shouldStickToLocalTrade(localTradeEligible, inStarDockSubmenu) {
    if (!this.tradeCfg.enabled || !localTradeEligible) {
      return false;
    }

    if (inStarDockSubmenu || this.isEscapePod || this.isAtStarDock || this._isRecoveryMode()) {
      return false;
    }

    if (!Number.isFinite(this.stats.portClass) || this.stats.portClass <= 0) {
      return false;
    }

    const rejectStreak = Number(this.tradeRuntime.localRejectStreak || 0);
    if (rejectStreak >= this.tradeCfg.moveOnRejectStreak) {
      return false;
    }

    const burst = Number(this.tradeRuntime.localPortBurst || 0);
    return burst < this.tradeCfg.localPortBurstMin;
  }

  _maybeRunStartupSurvey(now, inStarDockSubmenu) {
    if (this.startupSurveyDone || !this.startupSurveyCfg.enabled) {
      return false;
    }

    if (inStarDockSubmenu || this.cimState.active) {
      return false;
    }

    if (now - this.lastActionAtMs < this.startupSurveyCfg.minGapMs) {
      return false;
    }

    while (this.startupSurveyIndex < this.startupSurveyCommands.length) {
      const raw = this.startupSurveyCommands[this.startupSurveyIndex];
      const command = this._renderActionCommand(raw).trim();
      this.startupSurveyIndex += 1;
      if (!command) {
        continue;
      }

      const compact = command.replace(/\s+/g, "").toLowerCase();
      if (compact === "crq" || compact === "ciq") {
        const mode = compact === "ciq" ? "warps" : "ports";
        if (!this._shouldRunStartupCimMode(mode, now)) {
          this._publishEvent("startup.cim-skip", { mode, reason: "below-cimMinAge" });
          continue;
        }

        if (!this._startCimSync(mode)) {
          continue;
        }

        this.currentAction = `startup-survey-cim-${mode}`;
        this._setStatus({ phase: "sync", action: this.currentAction, note: `Startup CIM bootstrap (${mode})` });
        this.lastActionAtMs = now;

        if (this.startupSurveyIndex >= this.startupSurveyCommands.length) {
          this.startupSurveyDone = true;
        }

        return true;
      }

      this.currentAction = `startup-survey-${this.startupSurveyIndex}`;
      this._setStatus({ phase: "acting", action: this.currentAction, note: `Startup intel sweep: ${command}` });
      this._send(command, `startup survey ${this.startupSurveyIndex}`);
      this.lastActionAtMs = now;

      if (this.startupSurveyIndex >= this.startupSurveyCommands.length) {
        this.startupSurveyDone = true;
      }

      return true;
    }

    this.startupSurveyDone = true;
    return false;
  }

  _shouldRunStartupCimMode(mode, now = Date.now()) {
    const minAgeMs = Number(this.startupSurveyCfg.cimMinAgeMs || 0);
    if (!Number.isFinite(minAgeMs) || minAgeMs <= 0) {
      return true;
    }

    const lastAtMs = mode === "warps"
      ? Number(this.cimState.lastWarpCimAtMs || 0)
      : Number(this.cimState.lastPortCimAtMs || 0);

    if (!Number.isFinite(lastAtMs) || lastAtMs <= 0) {
      return false;
    }

    return now - lastAtMs >= minAgeMs;
  }

  _maybeRefreshInfo(now, inStarDockSubmenu) {
    if (!this.infoRefreshCfg.enabled) {
      return false;
    }

    if (this.infoRefreshCfg.skipInStarDockSubmenu && inStarDockSubmenu) {
      return false;
    }

    if (now - this.lastInfoRefreshAtMs < this.infoRefreshCfg.intervalMs) {
      return false;
    }

    if (this.infoRefreshCfg.minGapAfterActionMs > 0 && now - this.lastActionAtMs < this.infoRefreshCfg.minGapAfterActionMs) {
      return false;
    }

    const command = String(this.infoRefreshCfg.command || "").trim();
    if (!command) {
      return false;
    }

    this.currentAction = "info-refresh";
    this._setStatus({ phase: "acting", action: this.currentAction, note: `Refreshing info with ${command}` });
    this._send(command, "periodic info refresh");
    this.lastActionAtMs = now;
    this.lastInfoRefreshAtMs = now;
    return true;
  }

  _renderActionCommand(template, actionName = "") {
    const randomSector = this.randomSectorMin + Math.floor(Math.random() * (this.randomSectorMax - this.randomSectorMin + 1));
    const nextWarp = this._selectNextWarp(actionName);
    const stealAmount = this._computeStealAmount();
    const stealCredits = this._computeStealCredits();
    const upgradeAmount = this._computeUpgradeAmount(stealAmount);
    const withStrategyPlaceholders = Object.entries(this.staticPlaceholders).reduce((acc, [key, value]) => {
      return acc.replaceAll(`{${key}}`, String(value));
    }, String(template));

    const rendered = withStrategyPlaceholders
      .replaceAll("{randomSector}", String(randomSector))
      .replaceAll("{nextWarp}", nextWarp == null ? "0" : String(nextWarp))
      .replaceAll("{stardockSector}", this.stardockSector == null ? "0" : String(this.stardockSector))
      .replaceAll("{stardockUpgradeSequence}", this.stardockUpgradeSequence)
      .replaceAll("{stardockShipBuySequence}", this.stardockShipBuySequence)
      .replaceAll("{sector}", this.stats.sector == null ? "0" : String(this.stats.sector))
      .replaceAll("{sourceSector}", String(this.alignmentShiftCfg.sourceSector))
      .replaceAll("{turns}", this.stats.turns == null ? "0" : String(this.stats.turns))
      .replaceAll("{credits}", this.stats.credits == null ? "0" : String(this.stats.credits))
      .replaceAll("{exp}", this.stats.exp == null ? "0" : String(this.stats.exp))
      .replaceAll("{alignment}", this.stats.alignment == null ? "0" : String(this.stats.alignment))
      .replaceAll("{colonists}", this.stats.colonists == null ? "0" : String(this.stats.colonists))
      .replaceAll("{holds}", this.stats.holds == null ? "0" : String(this.stats.holds))
      .replaceAll("{shipId}", this.stats.shipId == null ? "0" : String(this.stats.shipId))
      .replaceAll("{stealAmount}", String(stealAmount))
        .replaceAll("{stealCredits}", String(stealCredits))
      .replaceAll("{upgradeAmount}", String(upgradeAmount));

    const normalized = this._normalizeTradeActionCommand(rendered, actionName);
    return this._normalizeWarpTargetCommand(normalized, actionName);
  }

  _normalizeWarpTargetCommand(command, actionName = "") {
    const name = String(actionName || "").toLowerCase();
    if (name !== "worldtrade-warp-hop") {
      return command;
    }

    const out = String(command || "");
    const match = out.match(/^m(\d+)\*/i);
    if (!match) {
      return out;
    }

    const target = Number(match[1]);
    const current = Number(this.stats.sector);
    if (!Number.isFinite(current) || !Number.isFinite(target) || target !== current) {
      return out;
    }

    const warps = Array.isArray(this.stats.warps) ? this.stats.warps.filter((n) => Number.isFinite(n) && n > 0 && n !== current) : [];
    if (warps.length === 0) {
      return out;
    }

    const alt = warps
      .map((warp) => ({ warp, score: this._scoreWarpCandidate(warp) }))
      .sort((a, b) => b.score - a.score || a.warp - b.warp)[0]?.warp;
    if (!Number.isFinite(alt) || alt <= 0) {
      return out;
    }

    return out.replace(/^m\d+\*/i, `m${alt}*`);
  }

  _updateMovementProgress(now = Date.now()) {
    const sector = Number(this.stats.sector);
    if (!Number.isFinite(sector) || sector <= 0) {
      return;
    }

    if (!Number.isFinite(this.movementProgress.lastSector) || this.movementProgress.lastSector !== sector) {
      this.movementProgress.lastSector = sector;
      this.movementProgress.lastSectorChangeAtMs = now;
    }
  }

  _shouldForceStuckWarp(now, inStarDockSubmenu) {
    if (!this.tradeCfg.stuckSectorWarpEnabled || inStarDockSubmenu || this.isEscapePod || this._isRecoveryMode()) {
      return false;
    }

    const sector = Number(this.stats.sector);
    if (!Number.isFinite(sector) || sector <= 0) {
      return false;
    }

    if (!Number.isFinite(this.movementProgress.lastSector)) {
      return false;
    }

    if (this.movementProgress.lastSector !== sector) {
      return false;
    }

    const dwellMs = now - this.movementProgress.lastSectorChangeAtMs;
    if (dwellMs < this.tradeCfg.stuckSectorWarpMs) {
      return false;
    }

    const hopAction = this.actions.find((a) => a.name === "worldtrade-warp-hop" && a.enabled);
    if (!hopAction) {
      return false;
    }

    const command = this._renderActionCommand(hopAction.command, hopAction.name).trim();
    if (!command) {
      return false;
    }

    this.currentAction = "worldtrade-warp-hop-stuck-forced";
    this._setStatus({ phase: "acting", action: this.currentAction, note: `Stuck in sector ${sector}; forcing warp-hop` });
    this._send(command, "action:worldtrade-warp-hop:stuck-forced");
    hopAction.lastRunAtMs = now;
    this.lastActionAtMs = now;
    this.tradeRuntime.forcedStuckWarps += 1;
    this.tradeRuntime.localPortBurst = 0;
    return true;
  }

  _normalizeTradeActionCommand(command, actionName = "") {
    const out = String(command || "").trim();
    if (!this.tradeCfg.forcePromptHaggle) {
      return out;
    }

    const name = String(actionName || "").toLowerCase();
    if (name === "worldtrade-local-port" && /^pt\*+/i.test(out)) {
      return "pt";
    }

    if (name === "worldtrade-warp-hop" && this._shouldLayTradeWarpFighters(out)) {
      return `${out}${this.movementSafetyCfg.figDropCommandPrefix}${this.movementSafetyCfg.fightersPerDrop}${this.movementSafetyCfg.figDropCommandSuffix}`;
    }

    return out;
  }

  _needsHoloScanner() {
    const scan = String(this.stats.scannerType || "").toLowerCase();
    return !/holo|holographic/.test(scan);
  }

  _shouldLayTradeWarpFighters(command) {
    if (!this.movementSafetyCfg.enabled || !this.movementSafetyCfg.layFightersOnTradeWarpHop) {
      return false;
    }

    if (this.movementSafetyCfg.fightersPerDrop <= 0) {
      return false;
    }

    const out = String(command || "");
    if (!/^m\d+\*/i.test(out)) {
      return false;
    }

    if (new RegExp(`${this.movementSafetyCfg.figDropCommandPrefix}\\d+`, "i").test(out)) {
      return false;
    }

    const here = this.stats.sector != null ? this.sectorIntel.get(this.stats.sector) : null;
    if (here && (here.hostile || (Number.isFinite(here.navHaz) && here.navHaz > this.movementSafetyCfg.maxNavHazToLay))) {
      return false;
    }

    if (!this.movementSafetyCfg.layOnlyOnGoodPairs) {
      return true;
    }

    return this._isCurrentOrAdjacentGoodPairSector();
  }

  _isCurrentOrAdjacentGoodPairSector() {
    const sector = Number(this.stats.sector);
    if (!Number.isFinite(sector) || sector <= 0) {
      return false;
    }

    if (this._sectorHasGoodPair(sector)) {
      return true;
    }

    if (!this.movementSafetyCfg.layOnAdjacentGoodPairs) {
      return false;
    }

    const warps = Array.isArray(this.stats.warps)
      ? this.stats.warps.filter((n) => Number.isFinite(n) && n > 0)
      : [];
    for (const warp of warps) {
      if (this._sectorHasGoodPair(warp)) {
        return true;
      }
    }

    return false;
  }

  _sectorHasGoodPair(sector) {
    const intel = this.sectorIntel.get(sector);
    if (!intel || !intel.portProfile || intel.portProfile.length !== 3 || [0, 9].includes(Number(intel.portClass))) {
      return false;
    }

    const warps = Array.isArray(intel.warps)
      ? intel.warps.filter((n) => Number.isFinite(n) && n > 0)
      : [];
    for (const warp of warps) {
      const other = this.sectorIntel.get(warp);
      if (!other || !other.portProfile || other.portProfile.length !== 3 || [0, 9].includes(Number(other.portClass))) {
        continue;
      }

      if (!this._isCandidatePairSafe(intel, other, warp)) {
        continue;
      }

      const summary = this._summarizePairProfiles(intel.portProfile, other.portProfile);
      if (!summary.valid || summary.complements <= 0) {
        continue;
      }

      const score = this._scorePreferredTradePair(intel.portProfile, other.portProfile);
      if (score > 0) {
        return true;
      }
    }

    return false;
  }

  _maybeRouteForHoloUpgrade(now, inStarDockSubmenu) {
    if (!this.upgradeCfg.enabled || !this.upgradeCfg.holoScannerPriority) {
      return false;
    }

    if (inStarDockSubmenu || this.isEscapePod || this._isRecoveryMode()) {
      return false;
    }

    if (!this._needsHoloScanner()) {
      return false;
    }

    const credits = Math.max(0, Number(this.stats.credits || 0));
    if (credits < this.upgradeCfg.holoScannerMinCredits) {
      return false;
    }

    if (!Number.isFinite(this.stardockSector) || !Number.isFinite(this.stats.sector) || this.stats.sector === this.stardockSector) {
      return false;
    }

    const hop = this._getNextHopTowardSector(this.stardockSector);
    if (!Number.isFinite(hop) || hop <= 0) {
      return false;
    }

    this.currentAction = "upgrade-route-holo-stardock";
    this._setStatus({ phase: "acting", action: this.currentAction, note: `Routing to StarDock ${this.stardockSector} for holo scanner upgrade` });
    this._send(`m${hop}*`, "holo scanner upgrade route");
    this.lastActionAtMs = now;
    return true;
  }

  _renderTemplate(template) {
    if (template == null) {
      return "";
    }

    const purchasePlan = this._computeStardockPurchasePlan();

    return String(template)
      .replaceAll("{username}", this.identity.username || "")
      .replaceAll("{password}", this.identity.password || "")
      .replaceAll("{character}", this.identity.character || "")
      .replaceAll("{sector}", this.stats.sector == null ? "0" : String(this.stats.sector))
      .replaceAll("{sourceSector}", String(this.alignmentShiftCfg.sourceSector))
      .replaceAll("{turns}", this.stats.turns == null ? "0" : String(this.stats.turns))
      .replaceAll("{credits}", this.stats.credits == null ? "0" : String(this.stats.credits))
      .replaceAll("{exp}", this.stats.exp == null ? "0" : String(this.stats.exp))
      .replaceAll("{alignment}", this.stats.alignment == null ? "0" : String(this.stats.alignment))
      .replaceAll("{colonists}", this.stats.colonists == null ? "0" : String(this.stats.colonists))
      .replaceAll("{holds}", this.stats.holds == null ? "0" : String(this.stats.holds))
        .replaceAll("{stardockBuyShield}", purchasePlan.shieldBuy)
        .replaceAll("{stardockBuyFighters}", purchasePlan.fighterBuy)
      .replaceAll("{stardockBuyScanner}", this.stardockBuyScanner)
      .replaceAll("{stardockBuyShipConfirm}", this.stardockBuyShipConfirm);
  }

  _selectNextWarp(actionName = "") {
    const skipFuelPairShortcut = /evil-recovery-hunt-port/i.test(String(actionName || "")) || this._isRecoveryMode();

    if (!skipFuelPairShortcut) {
      const intentWarp = this._selectTradeIntentWarp(actionName);
      if (Number.isFinite(intentWarp) && intentWarp > 0) {
        return intentWarp;
      }
    }

    const pairCandidates = skipFuelPairShortcut ? [] : this._getPreferredTradePairCandidates(3);
    if (pairCandidates.length > 0) {
      const preferred = pairCandidates[0];
      if (preferred && Number.isFinite(preferred.sector)) {
        return preferred.sector;
      }
    }

    if (!skipFuelPairShortcut) {
      const seekPairHop = this._selectKnownTradePairSeekWarp();
      if (Number.isFinite(seekPairHop) && seekPairHop > 0) {
        return seekPairHop;
      }
    }

    const warps = Array.isArray(this.stats.warps) ? this.stats.warps.filter((n) => Number.isFinite(n) && n > 0) : [];
    if (warps.length === 0) {
      return null;
    }

    const ranked = warps
      .map((warp) => ({
        warp,
        score: this._scoreWarpCandidate(warp),
      }))
      .sort((a, b) => b.score - a.score || a.warp - b.warp);

    const recentTail = this.recentSectors
      .slice(-4)
      .map((n) => Number(n))
      .filter((n) => Number.isFinite(n) && n > 0);
    const recentBlock = new Set(recentTail.slice(-3));
    const nonRecent = ranked.filter((item) => !recentBlock.has(item.warp));
    if (nonRecent.length > 0) {
      return nonRecent[0].warp;
    }

    if (skipFuelPairShortcut && recentTail.length >= 3) {
      const last = recentTail[recentTail.length - 1];
      const middle = recentTail[recentTail.length - 2];
      const third = recentTail[recentTail.length - 3];
      const inTwoSectorLoop = last === third && last !== middle;
      if (inTwoSectorLoop) {
        const breakLoop = ranked.filter((item) => item.warp !== middle);
        if (breakLoop.length > 0) {
          return breakLoop[0].warp;
        }
      }
    }

    const nonBacktrack = ranked.filter((item) => this.previousSector == null || item.warp !== this.previousSector);
    if (nonBacktrack.length > 0) {
      return nonBacktrack[0].warp;
    }

    return ranked[0].warp;
  }

  _getCommodityIndex(key) {
    const map = { fuel: 0, organics: 1, equipment: 2 };
    return Number.isInteger(map[key]) ? map[key] : -1;
  }

  _portBuysCommodity(profile, commodityKey) {
    const idx = this._getCommodityIndex(commodityKey);
    const text = String(profile || "").toUpperCase();
    return idx >= 0 && text.length === 3 && text[idx] === "B";
  }

  _portSellsCommodity(profile, commodityKey) {
    const idx = this._getCommodityIndex(commodityKey);
    const text = String(profile || "").toUpperCase();
    return idx >= 0 && text.length === 3 && text[idx] === "S";
  }

  _getDominantCargoForSale() {
    const cargo = [
      { key: "fuel", amount: Math.max(0, Number(this.stats.fuelHolds || 0)) },
      { key: "organics", amount: Math.max(0, Number(this.stats.organicsHolds || 0)) },
      { key: "equipment", amount: Math.max(0, Number(this.stats.equipmentHolds || 0)) },
    ].sort((a, b) => b.amount - a.amount);

    return cargo[0].amount > 0 ? cargo[0].key : "";
  }

  _selectTradeIntentWarp(actionName = "") {
    if (!this._isTradeActionName(actionName) || !Number.isFinite(this.stats.sector) || this.stats.sector <= 0) {
      return null;
    }

    const currentSector = this.stats.sector;
    const empty = Math.max(0, Number(this.stats.emptyHolds || 0));
    const sellCommodity = this._getDominantCargoForSale();
    const desiredBuyCommodity = this._getDesiredCargo();
    const needSell = Boolean(sellCommodity);
    const needBuy = empty > 0;
    if (!needSell && !needBuy) {
      return null;
    }

    let bestHop = null;
    let bestScore = Number.NEGATIVE_INFINITY;

    for (const [sector, intel] of this.sectorIntel.entries()) {
      if (!Number.isFinite(sector) || sector <= 0 || sector === currentSector) {
        continue;
      }

      if (!intel || !intel.portProfile || intel.portProfile.length !== 3 || [0, 9].includes(Number(intel.portClass))) {
        continue;
      }

      if (intel.busted || this.bustedSectors.has(sector) || intel.hostile) {
        continue;
      }

      if (Number.isFinite(intel.navHaz) && intel.navHaz > 3) {
        continue;
      }

      const canSellHere = needSell && this._portBuysCommodity(intel.portProfile, sellCommodity);
      const canBuyHere = needBuy && this._portSellsCommodity(intel.portProfile, desiredBuyCommodity);
      if (!canSellHere && !canBuyHere) {
        continue;
      }

      const hop = this._getNextHopTowardSector(sector);
      if (!Number.isFinite(hop) || hop <= 0) {
        continue;
      }

      let score = 0;
      if (canSellHere) {
        score += 120;
      }
      if (canBuyHere) {
        score += 90;
      }

      if (needSell && canSellHere && Array.isArray(intel.warps)) {
        const hasNextBuy = intel.warps.some((w) => {
          const other = this.sectorIntel.get(w);
          return other && this._portSellsCommodity(other.portProfile, desiredBuyCommodity);
        });
        if (hasNextBuy) {
          score += 40;
        }
      }

      if (this.previousSector != null && hop === this.previousSector) {
        score -= this.routeCfg.backtrackPenalty;
      }
      if (this.recentSectors.includes(hop)) {
        score -= this.routeCfg.recentSectorPenalty;
      }

      score += this._scoreWarpCandidate(hop, { includeJitter: false }) * 0.2;

      if (score > bestScore) {
        bestScore = score;
        bestHop = hop;
      }
    }

    return Number.isFinite(bestHop) && bestHop > 0 ? bestHop : null;
  }

  _selectKnownTradePairSeekWarp(limitPairs = 200) {
    if (!Number.isFinite(this.stats.sector) || this.stats.sector <= 0) {
      return null;
    }

    const recent = new Set(
      this.recentSectors
        .slice(-6)
        .map((n) => Number(n))
        .filter((n) => Number.isFinite(n) && n > 0),
    );

    const seenPairs = new Set();
    let best = null;
    let bestScore = Number.NEGATIVE_INFINITY;

    for (const [sector, intel] of this.sectorIntel.entries()) {
      if (!intel || !intel.portProfile || intel.portProfile.length !== 3) {
        continue;
      }

      if ([0, 9].includes(Number(intel.portClass))) {
        continue;
      }

      const warps = Array.isArray(intel.warps) ? intel.warps : [];
      for (const warp of warps) {
        if (!Number.isFinite(warp) || warp <= 0) {
          continue;
        }

        const other = this.sectorIntel.get(warp);
        if (!other || !other.portProfile || other.portProfile.length !== 3 || [0, 9].includes(Number(other.portClass))) {
          continue;
        }

        const key = sector < warp ? `${sector}-${warp}` : `${warp}-${sector}`;
        if (seenPairs.has(key)) {
          continue;
        }
        seenPairs.add(key);
        if (seenPairs.size > limitPairs) {
          break;
        }

        const summary = this._summarizePairProfiles(intel.portProfile, other.portProfile);
        if (!summary.valid || summary.complements <= 0) {
          continue;
        }

        if (!this._isCandidatePairSafe(intel, other, warp) || !this._isCandidatePairSafe(other, intel, sector)) {
          continue;
        }

        if (!this._passesFuelMarketDepth(intel, other)) {
          continue;
        }

        const pairScore = this._scorePreferredTradePair(intel.portProfile, other.portProfile);
        if (pairScore <= 0) {
          continue;
        }
        const endpoints = [sector, warp];
        for (const endpoint of endpoints) {
          const hop = endpoint === this.stats.sector ? endpoint : this._getNextHopTowardSector(endpoint);
          if (!Number.isFinite(hop) || hop <= 0) {
            continue;
          }

          let score = pairScore;
          if (hop !== this.stats.sector) {
            score += this._scoreWarpCandidate(hop, { includeJitter: false });
          }

          if (this.previousSector != null && hop === this.previousSector) {
            score -= this.routeCfg.backtrackPenalty;
          }
          if (recent.has(hop)) {
            score -= this.routeCfg.recentSectorPenalty;
          }

          if (score > bestScore) {
            bestScore = score;
            best = hop;
          }
        }
      }
    }

    if (!Number.isFinite(best) || best <= 0 || best === this.stats.sector) {
      return null;
    }

    return best;
  }

  _selectEscapeWarp() {
    const warps = Array.isArray(this.stats.warps) ? this.stats.warps.filter((n) => Number.isFinite(n) && n > 0) : [];
    if (warps.length === 0) {
      return null;
    }

    let best = null;
    let bestScore = Number.NEGATIVE_INFINITY;
    for (const warp of warps) {
      const intel = this.sectorIntel.get(warp);
      let score = this._scoreWarpCandidate(warp) + 30;
      if (intel && intel.hostile) {
        score -= this.routeCfg.hostileSectorPenalty;
      }
      if (this.previousSector != null && warp === this.previousSector) {
        score += 8;
      }
      if (score > bestScore) {
        bestScore = score;
        best = warp;
      }
    }

    return best;
  }

  _getNextHopTowardSector(targetSector) {
    if (!Number.isFinite(targetSector) || targetSector <= 0 || !Number.isFinite(this.stats.sector)) {
      return null;
    }

    const start = this.stats.sector;
    if (start === targetSector) {
      return null;
    }

    const queue = [start];
    const visited = new Set([start]);
    const parent = new Map();

    while (queue.length > 0) {
      const current = queue.shift();
      const intel = this.sectorIntel.get(current);
      const warps = intel && Array.isArray(intel.warps)
        ? intel.warps.filter((n) => Number.isFinite(n) && n > 0)
        : [];

      for (const warp of warps) {
        if (visited.has(warp)) {
          continue;
        }

        visited.add(warp);
        parent.set(warp, current);

        if (warp === targetSector) {
          let node = targetSector;
          while (parent.has(node) && parent.get(node) !== start) {
            node = parent.get(node);
          }
          return Number.isFinite(node) && node > 0 ? node : null;
        }

        queue.push(warp);
      }
    }

    return null;
  }

  _selectAlignmentSeekWarp(targetSector) {
    const routedHop = this._getNextHopTowardSector(targetSector);
    if (Number.isFinite(routedHop) && routedHop > 0) {
      return routedHop;
    }

    const warps = Array.isArray(this.stats.warps)
      ? this.stats.warps.filter((n) => Number.isFinite(n) && n > 0)
      : [];
    if (warps.length === 0) {
      return null;
    }

    const recent = new Set(this.recentSectors.slice(-6));
    const scored = warps.map((warp) => {
      const intel = this.sectorIntel.get(warp);
      let score = 0;

      if (!intel) {
        score += 180;
      } else {
        if (intel.explored === false) {
          score += 120;
        }
        if (!Array.isArray(intel.warps) || intel.warps.length === 0) {
          score += 40;
        }
      }

      if (this.previousSector != null && warp === this.previousSector) {
        score -= 220;
      }

      if (recent.has(warp)) {
        score -= 90;
      }

      score += randomInt(0, 4);
      return { warp, score };
    });

    scored.sort((a, b) => b.score - a.score || a.warp - b.warp);
    return scored[0].warp;
  }

  _upsertSectorIntel(sector, partial) {
    if (!Number.isFinite(sector)) {
      return;
    }

    const current = this.sectorIntel.get(sector) || {
      sector,
      explored: null,
      portClass: null,
      portProfile: "",
      warps: [],
      navHaz: null,
      market: {},
      busted: false,
      hostile: false,
      lastSeenAt: 0,
    };

    const next = {
      ...current,
      ...partial,
      lastSeenAt: Date.now(),
    };

    if (Array.isArray(partial.warps)) {
      next.warps = [...new Set(partial.warps.filter((n) => Number.isFinite(n) && n > 0))];
    }

    this.sectorIntel.set(sector, next);
  }

  _recordRecentSector(sector) {
    if (!Number.isFinite(sector)) {
      return;
    }

    if (this.recentSectors.length === 0 || this.recentSectors[this.recentSectors.length - 1] !== sector) {
      this.recentSectors.push(sector);
      const overshoot = this.recentSectors.length - this.routeCfg.recentMemorySize;
      if (overshoot > 0) {
        this.recentSectors.splice(0, overshoot);
      }
    }
  }

  _scoreWarpCandidate(targetSector, options = {}) {
    const includeJitter = options.includeJitter !== false;
    const intel = this.sectorIntel.get(targetSector);
    const currentIntel = this.stats.sector == null ? null : this.sectorIntel.get(this.stats.sector);
    let score = 0;

    if (!intel) {
      score += this.routeCfg.unknownSectorBonus;
    } else {
      if (intel.explored === false) {
        score += this.routeCfg.unexploredSectorBonus;
      }

      if (Number.isFinite(intel.portClass) && intel.portClass > 0 && intel.portClass !== 9) {
        score += this.routeCfg.anyPortBonus;
      }

      const buyCount = this._countChar(intel.portProfile || "", "B");
      if (buyCount >= 2) {
        score += this.routeCfg.doubleBuyPortBonus;
      } else if (buyCount === 1) {
        score += this.routeCfg.singleBuyPortBonus;
      }

      if (intel.busted || this.bustedSectors.has(targetSector)) {
        score -= this.routeCfg.bustedSectorPenalty;
      }

      if (this._isEvilBustActive(targetSector)) {
        score -= this.evilCfg.bustPenalty;
      }

      if (intel.hostile) {
        score -= this.routeCfg.hostileSectorPenalty;
      }

      if (Number.isFinite(intel.navHaz) && intel.navHaz > 3) {
        score -= 120;
      }
    }

    score -= this._getWarpFailurePenalty(targetSector);

    if (this.previousSector != null && targetSector === this.previousSector) {
      score -= this.routeCfg.backtrackPenalty;
    }

    if (this.recentSectors.includes(targetSector)) {
      score -= this.routeCfg.recentSectorPenalty;
    }

    if (currentIntel && intel && this._isLikelyProfitablePair(currentIntel.portProfile, intel.portProfile)) {
      score += this.routeCfg.profitablePairBonus;
    }

    if (currentIntel && intel) {
      score += this._scoreFuelFirstPair(currentIntel.portProfile, intel.portProfile);
    }

    if (includeJitter && this.routeCfg.randomJitterMax > 0) {
      score += randomInt(0, this.routeCfg.randomJitterMax);
    }

    return score;
  }

  _isLikelyProfitablePair(aProfile, bProfile) {
    const a = String(aProfile || "").toUpperCase();
    const b = String(bProfile || "").toUpperCase();
    if (a.length !== 3 || b.length !== 3) {
      return false;
    }

    let complements = 0;
    for (let i = 0; i < 3; i += 1) {
      if (a[i] === "S" && b[i] === "B") {
        complements += 1;
      }
      if (b[i] === "S" && a[i] === "B") {
        complements += 1;
      }
    }
    return complements >= 2;
  }

  _isTradeActionName(actionName) {
    const name = String(actionName || "").toLowerCase();
    return name.includes("trade") || name.includes("worldtrade") || name.includes("port");
  }

  _scoreFuelFirstPair(aProfile, bProfile) {
    const summary = this._summarizePairProfiles(aProfile, bProfile);
    if (!summary.fuelComplement) {
      return 0;
    }

    const extraComplements = Math.max(0, summary.complements - 1);
    return this.routeCfg.fuelPairBonus + (extraComplements * this.routeCfg.extraCommodityPairBonus);
  }

  _scorePreferredTradePair(aProfile, bProfile) {
    const summary = this._summarizePairProfiles(aProfile, bProfile);
    if (!summary.valid || summary.complements <= 0) {
      return 0;
    }

    const credits = Math.max(0, Number(this.stats.credits || 0));
    const preferCommodity = credits >= this.preferCommodityPairsMinCredits;
    let score = 0;

    if (preferCommodity) {
      if (summary.organicsComplement) {
        score += this.organicPairBonus;
      }
      if (summary.equipmentComplement) {
        score += this.equipmentPairBonus;
      }
      if (summary.fuelComplement) {
        score += Math.round(this.routeCfg.fuelPairBonus * this.fuelPairOpportunisticFactor);
      }
    } else {
      if (summary.fuelComplement) {
        score += this.routeCfg.fuelPairBonus;
      }
      if (summary.organicsComplement) {
        score += Math.round(this.organicPairBonus * 0.5);
      }
      if (summary.equipmentComplement) {
        score += Math.round(this.equipmentPairBonus * 0.5);
      }
    }

    const extraComplements = Math.max(0, summary.complements - 1);
    return score + (extraComplements * this.routeCfg.extraCommodityPairBonus);
  }

  _summarizePairProfiles(aProfile, bProfile) {
    const a = String(aProfile || "").toUpperCase();
    const b = String(bProfile || "").toUpperCase();
    if (a.length !== 3 || b.length !== 3) {
      return {
        valid: false,
        fuelComplement: false,
        organicsComplement: false,
        equipmentComplement: false,
        complements: 0,
      };
    }

    let complements = 0;
    for (let i = 0; i < 3; i += 1) {
      if ((a[i] === "S" && b[i] === "B") || (a[i] === "B" && b[i] === "S")) {
        complements += 1;
      }
    }

    const fuelComplement = (a[0] === "S" && b[0] === "B") || (a[0] === "B" && b[0] === "S");
    const organicsComplement = (a[1] === "S" && b[1] === "B") || (a[1] === "B" && b[1] === "S");
    const equipmentComplement = (a[2] === "S" && b[2] === "B") || (a[2] === "B" && b[2] === "S");
    return {
      valid: true,
      fuelComplement,
      organicsComplement,
      equipmentComplement,
      complements,
    };
  }

  _getPreferredTradePairCandidates(limit = 5) {
    if (this.stats.sector == null) {
      return [];
    }

    const currentIntel = this.sectorIntel.get(this.stats.sector);
    if (!currentIntel || !currentIntel.portProfile || currentIntel.portProfile.length !== 3) {
      return [];
    }

    const warps = Array.isArray(this.stats.warps) ? this.stats.warps.filter((n) => Number.isFinite(n) && n > 0) : [];
    const candidates = [];
    for (const warp of warps) {
      const intel = this.sectorIntel.get(warp);
      if (!intel || !intel.portProfile || intel.portProfile.length !== 3) {
        continue;
      }

      if ([0, 9].includes(Number(currentIntel.portClass)) || [0, 9].includes(Number(intel.portClass))) {
        continue;
      }

      if (!this._isCandidatePairSafe(currentIntel, intel, warp)) {
        continue;
      }

      const summary = this._summarizePairProfiles(currentIntel.portProfile, intel.portProfile);
      if (!summary.valid || summary.complements <= 0) {
        continue;
      }

      if (!this._passesFuelMarketDepth(currentIntel, intel)) {
        continue;
      }

      const score = this._scorePreferredTradePair(currentIntel.portProfile, intel.portProfile);
      if (score <= 0) {
        continue;
      }

      candidates.push({
        sector: warp,
        profile: intel.portProfile,
        portClass: intel.portClass,
        complements: summary.complements,
        score,
      });
    }

    return candidates
      .sort((a, b) => b.score - a.score || b.complements - a.complements || a.sector - b.sector)
      .slice(0, Math.max(1, limit));
  }

  _getFuelPairCandidates(limit = 5) {
    if (this.stats.sector == null) {
      return [];
    }

    const currentIntel = this.sectorIntel.get(this.stats.sector);
    if (!currentIntel || !currentIntel.portProfile || currentIntel.portProfile.length !== 3) {
      return [];
    }

    const warps = Array.isArray(this.stats.warps) ? this.stats.warps.filter((n) => Number.isFinite(n) && n > 0) : [];
    const candidates = [];
    for (const warp of warps) {
      const intel = this.sectorIntel.get(warp);
      if (!intel || !intel.portProfile || intel.portProfile.length !== 3) {
        continue;
      }

      if ([0, 9].includes(Number(currentIntel.portClass)) || [0, 9].includes(Number(intel.portClass))) {
        continue;
      }

      if (!this._isCandidatePairSafe(currentIntel, intel, warp)) {
        continue;
      }

      const summary = this._summarizePairProfiles(currentIntel.portProfile, intel.portProfile);
      if (!summary.valid || !summary.fuelComplement) {
        continue;
      }

      if (!this._passesFuelMarketDepth(currentIntel, intel)) {
        continue;
      }

      candidates.push({
        sector: warp,
        profile: intel.portProfile,
        portClass: intel.portClass,
        complements: summary.complements,
        score: this._scoreFuelFirstPair(currentIntel.portProfile, intel.portProfile),
      });
    }

    return candidates
      .sort((a, b) => b.score - a.score || b.complements - a.complements || a.sector - b.sector)
      .slice(0, Math.max(1, limit));
  }

  _isCandidatePairSafe(currentIntel, targetIntel, targetSector) {
    if (!currentIntel || !targetIntel) {
      return false;
    }

    if (currentIntel.busted || targetIntel.busted || this.bustedSectors.has(targetSector)) {
      return false;
    }

    if (targetIntel.hostile) {
      return false;
    }

    if (Number.isFinite(targetIntel.navHaz) && targetIntel.navHaz > 3) {
      return false;
    }

    if (this._getWarpFailurePenalty(targetSector) >= this.routeCfg.failedRoutePenalty * 2) {
      return false;
    }

    return true;
  }

  _passesFuelMarketDepth(currentIntel, targetIntel) {
    const summary = this._summarizePairProfiles(currentIntel.portProfile, targetIntel.portProfile);
    if (!summary.valid || !summary.fuelComplement) {
      return false;
    }

    const minAmount = this.routeCfg.minTradeProductAmount;
    const cFuel = currentIntel.market && currentIntel.market.fuel ? currentIntel.market.fuel : null;
    const tFuel = targetIntel.market && targetIntel.market.fuel ? targetIntel.market.fuel : null;

    const currentSellsFuel = currentIntel.portProfile[0] === "S";
    const targetSellsFuel = targetIntel.portProfile[0] === "S";

    if (currentSellsFuel && cFuel && cFuel.status === "selling" && Number.isFinite(cFuel.amount) && cFuel.amount < minAmount) {
      return false;
    }

    if (targetSellsFuel && tFuel && tFuel.status === "selling" && Number.isFinite(tFuel.amount) && tFuel.amount < minAmount) {
      return false;
    }

    return true;
  }

  _getTopWarpCandidates(limit = 5) {
    const warps = Array.isArray(this.stats.warps) ? this.stats.warps.filter((n) => Number.isFinite(n) && n > 0) : [];
    if (warps.length === 0) {
      return [];
    }

    const ranked = warps.map((sector) => ({
      sector,
      score: this._scoreWarpCandidate(sector, { includeJitter: false }),
    }));

    return ranked
      .sort((a, b) => b.score - a.score || a.sector - b.sector)
      .slice(0, Math.max(1, limit));
  }

  _isStableCommandPrompt(line) {
    const text = String(line || "").trimEnd();
    if (/^\s*computer\s+command\s*\[/i.test(text)) {
      return false;
    }

    return /Command\s*\[.*\]\s*\(\?=Help\)\?\s*:\s*$/i.test(text)
      || /^\[\d+\]\s*\(\?=Help\)\?\s*:\s*$/i.test(text);
  }

  _isInteractiveSubPrompt(line) {
    const text = String(line || "");
    return /(Your offer\s*\[\d+\]\s*\?|How many holds of|We'll\s+(sell|buy)\s+them\s+for|Our final offer|Agreed,\s*\d+\s*units|We're not interested\.|<StarDock>\s*Where to\?)/i.test(text);
  }

  _countChar(input, target) {
    let count = 0;
    for (const ch of String(input || "")) {
      if (ch === target) {
        count += 1;
      }
    }
    return count;
  }

  _isNoTradeSectorActive(sector) {
    if (!Number.isFinite(sector) || sector <= 0) {
      return false;
    }

    const seenAt = this.tradeRuntime.noTradeSectors.get(sector);
    if (!Number.isFinite(seenAt) || seenAt <= 0) {
      return false;
    }

    if (Date.now() - seenAt >= this.tradeCfg.noTradeSectorCooldownMs) {
      this.tradeRuntime.noTradeSectors.delete(sector);
      return false;
    }

    return true;
  }

  _getDesiredCargo() {
    if (!this.tradeCfg.autoDesiredCargoByNeed) {
      return this.tradeCfg.desiredCargo;
    }

    const empty = Math.max(0, Number(this.stats.emptyHolds || 0));
    const loaded = Math.max(0, Number(this.stats.cargoHolds || 0));
    if (loaded <= 0 && empty > 0) {
      return this.tradeCfg.desiredCargo;
    }

    const cargo = [
      { key: "fuel", amount: Math.max(0, Number(this.stats.fuelHolds || 0)) },
      { key: "organics", amount: Math.max(0, Number(this.stats.organicsHolds || 0)) },
      { key: "equipment", amount: Math.max(0, Number(this.stats.equipmentHolds || 0)) },
    ];
    cargo.sort((a, b) => b.amount - a.amount);
    if (cargo[0].amount <= 0) {
      return this.tradeCfg.desiredCargo;
    }

    // desiredCargo is the next commodity to BUY/LOAD after unloading current cargo.
    switch (cargo[0].key) {
      case "organics":
        return "equipment";
      case "equipment":
        return "organics";
      case "fuel":
        return "organics";
      default:
        return this.tradeCfg.desiredCargo;
    }
  }

  _getLocalTradeOpportunitySnapshot() {
    const profile = String(this.stats.portProfile || "").toUpperCase();
    const empty = Math.max(0, Number(this.stats.emptyHolds || 0));
    const desired = this._getDesiredCargo();
    const idxByCommodity = { fuel: 0, organics: 1, equipment: 2 };
    const desiredIdx = idxByCommodity[desired];
    const canBuyDesired = profile.length === 3
      && Number.isInteger(desiredIdx)
      && empty > 0
      && profile[desiredIdx] === "S";
    const canRouteDesiredSale = this._canLikelyResellCommoditySoon(desired);
    const canBuyDesiredRoutable = canBuyDesired && canRouteDesiredSale;

    const cargo = {
      fuel: Math.max(0, Number(this.stats.fuelHolds || 0)),
      organics: Math.max(0, Number(this.stats.organicsHolds || 0)),
      equipment: Math.max(0, Number(this.stats.equipmentHolds || 0)),
    };

    const canSellFuel = profile.length === 3 && cargo.fuel > 0 && profile[0] === "B";
    const canSellOrganics = profile.length === 3 && cargo.organics > 0 && profile[1] === "B";
    const canSellEquipment = profile.length === 3 && cargo.equipment > 0 && profile[2] === "B";
    const canSellAnyCargo = canSellFuel || canSellOrganics || canSellEquipment;

    return {
      sector: Number(this.stats.sector || 0),
      portProfile: profile,
      emptyHolds: empty,
      desiredCargo: desired,
      cargo,
      canBuyDesired,
      canBuyDesiredRoutable,
      canRouteDesiredSale,
      canSell: {
        fuel: canSellFuel,
        organics: canSellOrganics,
        equipment: canSellEquipment,
      },
      canTradeNow: canBuyDesiredRoutable || canSellAnyCargo,
    };
  }

  _canLikelyResellCommoditySoon(commodityKey) {
    const key = String(commodityKey || "").toLowerCase();
    const idx = this._getCommodityIndex(key);
    if (idx < 0 || !Number.isFinite(this.stats.sector)) {
      return false;
    }

    const current = this.sectorIntel.get(this.stats.sector);
    const immediateWarps = Array.isArray(this.stats.warps) && this.stats.warps.length > 0
      ? this.stats.warps
      : (current && Array.isArray(current.warps) ? current.warps : []);
    const oneHop = immediateWarps.filter((n) => Number.isFinite(n) && n > 0);
    for (const sector of oneHop) {
      const intel = this.sectorIntel.get(sector);
      if (intel && this._portBuysCommodity(intel.portProfile, key)) {
        return true;
      }
    }

    // Fall back to a 2-hop known path check.
    for (const sector of oneHop) {
      const intel = this.sectorIntel.get(sector);
      const twoHop = intel && Array.isArray(intel.warps)
        ? intel.warps.filter((n) => Number.isFinite(n) && n > 0)
        : [];
      for (const sector2 of twoHop) {
        const intel2 = this.sectorIntel.get(sector2);
        if (intel2 && this._portBuysCommodity(intel2.portProfile, key)) {
          return true;
        }
      }
    }

    return false;
  }

  _localPortCanTradeNow() {
    return this._getLocalTradeOpportunitySnapshot().canTradeNow;
  }

  _isActionEligible(action, now) {
    if (action.once && action.used) {
      return false;
    }

    if (action.cooldownMs > 0 && now - action.lastRunAtMs < action.cooldownMs) {
      return false;
    }

    const turns = this.stats.turns == null ? 0 : this.stats.turns;
    const credits = this.stats.credits == null ? 0 : this.stats.credits;
    const exp = this.stats.exp == null ? 0 : this.stats.exp;
    const alignment = this.stats.alignment == null ? 0 : this.stats.alignment;
    const holds = this.stats.holds == null ? 0 : this.stats.holds;
    const emptyHolds = Number.isFinite(this.stats.emptyHolds) ? this.stats.emptyHolds : null;
    const cargoLoaded = Number.isFinite(holds) && Number.isFinite(emptyHolds) && holds > 0 && emptyHolds < holds;
    const inRecoveryMode = this._isRecoveryMode();
    const needsHoloScanner = this._needsHoloScanner();
    const scannerPriorityAction = needsHoloScanner
      && this.upgradeCfg.enabled
      && this.upgradeCfg.holoScannerPriority
      && String(action.name || "").toLowerCase().includes("stardock-upgrade");

    if (turns < action.minTurns || turns > action.maxTurns) {
      return false;
    }

    if ((!scannerPriorityAction && credits < action.minCredits) || credits > action.maxCredits) {
      return false;
    }

    if (exp < action.minExp || exp > action.maxExp) {
      return false;
    }

    if (this.safeProgressionMinExp > 0 && exp < this.safeProgressionMinExp && this._isEvilAction(action)) {
      return false;
    }

    if (alignment < action.minAlignment || alignment > action.maxAlignment) {
      return false;
    }

    if (holds < action.minHolds || holds > action.maxHolds) {
      return false;
    }

    if (action.whenPromptMatches) {
      const promptLine = String(this.lastPromptLine || "");
      const compactPrompt = /^\[\d+\]\s*\(\?=Help\)\?\s*:/i.test(promptLine.trim());
      const syntheticPrompt = compactPrompt ? `Command ${promptLine}` : promptLine;
      if (!action.whenPromptMatches.test(promptLine) && !action.whenPromptMatches.test(syntheticPrompt)) {
        const actionName = String(action.name || "").toLowerCase();
        const isCoreTradeAction = actionName === "worldtrade-local-port" || actionName === "worldtrade-warp-hop";
        const promptRecentlySeen = this.lastCommandPromptMs > 0 && now - this.lastCommandPromptMs <= 12000;
        if (!(isCoreTradeAction && promptRecentlySeen)) {
          return false;
        }
      }
    }

    if (action.onlyWhenHasPort && (!Number.isFinite(this.stats.portClass) || this.stats.portClass <= 0)) {
      return false;
    }

    if (action.onlyWhenEscapePod && !this.isEscapePod) {
      return false;
    }

    if (action.onlyWhenNotEscapePod && this.isEscapePod) {
      return false;
    }

    if (action.onlyWhenAtStarDock && !this.isAtStarDock) {
      return false;
    }

    if (action.onlyWhenNotAtStarDock && this.isAtStarDock) {
      return false;
    }

    if (action.onlyWhenStardockKnown && !Number.isFinite(this.stardockSector)) {
      return false;
    }

    if (action.onlyWhenStardockUnknown && Number.isFinite(this.stardockSector)) {
      return false;
    }

    if (action.onlyWhenRecoveryMode && !inRecoveryMode) {
      return false;
    }

    if (action.onlyWhenNotRecoveryMode && inRecoveryMode) {
      return false;
    }

    if (action.onlyWhenCargoLoaded && !cargoLoaded) {
      return false;
    }

    if (String(action.name || "") === "worldtrade-local-port" && this._isNoTradeSectorActive(this.stats.sector)) {
      return false;
    }

    if (String(action.name || "") === "worldtrade-local-port" && !this._localPortCanTradeNow()) {
      return false;
    }

    if (this._isUpgradeAction(action) && this.upgradeCfg.enabled) {
      const available = Math.max(0, credits - this.upgradeCfg.minCashReserve);
      if (!scannerPriorityAction && credits < this.upgradeCfg.triggerCredits) {
        return false;
      }
      if (scannerPriorityAction && credits < this.upgradeCfg.holoScannerMinCredits) {
        return false;
      }
      if (!scannerPriorityAction && available < this.upgradeCfg.minSpendToUpgrade) {
        return false;
      }
    }

    const command = String(action.command || "");
    if (this.stealRequiresAlignment && /\bst3|\bs3|\bsteal\b|\brob\b|\bpr\*/i.test(command)) {
      if (!Number.isFinite(alignment) || alignment > this.stealAlignmentMax) {
        return false;
      }
    }

    if (this.isEscapePod && !action.onlyWhenEscapePod) {
      return false;
    }

    if (/\bst3|\bs3|\bsteal\b/i.test(command) && this.stats.sector != null) {
      if (this.bustedSectors.has(this.stats.sector)) {
        return false;
      }

      if (this._isEvilBustActive(this.stats.sector, "steal")) {
        return false;
      }
    }

    if (/\brob\b|\bpr\*/i.test(command) && this.stats.sector != null) {
      if (this._isEvilBustActive(this.stats.sector, "rob")) {
        return false;
      }
    }

    if (/\bm\{nextWarp\}\*/i.test(action.command || "") && this.stats.sector != null) {
      const here = this.sectorIntel.get(this.stats.sector);
      if (here && here.hostile && !this._isRecoveryMode()) {
        return false;
      }
    }

    return true;
  }

  _isEvilAction(action) {
    if (!action) {
      return false;
    }

    const name = String(action.name || "").toLowerCase();
    const command = String(action.command || "").toLowerCase();
    if (name.startsWith("evil-") || name.includes("steal") || name.includes("rob") || name.includes("ssm")) {
      return true;
    }

    return /\bsteal\b|\brob\b|\bst3\b|\bs3\b|\bpr\*/i.test(command);
  }

  _isUpgradeAction(action) {
    if (!action) {
      return false;
    }

    const name = String(action.name || "").toLowerCase();
    const command = String(action.command || "").toLowerCase();
    if (name.includes("upgrade") || name.includes("stardock")) {
      return true;
    }

    return /\bpsh\b|\bupg\b|\bo3\b/.test(command);
  }

  _computeUpgradeSpendBudget() {
    const credits = Math.max(0, Number(this.stats.credits || 0));
    if (!this.upgradeCfg.enabled || credits < this.upgradeCfg.triggerCredits) {
      return 0;
    }

    const available = Math.max(0, credits - this.upgradeCfg.minCashReserve);
    return Math.max(0, Math.min(available, this.upgradeCfg.maxSpendPerVisit));
  }

  _computeStardockPurchasePlan() {
    if (!this.upgradeCfg.autoPurchase) {
      return {
        shieldBuy: String(this.stardockBuyShield),
        fighterBuy: String(this.stardockBuyFighters),
        budget: 0,
      };
    }

    const budget = this._computeUpgradeSpendBudget();
    if (budget < this.upgradeCfg.minSpendToUpgrade) {
      return {
        shieldBuy: "0",
        fighterBuy: "0",
        budget,
      };
    }

    const shieldBudget = Math.floor(budget * this.upgradeCfg.shieldSpendRatio);
    const fighterBudget = Math.floor(budget * this.upgradeCfg.fighterSpendRatio);
    const shieldBuy = Math.max(0, Math.min(this.upgradeCfg.maxShieldBuy, Math.floor(shieldBudget / this.upgradeCfg.shieldUnitCost)));
    const fighterBuy = Math.max(0, Math.min(this.upgradeCfg.maxFighterBuy, Math.floor(fighterBudget / this.upgradeCfg.fighterUnitCost)));
    return {
      shieldBuy: String(shieldBuy),
      fighterBuy: String(fighterBuy),
      budget,
    };
  }

  _isRecoveryMode() {
    const credits = Number(this.stats.credits);
    if (!Number.isFinite(credits)) {
      return false;
    }

    if (this.recoveryOnlyWhenEscapePod && !this.isEscapePod) {
      return false;
    }

    return credits <= this.recoveryModeMaxCredits;
  }

  _trackRecoveryCredits(credits) {
    const numeric = Number(credits);
    if (!Number.isFinite(numeric)) {
      return;
    }

    if (!this._isRecoveryModeByCredits(numeric)) {
      this.recoveryStagnationTicks = 0;
      this.recoveryLastCredits = numeric;
      return;
    }

    if (this.recoveryLastCredits == null) {
      this.recoveryLastCredits = numeric;
      return;
    }

    if (numeric > this.recoveryLastCredits) {
      this.recoveryStagnationTicks = 0;
    } else {
      this.recoveryStagnationTicks += 1;
    }

    this.recoveryLastCredits = numeric;
  }

  _trackTradeProfitProgress(credits) {
    const numeric = Number(credits);
    if (!Number.isFinite(numeric)) {
      return;
    }

    if (!Number.isFinite(this.tradeRuntime.baselineCredits) || this.tradeRuntime.baselineCredits <= 0) {
      this.tradeRuntime.baselineCredits = numeric;
      this.tradeRuntime.bestNetCredits = 0;
      this.tradeRuntime.lastProfitAtMs = Date.now();
      return;
    }

    if (!Number.isFinite(this.tradeRuntime.baselineCredits)) {
      return;
    }

    const netCredits = numeric - this.tradeRuntime.baselineCredits;
    if (!Number.isFinite(this.tradeRuntime.bestNetCredits)) {
      this.tradeRuntime.bestNetCredits = netCredits;
      this.tradeRuntime.lastProfitAtMs = Date.now();
      return;
    }

    if (netCredits >= this.tradeRuntime.bestNetCredits + this.tradeCfg.profitQuestionMinNetCredits) {
      this.tradeRuntime.bestNetCredits = netCredits;
      this.tradeRuntime.lastProfitAtMs = Date.now();
    }
  }

  _maybeQuestionProfit(now, inStarDockSubmenu) {
    if (!this.tradeCfg.profitQuestionEnabled || inStarDockSubmenu || this.isEscapePod || this._isRecoveryMode()) {
      return false;
    }

    if (!Number.isFinite(this.tradeRuntime.baselineCredits) || !Number.isFinite(this.stats.credits)) {
      return false;
    }

    if (!Number.isFinite(this.tradeRuntime.lastProfitAtMs) || this.tradeRuntime.lastProfitAtMs <= 0) {
      this.tradeRuntime.lastProfitAtMs = now;
      return false;
    }

    const elapsedMs = now - this.tradeRuntime.lastProfitAtMs;
    if (elapsedMs < this.tradeCfg.profitQuestionMs) {
      return false;
    }

    const cooldownMs = Math.max(8000, Math.floor(this.tradeCfg.profitQuestionMs / 3));
    if (now - Number(this.tradeRuntime.lastProfitQuestionAtMs || 0) < cooldownMs) {
      return false;
    }

    const hopAction = this.actions.find((a) => a.name === "worldtrade-warp-hop" && a.enabled);
    if (!hopAction) {
      return false;
    }

    const command = this._renderActionCommand(hopAction.command, hopAction.name).trim();
    if (!command) {
      return false;
    }

    this._runTradeIterationTuning();
    const iteration = this.tradeRuntime.optimizerIteration;
    const targetIterations = this.tradeRuntime.optimizerTargetIterations;

    this.currentAction = "worldtrade-warp-hop-profit-forced";
    this._setStatus({
      phase: "acting",
      action: this.currentAction,
      note: `Profit stagnated ${Math.round(elapsedMs / 1000)}s; forcing trade route shift (iter ${iteration}/${targetIterations})`,
    });
    this._send(command, "action:worldtrade-warp-hop:profit-forced");
    hopAction.lastRunAtMs = now;
    this.lastActionAtMs = now;
    this.tradeRuntime.lastProfitQuestionAtMs = now;
    this.tradeRuntime.forcedProfitWarps += 1;
    this.tradeRuntime.localPortBurst = 0;
    return true;
  }

  _runTradeIterationTuning() {
    if (!this.tradeCfg.iterationTuningEnabled) {
      return;
    }

    const target = Math.max(1, Number(this.tradeCfg.iterationTargetIterations || 100));
    if (this.tradeRuntime.optimizerIteration >= target) {
      return;
    }

    this.tradeRuntime.optimizerIteration += 1;
    this.tradeRuntime.optimizerTargetIterations = target;

    // Gradually accelerate profit questioning and anti-stall movement.
    this.tradeCfg.profitQuestionMs = Math.max(
      this.tradeCfg.iterationMinProfitQuestionMs,
      this.tradeCfg.profitQuestionMs - this.tradeCfg.iterationProfitQuestionStepMs,
    );

    this.tradeCfg.stuckSectorWarpMs = Math.max(
      this.tradeCfg.iterationMinStuckSectorWarpMs,
      this.tradeCfg.stuckSectorWarpMs - this.tradeCfg.iterationStuckWarpStepMs,
    );

    // Increase route preference for known profitable pairs over random exploration.
    this.routeCfg.profitablePairBonus = Math.min(
      this.tradeCfg.iterationMaxProfitablePairBonus,
      this.routeCfg.profitablePairBonus + this.tradeCfg.iterationRouteBonusStep,
    );
  }

  _isInFedSpace(sector) {
    if (!Number.isFinite(sector) || sector <= 0) {
      return false;
    }

    return this.alignmentShiftCfg.fedSpaceSectors.includes(sector);
  }

  _maybeRunAlignmentShift(now, inStarDockSubmenu) {
    if (!this.alignmentShiftCfg.enabled) {
      return false;
    }

    const alignmentRaw = this.stats.alignment;
    const alignment = Number(alignmentRaw);
    const alignmentKnown = alignmentRaw != null && Number.isFinite(alignment);
    const target = this.alignmentShiftCfg.targetAlignmentMax;
    const forceForRecovery = this.alignmentShiftCfg.prioritizeInRecoveryUntilTarget
      && this._isRecoveryMode()
      && (!alignmentKnown || alignment > target);

    if (this.alignmentShiftCfg.suspendInRecovery && this._isRecoveryMode() && !forceForRecovery) {
      return false;
    }

    if (this.alignmentShiftCfg.suspendInEscapePod && this.isEscapePod && !forceForRecovery) {
      return false;
    }

    if (inStarDockSubmenu || this.cimState.active) {
      return false;
    }

    if (alignmentKnown && alignment <= target) {
      return false;
    }

    const colonists = Number(this.stats.colonists || 0);

    if (!alignmentKnown && this.alignmentShiftCfg.requireKnownAlignment) {
      const infoCmd = String(this.alignmentShiftCfg.infoCommand || "").trim();
      if (!infoCmd) {
        return false;
      }
      this.currentAction = "alignment-discover";
      this._setStatus({ phase: "acting", action: this.currentAction, note: "Alignment unknown, requesting info" });
      this._send(infoCmd, "alignment discover");
      this.lastActionAtMs = now;
      this.lastInfoRefreshAtMs = now;
      return true;
    }

    if (this.alignmentSelfDestructState.suppressUntilMs > now) {
      this.currentAction = "alignment-self-destruct-suppressed";
      this._setStatus({
        phase: "waiting",
        action: this.currentAction,
        note: `Self-destruct backoff active (${this.alignmentSelfDestructState.lastFailureReason || "no-effect"})`,
      });
      return false;
    }

    const requiredDrop = alignmentKnown ? Math.max(0, alignment - target) : 0;
    const holdsCapacity = Number.isFinite(this.stats.holds) && this.stats.holds > 0 ? Number(this.stats.holds) : 0;
    const colonistCapacity = Number.isFinite(colonists) && colonists > 0 ? Number(colonists) : 0;
    const holdCapacity = Math.max(holdsCapacity, colonistCapacity);

    if (this.alignmentShiftCfg.enableSelfDestructReset && alignmentKnown && alignment > target) {
      const colonistPathInsufficient = this.alignmentShiftCfg.selfDestructWhenColonistPathInsufficient
        && holdCapacity > 0
        && requiredDrop > holdCapacity;
      const recoveryOk = colonistPathInsufficient || !this.alignmentShiftCfg.selfDestructOnlyInRecovery || this._isRecoveryMode();
      const escapePodOk = !this.alignmentShiftCfg.selfDestructOnlyWhenNotEscapePod || !this.isEscapePod;
      const cooldownOk = (now - this.lastAlignmentSelfDestructAtMs) >= this.alignmentShiftCfg.selfDestructCooldownMs;
      const sdCmd = this._renderActionCommand(this.alignmentShiftCfg.selfDestructCommand).trim();
      if (recoveryOk && escapePodOk && cooldownOk && sdCmd) {
        this.currentAction = "alignment-self-destruct";
        this._setStatus({
          phase: "acting",
          action: this.currentAction,
          note: colonistPathInsufficient
            ? `Colonist path insufficient (need ${requiredDrop}, holds ${holdCapacity}); self-destruct reset`
            : `Fast alignment reset via self-destruct (align=${alignment}, target<=${target})`,
        });
        this._send(sdCmd, "alignment self destruct reset");
        this.lastActionAtMs = now;
        this.lastAlignmentSelfDestructAtMs = now;
        this.alignmentSelfDestructState.awaitingResult = true;
        this.alignmentSelfDestructState.lastAttemptAlignment = alignment;
        if (this.alignmentShiftCfg.verifyAfterSelfDestruct) {
          this.alignmentCheckState.pending = true;
        }
        return true;
      }

      if (this.alignmentShiftCfg.skipJettisonWhenInsufficientCapacity && holdCapacity > 0 && requiredDrop > holdCapacity) {
        const forcedSdCmd = this._renderActionCommand(this.alignmentShiftCfg.selfDestructCommand).trim();
        const escapePodOkForForced = !this.alignmentShiftCfg.selfDestructOnlyWhenNotEscapePod || !this.isEscapePod;
        if (forcedSdCmd && escapePodOkForForced) {
          this.currentAction = "alignment-self-destruct";
          this._setStatus({
            phase: "acting",
            action: this.currentAction,
            note: `Jettison path skipped (need ${requiredDrop}, holds ${holdCapacity}); forced self-destruct reset`,
          });
          this._send(forcedSdCmd, "alignment self destruct reset (insufficient jettison capacity)");
          this.lastActionAtMs = now;
          this.lastAlignmentSelfDestructAtMs = now;
          this.alignmentSelfDestructState.awaitingResult = true;
          this.alignmentSelfDestructState.lastAttemptAlignment = alignment;
          this.alignmentJettisonState.attempts = 0;
          this.alignmentLoadState.attempts = 0;
          this.alignmentLoadState.blocked = false;
          this.alignmentLoadState.blockedReason = "";
          if (this.alignmentShiftCfg.verifyAfterSelfDestruct) {
            this.alignmentCheckState.pending = true;
          }
          return true;
        }
      }
    }

    const needsColonists = !Number.isFinite(colonists) || colonists < this.alignmentShiftCfg.minColonistsToJettison;
    const atSource = Number.isFinite(this.stats.sector) && this.stats.sector === this.alignmentShiftCfg.sourceSector;
    const inFedSpace = this._isInFedSpace(this.stats.sector);

    if (needsColonists) {
      if (!atSource) {
        const sourceSeekStalled = this.alignmentSourceSeekState.attempts >= this.alignmentShiftCfg.maxSourceSeekAttemptsBeforeSelfDestruct;
        if (this.alignmentShiftCfg.enableSelfDestructReset && alignmentKnown && alignment > target && sourceSeekStalled) {
          const sdCmd = this._renderActionCommand(this.alignmentShiftCfg.selfDestructCommand).trim();
          if (sdCmd && (now - this.lastAlignmentSelfDestructAtMs) >= this.alignmentShiftCfg.selfDestructCooldownMs) {
            this.currentAction = "alignment-self-destruct";
            this._setStatus({
              phase: "acting",
              action: this.currentAction,
              note: `Source seek stalled (${this.alignmentSourceSeekState.attempts} hops without source); self-destruct reset`,
            });
            this._send(sdCmd, "alignment self destruct reset (source seek stalled)");
            this.lastActionAtMs = now;
            this.lastAlignmentSelfDestructAtMs = now;
            this.alignmentSelfDestructState.awaitingResult = true;
            this.alignmentSelfDestructState.lastAttemptAlignment = alignment;
            this.alignmentSourceSeekState.attempts = 0;
            if (this.alignmentShiftCfg.verifyAfterSelfDestruct) {
              this.alignmentCheckState.pending = true;
            }
            return true;
          }
        }
          const loadBlocked = this.alignmentLoadState.blocked;
          if (this.alignmentShiftCfg.enableSelfDestructReset && alignmentKnown && alignment > target && loadBlocked) {
            const sdCmd = this._renderActionCommand(this.alignmentShiftCfg.selfDestructCommand).trim();
            if (sdCmd) {
              this.currentAction = "alignment-self-destruct";
              this._setStatus({
                phase: "acting",
                action: this.currentAction,
                note: `Colonist load blocked (${this.alignmentLoadState.blockedReason}); self-destruct reset`,
              });
              this._send(sdCmd, "alignment self destruct reset (load blocked)");
              this.lastActionAtMs = now;
              this.lastAlignmentSelfDestructAtMs = now;
              this.alignmentSelfDestructState.awaitingResult = true;
              this.alignmentSelfDestructState.lastAttemptAlignment = alignment;
              this.alignmentLoadState.attempts = 0;
              this.alignmentLoadState.blocked = false;
              this.alignmentLoadState.blockedReason = "";
              if (this.alignmentShiftCfg.verifyAfterSelfDestruct) {
                this.alignmentCheckState.pending = true;
              }
              return true;
            }
          }


        const directSourceCmd = this._renderActionCommand(this.alignmentShiftCfg.moveToSourceCommand).trim();
        if (this.alignmentShiftCfg.preferDirectMoveToSource && directSourceCmd && !/\bm0\*?$/i.test(directSourceCmd)) {
          this.currentAction = "alignment-to-source";
          this._setStatus({
            phase: "acting",
            action: this.currentAction,
            note: `Routing to source sector ${this.alignmentShiftCfg.sourceSector} for colonists`,
          });
          this._send(directSourceCmd, "alignment direct source route");
          this.lastActionAtMs = now;
          this.alignmentSourceSeekState.attempts += 1;
          this.alignmentSourceSeekState.lastSector = this.stats.sector;
          return true;
        }

        const warps = Array.isArray(this.stats.warps)
          ? this.stats.warps.filter((n) => Number.isFinite(n) && n > 0)
          : [];
        const sourceIsAdjacent = warps.includes(this.alignmentShiftCfg.sourceSector);
        const goSource = sourceIsAdjacent
          ? this._renderActionCommand(this.alignmentShiftCfg.moveToSourceCommand)
          : (() => {
              const hop = this._selectAlignmentSeekWarp(this.alignmentShiftCfg.sourceSector);
              return Number.isFinite(hop) && hop > 0 ? `m${hop}*` : "";
            })();

        if (!goSource.trim() || /\bm0\*?$/i.test(goSource.trim())) {
          return false;
        }
        this.currentAction = sourceIsAdjacent ? "alignment-to-source" : "alignment-seek-source";
        this._setStatus({
          phase: "acting",
          action: this.currentAction,
          note: sourceIsAdjacent
            ? `Moving to sector ${this.alignmentShiftCfg.sourceSector} for colonists`
            : `Source ${this.alignmentShiftCfg.sourceSector} not adjacent, stepping toward known route`,
        });
        this._send(goSource, "alignment to source sector");
        this.lastActionAtMs = now;
        this.alignmentSourceSeekState.attempts += 1;
        this.alignmentSourceSeekState.lastSector = this.stats.sector;
        return true;
      }

      this.alignmentSourceSeekState.attempts = 0;

      const stalledLoadAttempts = this.alignmentLoadState.attempts >= this.alignmentShiftCfg.maxLoadAttemptsBeforeSelfDestruct;
      const knownColonists = Number.isFinite(colonists) ? colonists : 0;
      if (this.alignmentShiftCfg.enableSelfDestructReset && alignmentKnown && alignment > target && stalledLoadAttempts && knownColonists <= 0) {
        const sdCmd = this._renderActionCommand(this.alignmentShiftCfg.selfDestructCommand).trim();
        if (sdCmd && (now - this.lastAlignmentSelfDestructAtMs) >= this.alignmentShiftCfg.selfDestructCooldownMs) {
          this.currentAction = "alignment-self-destruct";
          this._setStatus({
            phase: "acting",
            action: this.currentAction,
            note: `Colonist load stalled (${this.alignmentLoadState.attempts} attempts, 0 colonists); self-destruct reset`,
          });
          this._send(sdCmd, "alignment self destruct reset (load stalled)");
          this.lastActionAtMs = now;
          this.lastAlignmentSelfDestructAtMs = now;
          this.alignmentSelfDestructState.awaitingResult = true;
          this.alignmentSelfDestructState.lastAttemptAlignment = alignment;
          this.alignmentLoadState.attempts = 0;
          if (this.alignmentShiftCfg.verifyAfterSelfDestruct) {
            this.alignmentCheckState.pending = true;
          }
          return true;
        }
      }

      const loadCmd = this._renderActionCommand(this.alignmentShiftCfg.loadColonistsCommand);
      if (!loadCmd.trim()) {
        return false;
      }
      this.currentAction = "alignment-load-colonists";
      this._setStatus({ phase: "acting", action: this.currentAction, note: "Loading colonists for alignment drop" });
      this._send(loadCmd, "alignment load colonists");
      this.lastActionAtMs = now;
      this.alignmentLoadState.attempts += 1;
      return true;
    }

    this.alignmentLoadState.blocked = false;
    this.alignmentLoadState.blockedReason = "";

    if (inFedSpace) {
      const leaveCmd = this._renderActionCommand(this.alignmentShiftCfg.leaveFedSpaceCommand);
      if (!leaveCmd.trim()) {
        return false;
      }
      this.currentAction = "alignment-leave-fed";
      this._setStatus({ phase: "acting", action: this.currentAction, note: "Leaving fed space before jettison" });
      this._send(leaveCmd, "alignment leave fed space");
      this.lastActionAtMs = now;
      return true;
    }

    const jettisonCmd = this._renderActionCommand(this.alignmentShiftCfg.jettisonCommand);
    if (!jettisonCmd.trim()) {
      return false;
    }

    const jettisonStalled = this.alignmentJettisonState.attempts >= this.alignmentShiftCfg.maxJettisonAttemptsBeforeSelfDestruct;
    if (this.alignmentShiftCfg.enableSelfDestructReset && alignmentKnown && alignment > target && jettisonStalled) {
      const sdCmd = this._renderActionCommand(this.alignmentShiftCfg.selfDestructCommand).trim();
      if (sdCmd) {
        this.currentAction = "alignment-self-destruct";
        this._setStatus({
          phase: "acting",
          action: this.currentAction,
          note: `Jettison stalled (${this.alignmentJettisonState.attempts} attempts, align=${alignment}); self-destruct reset`,
        });
        this._send(sdCmd, "alignment self destruct reset (jettison stalled)");
        this.lastActionAtMs = now;
        this.lastAlignmentSelfDestructAtMs = now;
        this.alignmentSelfDestructState.awaitingResult = true;
        this.alignmentSelfDestructState.lastAttemptAlignment = alignment;
        this.alignmentJettisonState.attempts = 0;
        if (this.alignmentShiftCfg.verifyAfterSelfDestruct) {
          this.alignmentCheckState.pending = true;
        }
        return true;
      }
    }

    this.currentAction = "alignment-jettison";
    this._setStatus({ phase: "acting", action: this.currentAction, note: `Jettisoning colonists (align>${target})` });
    this._send(jettisonCmd, "alignment jettison");
    this.lastActionAtMs = now;
    if (alignmentKnown) {
      const lastAttempt = this.alignmentJettisonState.lastAttemptAlignment;
      if (!Number.isFinite(lastAttempt) || alignment >= lastAttempt) {
        this.alignmentJettisonState.attempts += 1;
      } else {
        this.alignmentJettisonState.attempts = 1;
      }
      this.alignmentJettisonState.lastAttemptAlignment = alignment;
    }
    if (this.alignmentShiftCfg.verifyAfterJettison) {
      this.alignmentCheckState.pending = true;
    }
    return true;
  }

  _maybeRunAlignmentVerification(now, inStarDockSubmenu) {
    if (!this.alignmentCheckState.pending) {
      return false;
    }

    if (inStarDockSubmenu || this.cimState.active) {
      return false;
    }

    if (now - this.lastActionAtMs < this.alignmentShiftCfg.verificationMinGapMs) {
      this.currentAction = "alignment-verify-pending";
      this._setStatus({ phase: "waiting", action: this.currentAction, note: "Waiting to verify alignment after jettison/reset" });
      return true;
    }

    if (now - this.alignmentCheckState.requestedAtMs < this.alignmentShiftCfg.verificationMinGapMs) {
      this.currentAction = "alignment-verify-pending";
      this._setStatus({ phase: "waiting", action: this.currentAction, note: "Awaiting alignment verification cooldown" });
      return true;
    }

    const infoCmd = String(this.alignmentShiftCfg.infoCommand || "").trim();
    if (!infoCmd) {
      return false;
    }

    this.currentAction = "alignment-verify";
    this._setStatus({ phase: "acting", action: this.currentAction, note: "Verifying alignment after dump/reset" });
    this._send(infoCmd, "alignment verification info refresh");
    this.lastActionAtMs = now;
    this.lastInfoRefreshAtMs = now;
    this.alignmentCheckState.requestedAtMs = now;
    return true;
  }

  _isRecoveryModeByCredits(credits) {
    return Number.isFinite(credits) && credits <= this.recoveryModeMaxCredits;
  }

  _shouldRunRecoveryLiquidation() {
    if (this.recoveryLiquidationDone || !this.recoveryLiquidationSequence.trim()) {
      return false;
    }

    const credits = Number(this.stats.credits);
    if (!Number.isFinite(credits) || credits > this.recoveryLiquidationMaxCredits) {
      return false;
    }

    if (this.recoveryOnlyWhenEscapePod && !this.isEscapePod) {
      return false;
    }

    if (!this.isAtStarDock) {
      return false;
    }

    return this.recoveryStagnationTicks >= this.recoveryLiquidationStagnationTicks;
  }

  _computeStealAmount() {
    const exp = Math.max(0, Number(this.stats.exp || 0));
    const holds = Math.max(1, Number(this.stats.holds || 1));
    let steal = Math.floor(exp / this.stealHoldsExpDivisor);

    if (!Number.isFinite(steal) || steal < this.minStealHolds) {
      steal = this.minStealHolds;
    }

    steal = Math.min(steal, holds, this.maxStealHolds);
    return Math.max(1, steal);
  }

  _computeStealCredits() {
    const exp = Math.max(0, Number(this.stats.exp || 0));
    return Math.max(0, Math.floor(exp * this.stealCreditsMultiplier));
  }

  _computeUpgradeAmount(stealAmount) {
    const portEquipBuying = Math.max(0, Number(this.stats.portEquipBuying || 0));
    const raw = Math.floor((Math.max(0, stealAmount - portEquipBuying) / 10) + 1);
    return Math.max(1, raw);
  }

  _send(command, reason, options = {}) {
    const out = command == null ? "" : String(command);
    const allowEmpty = Boolean(options.allowEmpty);
    const sensitive = Boolean(options.sensitive);
    if (!allowEmpty && !out.trim()) {
      return;
    }

    this.telnet.send(out);
    this._markSupervisorProgress("tx");

    const routeMatch = out.match(/^\s*m(\d+)\*/i);
    if (routeMatch) {
      this.lastAttemptedWarpTarget = Number(routeMatch[1]);
    }

    const shown = sensitive ? "<redacted>" : out;
    this.lastTx = shown;
    this._setStatus({ phase: "sending", note: reason });
    this._publishEvent("tx.send", {
      reason,
      sensitive,
      empty: !String(out).trim(),
      command: sensitive ? "<redacted>" : this._truncateForEvent(out),
    });
    this._appendLog(`[TX       ${nowIso()}] ${shown}    // ${reason}`);
  }

  _buildDataServicesSnapshot(now = Date.now()) {
    const lastIntelSeen = Array.from(this.sectorIntel.values()).reduce((max, intel) => {
      const last = Number(intel && intel.lastSeenAt ? intel.lastSeenAt : 0);
      return last > max ? last : max;
    }, 0);

    return {
      intelAgeMs: lastIntelSeen > 0 ? now - lastIntelSeen : null,
      portCimAgeMs: this.cimState.lastPortCimAtMs > 0 ? now - this.cimState.lastPortCimAtMs : null,
      warpCimAgeMs: this.cimState.lastWarpCimAtMs > 0 ? now - this.cimState.lastWarpCimAtMs : null,
      lastIntelPersistAtMs: this.lastIntelPersistAtMs || 0,
      knownSectors: this.sectorIntel.size,
      missingPortCim: !Number.isFinite(this.cimState.lastPortCimAtMs) || this.cimState.lastPortCimAtMs <= 0,
      missingWarpCim: !Number.isFinite(this.cimState.lastWarpCimAtMs) || this.cimState.lastWarpCimAtMs <= 0,
    };
  }

  _log(message) {
    const line = `[BOT      ${nowIso()}] ${message}`;
    console.log(line);
    this._appendLog(line);
  }

  _appendLog(line) {
    fs.appendFileSync(this.logFilePath, line + "\n");
  }

  _setStatus(update) {
    if (update && update.phase) {
      this._updateSupervisorPhase(update.phase);
    }

    const prevPhase = this.statusState.phase;
    const prevAction = this.statusState.action;

    this.statusState = {
      ...this.statusState,
      ...update,
      updatedAt: nowIso(),
    };

    this._updateModeFromStatus(this.statusState);
    if (prevPhase !== this.statusState.phase || prevAction !== this.statusState.action) {
      this._publishEvent("status.transition", {
        fromPhase: prevPhase,
        toPhase: this.statusState.phase,
        fromAction: prevAction,
        toAction: this.statusState.action,
      });
    }

    this._writeStatus();
  }

  _updateModeFromStatus(status) {
    const action = String((status && status.action) || "").toLowerCase();
    const phase = String((status && status.phase) || "").toLowerCase();
    let nextMode = "General";
    let reason = phase || "status";

    if (action.includes("startup") || phase === "connecting" || phase === "connected" || phase === "nudging") {
      nextMode = "Startup";
      reason = action || phase;
    } else if (action.includes("cim") || phase === "sync") {
      nextMode = "DataSync";
      reason = action || phase;
    } else if (action.includes("recovery") || action.includes("liquidation")) {
      nextMode = "Recovery";
      reason = action;
    } else if (phase === "threat" || action.includes("evade") || action.includes("kill")) {
      nextMode = "Defense";
      reason = action || phase;
    } else if (phase === "idle") {
      nextMode = "Idle";
      reason = action || phase;
    }

    if (this.modeState.current !== nextMode) {
      this.modeState.current = nextMode;
      this.modeState.changedAtMs = Date.now();
      this.modeState.reason = reason;
      this._publishEvent("mode.change", { mode: nextMode, reason });
    }
  }

  _truncateForEvent(value) {
    const max = this.eventCfg.maxPayloadChars;
    const text = String(value == null ? "" : value);
    if (text.length <= max) {
      return text;
    }
    return `${text.slice(0, max)}...`;
  }

  _publishEvent(type, payload = {}, level = "info") {
    if (!this.eventCfg.enabled) {
      return;
    }

    const key = String(type || "event.unknown");
    if (!this.eventState.counters[key]) {
      const keys = Object.keys(this.eventState.counters);
      if (keys.length >= this.eventCfg.maxCounters) {
        return;
      }
      this.eventState.counters[key] = 0;
    }

    this.eventState.seq += 1;
    this.eventState.counters[key] += 1;

    const compactPayload = {};
    for (const [k, v] of Object.entries(payload || {})) {
      if (v == null) {
        continue;
      }

      if (typeof v === "number" || typeof v === "boolean") {
        compactPayload[k] = v;
      } else {
        compactPayload[k] = this._truncateForEvent(v);
      }
    }

    this.eventState.recent.push({
      seq: this.eventState.seq,
      at: nowIso(),
      type: key,
      level,
      payload: compactPayload,
    });

    if (this.eventState.recent.length > this.eventCfg.maxRecent) {
      this.eventState.recent.splice(0, this.eventState.recent.length - this.eventCfg.maxRecent);
    }
  }

  _getTopEventCounters(limit = 12) {
    const rows = Object.entries(this.eventState.counters).map(([name, count]) => ({ name, count }));
    rows.sort((a, b) => b.count - a.count || a.name.localeCompare(b.name));
    return rows.slice(0, Math.max(1, limit));
  }

  _updateSupervisorPhase(phase) {
    const nextPhase = String(phase || "").trim() || "unknown";
    if (this.supervisorState.phase === nextPhase) {
      return;
    }

    this.supervisorState.phase = nextPhase;
    this.supervisorState.phaseEnteredAtMs = Date.now();
  }

  _markSupervisorProgress(reason = "") {
    this.supervisorState.lastProgressAtMs = Date.now();
    if (reason !== "recovery") {
      this.supervisorState.freezeDetected = false;
      this.supervisorState.consecutiveRecoveries = 0;
    }
  }

  _maybeRecoverFrozenRuntime(now = Date.now()) {
    if (!this.supervisorCfg.enabled) {
      return false;
    }

    if (this.cimState.active) {
      return false;
    }

    const lastProgress = Math.max(
      this.supervisorState.lastProgressAtMs || 0,
      this.lastRxAtMs || 0,
      this.lastActionAtMs || 0,
      this.lastCommandPromptMs || 0
    );
    const idleMs = now - lastProgress;
    if (idleMs < this.supervisorCfg.freezeMs) {
      return false;
    }

    if (now - this.supervisorState.lastRecoveryAtMs < this.supervisorCfg.recoveryCooldownMs) {
      return false;
    }

    this.supervisorState.freezeDetected = true;
    this.supervisorState.lastRecoveryAtMs = now;
    this.supervisorState.recoveriesTotal += 1;
    this.supervisorState.consecutiveRecoveries += 1;
    this.supervisorState.lastRecoveryReason = `idle>${this.supervisorCfg.freezeMs}ms`;
    this._publishEvent("supervisor.freeze-recovery", {
      idleMs,
      recoveriesTotal: this.supervisorState.recoveriesTotal,
      consecutiveRecoveries: this.supervisorState.consecutiveRecoveries,
    }, "warn");

    const useKeepAlive = this.supervisorState.consecutiveRecoveries >= this.supervisorCfg.escalationKeepAliveAfter;
    const recoveryCommand = useKeepAlive ? String(this.keepAliveCommand || "") : "";

    this.currentAction = "supervisor-unfreeze";
    this._setStatus({
      phase: "supervisor",
      action: this.currentAction,
      note: `Runtime freeze recovery (${this.supervisorState.lastRecoveryReason})`,
    });
    this._send(recoveryCommand, "supervisor unfreeze", { allowEmpty: true });
    this.lastNudgeAtMs = now;
    this.lastActionAtMs = now;
    this.lastCommandPromptMs = 0;
    this.lastInteractivePromptMs = 0;
    this._markSupervisorProgress("recovery");
    return true;
  }

  _writeStatus() {
    const now = Date.now();
    const freshness = this._getIntelFreshness(now);
    const cimGuards = this._getCimRunGuards(now);
    const dataServices = this._buildDataServicesSnapshot(now);
    const startupTotal = this.startupSurveyCommands.length;
    const startupExecuted = Math.min(this.startupSurveyIndex, startupTotal);
    const startupNextCommand = this.startupSurveyDone || startupExecuted >= startupTotal
      ? ""
      : this.startupSurveyCommands[startupExecuted];

    const payload = {
      updatedAt: nowIso(),
      phase: this.statusState.phase,
      action: this.statusState.action,
      note: this.statusState.note,
      host: this.config.connection.host,
      port: this.config.connection.port,
      username: this.identity.username || "",
      currentAction: this.currentAction,
      lastPromptLine: this.lastPromptLine,
      lastRxLine: this.lastRxLine,
      lastTx: this.lastTx,
      stats: this.stats,
      routing: {
        previousSector: this.previousSector,
        recentSectors: this.recentSectors,
        bustedSectors: Array.from(this.bustedSectors),
        evilBustedSectors: Array.from(this.evilBustedSectors.entries()).map(([sector, info]) => ({
          sector,
          count: info.count,
          stealCount: info.stealCount || 0,
          robCount: info.robCount || 0,
          lastReason: info.lastReason,
          lastAt: info.lastAt,
          lastStealAt: info.lastStealAt || 0,
          lastRobAt: info.lastRobAt || 0,
          stealActive: this._isEvilBustActive(sector, "steal"),
          robActive: this._isEvilBustActive(sector, "rob"),
        })).slice(0, 20),
        failedWarpTargets: Array.from(this.failedWarpTargets.entries()).map(([sector, info]) => ({
          sector,
          count: info.count,
          lastReason: info.lastReason,
          lastAt: info.lastAt,
        })).slice(0, 12),
        topWarpCandidates: this._getTopWarpCandidates(6),
        topFuelPairs: this._getFuelPairCandidates(6),
      },
      cim: {
        enabled: this.cimCfg.enabled,
        active: this.cimState.active,
        mode: this.cimState.mode,
        nextMode: this.cimState.nextMode,
        selectedModeIfDue: this._pickNextCimMode(now),
        bootstrapIfMissing: this.cimCfg.bootstrapIfMissing,
        prioritizeStale: this.cimCfg.prioritizeStale,
        intervalMs: this.cimCfg.intervalMs,
        portFreshMs: this.cimCfg.portFreshMs,
        warpFreshMs: this.cimCfg.warpFreshMs,
        lastRunAtMs: this.cimState.lastRunAtMs,
        completedAtMs: this.cimState.completedAtMs,
        lastPortCimAtMs: this.cimState.lastPortCimAtMs,
        lastWarpCimAtMs: this.cimState.lastWarpCimAtMs,
        parsedPortLines: this.cimState.parsedPortLines,
        parsedWarpLines: this.cimState.parsedWarpLines,
        updatedPortSectors: this.cimState.updatedPortSectors,
        updatedWarpSectors: this.cimState.updatedWarpSectors,
        lastSummary: this.cimState.lastSummary,
        freshness,
        guards: cimGuards,
      },
      intel: {
        file: this.intelFilePath,
        knownSectors: this.sectorIntel.size,
        sectorsWithPorts: Array.from(this.sectorIntel.values()).filter((s) => Number.isFinite(s.portClass) && s.portClass > 0).length,
        sectorsWithWarps: Array.from(this.sectorIntel.values()).filter((s) => Array.isArray(s.warps) && s.warps.length > 0).length,
      },
      stardock: {
        sector: this.stardockSector,
        atStardock: this.isAtStarDock,
        escapePod: this.isEscapePod,
      },
      startupSurvey: {
        enabled: this.startupSurveyCfg.enabled,
        done: this.startupSurveyDone,
        executed: startupExecuted,
        total: startupTotal,
        minGapMs: this.startupSurveyCfg.minGapMs,
        nextCommand: startupNextCommand,
      },
      mode: {
        current: this.modeState.current,
        changedAtMs: this.modeState.changedAtMs,
        reason: this.modeState.reason,
      },
      events: {
        enabled: this.eventCfg.enabled,
        totalPublished: this.eventState.seq,
        topCounters: this._getTopEventCounters(10),
        recent: this.eventState.recent.slice(-10),
      },
      dataServices,
      supervisor: {
        enabled: this.supervisorCfg.enabled,
        phase: this.supervisorState.phase,
        freezeMs: this.supervisorCfg.freezeMs,
        recoveryCooldownMs: this.supervisorCfg.recoveryCooldownMs,
        escalationKeepAliveAfter: this.supervisorCfg.escalationKeepAliveAfter,
        freezeDetected: this.supervisorState.freezeDetected,
        recoveriesTotal: this.supervisorState.recoveriesTotal,
        consecutiveRecoveries: this.supervisorState.consecutiveRecoveries,
        lastProgressAtMs: this.supervisorState.lastProgressAtMs,
        lastRecoveryAtMs: this.supervisorState.lastRecoveryAtMs,
        lastRecoveryReason: this.supervisorState.lastRecoveryReason,
      },
      recovery: {
        enabled: this._isRecoveryMode(),
        maxCredits: this.recoveryModeMaxCredits,
        credits: this.stats.credits,
        emptyHolds: this.stats.emptyHolds,
        cargoHolds: this.stats.cargoHolds,
        stagnationTicks: this.recoveryStagnationTicks,
        stagnationThreshold: this.recoveryLiquidationStagnationTicks,
        liquidationConfigured: Boolean(this.recoveryLiquidationSequence.trim()),
        liquidationDone: this.recoveryLiquidationDone,
      },
      upgrades: {
        enabled: this.upgradeCfg.enabled,
        triggerCredits: this.upgradeCfg.triggerCredits,
        holoScannerPriority: this.upgradeCfg.holoScannerPriority,
        holoScannerMinCredits: this.upgradeCfg.holoScannerMinCredits,
        needsHoloScanner: this._needsHoloScanner(),
        minCashReserve: this.upgradeCfg.minCashReserve,
        maxSpendPerVisit: this.upgradeCfg.maxSpendPerVisit,
        minSpendToUpgrade: this.upgradeCfg.minSpendToUpgrade,
        budgetNow: this._computeUpgradeSpendBudget(),
        autoPurchase: this.upgradeCfg.autoPurchase,
        shieldUnitCost: this.upgradeCfg.shieldUnitCost,
        fighterUnitCost: this.upgradeCfg.fighterUnitCost,
        shieldSpendRatio: this.upgradeCfg.shieldSpendRatio,
        fighterSpendRatio: this.upgradeCfg.fighterSpendRatio,
        maxShieldBuy: this.upgradeCfg.maxShieldBuy,
        maxFighterBuy: this.upgradeCfg.maxFighterBuy,
        plannedShieldBuy: Number(this._computeStardockPurchasePlan().shieldBuy),
        plannedFighterBuy: Number(this._computeStardockPurchasePlan().fighterBuy),
      },
      movementSafety: {
        enabled: this.movementSafetyCfg.enabled,
        layFightersOnTradeWarpHop: this.movementSafetyCfg.layFightersOnTradeWarpHop,
        fightersPerDrop: this.movementSafetyCfg.fightersPerDrop,
        layOnlyOnGoodPairs: this.movementSafetyCfg.layOnlyOnGoodPairs,
        layOnAdjacentGoodPairs: this.movementSafetyCfg.layOnAdjacentGoodPairs,
        maxNavHazToLay: this.movementSafetyCfg.maxNavHazToLay,
      },
      steal: {
        requiresAlignment: this.stealRequiresAlignment,
        alignmentMax: this.stealAlignmentMax,
        holdsExpDivisor: this.stealHoldsExpDivisor,
        creditsMultiplier: this.stealCreditsMultiplier,
        computedHolds: this._computeStealAmount(),
        computedCredits: this._computeStealCredits(),
      },
      trade: {
        enabled: this.tradeCfg.enabled,
        forcePromptHaggle: this.tradeCfg.forcePromptHaggle,
        sellCommodity: this._getDominantCargoForSale(),
        buyCommodityTarget: this._getDesiredCargo(),
        desiredCargo: this._getDesiredCargo(),
        configuredDesiredCargo: this.tradeCfg.desiredCargo,
        autoDesiredCargoByNeed: this.tradeCfg.autoDesiredCargoByNeed,
        enableHoldStepModel: this.tradeCfg.enableHoldStepModel,
        localPortBurstMin: this.tradeCfg.localPortBurstMin,
        moveOnRejectStreak: this.tradeCfg.moveOnRejectStreak,
        noTradeSectorCooldownMs: this.tradeCfg.noTradeSectorCooldownMs,
        profitQuestionEnabled: this.tradeCfg.profitQuestionEnabled,
        profitQuestionMs: this.tradeCfg.profitQuestionMs,
        profitQuestionMinNetCredits: this.tradeCfg.profitQuestionMinNetCredits,
        iterationTuningEnabled: this.tradeCfg.iterationTuningEnabled,
        iterationTargetIterations: this.tradeCfg.iterationTargetIterations,
        iterationProfitQuestionStepMs: this.tradeCfg.iterationProfitQuestionStepMs,
        iterationMinProfitQuestionMs: this.tradeCfg.iterationMinProfitQuestionMs,
        iterationStuckWarpStepMs: this.tradeCfg.iterationStuckWarpStepMs,
        iterationMinStuckSectorWarpMs: this.tradeCfg.iterationMinStuckSectorWarpMs,
        iterationRouteBonusStep: this.tradeCfg.iterationRouteBonusStep,
        iterationMaxProfitablePairBonus: this.tradeCfg.iterationMaxProfitablePairBonus,
        stuckSectorWarpEnabled: this.tradeCfg.stuckSectorWarpEnabled,
        stuckSectorWarpMs: this.tradeCfg.stuckSectorWarpMs,
        buyOfferPct: this.tradeCfg.buyOfferPct,
        sellOfferPct: this.tradeCfg.sellOfferPct,
        maxHaggleRounds: this.tradeCfg.maxHaggleRounds,
        driftTolerance: this.tradeCfg.driftTolerance,
        uncertainThreshold: this.tradeCfg.uncertainThreshold,
        overbidWhenUncertain: this.tradeCfg.overbidWhenUncertain,
        correctionPct: this.tradeCfg.correctionPct,
        adaptiveHaggleEnabled: this.tradeCfg.adaptiveHaggleEnabled,
        adaptiveHaggleWindow: this.tradeCfg.adaptiveHaggleWindow,
        adaptiveHaggleMinSamples: this.tradeCfg.adaptiveHaggleMinSamples,
        adaptiveRejectRateThreshold: this.tradeCfg.adaptiveRejectRateThreshold,
        adaptiveBuyPctMax: this.tradeCfg.adaptiveBuyPctMax,
        adaptiveSellPctMin: this.tradeCfg.adaptiveSellPctMin,
        adaptiveUncertainThresholdBoost: this.tradeCfg.adaptiveUncertainThresholdBoost,
        adaptiveCorrectionScale: this.tradeCfg.adaptiveCorrectionScale,
        initialNoHaggleDeals: this.tradeCfg.initialNoHaggleDeals,
        adaptiveCurrent: this._getAdaptiveHaggleTuning(),
        baselineCredits: this.tradeRuntime.baselineCredits,
        baselineExp: this.tradeRuntime.baselineExp,
        bestNetCredits: this.tradeRuntime.bestNetCredits,
        lastProfitAtMs: this.tradeRuntime.lastProfitAtMs,
        lastProfitQuestionAtMs: this.tradeRuntime.lastProfitQuestionAtMs,
        netCredits: Number.isFinite(this.tradeRuntime.baselineCredits) && Number.isFinite(this.stats.credits)
          ? this.stats.credits - this.tradeRuntime.baselineCredits
          : null,
        netExp: Number.isFinite(this.tradeRuntime.baselineExp) && Number.isFinite(this.stats.exp)
          ? this.stats.exp - this.tradeRuntime.baselineExp
          : null,
        successfulDeals: this.tradeRuntime.successfulDeals,
        rejectedDeals: this.tradeRuntime.rejectedDeals,
        noTradeDeals: this.tradeRuntime.noTradeDeals,
        forcedWarpHops: this.tradeRuntime.forcedWarpHops,
        forcedStuckWarps: this.tradeRuntime.forcedStuckWarps,
        forcedProfitWarps: this.tradeRuntime.forcedProfitWarps,
        optimizerIteration: this.tradeRuntime.optimizerIteration,
        optimizerTargetIterations: this.tradeRuntime.optimizerTargetIterations,
        currentProfitablePairBonus: this.routeCfg.profitablePairBonus,
        localRejectStreak: this.tradeRuntime.localRejectStreak,
        localPortBurst: this.tradeRuntime.localPortBurst,
        lastTradeSector: this.tradeRuntime.lastTradeSector,
        noTradeSectorsActive: Array.from(this.tradeRuntime.noTradeSectors.entries())
          .map(([sector, atMs]) => ({ sector, atMs }))
          .slice(-12),
        localPortCanTradeNow: this._localPortCanTradeNow(),
        localOpportunity: this._getLocalTradeOpportunitySnapshot(),
        lastOutcome: this.tradeRuntime.lastOutcome,
        lastOfferSent: this.tradeRuntime.lastOfferSent,
        lastOfferAtMs: this.tradeRuntime.lastOfferAtMs,
        lastModelOffer: this.tradeRuntime.lastModelOffer,
        modelCount: this.tradeRuntime.priceModels.size,
      },
    };

    const statusJson = JSON.stringify(payload, null, 2);
    const tempBase = `${this.statusFilePath}.tmp`;
    let wrote = false;

    for (let attempt = 0; attempt < 4 && !wrote; attempt += 1) {
      const tempPath = `${tempBase}.${attempt}`;
      try {
        fs.writeFileSync(tempPath, statusJson);
        fs.renameSync(tempPath, this.statusFilePath);
        wrote = true;
      } catch (err) {
        // Readers can hold a transient lock; skip this cycle instead of crashing the bot.
        if (fs.existsSync(tempPath)) {
          try {
            fs.unlinkSync(tempPath);
          } catch {
            // ignore cleanup failure for temp files
          }
        }

        const code = err && err.code ? String(err.code) : "";
        const retryable = code === "EBUSY" || code === "EPERM" || code === "EACCES";
        if (!retryable) {
          this._log(`status write failed: ${code || err.message || "unknown error"}`);
          break;
        }
      }
    }

    if (!wrote) {
      try {
        fs.writeFileSync(this.statusFilePath, statusJson);
      } catch (err) {
        const code = err && err.code ? String(err.code) : "";
        this._log(`status write skipped: ${code || err.message || "unknown error"}`);
      }
    }
    this._writeIntelSnapshot();
  }
}

function main() {
  const configPathArg = process.argv[2] || path.resolve(process.cwd(), "config.json");

  if (!fs.existsSync(configPathArg)) {
    console.error(`Config file not found: ${configPathArg}`);
    console.error("Copy config.example.json to config.json and fill in credentials/prompts first.");
    process.exit(1);
  }

  const bot = new TradeWarsBot(configPathArg);
  bot.start().catch((err) => {
    console.error("Fatal bot error:", err);
    process.exit(1);
  });
}

module.exports = { TradeWarsBot, TelnetSocket, compilePattern };

if (require.main === module) {
  main();
}
