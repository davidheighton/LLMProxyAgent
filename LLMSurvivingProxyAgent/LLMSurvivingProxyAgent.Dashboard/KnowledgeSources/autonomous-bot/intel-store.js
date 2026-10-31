"use strict";

const fs = require("node:fs");
const path = require("node:path");
const { DatabaseSync } = require("node:sqlite");

class IntelStore {
  constructor(filePath) {
    this.filePath = filePath;
    fs.mkdirSync(path.dirname(filePath), { recursive: true });
    this.db = new DatabaseSync(filePath);
    this.db.exec("PRAGMA journal_mode = WAL; PRAGMA synchronous = NORMAL; PRAGMA busy_timeout = 2500;");
    this.db.exec(`
      CREATE TABLE IF NOT EXISTS sectors (
        sector INTEGER PRIMARY KEY,
        explored INTEGER,
        port_class INTEGER,
        port_profile TEXT NOT NULL DEFAULT '',
        warps_json TEXT NOT NULL DEFAULT '[]',
        unsafe_warps_json TEXT NOT NULL DEFAULT '[]',
        nav_haz REAL,
        market_json TEXT NOT NULL DEFAULT '{}',
        busted INTEGER NOT NULL DEFAULT 0,
        hostile INTEGER NOT NULL DEFAULT 0,
        last_seen_at INTEGER NOT NULL DEFAULT 0
      );
      CREATE INDEX IF NOT EXISTS idx_sectors_port_profile ON sectors(port_profile);
      CREATE INDEX IF NOT EXISTS idx_sectors_last_seen ON sectors(last_seen_at);
      CREATE INDEX IF NOT EXISTS idx_sectors_nav_haz ON sectors(nav_haz);
      CREATE TABLE IF NOT EXISTS metadata (
        key TEXT PRIMARY KEY,
        value TEXT NOT NULL
      );
    `);
    this.upsertStatement = this.db.prepare(`
      INSERT INTO sectors (
        sector, explored, port_class, port_profile, warps_json, unsafe_warps_json,
        nav_haz, market_json, busted, hostile, last_seen_at
      ) VALUES (?, ?, ?, ?, ?, ?, ?, ?, ?, ?, ?)
      ON CONFLICT(sector) DO UPDATE SET
        explored = excluded.explored,
        port_class = excluded.port_class,
        port_profile = excluded.port_profile,
        warps_json = excluded.warps_json,
        unsafe_warps_json = excluded.unsafe_warps_json,
        nav_haz = excluded.nav_haz,
        market_json = excluded.market_json,
        busted = excluded.busted,
        hostile = excluded.hostile,
        last_seen_at = excluded.last_seen_at
    `);
    this.selectAllStatement = this.db.prepare("SELECT * FROM sectors ORDER BY sector");
    this.countStatement = this.db.prepare("SELECT COUNT(*) AS count FROM sectors");
    this.metadataStatement = this.db.prepare("SELECT value FROM metadata WHERE key = ?");
    this.saveMetadataStatement = this.db.prepare(`
      INSERT INTO metadata (key, value) VALUES (?, ?)
      ON CONFLICT(key) DO UPDATE SET value = excluded.value
    `);
  }

  count() {
    return Number(this.countStatement.get().count || 0);
  }

  loadAll() {
    return this.selectAllStatement.all().map((row) => ({
      sector: Number(row.sector),
      explored: row.explored == null ? null : Boolean(row.explored),
      portClass: row.port_class == null ? null : Number(row.port_class),
      portProfile: String(row.port_profile || ""),
      warps: JSON.parse(row.warps_json || "[]"),
      unsafeWarps: JSON.parse(row.unsafe_warps_json || "[]"),
      navHaz: row.nav_haz == null ? null : Number(row.nav_haz),
      market: JSON.parse(row.market_json || "{}"),
      busted: Boolean(row.busted),
      hostile: Boolean(row.hostile),
      lastSeenAt: Number(row.last_seen_at || 0),
    }));
  }

  upsert(intel) {
    this.upsertStatement.run(
      intel.sector,
      intel.explored == null ? null : (intel.explored ? 1 : 0),
      intel.portClass,
      intel.portProfile || "",
      JSON.stringify(intel.warps || []),
      JSON.stringify(intel.unsafeWarps || []),
      intel.navHaz,
      JSON.stringify(intel.market || {}),
      intel.busted ? 1 : 0,
      intel.hostile ? 1 : 0,
      intel.lastSeenAt || 0,
    );
  }

  loadMetadata(key) {
    const row = this.metadataStatement.get(String(key));
    if (!row) {
      return null;
    }
    try {
      return JSON.parse(String(row.value));
    } catch {
      return null;
    }
  }

  saveMetadata(key, value) {
    this.saveMetadataStatement.run(String(key), JSON.stringify(value));
  }

  close() {
    this.db.close();
  }
}

module.exports = { IntelStore };