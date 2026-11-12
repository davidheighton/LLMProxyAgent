# Iteration 34

## Result

- MTC remained alive through the scheduled 15-minute window. The graceful shutdown was requested at 04:32:30.048, 899.576 seconds after the 04:17:30.472 start (0.424 seconds short of 900).
- The run was productive under the campaign rule because 168 sector transitions were observed, but movement did not produce earnings.
- The ship moved among sectors 172, 206, and 805. Its last verified state was sector 172, 65,009 turns, 500 credits, 30 fighters, and 20/20 empty holds.
- Three trade attempts failed to transact. Two verified affordable Fuel Ore quotes (107 and 191 credits against 500 credits) were declined with `0`; a third 140-credit quote was declined while the synchronized balance was unavailable.
- No positive cargo/credit delta, Density Scanner purchase, scan, or WorldTrade start was verified.
- The debug archive recorded 328 Reflex entries and no Pilot or Strategic Review entries. This establishes that those lanes were not observed in the persisted debug log; it does not distinguish absent invocations from missing durable history.

## Root cause

- The logs show the direct sender selecting 20/20 Fuel Ore with 500 credits, then handing quote handling to untracked recovery. Recovery restarted the trade handler without the selected product/quantity state (`product=none`, `quantity=0`) and therefore treated affordable quotes as unselected and sent `0`.
- The existing sender aborts when multiple prompts arrive before a response, which is consistent with the handoff, but that exact triggering exception was not present in the archived log. The confirmed defect is the loss of selection context between the direct attempt and recovery.
- The final resource state was unchanged, so this run did not spend the 500 credits.

## Repairs and validation

- Correlated a live recovery quote with the immediately preceding fresh quantity prompt. Affordable quotes can now retain their verified product/sale context; an unmatched quote is left unanswered instead of receiving `0`.
- After untracked recovery, the worker now schedules a fresh ship-status snapshot against the recovered opportunity before considering the transaction verified.
- Added MTC safety-harness cases for accepting a correlated affordable quote and leaving an unmatched quote unanswered. All 151 harness checks passed.
- Added worker coverage for requesting a fresh snapshot after untracked recovery. The full TradeWars suite passed: 274 passed, 0 failed.
- The Release LLM Control Center build succeeded with 0 warnings and 0 errors.
- These repairs were made after iteration 34. They are source-tested and build-validated, but still require a live purchase and fresh cargo/credit delta to close DEFECT-0029.

## Runtime metrics

- 29 samples covered 04:17:30.497–04:32:27.449.
- Average MTC working set: 490.7 MiB (418.5–569.5 MiB); average private memory: 438.5 MiB (364.0–519.5 MiB); thread count: 91–99 (95.0 average).
- Sampled CPU use was approximately 48.1% of one core over the metric interval.
- Container memory was stable between first and last samples: LiteLLM 324.9 MiB, API 47.71 MiB, OpenWebUI 691.2 MiB, Apache 6.977 MiB, and DB 32.05 MiB.
- The timed capture initially used an incorrect game-log filename. The actual dated game log and debug log were copied manually after cutoff; start/end metadata and all 29 metric samples are preserved in the session workspace archive `files/campaign-34-run`.

## Remaining risks

- A live run must confirm that recovery accepts the quote, the next snapshot verifies cargo and credit deltas, and trade planning does not resume the 172/805 route loop.
- Pilot and Strategic prompt/response history still needs durable capture; the current run's debug log alone cannot prove whether those lanes were invoked.
- Scanner purchase and WorldTrade remain unverified. Iteration 35 is the first live validation of the new recovery path.
