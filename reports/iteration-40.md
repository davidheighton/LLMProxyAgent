# Iteration 40

## Result

- MTC started from the rebuilt Release executable and remained connected through a 900.019-second observation window, 07:08:56.613–07:23:56.634 local. It disconnected immediately after the cutoff during shutdown.
- During startup before the measured window, the ship bought 20 Fuel Ore for 197 credits and moved from sector 172 to StarDock sector 551. At the observation-window start it had 5,555 credits, 20 Fuel Ore, and full holds.
- After arrival, the debug log recorded `selected=0` at an Equipment quantity prompt. This was a quantity skip, not a response to a price quote; available evidence does not establish whether skipping that port good was expected or a missed objective opportunity.
- During the window, the agent sold the full 20-unit Fuel Ore load at StarDock for 966 credits. Untracked recovery retained the selected sale intent and the 5,555-credit starting balance, accepted the quote, and a fresh snapshot verified sector 551, 6,521 credits, and 20 empty holds.
- The game stayed at StarDock after arrival. No Density Scanner purchase or WorldTrade start was verified. The agent did not complete the full objective.
- MTC debug logs contained 54 Reflex entries and no Pilot or Strategic entries. An HTTP fetch of the dashboard markup included 0 decisions and generic unhandled/reconnect text; without an interactive browser session, that does not confirm the reconnect state was visibly active. MTC stayed connected to the game and executed the verified sale.

## Root cause and repair validation

- The iteration-39 repair keeps the active trade context after `SendAsync` returns until a fresh snapshot verifies the transaction or the bounded recovery context expires.
- Iteration 40 live-validated the late-recovery path: a delayed, untracked Fuel Ore sale prompt retained `sellingExistingCargo=true` and 5,555 available credits, accepted the 966-credit quote, and was followed by a fresh snapshot showing the cargo and credit changes.
- DEFECT-0029 is now VALIDATED for the repeated port-haggle/recovery failure. Scanner acquisition and WorldTrade remain separate unmet objectives.

## Validation

- Full TradeWars regression suite: 277/277 passed.
- All `AutonomousTestWorkerTests`: 73/73 passed.
- MTC RPC safety/dashboard harness: 152 checks passed.
- MTC Release build: 0 warnings and 0 errors.
- No source changes were made during iteration 40; this run validates the iteration-39 source repair.

## Runtime metrics

- Runtime sampler captured 29 samples over 900.019 seconds. Process working set averaged 470.2 MiB (431.5–571.4 MiB); private memory averaged 414.8 MiB (375.2–521.4 MiB).
- Sampled CPU increased by 89.312 seconds over 896.541 sampled seconds (9.96% of one core).
- Container time-series, MTC debug/game logs, and the game event archive are stored in the `campaign-40-run` session archive. No in-window application exception was found in the debug log.

## Remaining risks

- The scanner objective remains blocked: the ship ended at StarDock with 6,521 credits and empty holds, but no Dock Shopper/Density Scanner purchase was verified. The cause is not confirmed; the dashboard reported 0 decisions and a paused/unhandled session.
- WorldTrade was not started or monitored.
- Prompt/reply history for Pilot and Strategic remains unavailable. Persisted telemetry had 54 Reflex entries and no Pilot/Strategic entries.
- The dashboard markup and missing Pilot/Strategic debug entries leave Pilot scheduling and script selection uncertain. The static HTTP check cannot distinguish a hidden reconnect modal from an active UI failure.

## Campaign fields

- **Issues found:** Late untracked recovery was rechecked and passed live; scanner/WorldTrade progress remained absent; role-history telemetry remained incomplete and the dashboard's visible reconnect state could not be confirmed without an interactive browser.
- **Root cause confirmed:** The former premature clearing of active trade context was the cause of the delayed trade-selection loss.
- **Repair validated:** Retain active intent through fresh trade verification or bounded expiry.
- **Files modified:** Campaign report, scorecard, defect ledger, and lessons. No application source files changed in this iteration.
- **Prompt changes:** None.
- **Performance changes:** None. The run metrics are recorded above; the sampler ended within 0.019 seconds of its 900-second target.
- **Lessons learned:** A fresh cargo/credit snapshot after a delayed quote is the required success proof. A successful trade still does not imply the scanner or WorldTrade objective has progressed.
