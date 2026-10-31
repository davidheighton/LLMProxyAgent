# Iteration 01

**Campaign status:** 1 of 20 iterations complete; 19 remain.  
**Autonomous window:** 2026-10-29 02:42:19.526-06:00 to 02:57:19.526-06:00 (exactly 15 minutes).  
**Agent:** Meridian / MTC.

## Run summary

- MTC stayed running for the full window. The game server disconnected once at 02:49:59.146 and MTC reconnected at 02:50:04.157, a 5.011-second interruption.
- After reconnect, the worker remained at “Waiting for a recognized login prompt.” Before the disconnect it waited for a fresh authenticated Command prompt.
- The Pilot made no LLM calls during the window. There was one Strategic Review and 14 Reflex interactions.
- The ship was verified in sector 551 with 65,520 turns at the captured status point. No scanner purchase or WorldTrade start was verified.
- The exact server text that caused the post-reconnect login wait was not retained. No speculative login or game input was sent.

## Issues and confirmed causes

| Defect | Finding | Root cause / status |
|---|---|---|
| DEFECT-0001 | Fixed CIM scripts failed to resolve reliably after nested script execution. | The TWX interpreter can change its relative script base. MTC now resolves the fixed CIM names under the configured scripts root and fails explicitly if missing. Both scripts loaded from the expected `utilities` directory during runtime. **Validated.** |
| DEFECT-0002 | Reflex output used confidence values 20, 50, 40, 3, 3, 50, 70, and 30 in 8 of 14 responses. | The Reflex prompt and schema specified a number but not the required `[0,1]` fraction; the deterministic gate correctly rejects values outside that range. Prompt, schema, validation reporting, and regression coverage were added after the run. **Fixed; live verification pending.** |
| DEFECT-0003 | The Strategic Review chose `continue` while also returning objective-transition fields. | The prompt’s generic “null where appropriate” guidance and permissive schema did not encode decision-specific field rules. The provider now validates field/decision consistency before marking the interaction successful; the service also rejects proposal IDs on `proposeAndSelect`. **Fixed; live verification pending.** |
| DEFECT-0004 | The Strategic Review described `starDockShopper` as a way to buy Organics. | The prompt named approved scripts but did not make their responsibilities explicit. It now restricts `starDockShopper` to verified StarDock hardware/upgrades and reserves port-pair trading for `worldTrade`. **Fixed; live verification pending.** |
| DEFECT-0005 | The Pilot did not act and remained blocked on an authenticated prompt after reconnect. | The transport auto-reconnected, but the worker did not reach a recognized login or Command prompt. The precise terminal prompt and login state were not captured, so the root cause remains unconfirmed. **Investigating.** |
| DEFECT-0006 | Dashboard model telemetry did not agree with the recorded model interaction history. | The monitor showed zero requests / `qwen3:1.7b` while the Strategic Review history identified `qwen3:8b`. The source of the telemetry mismatch is not confirmed. **Investigating.** |
| DEFECT-0007 | LiteLLM was marked unhealthy while its local liveness/readiness endpoints returned HTTP 200. | Health-check state and endpoint readiness disagree; LiteLLM was not required for the observed direct Ollama calls. No container changes were made. **Investigating.** |

The Reflex history shows 6/14 confidence values within the gate’s numeric range. The Strategic Review response also violated the objective-field contract and assigned a trading purpose to the StarDock script. These are output-contract measurements, not a complete semantic-quality score.

## Repairs and validation

Repairs already present before the live window:

- Canonicalized the two fixed CIM script paths against the configured scripts root.
- Enabled detailed MTC debug, verbose parameter, AutoRecorder, and port-haggle logging; left the high-volume script VM trace disabled.
- Added the Density Scanner choice, separate scanner ownership parsing, selected-target enforcement, post-purchase verification, and corresponding worker tests. This was not exercised in the live window because the Pilot did not act.
- Updated the stale MTC RPC fixture to provide quick-status evidence.

Repairs after analyzing this window:

- Explicitly defined Reflex confidence as a fraction in `[0,1]` in both prompt and JSON schema.
- Validated Reflex assessments before recording their LLM interaction as successful; out-of-range confidence now has a specific failure reason.
- Added explicit Strategic Review field mappings and script responsibilities. Invalid objective payloads are now recorded as failed model interactions instead of successful responses later rejected by the service.
- Added service validation for a non-null `selectedProposalId` on `proposeAndSelect`.

Validation results after repairs:

- Focused Reflex and Strategic Review tests: **24 passed, 0 failed**.
- Full TradeWars regression suite: **230 passed, 0 failed**.
- MTC build: **succeeded, 0 warnings, 0 errors**.
- Earlier MTC RPC safety harness: passed.
- The TWX Dock Shopper scanner-purchase path was not exercised by this live run; it remains a next-run verification item.

## Metrics and artifacts

The metrics sampler produced 23 samples from 02:45:24.489 to 02:57:09. The sample did not cover the entire 15-minute window.

- MTC working set: **342.1–393.5 MiB**, average **363.0 MiB**.
- MTC CPU delta during the sample: **27.92 CPU seconds / 705 seconds elapsed**, or **3.96% of one core** (about **0.17% of a 24-core host**).
- Process survival: 100% of the autonomous window.
- Connection availability, accounting for the 5.011-second reconnect: approximately **99.44%**.
- These are sampled MTC/process measurements, not comprehensive host or leak analysis. No crash or stack trace was observed in the captured interval.

Persisted evidence:

- [iteration-01-reflex-history.txt](./iteration-01-reflex-history.txt) — 14 prompt/response records.
- [iteration-01-strategic-history.txt](./iteration-01-strategic-history.txt) — Strategic Review prompt and response.
- [iteration-01-dashboard-events.txt](./iteration-01-dashboard-events.txt) — dashboard event/status snapshot.
- [iteration-01-metrics.jsonl](./iteration-01-metrics.jsonl) — sampled process/container metrics.
- [iteration-01-debug.log](./iteration-01-debug.log) — MTC debug lines from the run interval.
- The original rolling log remains at `LLMControlCenter/logs/Meridian Game D_debug.log`.

Full prompts, raw model responses, structured decisions, and available rationale fields were captured. Hidden model reasoning was not used as an acceptance signal; the structured summaries and rationales are the auditable decision trace.

## Scorecard

| Metric | Iteration 01 | Measurement |
|---|---:|---|
| Build Stability | 100% | Post-repair MTC build succeeded; 230/230 regression tests passed. |
| Runtime Stability | 99.44% | Connected time during the 900-second run; the MTC process itself survived the entire window. |
| LLM Reasoning | 40% | Limited contract proxy: `(6 in-range Reflex outputs + 0 consistent Strategic outputs) / 15 observed outputs`. Not a semantic grader. |
| Prompt Quality | 100% source-contract coverage | Both newly targeted prompt contracts are now explicit and asserted by tests; the live responses predate these changes. |
| Autonomous Recovery | 0% end-to-end | Transport recovered, but the agent did not return to a fresh authenticated Command prompt. |
| Regression Rate | 0% observed | No previously fixed defect was observed to recur in this run. |
| Defect Closure Rate | 14.3% | 1 of 7 tracked defects is runtime-validated; prompt fixes await live verification. |

## Remaining risks and next-run checks

1. Capture the exact post-reconnect server prompt and determine why the login state is not recognized before adding any automatic response.
2. Verify that the Pilot resumes, purchases a Density Scanner when prerequisites allow, and starts `worldTrade` only after verified Holo ownership at StarDock.
3. Confirm that invalid Reflex and Strategic outputs are visibly marked as failed in dashboard history.
4. Reconcile model request telemetry with stored interaction records.
5. Investigate the LiteLLM health-check mismatch without disrupting the direct Ollama path.
6. Measure resources throughout the full window and retain the same metrics fields in each iteration.
