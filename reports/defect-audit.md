# Defect Audit — Through Iteration 40

## Summary

- **Total defects discovered:** 32
- **Source-fixed:** 16 (`FIXED`)
- **Runtime-validated:** 7 (`VALIDATED`)
- **Open/investigating:** 6 (`INVESTIGATING`)
- **Regressed:** 3 (`REGRESSED`)
- **Unresolved or regressed:** 9

Statuses are preserved in [defect-ledger.json](./defect-ledger.json). No defect was deleted. DEFECT-0029 was promoted to `VALIDATED` after iteration 40 live-verified a delayed Fuel Ore sale recovery and the resulting fresh cargo/credit snapshot.

## Open and regressed defects

- `DEFECT-0011` — WebView2 focus exception can terminate MTC.
- `DEFECT-0020` — Empty Ollama final content fails the Pilot action.
- `DEFECT-0021` — LLM prompt and response history is unavailable after shutdown; iteration 40's HTTP-fetched dashboard markup included 0 decisions and generic reconnect text, but no interactive browser confirmed an active visible reconnect state.
- `DEFECT-0022` — Reflex misclassifies routine ship-state changes as urgent.
- `DEFECT-0023` — Autopilot confirmation blocks port-trade recovery.
- `DEFECT-0024` — Ollama inference requests time out during autonomous review.
- `DEFECT-0026` — Scannerless stale trading objective caused route oscillation; marked regressed.
- `DEFECT-0028` — Stale ship snapshot blocks progress at StarDock.
- `DEFECT-0032` — Scanner-first StarDock visit purchased cargo holds instead of a scanner.

No separate new defect was created for iteration 40's dashboard markup because its visible state and cause are unconfirmed and it overlaps the unresolved prompt/history and Pilot-observability evidence gaps.

## Highest-impact failures observed

1. **Scannerless movement and route oscillation:** repeated routes among sectors without verified earnings or scanner ownership risked consuming turns and exposing the ship to unsafe sectors. The recurrence is tracked as `DEFECT-0026`.
2. **Trade intent lost during delayed prompts:** affordable trades were declined or followed by zero quantity responses when the selected product and synchronized credit context were cleared before transaction verification. Iteration 40 verified the retain-through-verification repair; `DEFECT-0029` is now validated.
3. **Long no-progress windows:** several 15-minute sessions consumed time and generated Reflex activity without verified trade, scanner, or WorldTrade progress.
4. **Unapproved or wrong hardware purchase:** historical telemetry recorded 22 Cargo Holds bought while the scanner objective was unmet; `DEFECT-0032` remains investigating.
5. **Unavailable diagnostic history:** missing durable Pilot/Strategic prompt and response data, plus the uncertain dashboard state, prevented reliable attribution of stalls.

No reliable aggregate credit-loss amount is available from the audited evidence; this report does not invent one.

## Most common root-cause classes

- Prompt correlation, heartbeat duplication, and stale prompt sequencing.
- State lifetime errors: clearing selected trade intent before fresh verification.
- Stale/incomplete snapshots and decisions made without postcondition evidence.
- Structured LLM response schemas that did not match worker requirements.
- Missing durable telemetry, preventing distinction between uninvoked model lanes and missing history.

## Trend by iteration

- Trade prompt correlation and quote handling were repaired incrementally; iterations 38 and 40 recorded verified trades and positive earnings.
- Iteration 39 exposed that pre-send context alone was insufficient because it was cleared before fresh transaction verification.
- Iteration 40 live-validated retaining context through verification: a delayed 966-credit sale offer was accepted, and a fresh snapshot confirmed 6,521 credits with empty holds.
- Scanner acquisition and WorldTrade remained unverified at iteration 40.
- The scorecard has 19 rows for iterations 21–40; iteration 23 is missing a scorecard row despite being referenced as having runtime samples.

## Stability and prompt-quality trend

- Among the available scorecard rows for iterations 21–40, build stability averaged 99.98% across 18 populated rows; runtime stability averaged 99.36% across 6; prompt quality averaged 97.44% across 13.
- LLM reasoning quality averaged 33.35% across only 2 scored rows. This is insufficient to claim a reliable trend.
- Iteration 40 remained connected for 900 seconds. Its fetched dashboard markup included 0 decisions and generic reconnect text, but an interactive UI state was not captured. The debug log contained 54 Reflex entries and no Pilot or Strategic entries; durable history remains inadequate.

## Recommended priorities

1. Capture the interactive dashboard circuit state and exact exception, if any, without conflating UI status with game-connection health.
2. Persist per-role request and final structured-response history locally before stopping MTC.
3. Complete the scanner-first StarDock transaction with an explicit quote/affordability guard and fresh ownership verification.
4. Start and monitor WorldTrade only after confirming scanner ownership; verify ongoing status and positive earnings.
5. Keep trade-context persistence, fresh snapshot verification, full-hold sizing, and quote affordability in the regression suite.
6. Resolve the iteration-23 scorecard evidence gap and maintain the ledger/status counts on future runs.

## Campaign result

The port-trade recovery defect is validated, but the end-to-end objective remains incomplete. The remaining validation target is scanner purchase followed by a confirmed WorldTrade start and positive earnings.
