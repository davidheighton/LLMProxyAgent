#!/usr/bin/env node
"use strict";

const crypto = require("node:crypto");
const fs = require("node:fs");
const path = require("node:path");
const { describeMacro } = require("./macro-semantics");

const workspaceRoot = path.resolve(__dirname, "..");
const defaultRoots = [
  "unencrypted-scripts",
  "__mom_bot3_1045",
  "TWX30-main/scripts",
  "TWX30-main/include",
  "mombot",
  "Pink-Scripts",
  "TWX-Scripts",
  "Zed/Scripts",
];

const familyRules = [
  ["combat", /kill|combat|photon|torpedo|attack|citkill|pdrop/i],
  ["defense", /grid|fighter|mine|sentinel|surround|reloader|defend/i],
  ["trading", /trade|merchant|merch|port|sst|ssm|psst|worldtrade|haggle/i],
  ["refit", /refurb|furb|upgrade|restock|fillship|scanner|hardware/i],
  ["navigation", /move|warp|wander|mow|twarp|bwarp|route|sector|map|xenter/i],
  ["intel", /cim|update|status|scan|probe|density|figs|search|dump/i],
  ["planets", /planet|colon|terra|citadel|land|orbit/i],
  ["operations", /login|connect|switchboard|help|daemon|startup|keepalive/i],
];

function sha256(content) {
  return crypto.createHash("sha256").update(content).digest("hex").toUpperCase();
}

function relative(filePath) {
  return path.relative(workspaceRoot, filePath).replace(/\\/g, "/");
}

function tokenizeArguments(text) {
  return [...String(text || "").matchAll(/"(?:[^"]|"")*"|\S+/g)].map((match) => {
    const token = match[0];
    if (token.startsWith('"') && token.endsWith('"')) {
      return {
        raw: token,
        value: token.slice(1, -1).replace(/""/g, '"'),
        literal: true,
      };
    }
    return { raw: token, value: token, literal: false };
  });
}

function parseMenuSource(filePath, content) {
  const lines = content.split(/\r?\n/);
  const menus = [];
  const menuHelp = [];
  const menuValues = [];
  const openMenus = [];
  const labels = new Map();
  const macroSequences = [];
  const inputOperations = [];

  for (let index = 0; index < lines.length; index += 1) {
    const line = lines[index];
    const lineNumber = index + 1;
    const labelMatch = line.match(/^\s*:([A-Za-z0-9_~.-]+)/);
    if (labelMatch) {
      labels.set(labelMatch[1].toLowerCase(), lineNumber);
    }

    const menuMatch = line.match(/^\s*addMenu\s+(.+?)\s*$/i);
    if (menuMatch) {
      const args = tokenizeArguments(menuMatch[1]);
      menus.push({
        line: lineNumber,
        parent: args[0] ? args[0].value : "",
        name: args[1] ? args[1].value : "",
        label: args[2] ? args[2].value : "",
        key: args[3] ? args[3].value : "",
        action: args[4] ? args[4].value : "",
        help: args[5] ? args[5].value : "",
        tailFlag: args[6] ? args[6].value : null,
        tailFlagLiteral: Boolean(args[6] && args[6].literal),
        dynamicFields: args.filter((arg) => !arg.literal).map((arg) => arg.raw),
      });
    }

    const helpMatch = line.match(/^\s*setMenuHelp\s+(.+?)\s*$/i);
    if (helpMatch) {
      const args = tokenizeArguments(helpMatch[1]);
      menuHelp.push({
        line: lineNumber,
        name: args[0] ? args[0].value : "",
        text: args[1] ? args[1].value : "",
        dynamicFields: args.filter((arg) => !arg.literal).map((arg) => arg.raw),
      });
    }

    const valueMatch = line.match(/^\s*setMenuValue\s+(.+?)\s*$/i);
    if (valueMatch) {
      const args = tokenizeArguments(valueMatch[1]);
      menuValues.push({
        line: lineNumber,
        name: args[0] ? args[0].value : "",
        value: args[1] ? args[1].value : "",
        dynamic: Boolean(args[1] && !args[1].literal),
      });
    }

    const openMatch = line.match(/^\s*openMenu\s+(.+?)\s*$/i);
    if (openMatch) {
      const args = tokenizeArguments(openMatch[1]);
      openMenus.push({
        line: lineNumber,
        name: args[0] ? args[0].value : "",
        dynamic: Boolean(args[0] && !args[0].literal),
      });
    }

    const inputMatch = line.match(/^\s*(getConsoleInput|getInput)\s+(.+?)\s*$/i);
    if (inputMatch) {
      const args = tokenizeArguments(inputMatch[2]);
      inputOperations.push({
        line: lineNumber,
        operation: inputMatch[1],
        target: args[0] ? args[0].value : "",
        prompt: args[1] ? args[1].value : "",
      });
    }

    const sendMatch = line.match(/^\s*(send|sendln|sendchar|sendtext)\s+(.+?)\s*$/i);
    if (sendMatch) {
      const args = tokenizeArguments(sendMatch[2]);
      const expression = args[0] ? args[0].value : "";
      macroSequences.push({
        line: lineNumber,
        operation: sendMatch[1].toLowerCase(),
        expression,
        literal: Boolean(args[0] && args[0].literal),
        containsEnter: /\*/.test(expression),
        dynamicReferences: [...expression.matchAll(/\$[A-Za-z0-9_~.]+|#\d+/g)].map((match) => match[0]),
        semantics: describeMacro(expression),
      });
    }
  }

  const menuByName = new Map(menus.map((menu) => [menu.name.toLowerCase(), menu]));
  for (const menu of menus) {
    const action = String(menu.action || "").replace(/^:/, "").toLowerCase();
    menu.handlerLine = labels.get(action) || null;
    menu.helpEntries = menuHelp.filter((help) => help.name.toLowerCase() === menu.name.toLowerCase());
    menu.valueEntries = menuValues.filter((value) => value.name.toLowerCase() === menu.name.toLowerCase());
    menu.parentKnown = !menu.parent || menuByName.has(menu.parent.toLowerCase()) || menu.parent.toUpperCase() === "TWX_MAIN";
  }

  return {
    path: relative(filePath),
    menus,
    menuHelp,
    menuValues,
    openMenus,
    inputOperations,
    macroSequences,
    menuCount: menus.length,
    macroCount: macroSequences.length,
  };
}

function classify(filePath, content, menuSource = parseMenuSource(filePath, content)) {
  const haystack = `${path.basename(filePath)}\n${content}`;
  const families = familyRules.filter(([, pattern]) => pattern.test(haystack)).map(([name]) => name);
  const labels = [...content.matchAll(/^\s*:([A-Za-z0-9_~.-]+)/gim)].map((match) => match[1]);
  const includes = [...content.matchAll(/^\s*include\s+([^\s;]+)/gim)].map((match) => match[1]);
  const signals = {
    sendsCommands: /\b(send|sendln|sendchar|sendtext)\b/i.test(content),
    changesCredits: /\b(steal|rob|buy|sell|haggle|upgrade|refurb|fighter|shield|hold)\b/i.test(content),
    changesMap: /\b(cim|update|sector|warp|fig|mine|navhaz|density|dscan)\b/i.test(content),
    combatRisk: /\b(kill|attack|photon|torpedo|pdrop|rob|steal|surround)\b/i.test(content),
    destructive: /\b(self.?destruct|jettison|destroy|drop\s+fighters)\b/i.test(content),
  };

  return {
    path: relative(filePath),
    root: relative(filePath).split("/")[0] || "workspace",
    sizeBytes: Buffer.byteLength(content, "latin1"),
    lineCount: content.split(/\r?\n/).length,
    sha256: sha256(content),
    families: families.length > 0 ? families : ["uncategorized"],
    labels: [...new Set(labels)].sort(),
    includes: [...new Set(includes)].sort(),
    menuCount: menuSource.menuCount,
    macroCount: menuSource.macroCount,
    signals,
  };
}

function collectFiles() {
  const files = [];
  const visit = (directory) => {
    if (!fs.existsSync(directory)) return;
    for (const entry of fs.readdirSync(directory, { withFileTypes: true })) {
      const fullPath = path.join(directory, entry.name);
      if (entry.isDirectory()) visit(fullPath);
      else if (/\.ts$/i.test(entry.name)) files.push(fullPath);
    }
  };
  for (const root of defaultRoots) visit(path.join(workspaceRoot, root));
  return [...new Set(files.map((filePath) => path.resolve(filePath)))].sort((a, b) => a.localeCompare(b));
}

function main() {
  if (process.argv[2] === "--describe") {
    const macro = process.argv[3] || "";
    const startMenu = process.argv[4] || "unknown";
    console.log(JSON.stringify(describeMacro(macro, { startMenu }), null, 2));
    return;
  }
  const output = path.resolve(process.argv[2] || path.join(__dirname, "logs", "script-knowledge.json"));
  const files = collectFiles();
  const sources = files.map((filePath) => ({
    filePath,
    content: fs.readFileSync(filePath, "latin1"),
  }));
  const menuSources = sources.map(({ filePath, content }) => parseMenuSource(filePath, content));
  const scripts = sources.map(({ filePath, content }, index) => classify(filePath, content, menuSources[index]));
  const familyCounts = {};
  const rootCounts = {};
  for (const script of scripts) {
    for (const family of script.families) familyCounts[family] = (familyCounts[family] || 0) + 1;
    rootCounts[script.root] = (rootCounts[script.root] || 0) + 1;
  }

  const payload = {
    schemaVersion: 1,
    generatedAt: new Date().toISOString(),
    workspaceRoot,
    sourceRoots: defaultRoots,
    summary: {
      scriptCount: scripts.length,
      familyCounts,
      rootCounts,
      riskyScriptCount: scripts.filter((script) => script.signals.combatRisk || script.signals.destructive).length,
      menuScriptCount: scripts.filter((script) => script.menuCount > 0).length,
      macroSequenceCount: scripts.reduce((total, script) => total + script.macroCount, 0),
    },
    scripts,
  };
  fs.mkdirSync(path.dirname(output), { recursive: true });
  fs.writeFileSync(output, JSON.stringify(payload, null, 2));
  const menuOutput = path.join(path.dirname(output), "menu-knowledge.json");
  const menuPayload = {
    schemaVersion: 1,
    generatedAt: payload.generatedAt,
    workspaceRoot,
    sourceRoots: defaultRoots,
    summary: {
      scriptCount: menuSources.length,
      menuScriptCount: menuSources.filter((source) => source.menuCount > 0).length,
      menuCount: menuSources.reduce((total, source) => total + source.menuCount, 0),
      menuHelpCount: menuSources.reduce((total, source) => total + source.menuHelp.length, 0),
      menuValueCount: menuSources.reduce((total, source) => total + source.menuValues.length, 0),
      openMenuCount: menuSources.reduce((total, source) => total + source.openMenus.length, 0),
      macroSequenceCount: menuSources.reduce((total, source) => total + source.macroCount, 0),
      inputOperationCount: menuSources.reduce((total, source) => total + source.inputOperations.length, 0),
    },
    scripts: menuSources,
  };
  fs.writeFileSync(menuOutput, JSON.stringify(menuPayload, null, 2));
  console.log(`Learned ${scripts.length} scripts into ${output}`);
  console.log(`Learned menu and macro structures into ${menuOutput}`);
  console.log(JSON.stringify(payload.summary, null, 2));
}

main();