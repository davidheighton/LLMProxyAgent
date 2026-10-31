# TWBot Routine Catalog (Phase 2)

Implementation status:
- State-based routine policy selection is implemented in runtime.
- Primary/fallback routine mapping is now configured in active config.
- StarDock upgrade sequence is configured with a non-empty baseline macro.
- Local-trade gating now allows speculative buys when a port can sell desired cargo (`allowSpeculativeLocalBuys`).
- Avoid-clear login prompt handling is explicitly auto-answered (`N`) to prevent pre-command stalls.

This catalog inventories the routines currently available across:
- autonomous bot runtime/config (`autonomous-bot`)
- TWX include scripts (`TWX30-main/scripts/include`)
- MOMBot include/help scripts (`TWX30-main/scripts/mombot`)
- Pink scripts (`Pink-Scripts/scripts`)

The goal is to define what exists now so we can teach the bot *when* and *how* to use each routine.

## 1) Runtime-Configured Routines (autonomous-bot)

Source: `config.192.168.4.75.json`

### 1.1 Bootstrap / Discovery
- `bootstrap-status` -> `/`
- `discover-stardock` -> `v`
- `bootstrap-ship-info` -> `i`

Use when:
- Startup state is unknown.
- Dock location is unknown.
- We need fresh ship stats before strategy decisions.

### 1.2 Escape Pod Recovery / Dock Reacquisition
- `escapepod-return-stardock` -> `b{stardockSector}*`
- `escapepod-leave-stardock` -> `m{nextWarp}*`
- `stardock-buy-ship` -> `{stardockShipBuySequence}` (default `s*b`)

Use when:
- `onlyWhenEscapePod` is true and StarDock is known.

### 1.3 StarDock Upgrade / Spend
- `stardock-upgrade-survival` -> `{stardockUpgradeSequence}`
- `stardock-exit-low-credits` -> `m25358*`

Important:
- `stardockUpgradeSequence` is currently configured as `ps  gygqs  p`; tune it for the target server before enabling live upgrades.

### 1.4 Local Trading / Core Worldtrade
- `worldtrade-local-port` -> `pt`
- `worldtrade-warp-hop` -> `m{nextWarp}*`
- `recovery-sell-cargo-local-port` -> `pt` (disabled)

Use when:
- Not in escape pod and not at StarDock.
- Port is present and local trade opportunity exists.

### 1.5 Evil Economy Routines (steal/rob/upgrade)
- `evil-recovery-steal-local` -> `pr*st3{stealAmount}*`
- `evil-recovery-upgrade-local` -> `o3{upgradeAmount}*q`
- `evil-recovery-hunt-port` -> `m{nextWarp}*`

Use when:
- Negative alignment policy permits it.
- EXP and alignment thresholds are met.

### 1.6 SSM / SST / Port Upgrade Advanced Legs
- `ssm-leg-a` -> `{ssmSectorA}*pt**0*0*pr*st3{stealAmount}*`
- `ssm-leg-b` -> `{ssmSectorB}*pt**0*0*pr*st3{stealAmount}*`
- `port-upgrade-equip` -> `o3{upgradeAmount}*q`
- `sst-hop-ship2` -> `x{sstShip2}*qpt**0*0*pr*s3{stealAmount}*` (disabled)
- `sst-hop-ship1` -> `x{sstShip1}*qpt**0*0*pr*s3{stealAmount}*` (disabled)

Use when:
- Credits/EXP are high enough and high-risk economic loops are desired.

## 2) Runtime Internal Routine Families (bot.js)

Source: `bot.js`

### 2.1 Prompt Engines
- Trade prompt handler (`_handleTradePrompts`)
- Travel prompt handler (`_handleTravelPrompts`)
- Generic auto-responses (`_handleAutoResponses`)
- Prompt gate and dispatcher (`_handlePrompts`)

### 2.2 Action Scheduling and Fallback
- Core scheduler (`_executeNextAction`)
- No-eligible fallback (`_tryRunNoEligibleFallbackAction`)
- Emergency warp fallback (`_tryRunEmergencyWarpFallback`)
- Stuck progress / forced warp checks (`_updateMovementProgress`, `_shouldForceStuckWarp`)

### 2.3 Navigation and Route Scoring
- Next hop selectors (`_selectNextWarp`, `_selectTradeIntentWarp`, `_selectKnownTradePairSeekWarp`)
- Candidate scoring (`_scoreWarpCandidate`)
- Escape and alignment route selection (`_selectEscapeWarp`, `_selectAlignmentSeekWarp`)

### 2.4 Trade Intelligence and Pair Map
- Pair scoring (`_scorePreferredTradePair`, `_scoreFuelFirstPair`)
- Pair rebuild / retrieval (`_rebuildTradePairMap`, `_getTradePairMap`)
- No-trade suppression (`_recordNoTradePair`, `_isNoTradePairActive`, `_isNoTradeSectorActive`)
- Local opportunity snapshots (`_getLocalTradeOpportunitySnapshot`)

### 2.5 Upgrade / Restock Compute Routines
- Scanner need checks (`_needsHoloScanner`, `_needsDensityScanner`)
- Scanner purchase command builder (`_computeStardockScannerUpgradeSequence`, `_computeStardockScannerChoice`)
- Spend budgeting (`_computeUpgradeSpendBudget`)
- Shield/fighter plan (`_computeStardockPurchasePlan`)
- Fighter restock target select (`_selectFighterRestockWarp`)

### 2.6 Data and Sync
- CIM parser + orchestrator (`_handleCimData`, `_startCimSync`, `_maybeRunCimSync`)
- Intel persistence (`_loadIntelSnapshot`, `_writeIntelSnapshot`)
- Status/events/supervisor (`_writeStatus`, `_publishEvent`, `_maybeRecoverFrozenRuntime`)

## 3) Legacy Upgrade / Hardware Command Routines (Reusable)

## 3.1 TWX Refurb Routine (canonical holds/fighters/shields)

Source: `TWX30-main/scripts/include/Refurb.ts`

- Enter class 9 style refurb menu:
  - `ps  gygqs  p`
- Enter class 0 refurb menu:
  - `pty`
- Parse purchase lines:
  - `A  Cargo holds`
  - `B  Fighters`
  - `C  Shield Points`
- Buy operations:
  - Holds: `a<amount>*y`
  - Fighters: `b<amount>*`
  - Shields: `c<amount>*`
- Exit:
  - class9: `qqq`
  - class0: `q`

## 3.2 MOMBot Merchant Dock + Hardware Routine

Source: `TWX30-main/scripts/mombot/include/merchant.ts`

- Dock bank/nav macro:
  - `m <stardock>* * P S G Y G Q`
- Return/home macro:
  - `Q Q Q Q Z N M <start>* Y Y Y * L Z<planet>* p s s * * c *`
- Hardware Emporium loop:
  - `h`
  - buy branch `L` (limpet) and `M` (armid) branches with quantity prompts
  - exit: `q q q q *`

## 3.3 Pink SupG Refurb/Restock Routine

Sources:
- `Pink-Scripts/scripts/SupGWorldSSM20.ts`
- `Pink-Scripts/scripts/Supg/pinkSSM.ts`

- Enter class0 trade/refit path:
  - `pt`
- Optional prompt handling:
  - send `y` on limpet/nav-confirm prompts
- Buy operations:
  - Holds: `a<max>*y`
  - Shields: `c<max>*`
  - Fighters: `b<max>*`
- Exit:
  - `Q`

## 3.4 Pink Hardware/Photon Purchase Example

Source: `Pink-Scripts/scripts/supgambushphoton.ts`

- Open stardock/hardware path:
  - `ps`
- Buy ptorp example:
  - `hp1*`
- Exit:
  - `q`

## 4) MOMBot Documented Routine Inventory (help topics)

Source: `TWX30-main/scripts/mombot/help/*.txt`

High-value routine families visible in help topics include:
- Navigation/movement: `move`, `mow`, `safemow`, `twarp`, `bwarp`, `xenter`, `wander`, `runaway`
- Trading/cash: `trade`, `merch`, `port`, `refurb`, `lsd`, `sst`, `wsst`, `rob`, `wrob`
- Upgrade/refit/resource: `upgrade`, `furb`, `refurb`, `buy`, `topoff`, `fillships`, `strip`, `stripships`
- Combat/offense: `kill`, `dockkill`, `citkill`, `hkill`, `pelk`, `pxelk`, `plock`, `pdrop`
- Grid/defense: `surround`, `ramgrid`, `ugrid`, `pgrid`, `reloader`, `sentinel`
- Intel/ops: `cim`, `update`, `status`, `qreport`, `figs`, `density`, `dscan`, `probe`

## 5) Initial When/How Policy Draft

This section is a first pass policy map. We can convert this into code rules next.

### 5.1 Economic state -> routine family
- Escape pod: prioritize `escapepod-return-stardock` and ship reacquisition routines.
- Low credits, non-escape: prefer local `pt` and conservative worldtrade hops.
- Mid credits with no scanner: route to StarDock and run scanner-first upgrade routine.
- High credits with stable pair map: use worldtrade pair loops; optionally add SSM legs.
- Evil mode thresholds met: enable steal/upgrade loops with bust cooldown controls.

### 5.2 Location state -> routine family
- At StarDock + not escape pod:
  - run populated `stardock-upgrade-survival`
  - run hardware purchase branch only when budget permits
- At normal port:
  - run `worldtrade-local-port`
  - if no trade opportunity, mark no-trade pair/sector and hop
- In transit/stuck:
  - invoke emergency warp fallback and sector info refresh

### 5.3 Safety state -> routine family
- Threat/route-fail alerts: avoid risky target sectors and prefer safe fallback warp.
- Recovery mode / poor liquidity: suppress expensive upgrades, prioritize credits recovery.
- Scanner absent + threshold reached: elevate scanner route over marginal trade loop.

## 6) Gaps to Close Before Teaching Automation

1. Tune credit thresholds so upgrade routines can trigger in realistic bankroll windows.
2. Add trace events for "routine skipped reason" for validation.

## 7) Login, Account Creation, and Session Recovery

Source-backed TWX/Pink login, M()MBot logoff/relog, Z-Bot setup, and current
Node-bot provisioning gaps are summarized in
[LOGIN_SESSION_PROCEDURES.md](./LOGIN_SESSION_PROCEDURES.md).

- Existing-account login and new-character creation are distinct flows.
- Alias, ship, and home-planet prompts are conditional; never assume every
  game asks for all of them.
- M()MBot `logoff`/`relog` perform session transitions; `exit`/`xenter` is an
  in-game routine with additional side effects, not a safe logout shortcut.
- The current Node-bot's automatic account-creation replies and plaintext
  generated-credential persistence require hardening before unattended use.

---

If this catalog structure works for you, next step is to encode section 5 into a concrete policy table in config (or in code) and wire each state to one primary + one fallback routine.

## Scanner-safe movement

The autonomous movement gate follows the legacy MOMBOT/Pink scan-before-move pattern. Ordinary warp hops issue `sd` or `shsd`, wait for the command prompt, reject reported mines, limpets, armids, navhaz, and enemy fighters, and only then transmit the held warp command. Configure it with `strategy.movementSafety.scanBeforeWarp` and `scanHoloWhenAvailable`.

## Fighter grid intelligence

Legacy `fighters~deploy`, `grid~surround`, `passgrid`, and `figs` routines show the safe pattern: deploy only outside FedSpace/StarDock, classify ownership and fighter type, keep a ship reserve, and refresh deployed-fighter data before using fighter sectors as routing anchors. The Node bot currently implements the observational half with `strategy.fighterIntel.enabled` and `G` refreshes; automatic drops remain disabled until the deployment policy is implemented and tested.
