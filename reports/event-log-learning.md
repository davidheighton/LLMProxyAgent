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
