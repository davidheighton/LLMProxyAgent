# Autonomous TradeWars 2002 Bot (Node.js)

This is a standalone, unattended bot framework for TradeWars-like telnet servers.

It includes:

- Telnet connection handling (including basic negotiation responses).
- Prompt-driven login.
- Rule-based auto-responses for common game prompts.
- Independent action loop with cooldowns and turn-aware action filtering.
- Session logging for replay/tuning.
- TWX-style periodic CIM sync for port and warp map intelligence.

For source-backed account setup, login, logout, reconnect patterns, and current
provisioning gaps, see [LOGIN_SESSION_PROCEDURES.md](./LOGIN_SESSION_PROCEDURES.md).

## 1) Setup

1. Make sure Node.js 22.5+ is installed. The storage layer uses the built-in `node:sqlite` API.
2. Copy `config.example.json` to `config.json`.
3. Edit `config.json` with your host, credentials, prompt patterns, and actions.

PowerShell:

```powershell
Copy-Item .\config.example.json .\config.json
```

## 2) Run

From this folder:

```powershell
node .\bot.js .\config.json
```

Logs are written to `./logs/latest.log` (or your configured path).

The bot also records an append-only event journal at `./logs/events.jsonl` and
loads the generated script knowledge model from `./logs/script-knowledge.json`.
The learner also writes `./logs/menu-knowledge.json`, a source-linked database
of TWX/MOMBot/Pink/Zed menu definitions, parent/child relationships, keys,
handlers, help text, displayed values, menu entry points, input operations, and
literal or dynamic `send` command sequences. Dynamic expressions are retained
as source expressions rather than guessed values, so recovered macros can be
reviewed and adapted safely.

The runtime menu learner writes a host-specific graph to
`./logs/<host>.menu-knowledge.json`. It records observed main, Computer,
Corporate, and StarDock prompts, available keys, navigation paths, and every
prompt-aware key transition. Main-menu discovery is seeded with the standard
Trade Wars command table, including `C` for Computer, `T` for Corporate, and
`P` for Port/StarDock entry. Discovery is observe-only until a route is
explicitly promoted to a hardcoded action. Self-destruct commands are blocked
while a menu or discovery action is active and recorded in the database.
Live sector intelligence is stored in `./logs/intel.sqlite` using SQLite WAL
mode and indexed per-sector upserts; the JSON file remains a readable export.
Build or refresh that model from the workspace with:

```powershell
node .\learn-scripts.js
```

Configured macros are interpreted against the live menu before transmission.
The static learner can explain a sequence directly:

```powershell
node .\learn-scripts.js --describe VID main
```

For example, on the standard main command menu, `VID` means `V` (View Game
Status), `I` (Ship Information), and `D` (Re-Display Sector). The executor
sends one key, waits for the expected menu prompt, and only then advances to
the next key. Generic or context-invalid options are reported for review but
remain blocked instead of being sent blindly.

Compound commands are parsed as commands, not as unrelated letters. For
example, `cby` means Computer -> self-destruct, followed by the server prompt
`ARE YOU SURE CAPTAIN? (Y/N)`; `Y` completes the destructive action and `N`
aborts it. The autonomous bot may issue the configured `cby` request only when
its recovery gates allow it, and does not automatically send the confirmation.

All outbound input passes through the command-frame layer. Menu keys are exactly one printable key with no CR/LF. `*`-terminated game macros and raw caret commands are sent without an extra CR. Ordinary submitted answers such as quantities use an explicit CR terminator, while text frames are the only frames allowed to contain spaces. Empty input is never implicit: it is labeled `intentionalPauseClear` and sends exactly one CR to consume a confirmed `[Pause]` or equivalent Enter prompt. CR, LF, non-printable bytes, and unexpected whitespace are rejected before transmission.

Prompt recovery is bounded and prompt-aware. When the runtime is frozen, it may clear an observed pause or send one `Q` from a known submenu, then waits for a stable command prompt. It never emits the broad legacy MOMBot reset macros blindly; after three unsuccessful recovery steps it stops issuing recovery keys and requires a new observed prompt or operator intervention.

Computer settings are configured through `strategy.computerSettings`. The bot
opens `CN`, reads the live `(1) ANSI graphics` and `(B) Screen Pauses` values,
then sends each CN toggle only when a setting differs from the desired value.
The default unattended profile also sets CN9 `abortDisplay` to `all`, which
lets advanced macros abort long displays on any key; use `space` for scripts
that must not let ordinary macro keys abort output. Set `screenPauses` to `off`
to avoid pause prompts, or to `on` when pause consumption is intentional. CN4
defaults to `preserve` because changing the Sub-space radio channel is a
separate operational choice.

The learner scans recovered unencrypted scripts plus the TWX, MOMBot, Pink, and
Zed script trees. It records source hashes, routine families, labels, includes,
and risk signals without executing any script. Each macro record also contains
a token-by-token semantic translation with category, effect, safety, certainty,
and risk. `menu-confirmed` is exact; `menu-dependent` and `unknown` are
explanatory only and remain blocked by the runtime until the live menu proves
their meaning. Menu and macro records include source-relative line numbers to
support targeted review and new sequence design.

The process handles `SIGINT`/`SIGTERM` for a clean stop. If the telnet server
closes the connection, the action loop stops and the disconnect is recorded in
the status file and event log; restart the process to reconnect.

### TradeWars-style watcher

Run the read-only ANSI terminal dashboard in a second terminal while the bot is
running. It reads the configured status snapshot and renders a simulated game
text pane beside ship telemetry, navigation, trade, CIM, and safety data:

```powershell
node .\trade-wars-watcher.js
```

Use `q` or `Ctrl+C` to exit, `r` to refresh immediately, and `--status` to
watch a different status file. `--once` renders one snapshot for a quick
smoke test; `--no-color` disables ANSI color output.

### Operator control

The bot exposes a localhost-only control page on port `8799` by default:

```powershell
Start-Process http://127.0.0.1:8799/
```

Click **Take over** before sending a command. Takeover pauses the bot's action
loop and blocks automatic prompt responses while continuing to parse and log
server output. Use the command box to send a single key, submitted command, or
TradeWars macro such as `m10445*`. Click **Release to bot** when finished.

The interface refuses destructive commands such as `cby` unless the explicit
confirmation checkbox is selected. It is bound to `127.0.0.1`, so it does not
accept control connections from other machines. The status snapshot includes
the current owner mode and the last 20 operator commands.

You can also connect a TradeWars client directly to the bot's local telnet
bridge on port `2323`:

```text
Host: 127.0.0.1
Port: 2323
```

Only one bridge client is accepted. Connecting starts in monitor-only mode;
server text is mirrored back to the client and ordinary input is ignored. Enter
`TWCTRL-4F7A-TAKEOVER` followed by Enter to toggle control. The first use pauses
automation and forwards subsequent commands; the second releases control and
allows the bot to resume. Closing the client also releases control.

## 3) How Independence Works

The bot runs with this model:

1. Connect and watch incoming lines.
2. Match login prompts and send credentials.
3. Apply `autoResponses` when prompts are detected.
4. When a command prompt is detected, pick the next eligible action from `strategy.actions`.
5. If idle too long, send keepalive command.

## 4) Customization Tips

- Update `prompts.commandPrompt` so it matches your server exactly.
- Keep `minActionDelayMs` conservative to avoid command floods.
- Start with safe actions only (status checks, movement).
- Add one new action at a time and validate in logs.

## 5) CIM Backend Sync

The bot can run periodic computer pulls inspired by TWX update flows:

- Port data pull command (default): ^rq
- Warp data pull command (default): ^iq
- CIM commands are sent as literal caret sequences without CRLF. On TWGS 1.03+, `^` enters interrogation mode; `^rq` requests port data and `^iq` requests warp data.
- Completion marker: ENDINTERROG

Key strategy settings:

- strategy.cim.enabled
- strategy.cim.intervalMs
- strategy.cim.includeWarpData
- strategy.cim.portCommand
- strategy.cim.warpCommand
- strategy.cim.completionPattern
- strategy.cim.timeoutMs

The bot stores merged intelligence to the configured intel snapshot file and reloads it on startup.
This means mapping and port profile knowledge persist across reconnects.

## 6) Example Action Templates

## 7) LLM Decision-Making Research

See [LLM_GAMEPLAY_RESEARCH.md](./LLM_GAMEPLAY_RESEARCH.md) for research
findings and a staged, safety-first approach to evaluating an LLM as a
high-level strategy selector. No LLM is currently part of the live decision
loop.

Supported placeholders in action commands:

- `{randomSector}` random sector in configured range.
- `{sector}` last parsed sector.
- `{turns}` last parsed turns value.
- `{credits}` last parsed credits value.
- `{stealAmount}` computed goods steal size (default guideline: exp / 20, capped by holds).
- `{stealCredits}` computed credit steal ceiling (default guideline: exp * 10).

Steal/rob action safety knobs:

- `strategy.stealRequiresAlignment` (default `true`) enforces alignment gating.
- `strategy.stealAlignmentMax` (default `-100`) requires player alignment to be at or below this value.
- `strategy.stealHoldsExpDivisor` (default `20`) controls steal goods sizing.
- `strategy.stealCreditsMultiplier` (default `10`) controls steal credits sizing.

Trade pacing knobs:

- `strategy.trade.forcePromptHaggle` forces local trade actions to use prompt-driven `pt` (instead of star-padded `pt******`) so haggle offer logic is actually used.
- `strategy.trade.pairHarvest` enables bounded high-value pair sessions. The bot first uses a safe pair from cached SQLite intel, runs a port CIM only on a cache miss, explores when that CIM still produces no pair, alternates endpoints, and trades only the configured percentage.
- `strategy.trade.desiredCargo` sets preferred commodity to buy/load when holds are empty (fuel, organics, equipment).
- `strategy.trade.autoDesiredCargoByNeed` infers next buy target from current held cargo (for example, holding organics prefers buying equipment after selling out).
- `strategy.trade.localPortBurstMin` keeps the bot on local `pt` trades for this many action cycles before allowing warp-hop.
- `strategy.trade.moveOnRejectStreak` allows early warp-hop when repeated local trade rejections indicate a dry pair.
- `strategy.trade.noTradeSectorCooldownMs` temporarily suppresses local `pt` in sectors that report no possible transaction.

Dashboard visibility:

- Status output now includes `trade.localOpportunity` with hold composition, desired cargo, per-product sellability, desired buyability, and `canTradeNow` for the current port.

Upgrade policy knobs:

- `strategy.upgrades.triggerCredits` minimum credits before any upgrade/stardock actions run.
- `strategy.upgrades.minCashReserve` credits the bot always keeps on hand.
- `strategy.upgrades.maxSpendPerVisit` caps upgrade spend per visit.
- `strategy.upgrades.minSpendToUpgrade` prevents tiny/noisy purchases.
- `strategy.upgrades.autoPurchase` enables budget-based shield/fighter quantities for Stardock prompts.
- `strategy.upgrades.shieldUnitCost`, `strategy.upgrades.fighterUnitCost`, `strategy.upgrades.shieldSpendRatio`, `strategy.upgrades.fighterSpendRatio` tune budget allocation.
- `strategy.upgrades.holoScannerPriority` and `strategy.upgrades.holoScannerMinCredits` control optional Holo Scanner upgrades. Density Scanner is the minimum capability for WorldTrade and scan-before-warp safety; Holo also provides density scans and makes exploration faster and safer.
- `strategy.restock.fighterLowWatermark` and `strategy.restock.fighterMinimum` trigger defensive fighter restock up to the configured minimum.
- `strategy.restock.shieldLowWatermark` and `strategy.restock.shieldMinimum` trigger defensive shield restock up to the configured minimum.
- `strategy.restock.restockMinCredits` enables defensive restock only when enough cash exists; defensive purchases still preserve `strategy.upgrades.minCashReserve` and `maxSpendPerVisit`.
- `strategy.restock.routeCooldownMs` limits repeated defensive StarDock route discovery/retries; known StarDock hops are not delayed by this cooldown.
- Defensive restock is separate from `strategy.upgrades.triggerCredits`, so low fighter/shield stock can be repaired without lowering the normal upgrade gate.

Movement safety knobs (script-inspired):

- `strategy.movementSafety.layFightersOnTradeWarpHop` appends fighter drops on trade warp hops.
- `strategy.movementSafety.fightersPerDrop` controls per-hop fighter drop amount.
- `strategy.movementSafety.layOnlyOnGoodPairs` restricts fig-lay to sectors in strong pair routes.
- `strategy.movementSafety.layOnAdjacentGoodPairs` also allows fig-lay in sectors adjacent to strong pair routes.
- `strategy.movementSafety.maxNavHazToLay` blocks fig-lay in high navhaz sectors.

## 7) Safety and Rules

You are responsible for following your server rules and terms.
Use on private/test servers first before running on live games.

### Scanner-safe movement

When `strategy.movementSafety.scanBeforeWarp` is enabled, ordinary movement is held until a scanner preflight completes. A density scanner sends `sd`; a holographic scanner sends `shsd`. The bot refuses a target when the scan reports mines, limpets, armids, navhaz, or enemy fighters, records the target as unsafe, and waits for route selection to choose another hop. StarDock autopilot and operator commands remain explicit exceptions.

Do not start WorldTrade or ordinary unknown-sector exploration until fresh ship status verifies either a Density Scanner or a Holo Scanner on the active ship. If neither is installed, prioritize StarDock scanner acquisition. An Escape Pod or a ship with one or fewer holds must reacquire a suitable ship before trading.

### Fighter intelligence and grid safety

`strategy.fighterIntel.enabled` enables periodic `G` refreshes for deployed-fighter locations. Counts and ownership are stored with sector intel for future figwarp/transwarp routing. Automatic deployment remains disabled by default (`deployEnabled: false`); deployment must preserve the configured fighter reserve, avoid FedSpace and StarDock, and distinguish personal, corporate, defensive, toll, and offensive fighters before it is enabled.
