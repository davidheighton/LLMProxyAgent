# Meridian Event-Log Learning Before Reset

## Scope and method

Reviewed five per-game event files covering 2026-10-26 through 2026-10-30,
plus the available Meridian terminal and MTC diagnostic logs. The event files
contain 161,984 records. Counts below use event kinds and synchronized ship
status metadata; repeated text carried forward in terminal output is not
counted as a new ship-state observation.

Sensitive login input is redacted by the event logger. This report does not
include credentials, raw account prompts, or raw terminal transcripts.

## Confirmed gameplay evidence

- There are 555 recorded sector changes. The history includes repeated entries
  into sector 99.
- At 2026-10-29 13:57:49 UTC the ship moved from sector 369 into sector 99.
  The first following complete synchronized status, at 13:57:50 UTC, reported
  an Escape Pod with one hold in sector 99 and 21,856 credits. At 14:00:18 UTC
  the ship moved from sector 99 to sector 32.
- The preceding complete status for the Merchant Cruiser was recorded at
  08:44:10 UTC in sector 369: 40 holds, no scanner recorded, and 21,856
  credits. There is a multi-hour status gap before the loss, so the log does
  not establish the exact hazard, defensive equipment, or scanner state at
  the instant the ship entered sector 99.
- Across complete status snapshots, 473 Merchant Cruiser records show no
  scanner and 1,412 Escape Pod records show a Density Scanner. No Holo Scanner
  status was observed. The later Escape Pod status does not prove that the
  Cruiser had a scanner when it entered sector 99.
- Eleven WorldTrade-related client-input events appear around sectors 783 and
  369 on 2026-10-29. They do not prove that the script completed or generated
  earnings. The status at sector 369 later showed 21,856 credits; no successful
  WorldTrade result or positive credit delta can be verified from these logs.
- The final saved game profile was stale: it showed sector 97, 65,520 turns,
  21,856 credits, and one hold. The latest game event was at the BBS name/login
  stage after the prior character was deleted.

## Login and runtime observations

- The event stream records 206 connects and 154 disconnects.
- It contains 59 actual `clear avoids` server-prompt events, 507 BBS-name
  prompts, and 252 password prompts. Repeated login stages must be handled as
  fresh state transitions; a connected socket alone is not authenticated state.
- Iteration 18 ran for the scheduled 900-second window. MTC was present and
  Docker statistics succeeded in all 29 recorded samples. The event window
  stayed in sector 97 and contains no sector change or client-input action.
  The runtime displayed an empty Ollama test-action decision; the exact
  response cause was not durably captured.
- The event JSONL contains no durable LLM prompt/reply transcript. The
  in-memory interaction history was unavailable after MTC stopped, so LLM
  reasoning quality cannot be scored from this evidence.

## Operational lessons carried forward

1. Make Density Scanner acquisition the first exploration/trading-enablement
   objective when neither scanner is verified. Holo Scanner also provides
   Density scans and is an optional faster/safer upgrade.
2. Do not start WorldTrade until fresh synchronized ship status confirms
   Density or Holo Scanner ownership on the active ship.
3. Use a scanner preflight before each ordinary warp. Treat sector 99 as a
   demonstrated ship-loss location in this campaign, not as a safe route.
4. An Escape Pod or a ship with one or fewer holds must reacquire a suitable
   ship before port trading or WorldTrade.
5. Confirm script completion and earnings with fresh status and credit/hold
   deltas. A script-name event or model action is not success evidence.
6. Handle BBS name, password, and clear-avoids prompts separately, using only
   the currently observed prompt. Do not replay old answers after reconnects.
7. Preserve per-run LLM prompts and final responses before stopping MTC. Keep
   hidden/internal reasoning separate from the user-visible final response.
8. Archive the historical data used for these lessons, clear the active
   character snapshot and transient saved variables, and start with a fresh
   per-game database.

## Archived sources

Historical event JSONL, terminal logs, MTC diagnostics, and the old `.xdb` are
preserved under `reports/pre-reset-20261029/` for provenance. Active game-state
caches are reset separately so future autonomous decisions cannot treat the
old character's state as current.

## Credit spending and WorldTrade launch review

- At 2026-10-29 07:51:26 UTC, the ship was in sector 369 with 36,919 credits
  and 18 empty holds. At 07:52 UTC, the terminal prompted to install Cargo
  Holds; the recorded quantity was 22. The next complete status, in sector 1,
  showed 22,466 credits, 40 total holds, and 18 Fuel Ore. The 14,453-credit
  decrease is therefore attributable to the StarDock hold purchase in this
  interval; no scanner was recorded. The logs confirm what was bought, but do
  not preserve the Pilot/Strategic decision that selected it.
- The later archived balance of 21,856 credits followed a separate purchase:
  at 08:10 UTC the ship bought 16 Equipment for a 610-credit reduction. At
  08:44 UTC the cargo was empty and credits remained 21,856, so the archive
  does not verify positive earnings from that cargo.
- WorldTrade launches around 08:00 UTC failed to compile:
  `trading-cashing/2-worldtrade.ts` reported a missing `include\header` at
  line 124. Thus the script-name inputs are not evidence of a running routine
  or earned credits.
- These transactions predate the later one-credit state. The retained event
  logs do not show the transaction that caused that later balance, so do not
  conflate the 14,453-credit purchase with the one-credit loss.
- Operational guard: while the goal is scanner acquisition, authorize only a
  Density Scanner by default. Require explicit approval and a budget check for
  cargo holds or other hardware. Verify each transaction from fresh status
  deltas, and do not call a trade profitable without a positive credit delta.

## Learn-mode Game D and Game E sessions (2026-11-03 to 2026-11-04)

### Scope and capture

Reviewed both dates of the Game D and Game E terminal recordings, their
per-game debug logs, the relevant colonization script, and the persisted
Learn-mode observations. The main sessions are:

- **Game D recovery:** `learn-bcad5d7089a245daa3aa8c02c4d55437`, 13
  `SituationObservation` records from 05:26:57Z to 05:54:24Z (22:26-22:54
  local on November 3).
- **Game E:** `learn-944cb0874b5a40f4936084d4307f5653`, 22
  `SituationObservation` records from 05:57:03Z to 07:28:30Z (22:57 on
  November 3 to 00:28 on November 4).
- **Separate Game D follow-up:** `learn-3d53022605d948f5a9c3ec8c29e91128`,
  4 observations from 07:01:31Z to 07:16:53Z (00:01-00:16 on November 4).
  This is not the Escape Pod recovery session.

The Learn records are state observations, not linked action trajectories:
there are no matching trajectory episodes or steps, and outcomes are marked
unmeasured. The terminal logs do expose specific actions and resulting states,
so those sequences can be kept as operational evidence; they still do not
reliably identify which older agent issued every command or provide a
consistent action/reward label. Do not count observations as actions or infer
agent performance from them.

### Game D: ship loss and recovery

The recovery session starts in sector 172 in a Kvertoo Merchant Cruiser with
20 empty holds, a Density Scanner, 8,039 credits, and 65,520 turns. Before the
loss, the log shows the cruiser expanded to 75 holds and fitted with 400
shields. At sector 829, a Ferrengi attack overwhelmed its single fighter
against 1,926 attackers. The pilot abandoned the destroyed cruiser in an
Escape Pod.

The pod reached StarDock in sector 551 with about 482,057 credits, one cargo
hold, 30 shields, and a Density Scanner. Selling its remaining Organics added
79 credits. The pilot then purchased a T'Khasi Orion and refitted it: 30
additional holds for 34,170 credits (60 total), 750 shields for 82,500, and
750 fighters for 179,250. A later Learn snapshot records the Orion in sector
512 with 60 holds full of Equipment, 750 shields, and 6,583 credits. This is
a logged successful ship-recovery sequence after a catastrophic hull loss,
not a reward label for a named agent.

### Game E: ship refits and planet development

The session begins in sector 10 with a Sverdlov Merchant Cruiser, 20 holds
full of Fuel Ore, 39,727 credits, and 65,484 turns. It includes a Colonial
Transport refit with 200 additional holds for 647,800 credits, 500 shields,
and 200 fighters, plus further ship changes. A QuadStar Merchant Freighter
snapshot shows 65 holds, 300 fighters, 500 shields, and a Holo Scanner. The
final Learn snapshot is an Antarian Colonial Transport in sector 536 with
250 empty holds, 1,399,690 credits, and 58,586 turns. Cash peaked at
3,049,533 in sector 2909, so neither the peak nor the cash delta alone
measures the value of this multi-ship session. Recheck scanner state after
each hull change.

The log records purchase of two Aldus Genesis Torpedoes for 40,000 credits;
subsequent status still shows both aboard. It does not show them being fired
or creating the planet discussed below, so do not attribute that planet to
this purchase.

The colonization activity concerns planet #5, Erebus E, in sector 536
(Class O, Oceanic, no Citadel). The `planet-citadel\\2-col.ts` script ran
from 00:12:47 to 00:28:21 and the terminal shows repeated Terra-to-planet
colonist runs, assigning the arrivals to Organics. Planet status grows from
49,000 Organics colonists in early observations to 399,000 and then in
repeated 250,000 increments, reaching 6,400,000; daily Organics output rises
from 24 to 3,200. At the final status, a separate 30,000-colonist allocation
to Fighters produces 213 Fighters daily, with 2 Fighters on the planet and
200 aboard the ship. This is clear ongoing colony development and production
investment, but not evidence of a completed Citadel or a finished
colonization objective.

### Separate Game D follow-up

The short post-midnight D session starts with 66,026 credits and a High
Velocity corporate FlagShip with 85 empty holds and a Holo Scanner. It peaks
at 1,189,183 credits and ends in sector 551 with 86,007 credits and 85 Fuel
Ore aboard, using 1,458 turns. Keep it separate from the earlier Kvertoo
loss/Escape Pod/Orion recovery; its cash and cargo are unrelated.

### Learning carried forward

1. Preserve the D recovery chain as a concrete operational sequence:
   ship/fighter mismatch, Escape Pod survival, StarDock arrival, hull purchase,
   refit, and resumed travel. Treat the loss and recovery as observed outcomes,
   not as model reward or proof that a named agent caused them.
2. Record ship type, holds, shields, fighters, cargo, scanner, credits, and
   turns before and after each hull change. Cash alone hides both the cost and
   retained value of ships and cargo.
3. Treat the E colonization log as positive evidence for repeated colonist
   delivery and production growth. Preserve the table's units and distinguish
   workforce/output from stock on hand; do not infer Genesis Torpedo use or
   Citadel completion.
4. Keep the two D Learn sessions distinct, and do not compare these older
   agent sessions to learner bots as action-level performance until command
   attribution and trajectory/reward capture are available.

### Sources

- [Game D terminal log, November 3](<../LLMControlCenter/logs/2026-11-03 exiledTWGS  - Game D.log>)
- [Game D terminal log, November 4](<../LLMControlCenter/logs/2026-11-04 exiledTWGS  - Game D.log>)
- [Game E terminal log, November 3](<../LLMControlCenter/logs/2026-11-03 exiledTWGS  - Game E.log>)
- [Game E terminal log, November 4](<../LLMControlCenter/logs/2026-11-04 exiledTWGS  - Game E.log>)
- [Game D debug log](<../LLMControlCenter/logs/exiledTWGS  - Game D_debug.log>)
- [Game E debug log](<../LLMControlCenter/logs/exiledTWGS  - Game E_debug.log>)
- [2-col.ts](<../LLMProxyScripts/planet-citadel/2-col.ts>)
