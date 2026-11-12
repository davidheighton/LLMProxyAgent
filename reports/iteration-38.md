# Iteration 38

## Result

- The 900-second observation window ran from 06:07:43.350 to 06:22:43.375 local (900.026 seconds). MTC was stopped after the cutoff.
- The agent completed nine verified port transactions and increased credits from 309 to 4,072 (+3,763). It arrived at StarDock sector 551 with 20 Fuel Ore aboard and no empty holds.
- The heartbeat filtering repair was live-validated: affordable quotes were accepted and fresh ship snapshots confirmed cargo and credit changes.
- One Organics purchase attempt still failed. The original trade request had selected Organics with 1,010 credits, but untracked recovery later had no selected product and unknown credits and submitted `0`. No Density Scanner purchase or WorldTrade start was verified.
- One reconnect gap lasted approximately 5.016 seconds. The debug log contained 65 Reflex entries and no Pilot or Strategic entries; durable lane history is unavailable, so this does not prove those lanes were never invoked.

## Root cause and repair

- The confirmed heartbeat defect was addressed by excluding heartbeat copies from actionable trade-prompt recovery and prompt sequencing. Iteration 38's completed purchases and sales verify that this repair improved live trade execution.
- The remaining skipped Organics opportunity was traced to state timing: `PendingPortTrade` was stored only after `SendAsync` returned. If the sender was interrupted while a quantity prompt remained active, untracked recovery could not recover the selected opportunity or its original synchronized credit balance, so the sender safely declined with `0`.
- The worker now stores a short-lived active trade attempt and starting snapshot before awaiting the sender. Recovery reuses that request only for a recent attempt at the same sector, and passes its selected product, sale/buy intent, follow-up product, and credit balance through the recovery contract. The sender uses those values to select the intended product and still applies the existing affordability and prompt-freshness checks.

## Validation

- Autonomous worker test class: 72/72 passed, including a regression proving that an interrupted Organics purchase retains its product and 1,000-credit balance during recovery.
- MTC RPC safety/dashboard harness: 152 checks passed, including a full-hold purchase recovery using the requested product and an exact-balance quote.
- MTC Release build: succeeded with 0 warnings and 0 errors.
- The complete TradeWars test suite reported 275 passed and one `WorldEngineTests` cleanup failure because a temporary SQLite file was in use. The affected test passed when rerun alone; the trade-worker tests all passed.
- The active-request recovery change is unit/harness validated but not yet live-validated. Continue with iteration 39 and verify that an interrupted trade attempt selects the original requested product without an unnecessary `0`.

## Runtime metrics

- Connected time was 895.010 seconds of the 900-second target (99.44%); one reconnect gap accounted for approximately 5.016 seconds.
- Thirty-one samples covered 06:07:43.354–06:22:43.358 (900.0 seconds): average working set 480.0 MiB (401.4–615.3 MiB), average private memory 425.2 MiB (344.5–562.1 MiB), and sampled CPU increase 197.7 seconds (21.97% of one core over the sample interval).
- The raw debug and daily game logs, time-filtered game-event archive, and runtime metrics are stored in the `campaign-38-run` session archive.

## Remaining risks

- No Density Scanner purchase or WorldTrade start was verified. The ship reached StarDock with full Fuel Ore cargo, so the next run should verify the scanner purchase decision and preserve funding for it.
- The new active-request recovery fix needs a live run. Keep DEFECT-0029 open until recovery reliably preserves the requested trade and fresh telemetry confirms the cargo and credit delta.
- Pilot and Strategic prompt history remains unavailable in the durable archive.
