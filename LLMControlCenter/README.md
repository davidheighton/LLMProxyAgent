# LLM Control Center

LLMControlCenter is the primary application and composition root for the
TradeWars client, TWX proxy/interpreter/database, agent runtime, and management
studio. The desktop client owns the live MTC session and its TWX `ModDatabase`;
the embedded dashboard owns agent objectives, model settings, knowledge,
events, and durable agent records. Agent decisions use the live client context
and send approved actions back through the same in-process MTC/TWX session.
There is no second game proxy or server connection inside the embedded host.

The Control Center opens to the **Fleet overview**. Its navigation groups
controller-wide views (Fleet, Agents, Knowledge, Activity, and System Settings)
with the active agent workspace (Overview, Live Terminal, Runtime, Strategy,
Objectives, Events, and Session Settings). The embedded dashboard runs on a
loopback-only ephemeral port and hides its duplicate navigation so the desktop
shell remains the single control surface. On startup, when not already
connected, MTC loads the most recent saved game profile before connecting so
stale in-memory relay settings are not reused. It retries the game connection
until it succeeds; if no profile is available, it prompts for one and continues
waiting for a connection.
Independent simultaneous MTC game sessions are not yet implemented.

The shared runtime boundary keeps each subsystem's source of truth clear:

- MTC owns the terminal, live connection lifecycle, local TWX interpreter,
  active script inventory, and command dispatch.
- TWXProxy.Core owns the game database and legacy script/macro execution
  semantics; the agent reads current and adjacent sector knowledge from that
  live database rather than maintaining a competing copy.
- The agent platform owns strategic objectives, reactive/strategic records,
  model configuration, and reviewed knowledge in its separate persistent
  stores. Those records do not replace live TWX observations.
- Script execution remains routed through MTC's TWX script service. The agent
  receives the running-script inventory and can start or stop the reviewed
  World Trade and StarDock Shopper scripts. Script-menu responses are routed
  through the TWX prompt observer and validated against visible choices,
  numeric bounds, and the still-current prompt; arbitrary script variables and
  unreviewed catalog entries are not writable or executable by the model.
- Learn mode is an observe-only session for human gameplay. It records
  authenticated, synchronized game snapshots to the agent's experience store
  when the game state changes (sampled every 15 seconds), while autonomous
  gameplay and agent command/script tools are disabled. It records experience
  evidence; it does not train or update the model weights.

The standalone web dashboard is retained as a development and migration
surface. It cannot start a second game proxy, autonomous gameplay worker, or
autonomous login by default. LLMControlCenter is the supported application for
live play. The legacy dashboard host can be enabled only for isolated
Development testing with `LLMControlCenter:AllowLegacyStandaloneHost=true`.

The agent receives a unified runtime snapshot containing fresh MTC terminal
state, current and adjacent sectors from the active TWX database, the last
port-CIM timestamp, active interpreter scripts, approved script capabilities,
and bounded TWX catalog filename/category metadata. Both the Pilot and
Strategic Review receive that same integrated snapshot; strategic review
records retain the MTC/TWX evidence used to form objectives. Catalog metadata
is discovery context only, not a safety review or execution authorization.
The catalog is parsed once per file version and reused across agent snapshots;
file timestamp and size changes trigger a reload. The dashboard displays the
integration status and catalog evidence for diagnostics.

The dashboard's Meridian · Agent Session navigation has a dedicated Live
monitor page with Overview, Pilot model, Background work, LLM history, and
Checks & timers tabs; it refreshes every two seconds. Pilot model telemetry
covers Pilot provider requests only; Reflex and Strategic Review interactions
are shown in their separate LLM history pages. LLM history has separate Pilot,
Strategic Review, and Reflex review pages, each showing the
captured system/user prompt, raw Ollama response, request status, and any
applied strategic outcome. History is bounded in memory and clears when the
client restarts. The Background work panel includes game-event queue depth and dropped
event counts. Runtime remains the place for session controls and
integrated-service diagnostics. The desktop Game Agent
window also has a live Monitor tab alongside its context view. It reports the
selected provider/model and configured thinking behavior, model-request
latency and backend-reported token usage, active TWX scripts and Mombot state,
autonomous-worker activity, reactive/strategic queue depth, Pilot
model/thinking mode and decision latency/counts, watchdog progress and
cooldown, and the next presence heartbeat. Token totals remain unavailable
when a provider does not return usage metadata; the UI does not estimate them.

Autonomous play uses fresh sector and warp data, checks adjacent ports from the
live database (including CIM-imported port classes) for compatible trades, and
routes to a nearby seller when the current port is a buyer. Before scanner
ownership, the Pilot receives the verified scanner funding target and shortfall;
the worker enforces a current-port trade or the exact verified seller route
before accepting another movement recommendation. It avoids redundant
sector-display input when fresh warp data is already available. Movement and
commands remain subject to the active session's approval settings. At
StarDock, the agent preserves full Fuel Ore holds and uses the fixed Dock
Shopper flow to purchase a Holo Scanner before starting World Trade. If the
verified StarDock is in Federation space, it does not deploy a fighter there;
the scanner purchase flow can proceed without a sector fighter. World Trade
starts only after a fresh ship snapshot verifies scanner ownership. A planned
World Trade route is executed through verified adjacent hops, and the fixed
World Trade script starts automatically at StarDock when its prerequisites are
confirmed. MTC's JSON-RPC script-loading boundary enforces the same StarDock,
scanner-ownership, and fresh-status prerequisites for World Trade script
references, so generic script requests cannot bypass the worker's checks.

Autonomous Strategic Review runs on a 20-minute background interval, with a
15-minute minimum between event-triggered reviews. Completing an objective can
immediately trigger selection of the next one. Pilot actions continue between
reviews; recent reactive assessments are passed to Pilot as bounded signals,
not commands or verified facts, and cannot bypass the worker's action checks.

## Legacy agent and automation review

The legacy Node autonomous bot does not contain a live LLM integration. Its
LLM document is research and a staged proposal for a bounded strategy selector;
the bot itself is rule-based. The MTC Game Agent has working LM Studio, Ollama,
OpenAI, and Anthropic chat adapters. Those models may return structured tool
requests for explicitly requested actions; the local application enforces the
script, prompt-response, and Mombot command policies before dispatch. The
runtime Pilot, Strategic Review, and Reflex roles are separate and currently
use local Ollama providers. The unused `IModelProvider` macro-selection
contract is unfinished scaffolding, not an active model integration.

The legacy bot and TWX script corpus contain reusable trade-pair selection,
adaptive haggle, CIM freshness, prompt-aware recovery, event/supervisor
telemetry, and source-derived script/menu analysis. The integrated platform
already owns the canonical live database and script service, and has bounded
trade actions, MTC haggle support, event history, and an action watchdog. The
autonomous action knowledge retrieval now also searches procedural prompt and
script guidance and tactical recovery guidance from the curated legacy
knowledge sources.

The next integration boundary is a source-backed routine index that can supply
script provenance, routine family, menu/macro structure, and heuristic risk
signals to the agent as evidence. These records must remain explicitly
unverified; they can inform recommendations but do not grant execution
authority. Any future model-provider unification must keep advice, strategic
review, and action-selection roles explicit, preserve local inference as the
autonomous default, and require a separate visible choice before sending live
game state to a cloud provider. Raw legacy macros, destructive confirmations,
combat routines, and arbitrary catalog scripts are not candidates for automatic
execution. Mombot's switchboard is primarily its outbound message formatter;
the user-interface/service path parses local, private, and subspace commands
and dispatches modules. The Game Agent only submits local/self Mombot commands;
incoming private or subspace messages cannot authorize tool calls; a tool call
must also match an explicit action request from the local Game Agent chat and
the application allowlist.
The existing legacy Mombot authorization logic treats an empty authorized-user
list as open, so remote command routes should not be enabled without an
explicit sender allowlist.

## Build and run

The solution includes the desktop client, TWXProxy.Core, the agent runtime,
model, knowledge, macros, and TradeWars projects, plus the TradeWars test
project. From the workspace root in PowerShell:

```powershell
dotnet build .\LLMControlCenter\LLMControlCenter.sln
dotnet run --project .\LLMControlCenter\LLMControlCenter.csproj
```

On Windows, the app uses an STA entry point and a supported-OS manifest for the
embedded WebView2 host.

The embedded agent starts automatically after the game connection and
configured Ollama model are ready. If the local Ollama endpoint is unavailable,
the startup flow attempts to launch `ollama serve` and waits for the configured
model to appear; it does not download missing models. Startup progress and
connection/model failures are recorded in the agent activity and application
debug log. Run `Start-LLMControlCenter.bat` to build pending changes, repair a
build missing embedded UI assets, and verify that the client stays open.
Run `Install-LLMControlCenter-Startup.bat` once to also launch it at Windows
sign-in; run `Uninstall-LLMControlCenter-Startup.bat` to remove that startup
shortcut.
