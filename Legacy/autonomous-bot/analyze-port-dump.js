#!/usr/bin/env node
"use strict";

const fs = require("fs");
const path = require("path");

const DEFAULT_LOG = path.join(__dirname, "logs", "192.168.4.75.log");
const TOP_N = Number(process.argv[3] || 25);
const logPath = process.argv[2] ? path.resolve(process.argv[2]) : DEFAULT_LOG;

function stripAnsi(input) {
  return String(input || "").replace(/\x1b\[[0-9;]*[A-Za-z]/g, "");
}

function cleanLine(raw) {
  let line = stripAnsi(raw).replace(/\r/g, "").trimEnd();

  // Keep only the actual game text after log prefixes like:
  // [RX-LINE  2026-07-03T...] Ports   : Foo, Class 3 (SBB)
  const idx = line.indexOf("] ");
  if (idx >= 0 && line.startsWith("[")) {
    line = line.slice(idx + 2);
  }

  return line;
}

function deriveClassFromProfile(profile) {
  const p = String(profile || "").toUpperCase();
  // Mapping compatible with TWX Pascal convention.
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
  return map[p] || 0;
}

function ensureSector(intel, sectorNum) {
  if (!intel.has(sectorNum)) {
    intel.set(sectorNum, {
      sector: sectorNum,
      portName: "",
      portClass: 0,
      portProfile: "",
      warps: new Set(),
    });
  }
  return intel.get(sectorNum);
}

function countComplements(a, b) {
  if (a.length !== 3 || b.length !== 3) {
    return 0;
  }

  let c = 0;
  for (let i = 0; i < 3; i += 1) {
    if ((a[i] === "S" && b[i] === "B") || (a[i] === "B" && b[i] === "S")) {
      c += 1;
    }
  }
  return c;
}

function isFuelComplement(a, b) {
  if (a.length !== 3 || b.length !== 3) {
    return false;
  }
  return (a[0] === "S" && b[0] === "B") || (a[0] === "B" && b[0] === "S");
}

function parseLog(fileContent) {
  const intel = new Map();
  let currentSector = 0;

  const rxSector = /^\s*Sector\s{1,4}:\s*(\d+)\b/i;
  const rxWarps = /^\s*Warps to Sector\(s\)\s*:\s*(.+)$/i;
  const rxPort = /^\s*Ports\s+:\s+(.+),\s+Class\s+(\d+)(?:\s*\(([BSbs]{3})\))?/i;

  const lines = fileContent.split("\n");
  for (const raw of lines) {
    const line = cleanLine(raw);
    if (!line) {
      continue;
    }

    const sectorMatch = line.match(rxSector);
    if (sectorMatch) {
      currentSector = Number(sectorMatch[1]);
      ensureSector(intel, currentSector);
      continue;
    }

    const warpsMatch = line.match(rxWarps);
    if (warpsMatch && currentSector > 0) {
      const origin = ensureSector(intel, currentSector);
      const tokens = warpsMatch[1].split(/[\s-]+/).filter(Boolean);
      for (const t of tokens) {
        const n = Number(String(t).replace(/[()]/g, ""));
        if (!Number.isFinite(n) || n <= 0) {
          continue;
        }
        origin.warps.add(n);
        ensureSector(intel, n).warps.add(currentSector);
      }
      continue;
    }

    const portMatch = line.match(rxPort);
    if (portMatch && currentSector > 0) {
      const s = ensureSector(intel, currentSector);
      const portName = String(portMatch[1] || "").trim();
      const portClass = Number(portMatch[2]) || 0;
      const portProfile = String(portMatch[3] || "").toUpperCase();

      s.portName = portName;
      s.portClass = portClass;
      s.portProfile = portProfile;

      if (!s.portClass && s.portProfile.length === 3) {
        s.portClass = deriveClassFromProfile(s.portProfile);
      }
    }
  }

  return intel;
}

function buildFuelFirstPairs(intel) {
  const pairs = [];
  const seen = new Set();

  for (const [sectorA, dataA] of intel.entries()) {
    if (!dataA.portProfile || dataA.portProfile.length !== 3) {
      continue;
    }

    for (const sectorB of dataA.warps) {
      const key = sectorA < sectorB ? `${sectorA}-${sectorB}` : `${sectorB}-${sectorA}`;
      if (seen.has(key)) {
        continue;
      }
      seen.add(key);

      const dataB = intel.get(sectorB);
      if (!dataB || !dataB.portProfile || dataB.portProfile.length !== 3) {
        continue;
      }

      // Skip special ports for normal trade-route pairing.
      if ([0, 9].includes(dataA.portClass) || [0, 9].includes(dataB.portClass)) {
        continue;
      }

      const a = dataA.portProfile;
      const b = dataB.portProfile;
      if (!isFuelComplement(a, b)) {
        continue;
      }

      const complements = countComplements(a, b);
      const extraComplements = Math.max(0, complements - 1);
      const score = 100 + (extraComplements * 25);

      pairs.push({
        sectorA,
        sectorB,
        profileA: a,
        profileB: b,
        classA: dataA.portClass,
        classB: dataB.portClass,
        nameA: dataA.portName,
        nameB: dataB.portName,
        complements,
        score,
      });
    }
  }

  pairs.sort((x, y) => y.score - x.score || y.complements - x.complements || x.sectorA - y.sectorA || x.sectorB - y.sectorB);
  return pairs;
}

function main() {
  if (!fs.existsSync(logPath)) {
    console.error(`Log file not found: ${logPath}`);
    process.exit(1);
  }

  const buf = fs.readFileSync(logPath);
  const content = buf.toString("latin1");

  const intel = parseLog(content);
  const allSectors = [...intel.values()];
  const withPorts = allSectors.filter((s) => s.portProfile.length === 3);
  const pairs = buildFuelFirstPairs(intel);

  const report = {
    sourceLog: logPath,
    scannedSectors: allSectors.length,
    sectorsWithPortProfiles: withPorts.length,
    discoveredFuelFirstPairs: pairs.length,
    topFuelPairs: pairs.slice(0, TOP_N),
  };

  console.log(JSON.stringify(report, null, 2));
}

main();
