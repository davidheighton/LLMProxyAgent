# Final Analysis — TradeWars Reliability Campaign Through Iteration 40

## Campaign outcome and evidence limits

The campaign executed through iteration 40. The requested end-to-end objective is **not complete**: trade recovery now has a live-validated successful sale, but the ship did not purchase a Density Scanner or start and monitor WorldTrade. Iteration 40 ended at StarDock sector 551 with 6,521 credits and 20 empty holds.

The scorecard contains 19 rows for iterations 21–40; iteration 23 has runtime evidence noted in the ledger but no scorecard row. Scores below use only recorded, non-null values and must not be interpreted as a complete 20-row trend.

## 1. Total issues discovered

- The persistent ledger contains 32 unique defects.

## 2. Total issues fixed and validated

- 16 are marked `FIXED` at source level and 7 are `VALIDATED` by runtime evidence.
- Thus 23 defects have a recorded fix or runtime validation; only 7 have the stronger `VALIDATED` status. Nine remain unresolved or regressed.
- DEFECT-0029, repeated port-trade haggle/recovery failures, is now `VALIDATED`. Iteration 40 retained the selected sale context through delayed recovery, accepted the 966-credit Fuel Ore offer, and verified the resulting 6,521-credit balance and 20 empty holds.
- Iteration 40 also logged one `selected=0` Equipment quantity prompt after StarDock arrival. It was not a price-quote response; the available evidence does not determine whether that product skip was expected or a missed objective opportunity.

## 3. Most common failure patterns

- **Prompt/context correlation and premature state clearing:** stale or duplicated prompts, heartbeats treated as actionable prompts, and trade context cleared before a fresh transaction snapshot led to missed or misrouted responses. Filtering heartbeat copies and retaining trade intent through verification produced verified live trades.
- **Stale or incomplete state evidence:** movement, a selected quantity, or an accepted quote was sometimes mistaken for progress. Fresh cargo and credit deltas are now the required evidence of a completed trade.
- **Scannerless routing and objective drift:** the agent repeatedly moved without establishing earnings or completing the scanner objective. These behaviors remain represented by open/regressed defects.
- **LLM output contracts and lane observability:** several prompt/schema mismatches were corrected, but durable Pilot/Strategic prompt and response history remains unavailable.
- **Dashboard/runtime observability gap:** an HTTP fetch of dashboard markup included 0 decisions and generic reconnect text while the game connection and trade sender remained active. Without an interactive browser session, the visible circuit state and its effect on Pilot scheduling are unconfirmed.

## 4. Stability trend

- Across the 19 recorded scorecard rows for iterations 21–40, build stability averaged **99.98%** (18 non-null values); the latest row is 100%.
- Runtime stability averaged **99.36%** (6 non-null values), rising from 99.95% on the first scored row to 100% in iteration 40. These values represent connected-time scoring only where that evidence existed; they do not prove gameplay progress.
- Iteration 40 remained connected for the full 900-second window and disconnected after the cutoff. The remaining-time-capped sampler finished at 900.019 seconds.
- The campaign was not monotonically productive: multiple windows remained idle or failed to verify transactions. Iteration 40 verified a sale, but no scanner or WorldTrade progress followed.

## 5. LLM quality trend

- Only 2 of the 19 campaign scorecard rows contain a non-null LLM reasoning score; their average is 33.35%. The sample is too sparse for a reliable quality trend.
- Iteration 39 recorded 330 Reflex entries; iteration 40 recorded 54. Neither run had Pilot or Strategic entries in the durable debug log; iteration 40's fetched dashboard markup included 0 decisions.
- Missing durable prompt/reply history means these observations cannot distinguish an uninvoked reasoning lane from missing dashboard/history telemetry. No semantic-quality improvement is claimed.

## 6. Performance trend

- Iteration 39 averaged 499.8 MiB working set, 443.9 MiB private memory, and 46.7% of one core over its sampled interval.
- Iteration 40 averaged 470.2 MiB working set, 414.8 MiB private memory, and 9.96% of one core. The two samples had different activity and do not establish a long-term memory or CPU trend.
- Container metrics were captured for iteration 39 and 40. No leak or scaling conclusion is supported by these two short samples.

## 7. Architectural weaknesses

- Prior dashboard observations and iteration 40's static markup indicate a possible mismatch between dashboard state and native game connectivity. The iteration-40 fetch did not confirm an active circuit failure.
- LLM prompt/reply history is not durably archived with each run, preventing confident diagnosis of Pilot and Strategic invocation.
- Trade execution, scanner acquisition, and WorldTrade initiation do not yet have a single verified end-to-end progress contract. A successful sale is not enough to prove the strategy has advanced.
- Some scorecard history is incomplete: iteration 23 has no scorecard row despite a recorded evidence reference.

## 8. Remaining known issues

- The Density Scanner was not purchased. The latest verified state is StarDock sector 551, 6,521 credits, and 20 empty holds.
- WorldTrade was not started or monitored.
- Pilot scheduling and the dashboard's interactive state remain uncertain; the captured dashboard markup alone does not confirm an active reconnect failure.
- Pilot and Strategic invocation remains ambiguous because durable role-level transcripts are unavailable.
- Six defects remain investigating and three remain regressed; see [defect-ledger.md](./defect-ledger.md).

## 9. Technical-debt backlog

- Persist per-run LLM request metadata and final structured responses, with safe local retention and export.
- Verify the Blazor dashboard's interactive circuit state independently from native game-session health, then expose truthful decision counters.
- Add an end-to-end objective checkpoint for scanner purchase, verifying ownership after the hardware transaction before starting WorldTrade.
- Capture and verify WorldTrade start, running status, trade count, and positive earnings.
- Backfill or explicitly mark the missing iteration-23 scorecard row from its source evidence.
- Continue requiring fresh snapshot evidence for every trade and upgrade; do not infer success from inputs or movement alone.

## 10. Recommended next development priorities

1. Reproduce the dashboard unhandled/reconnect state while the native game connection remains active; retain the exact exception and distinguish UI circuit failure from agent-worker health.
2. Verify whether Pilot and Strategic lanes are invoked in this state by exporting structured request/result metadata before shutdown.
3. Resume from the verified StarDock state, purchase a Density Scanner only after confirming current credits and the hardware quote, and verify scanner ownership in a fresh snapshot.
4. Start the default `2_worldtrade` script only after scanner ownership is confirmed; monitor its status and verify positive earnings.
5. Continue using the delayed-prompt trade regression and full-hold purchase/sale tests as release gates.

## Campaign scorecard snapshot

| Metric | Recorded trend for iterations 21–40 | Limitation |
|---|---:|---|
| Build stability | 99.98% average; 100% latest | 18 of 19 scorecard rows populated |
| Runtime stability | 99.36% average; 100% latest | 6 of 19 rows had a non-null score |
| LLM reasoning | 33.35% average | Only 2 scored rows; not a semantic-quality measure |
| Prompt quality | 97.44% average; 100% latest | 13 scored rows |
| Autonomous recovery | 100% average where scored | Only 8 scored rows |
| Regression rate | 57.78% average; 0% latest | 15 scored rows; not monotonic |
| Runtime-validated defect closure | 20.8% first scored row; 21.9% latest | 7 of 32 defects currently validated |

The campaign improved trade-prompt recovery and verified profitable trades, but it did **not** satisfy the scanner-plus-WorldTrade success objective. The campaign should be considered technically useful but operationally incomplete.
