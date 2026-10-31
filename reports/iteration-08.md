# Iteration 08

**Campaign status:** 8 of 20 iterations complete; 12 remain.  
**Autonomous window:** 2026-10-29 05:26:24.366-06:00 to 05:41:24.366-06:00 (exactly 15 minutes).  
**Agent:** Meridian / MTC.

## Run summary

- The standard launcher built and started MTC. The terminal connected and Meridian reported ready at 05:26:24.366.
- At the cutoff, MTC remained running and connected. The dashboard still showed Meridian waiting for a recognized login prompt; the current unclassified surface had length 44. Meridian was stopped through Runtime controls after evidence capture, and MTC PID 41600 was then stopped.
- The dashboard continued to show no verified game situation. Reactive and Strategic queues were empty and neither lane had completed a cycle.
- Safe event aggregation found 31 server-prompt events, including five surfaces of length 44. Eight client-input events occurred between 05:26:24 and 05:26:29; no later inputs were recorded. Input contents, raw prompt text, and fingerprint values were not accessed or persisted.
- No Pilot action, authenticated Command state, scanner purchase, StarDock action, or WorldTrade start was verified. No LLM transcript or decision was available to collect.

## Issues and root causes

| Defect | Finding | Root cause / status |
|---|---|---|
| DEFECT-0005 | Meridian again failed to reach authenticated Command readiness. The current surface remained unclassified at safe length 44 after the initial login inputs stopped. | The worker's fail-closed behavior is confirmed; the exact server prompt and ultimate authentication failure cause remain unknown. **Investigating.** |
| DEFECT-0009 | Bare BBS name prompt support remains present in source and regression tests. | The live surface remains unclassified, so this parser rule is not confirmed as the cause. **Fixed; live validation pending.** |
| DEFECT-0010 | The dashboard safely indicates when an unclassified prompt contains text. | The safe length diagnostic remained visible; raw text was omitted. **Validated.** |

## Repairs applied

- No source, prompt, or configuration repair was made. Iteration 8 reproduced the login stall without identifying the prompt type; guessing a response would risk sending credentials or unintended game input.
- The full TradeWars suite passed after the window. Existing prompt parser and response-contract fixes remain covered by regression tests.

## Validation

- The standard launcher build succeeded before startup.
- Full TradeWars test suite: **233 passed, 0 failed, 0 skipped**.
- The dashboard showed the game terminal Connected and Meridian Running at the cutoff. Meridian was stopped through Runtime controls; MTC PID 41600 was stopped by exact PID after capture.
- MTC remained running in all **31** resource samples; Docker statistics succeeded in all samples and covered five containers.
- The dashboard showed no verified game state, no reactive or strategic cycles, and empty queues. No scanner purchase or WorldTrade start occurred.
- Run-scoped debug logging recorded startup readiness but no further debug writes during the autonomous window. Raw terminal output was not copied into campaign artifacts.

## Metrics and artifacts

Resource sampling produced **31 samples**, from 05:26:52 to 05:41:24. Sampling began about 28 seconds after the window started and ended at the scheduled cutoff.

- MTC working set: **260.7–273.0 MiB**, average **268.5 MiB**.
- MTC private bytes: **221.5–230.5 MiB**, average **226.6 MiB**.
- MTC thread count: **88–98**.
- Sampled MTC CPU delta: **14.22 CPU seconds over 872 seconds**, or **1.63% of one core**.
- Docker statistics succeeded in all 31 samples and covered five containers.
- This sample interval omitted the first 28 seconds and cannot establish absence of longer-term memory growth.

Persisted evidence:

- [iteration-08-dashboard-events.txt](./iteration-08-dashboard-events.txt) — final connection, agent, model-readiness, background-work, and safe event observations.
- [iteration-08-metrics.jsonl](./iteration-08-metrics.jsonl) — sampled MTC and container metrics.
- [iteration-08-debug.log](./iteration-08-debug.log) — run-scoped debug logging summary; no prompt or raw terminal text.
- Event counts were aggregated locally; raw prompt text and fingerprint values were not copied to the report.

## Scorecard

| Metric | Iteration 08 | Measurement |
|---|---:|---|
| Build Stability | 100% | Launcher build succeeded; the full regression suite passed (233/233). |
| Runtime Stability | 100% | Terminal remained connected for the exact 900-second window; MTC survived every sample. |
| LLM Reasoning | N/A | No Pilot, Reflex, or Strategic Review response was available for evaluation. |
| Prompt Quality | 100% source-contract coverage | Existing prompt-contract tests remained in force; no live response was available. |
| Autonomous Recovery | 0% | The worker did not reach a verified authenticated Command prompt. |
| Regression Rate | N/A | No previously fixed model-output contract was exercised. |
| Defect Closure Rate | 40.0% | 4 of 10 recorded defects are runtime-validated: DEFECT-0001, DEFECT-0006, DEFECT-0007, and DEFECT-0010. |

## Remaining risks and next-run checks

1. Keep raw MTC terminal text private. Five length-44 observations do not establish that the surfaces are identical or identify their meaning.
2. Continue tracing the boundary from MTC `isPrompt` lines through `ResolveGameAgentPromptSurface` and the login worker; add a recognized category only when evidence identifies the actual prompt type.
3. Keep the worker fail-closed for unknown login prompts; do not ask the model or send speculative input while authentication is unverified.
4. Continue the acceptance checklist only after authenticated game state is verified: acquire the Density Scanner, complete ship upgrades and Holo Scanner purchase at StarDock, then start `worldTrade` only after verified Holo ownership.
5. Continue anti-regression verification before the next autonomous window.
