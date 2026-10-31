const fs = require("node:fs");
const { execFileSync } = require("node:child_process");
const path = require("node:path");

const root = path.join(__dirname, "..");
const statusPath = path.join(__dirname, "logs", "192.168.4.75.status.json");
const eventsPath = path.join(__dirname, "logs", "events.jsonl");
const processJson = execFileSync("powershell.exe", [
  "-NoProfile",
  "-Command",
  "Get-CimInstance Win32_Process | Where-Object { $_.Name -eq 'node.exe' -and $_.CommandLine -match 'autonomous-bot[\\\\/]((bot|trade-wars-watcher)\\.js)' } | Select-Object ProcessId,CommandLine | ConvertTo-Json -Compress",
], { cwd: root, encoding: "utf8" }).trim();

const processes = processJson ? JSON.parse(processJson) : [];
const normalizedProcesses = Array.isArray(processes) ? processes : [processes];
const status = JSON.parse(fs.readFileSync(statusPath, "utf8"));
const eventBytes = fs.statSync(eventsPath).size;
const result = {
  botProcesses: normalizedProcesses.filter((process) => /autonomous-bot[\\/]bot\.js/.test(process.CommandLine || "")).length,
  watcherProcesses: normalizedProcesses.filter((process) => /trade-wars-watcher\.js/.test(process.CommandLine || "")).length,
  statusPhase: status.phase,
  statusPendingWrites: status.timing && status.timing.pendingPersistenceWrites,
  statusDroppedWrites: status.timing && status.timing.droppedPersistenceWrites,
  eventBytes,
};

console.log(JSON.stringify(result));
if (result.watcherProcesses < 1 || result.statusDroppedWrites !== 0 || result.eventBytes < 1) {
  process.exitCode = 1;
}
