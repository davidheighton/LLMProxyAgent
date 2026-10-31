'use strict';

const fs = require('node:fs');
const path = require('node:path');
const readline = require('node:readline');

const ESC = '\u001b[';
const COLORS = {
  reset: `${ESC}0m`,
  bold: `${ESC}1m`,
  dim: `${ESC}2m`,
  cyan: `${ESC}36m`,
  brightCyan: `${ESC}96m`,
  green: `${ESC}32m`,
  brightGreen: `${ESC}92m`,
  yellow: `${ESC}33m`,
  brightYellow: `${ESC}93m`,
  red: `${ESC}31m`,
  brightRed: `${ESC}91m`,
  blue: `${ESC}34m`,
  magenta: `${ESC}35m`,
  white: `${ESC}37m`,
  gray: `${ESC}90m`,
};

const argv = process.argv.slice(2);
const option = (name, fallback) => {
  const index = argv.indexOf(name);
  return index >= 0 && argv[index + 1] ? argv[index + 1] : fallback;
};
const hasFlag = (name) => argv.includes(name);
const statusFile = path.resolve(option('--status', path.join(__dirname, 'logs', '192.168.4.75.status.json')));
const refreshMs = Math.max(150, Number(option('--refresh', '500')) || 500);
const once = hasFlag('--once');
const noColor = hasFlag('--no-color') || !process.stdout.isTTY;
const color = (name, text) => noColor ? String(text) : `${COLORS[name] || ''}${text}${COLORS.reset}`;

let snapshot = null;
let readErrors = 0;
let updateCount = 0;
let hopCount = 0;
let lastUpdatedAt = '';
let lastSector = null;
let startedAt = Date.now();
let timer = null;
let viewMode = 'all';

function value(input, fallback = '-') {
  if (input === null || input === undefined || String(input).trim() === '') return fallback;
  return String(input);
}

function number(input, fallback = 0) {
  const parsed = Number(input);
  return Number.isFinite(parsed) ? parsed : fallback;
}

function formatNumber(input) {
  const parsed = Number(input);
  return Number.isFinite(parsed) ? parsed.toLocaleString('en-US') : value(input);
}

function formatAge(iso) {
  if (!iso) return '-';
  const age = Math.max(0, (Date.now() - Date.parse(iso)) / 1000);
  return `${age.toFixed(1)}s`;
}

function uptime() {
  const seconds = Math.floor((Date.now() - startedAt) / 1000);
  const hours = Math.floor(seconds / 3600);
  const minutes = Math.floor((seconds % 3600) / 60);
  return `${String(hours).padStart(2, '0')}:${String(minutes).padStart(2, '0')}:${String(seconds % 60).padStart(2, '0')}`;
}

function visibleLength(text) {
  return String(text).replace(/\u001b\[[0-?]*[ -/]*[@-~]/g, '').length;
}

function fit(text, width, align = 'left') {
  const raw = String(text ?? '').replace(/[\r\n\t]/g, ' ');
  if (raw.length >= width) return raw.slice(0, Math.max(0, width - 1)) + (width > 0 ? '>' : '');
  const spaces = ' '.repeat(width - raw.length);
  return align === 'right' ? spaces + raw : raw + spaces;
}

function paintFit(name, text, width, align = 'left') {
  return color(name, fit(text, width, align));
}

function centered(text, width) {
  const raw = String(text);
  if (raw.length >= width) return fit(raw, width);
  const left = Math.floor((width - raw.length) / 2);
  return ' '.repeat(left) + raw + ' '.repeat(width - raw.length - left);
}

function box(title, content, width, titleColor = 'brightCyan') {
  const safeWidth = Math.max(18, width);
  const inner = safeWidth - 2;
  const lines = [
    color(titleColor, `+${'-'.repeat(inner)}+`),
    color(titleColor, `|`) + color('bold', centered(` ${title} `, inner)) + color(titleColor, '|'),
    color(titleColor, `+${'-'.repeat(inner)}+`),
  ];
  for (const line of content) {
    const raw = visibleLength(line);
    const trimmed = raw > inner ? fit(line.replace(/\u001b\[[0-?]*[ -/]*[@-~]/g, ''), inner) : line;
    lines.push(color(titleColor, '|') + trimmed + ' '.repeat(Math.max(0, inner - visibleLength(trimmed))) + color(titleColor, '|'));
  }
  lines.push(color(titleColor, `+${'-'.repeat(inner)}+`));
  return lines;
}

function pairLine(label, left, right, width, leftColor = 'white', rightColor = 'brightCyan') {
  const gap = ' ';
  const half = Math.floor((width - gap.length) / 2);
  return paintFit(leftColor, `${label}${value(left)}`, half) + gap + paintFit(rightColor, value(right), width - half - gap.length);
}

function promptColor(text) {
  const lower = String(text).toLowerCase();
  if (!text) return 'gray';
  if (/error|fail|bust|unable|denied|no trade/.test(lower)) return 'brightRed';
  if (/command|sector|prompt|\?\s*[:)]/.test(lower)) return 'brightCyan';
  if (/warps|credits|fighters|holds|port|class/.test(lower)) return 'brightGreen';
  return 'white';
}

function gameLines(status, width) {
  const lines = [];
  const rx = value(status.lastRxLine, 'Waiting for server text...');
  const prompt = value(status.lastPromptLine, 'No command prompt captured.');
  const tx = value(status.lastTx, '(no transmission)');
  lines.push(paintFit('gray', ' TRADEWARS 2002 // LIVE TELNET MIRROR', width));
  lines.push(paintFit('gray', ` ${status.host || 'unknown'}:${status.port || 23}  character: ${value(status.username)}`, width));
  lines.push(paintFit(promptColor(rx), ` ${rx}`, width));
  lines.push(paintFit(promptColor(prompt), ` ${prompt}`, width));
  lines.push(paintFit('brightYellow', ` > ${tx}`, width));
  lines.push(paintFit('gray', ' ' + '-'.repeat(Math.max(1, width - 1)), width));

  const events = Array.isArray(status.events && status.events.recent) ? status.events.recent : [];
  for (const event of events.slice(-Math.max(3, Math.floor((width > 70 ? 8 : 5))))) {
    const time = event.at ? new Date(event.at).toLocaleTimeString('en-US', { hour12: false }) : '--:--:--';
    const payload = event.payload || {};
    let detail = event.type || 'event';
    if (payload.reason) detail += ` ${payload.reason}`;
    else if (payload.toAction) detail += ` -> ${payload.toAction}`;
    lines.push(paintFit(event.level === 'error' ? 'brightRed' : 'cyan', ` ${time} ${detail}`, width));
  }
  return lines;
}

function sectorDisplayLines(stats) {
  const sector = value(stats.sector);
  const space = stats.explored === false ? 'uncharted' : stats.explored === true ? 'charted' : 'unknown';
  const warps = Array.isArray(stats.warps) ? stats.warps.filter((warp) => Number(warp) > 0) : [];
  const unsafe = Array.isArray(stats.unsafeWarps) ? stats.unsafeWarps.filter((warp) => Number(warp) > 0) : [];
  const lines = [
    `  Sector  : ${sector} in ${space} space.`,
  ];
  if (Number(stats.portClass) > 0) {
    const profile = value(stats.portProfile, '---');
    lines.push(`  Ports   : Class ${value(stats.portClass)} (${profile})`);
  } else {
    lines.push('  Ports   : No port in this sector.');
  }
  lines.push(`  Warps to Sector(s) :  ${warps.join(' - ') || '-'}`);
  if (unsafe.length > 0) {
    lines.push(`  Unsafe warps        :  ${unsafe.join(' - ')}`);
  }
  return lines;
}

function screenTitle(title, width) {
  const line = `-=-- ${title} -=-`;
  return [
    fit(line, width),
    fit('', width),
  ];
}

function gamePrompt(status, width) {
  const sector = value(status.stats && status.stats.sector);
  return fit(`Command [TL=00:00:00]:[${sector}] (?=Help)? : ${value(status.lastTx, '')}`, width);
}

function sectorScreenLines(status, width) {
  const stats = status.stats || {};
  const routing = status.routing || {};
  const lines = [
    ...screenTitle('Sector Display', width),
    ...sectorDisplayLines(stats).map((line) => fit(line, width)),
  ];
  if (routing.previousSector !== undefined && routing.previousSector !== null) {
    lines.push(fit(`Previous Sector       : ${value(routing.previousSector)}`, width));
  }
  lines.push(fit(`Navigation Status     : ${value(status.phase)} / ${value(status.currentAction)}`, width));
  lines.push(fit(`Last Server Update    : ${value(status.updatedAt)} (${formatAge(status.updatedAt)})`, width));
  lines.push(fit('', width));
  lines.push(fit('Command [Sector] (?=Help)? :', width));
  return lines;
}

function shipInfoScreenLines(status, width) {
  const stats = status.stats || {};
  const dock = status.stardock || {};
  const upgrades = status.upgrades || {};
  const cargo = `${value(stats.fuelHolds, 0)} Fuel Ore  ${value(stats.organicsHolds, 0)} Organics  ${value(stats.equipmentHolds, 0)} Equipment`;
  return [
    ...screenTitle('Ship Information', width),
    fit(`Trader Name    : ${value(status.username)}`, width),
    fit(`Rank and Exp    : ${formatNumber(stats.exp)} points, Alignment=${value(stats.alignment)}`, width),
    fit(`Ship Name       : ${value(stats.shipName, 'Galileo')}`, width),
    fit(`Ship Info       : ${value(stats.shipType)}`, width),
    fit(`Ship ID         : ${value(stats.shipId)}`, width),
    fit(`Current Sector  : ${value(stats.sector)}`, width),
    fit(`Total Holds     : ${value(stats.holds)} - ${cargo}`, width),
    fit(`Empty Holds     : ${value(stats.emptyHolds)}    Cargo Holds: ${value(stats.cargoHolds)}`, width),
    fit(`Fighters        : ${formatNumber(stats.fighters)}`, width),
    fit(`Shield points   : ${formatNumber(stats.shields)}`, width),
    fit(`LongRange Scan  : ${value(stats.scannerType)}`, width),
    fit(`Credits         : ${formatNumber(stats.credits)}`, width),
    fit(`StarDock        : ${dock.atStardock ? 'DOCKED' : 'Away'} (${value(dock.sector)})`, width),
    fit(`Upgrade         : ${upgrades.needsHoloScanner ? 'Holographic scanner needed' : 'Scanner ready'}`, width),
    fit('', width),
    fit('Command [Ship Information] (?=Help)? :', width),
  ];
}

function computerScreenLines(status, width) {
  const stats = status.stats || {};
  const routing = status.routing || {};
  const trade = status.trade || {};
  const cim = status.cim || {};
  return [
    ...screenTitle('Computer', width),
    fit('<Computer activated>', width),
    fit(`Current Sector : ${value(stats.sector)}`, width),
    fit(`Ship           : ${value(stats.shipType)}`, width),
    fit(`StarDock       : ${value(status.stardock && status.stardock.sector)}`, width),
    fit(`Known Sectors  : ${formatNumber(status.intel && status.intel.knownSectors)}`, width),
    fit(`Known Warps    : ${formatNumber(status.intel && status.intel.sectorsWithWarps)}`, width),
    fit(`CIM Status     : ${cim.active ? `Active (${value(cim.mode)})` : 'Ready'}`, width),
    fit(`Best Trade Pair: ${routing.tradePairMap && routing.tradePairMap.topPairs && routing.tradePairMap.topPairs[0] ? `${routing.tradePairMap.topPairs[0].a} - ${routing.tradePairMap.topPairs[0].b}` : 'Searching'}`, width),
    fit(`Deals / Net    : ${value(trade.successfulDeals, 0)} / ${formatNumber(trade.netCredits)}`, width),
    fit('', width),
    fit('C  Onboard Computer       I  Ship Information', width),
    fit('V  Game Status             P  Port and Trade', width),
    fit('M  Move to a Sector        S  Long Range Scan', width),
    fit('Q  Quit and Exit', width),
    fit('', width),
    fit('Computer command (?=Help) :', width),
  ];
}

function operationsScreenLines(status, width) {
  const supervisor = status.supervisor || {};
  const startup = status.startupSurvey || {};
  const cim = status.cim || {};
  const upgrades = status.upgrades || {};
  const events = Array.isArray(status.events && status.events.recent) ? status.events.recent : [];
  const lines = [
    ...screenTitle('Bot Operations / Radio Log', width),
    fit(`Action          : ${value(status.currentAction)}`, width),
    fit(`Phase           : ${value(status.phase)}`, width),
    fit(`Startup         : ${value(startup.executed, 0)}/${value(startup.total, 0)} ${startup.done ? 'complete' : 'active'}`, width),
    fit(`CIM             : ${cim.active ? `ACTIVE ${value(cim.mode)}` : 'READY'}  Warp age ${value(cim.warpCimAgeMs)}`, width),
    fit(`Safety          : ${supervisor.freezeDetected ? 'FREEZE DETECTED' : 'CLEAR'}`, width),
    fit(`Last RX         : ${value(status.lastRxLine)}`, width),
    fit(`Last TX         : ${value(status.lastTx)}`, width),
    fit('', width),
    'Recent events:',
  ];
  for (const event of events.slice(-6)) {
    const time = event.at ? new Date(event.at).toLocaleTimeString('en-US', { hour12: false }) : '--:--:--';
    lines.push(fit(` ${time} ${event.type || 'event'} ${event.payload && (event.payload.reason || event.payload.toAction || '')}`, width));
  }
  lines.push(fit('', width), fit('Command [Operations] (?=Help) :', width));
  return lines;
}

function botOverlayLines(status, width) {
  const stats = status.stats || {};
  const routing = status.routing || {};
  const trade = status.trade || {};
  const cim = status.cim || {};
  const dock = status.stardock || {};
  const supervisor = status.supervisor || {};
  const startup = status.startupSurvey || {};
  const upgrades = status.upgrades || {};
  const intel = status.intel || {};
  const topWarp = Array.isArray(routing.topWarpCandidates) ? routing.topWarpCandidates[0] : null;
  const topPair = routing.tradePairMap && Array.isArray(routing.tradePairMap.topPairs)
    ? routing.tradePairMap.topPairs[0]
    : null;
  const probe = /probe/i.test(value(stats.shipType, ''));
  const decision = value(status.note, value(status.currentAction, 'No active decision'));
  const lines = [
    '+=-- BOT OVERLAY // NAVIGATION, TRADE & DECISIONS --=+',
    `Navigation : sector ${value(stats.sector)} -> ${topWarp ? `${topWarp.sector} (score ${topWarp.score})` : 'no scored warp'}  recent ${(routing.recentSectors || []).slice(-5).join(' > ') || '-'}`,
    `Route      : ${value(status.currentAction)}  previous ${value(routing.previousSector)}  failed ${(routing.failedWarpTargets || []).length}`,
    `Trade      : pair ${topPair ? `${topPair.a}-${topPair.b}` : 'searching'}  score ${topPair ? topPair.totalScore : '-'}  pairs ${value(routing.tradePairMap && routing.tradePairMap.pairCount, 0)}`,
    `Economy    : credits ${formatNumber(stats.credits)}  deals ${value(trade.successfulDeals, 0)}  net ${formatNumber(trade.netCredits)}  holds ${value(stats.emptyHolds, 0)}/${value(stats.holds, 0)} empty`,
    `Intel      : ${formatNumber(intel.knownSectors)} sectors  ${formatNumber(intel.sectorsWithPorts)} ports  ${formatNumber(intel.sectorsWithWarps)} warp maps`,
    `CIM        : ${cim.active ? `ACTIVE ${value(cim.mode)}` : 'idle'}  warp ${cim.lastWarpCimAtMs ? formatAge(new Date(cim.lastWarpCimAtMs).toISOString()) : '-'}  port ${cim.lastPortCimAtMs ? formatAge(new Date(cim.lastPortCimAtMs).toISOString()) : '-'}  safe ${cim.guards && cim.guards.safeNow ? 'yes' : 'no'}`,
    `Recovery   : ${probe ? 'ESCAPE POD' : 'normal ship'}  StarDock ${dock.atStardock ? 'DOCKED' : 'away'} @ ${value(dock.sector)}  blocked ${value(dock.blockedCount, 0)}`,
    `Process    : ${value(status.phase)}  startup ${value(startup.executed, 0)}/${value(startup.total, 0)}  safety ${supervisor.freezeDetected ? 'FREEZE' : 'clear'}`,
    `Consider    : ${decision}`,
    `Upgrade    : budget ${formatNumber(upgrades.budgetNow)}  holo ${upgrades.needsHoloScanner ? 'needed' : 'ready'}  threshold ${value(upgrades.triggerCredits, 'n/a')}`,
    '+=-- read-only telemetry; native screens above are server-shaped --=+',
  ];
  return lines.map((line) => fit(line, width));
}

function colorScreenLine(line) {
  const raw = String(line);
  const plain = raw.trim();
  if (plain.startsWith('+=') || plain.startsWith('-=') || plain.startsWith('Trade Wars')) return color('brightCyan', raw);
  if (/Command|command|Views:|read-only/.test(plain)) return color('brightYellow', raw);
  if (/FREEZE|failed [1-9]|blocked [1-9]/i.test(plain)) return color('brightRed', raw);
  if (/CIM\s+: ACTIVE|DOCKED|safe yes|clear|score [1-9]/i.test(plain)) return color('brightGreen', raw);
  if (/Consider|Process|Recovery|Upgrade/.test(plain)) return color('yellow', raw);
  if (/Navigation|Route|Trade|Economy|Intel|CIM/.test(plain)) return color('brightCyan', raw);
  return color('white', raw);
}

function navigationPanelLines(status) {
  const stats = status.stats || {};
  const routing = status.routing || {};
  const trade = status.trade || {};
  const intel = status.intel || {};
  const topWarp = Array.isArray(routing.topWarpCandidates) ? routing.topWarpCandidates[0] : null;
  const topPair = routing.tradePairMap && Array.isArray(routing.tradePairMap.topPairs)
    ? routing.tradePairMap.topPairs[0]
    : null;
  return [
    `Sector       ${value(stats.sector)}  prev ${value(routing.previousSector)}`,
    `Warps        ${(stats.warps || []).join(' - ') || '-'}`,
    `Unsafe       ${(stats.unsafeWarps || []).join(' - ') || 'none'}`,
    `Next scored  ${topWarp ? `${topWarp.sector} / ${topWarp.score}` : 'none'}`,
    `Recent       ${(routing.recentSectors || []).slice(-5).join(' > ') || '-'}`,
    `Failed       ${(routing.failedWarpTargets || []).join(', ') || 'none'}`,
    `Trade pair   ${topPair ? `${topPair.a}-${topPair.b}` : 'searching'}`,
    `Pair score   ${topPair ? topPair.totalScore : '-'}  count ${value(routing.tradePairMap && routing.tradePairMap.pairCount, 0)}`,
    `Deals / net  ${value(trade.successfulDeals, 0)} / ${formatNumber(trade.netCredits)}`,
    `Intel        ${formatNumber(intel.knownSectors)} sectors / ${formatNumber(intel.sectorsWithPorts)} ports`,
  ];
}

function processingPanelLines(status) {
  const stats = status.stats || {};
  const cim = status.cim || {};
  const dock = status.stardock || {};
  const startup = status.startupSurvey || {};
  const supervisor = status.supervisor || {};
  const upgrades = status.upgrades || {};
  const probe = /probe/i.test(value(stats.shipType, ''));
  const tasks = [
    `ACTIVE   ${value(status.currentAction)}`,
    `PHASE    ${value(status.phase)}  ${value(status.note, '')}`,
    `${probe ? 'READY    ' : 'CHECK    '}Recovery ${probe ? 'escape pod route' : 'normal ship loop'}`,
    `${cim.active ? 'RUNNING  ' : 'WAITING  '}CIM ${cim.active ? value(cim.mode) : 'freshness gate'}`,
    `${dock.atStardock ? 'READY    ' : 'HOLD     '}StarDock ${dock.atStardock ? 'menu action' : `route @ ${value(dock.sector)}`}`,
    `${startup.done ? 'DONE     ' : 'PENDING  '}Startup survey ${value(startup.executed, 0)}/${value(startup.total, 0)}`,
    `${supervisor.freezeDetected ? 'ALERT    ' : 'WATCH    '}Supervisor ${supervisor.freezeDetected ? 'freeze recovery' : 'progress monitor'}`,
    `${upgrades.needsHoloScanner ? 'QUEUE    ' : 'SKIP     '}Scanner upgrade ${upgrades.needsHoloScanner ? 'needed' : 'not required'}`,
    `CREDITS  ${formatNumber(stats.credits)}  holds ${value(stats.emptyHolds, 0)}/${value(stats.holds, 0)} empty`,
  ];
  return tasks;
}

function gamePanelLines(status) {
  const stats = status.stats || {};
  const dock = status.stardock || {};
  return [
    ...sectorDisplayLines(stats),
    '',
    `Ship      : ${value(stats.shipType)}`,
    `Credits   : ${formatNumber(stats.credits)}`,
    `Holds     : ${value(stats.emptyHolds, 0)} empty / ${value(stats.holds, 0)} total`,
    `Fighters  : ${formatNumber(stats.fighters)}  Shields ${formatNumber(stats.shields)}`,
    `Scanner   : ${value(stats.scannerType)}`,
    `StarDock  : ${dock.atStardock ? 'DOCKED' : 'away'} @ ${value(dock.sector)}`,
    '',
    `RX        : ${value(status.lastRxLine)}`,
    `TX        : ${value(status.lastTx)}`,
    `Prompt    : ${value(status.lastPromptLine)}`,
  ];
}

function consoleColumns(status, width) {
  const gap = 1;
  const leftWidth = Math.max(40, Math.floor((width - gap) * 0.48));
  const rightWidth = width - gap - leftWidth;
  const left = box('GAME TERMINAL / SHIP STATE', gamePanelLines(status), leftWidth, 'brightCyan');
  const rightTop = box('NAVIGATION / TRADE', navigationPanelLines(status), rightWidth, 'brightGreen');
  const rightBottom = box('PROCESSING / TASKS UNDER CONSIDERATION', processingPanelLines(status), rightWidth, 'yellow');
  const right = [...rightTop, ...rightBottom];
  const height = Math.max(left.length, right.length);
  const lines = [];
  for (let index = 0; index < height; index += 1) {
    lines.push(`${left[index] || ' '.repeat(leftWidth)} ${right[index] || ' '.repeat(rightWidth)}`);
  }
  return lines;
}

function sessionFooter(width) {
  return [
    fit('', width),
    fit(`Watcher uptime ${uptime()}  |  updates ${updateCount}  |  hops ${hopCount}  |  read errors ${readErrors}`, width),
    fit('Views: 0 all  1 sector  2 ship information  3 computer  4 operations  |  r redisplay  q quit', width),
  ];
}

function render(status) {
  const columns = process.stdout.columns || 120;
  const rows = process.stdout.rows || 40;
  const width = Math.max(60, columns);
  const contentWidth = Math.max(58, width - 1);
  const body = [
    color('brightCyan', fit('Trade Wars 2002 // Live Game Display', contentWidth)),
    color('gray', fit(`Game A  ${value(status.host)}:${value(status.port)}  Player: ${value(status.username)}  View: ${viewMode}`, contentWidth)),
    color('brightYellow', fit(`Last command: ${value(status.lastTx, '(none)')}  Server: ${value(status.lastRxLine, '(waiting)')}`, contentWidth)),
    fit('', contentWidth),
  ];

  if (viewMode === 'all' && columns >= 100) {
    body.push(...consoleColumns(status, contentWidth));
  } else if (viewMode === 'all' || viewMode === 'sector') {
    body.push(...sectorScreenLines(status, contentWidth));
    if (viewMode === 'all') body.push(...botOverlayLines(status, contentWidth));
  }
  if (viewMode !== 'all') {
    if (viewMode === 'ship') body.push(...shipInfoScreenLines(status, contentWidth));
    if (viewMode === 'computer') body.push(...computerScreenLines(status, contentWidth));
    if (viewMode === 'operations') body.push(...operationsScreenLines(status, contentWidth));
  }
  body.push(...sessionFooter(contentWidth));

  const visibleRows = Math.max(8, rows - 1);
  process.stdout.write(`${ESC}2J${ESC}H${body.slice(0, visibleRows).map(colorScreenLine).join('\n')}${ESC}0m`);
}

function readSnapshot() {
  try {
    const parsed = JSON.parse(fs.readFileSync(statusFile, 'utf8'));
    if (parsed.updatedAt !== lastUpdatedAt) {
      updateCount += 1;
      if (lastSector !== null && parsed.stats && parsed.stats.sector !== lastSector) hopCount += 1;
      lastSector = parsed.stats ? parsed.stats.sector : lastSector;
      lastUpdatedAt = parsed.updatedAt || lastUpdatedAt;
    }
    snapshot = parsed;
  } catch (error) {
    readErrors += 1;
  }
}

function draw() {
  readSnapshot();
  const status = snapshot || {
    phase: 'waiting', currentAction: 'status-file-not-found', host: '-', port: 23,
    stats: {}, routing: {}, events: { recent: [] }, mode: {}, stardock: {}, startupSurvey: {},
  };
  render(status);
}

function shutdown() {
  if (timer) clearInterval(timer);
  if (process.stdin.isTTY) process.stdin.setRawMode(false);
  process.stdin.pause();
  process.stdout.write(`${ESC}0m${ESC}?25h${ESC}r\n`);
}

process.stdout.write(`${ESC}?1049h${ESC}?25l`);
process.on('SIGINT', shutdown);
process.on('SIGTERM', shutdown);
process.stdout.on('resize', draw);
if (!once && process.stdin.isTTY) {
  readline.emitKeypressEvents(process.stdin);
  process.stdin.setRawMode(true);
  process.stdin.on('keypress', (_input, key) => {
    if (key && (key.name === 'q' || (key.ctrl && key.name === 'c'))) shutdown();
    if (key && key.name === 'r') draw();
    if (key && (key.name === '0' || key.sequence === '0')) { viewMode = 'all'; draw(); }
    if (key && (key.name === '1' || key.sequence === '1')) { viewMode = 'sector'; draw(); }
    if (key && (key.name === '2' || key.sequence === '2')) { viewMode = 'ship'; draw(); }
    if (key && (key.name === '3' || key.sequence === '3')) { viewMode = 'computer'; draw(); }
    if (key && (key.name === '4' || key.sequence === '4')) { viewMode = 'operations'; draw(); }
  });
}
draw();
if (once) {
  shutdown();
} else {
  timer = setInterval(draw, refreshMs);
}
