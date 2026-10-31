# Iteration 07

**Campaign status:** 7 of 20 iterations complete; 13 remain.  
**Autonomous window:** 2026-10-29 05:08:17.474-06:00 to 05:23:17.474-06:00 (exactly 15 minutes).  
**Agent:** Meridian / MTC.

## Run summary

- The standard launcher built and started MTC. The terminal connected, telnet negotiation completed, and MTC logged agent readiness at 05:08:17.474.
- At post-cutoff inspection, the terminal was Connected and Meridian was Running, waiting for a recognized login prompt. The dashboard showed an unclassified surface of length 44 with raw text omitted. Meridian was stopped through Runtime controls after evidence capture; the terminal remained connected.
- The Pilot model panel showed Ollama / `qwen3:1.7b`, idle, with zero completed and failed requests. LLM history showed zero Pilot, Strategic Review, and Reflex interactions. Reactive and Strategic queues were empty, with no cycles.
- No authenticated Command state, verified game situation, scanner purchase, StarDock action, or WorldTrade start was observed.
- A privacy-safe event-summary query found **26 prompt events**: nine recognized labels (Pause 3, Menu 2, DailyLog 2, Password 2) and 17 unclassified surfaces. Five unclassified events had length 44 and represented **two distinct safe fingerprints**; both matched the two distinct length-44 fingerprints from iteration 6. The report retains only counts, not raw prompt text or fingerprint values.
- Six client-input events occurred in the initial login sequence between 05:08:17 and 05:08:22; no later client input was recorded. Input contents were not accessed. No speculative input was sent.

## Issues and root causes

| Defect | Finding | Root cause / status |
|---|---|---|
| DEFECT-0005 | Meridian again failed to reach authenticated Command readiness. Safe counts show 17 unclassified events, including five length-44 surfaces with two distinct fingerprints. | MTC may forward an unclassified prompt line to the worker, which safely ignores unknown surfaces. Six login inputs occurred in the first five seconds, then none. The exact prompt meaning and ultimate authentication failure cause remain unconfirmed. **Investigating.** |
| DEFECT-0009 | Bare BBS name prompt support remains present. | No exact raw prompt was retained to determine whether this parser rule applies. **Fixed; live validation pending.** |
| DEFECT-0010 | The dashboard safely indicates when an unclassified prompt contains text. | Safe length reporting remained visible, with raw text omitted. **Validated.** |

## Repairs applied

- No source or prompt repair was made during this iteration. Iteration 6 already narrowed the event path; this run confirmed the same fail-closed behavior and added no new identifying evidence.
- No raw prompt, credential, or input was captured or guessed.

## Validation

- The standard launcher build and MTC startup succeeded.
- The full TradeWars suite passed immediately before the window (**232 passed, 0 failed**); no source changed during this iteration.
- MTC remained running in all **31** resource samples, and Docker statistics succeeded in all samples.
- The final monitor capture showed the terminal Connected and Meridian Running before the post-window stop. After stopping Meridian, the terminal remained connected.
- The rolling game debug log contains startup and telnet negotiation only; no prompt, gameplay action, exception, warning, stack trace, or disconnect was persisted there.

## Metrics and artifacts

Resource sampling produced **31 samples**, from 05:08:46 to 05:23:17. It began about 29 seconds after the window started and ended at the scheduled cutoff.

- MTC working set: **261.9–272.3 MiB**, average **266.1 MiB**.
- MTC private bytes: **221.4–230.1 MiB**, average **224.3 MiB**.
- MTC thread count: **89–98**.
- Sampled MTC CPU delta: **14.23 CPU seconds over 871.00 seconds**, or **1.63% of one core**.
- All 31 samples recorded MTC running; Docker statistics had no failed samples and covered five containers.
- This sample interval omitted the first 29 seconds and cannot establish absence of longer-term memory growth.

Persisted evidence:

- [iteration-07-dashboard-events.txt](./iteration-07-dashboard-events.txt) — final connection, agent, model, background-work, and role-history observations.
- [iteration-07-metrics.jsonl](./iteration-07-metrics.jsonl) — sampled MTC and container metrics.
- [iteration-07-debug.log](./iteration-07-debug.log) — startup and telnet negotiation lines; no prompt or gameplay lines were logged.
- Event counts and fingerprint aggregation were computed in memory; raw prompt text and fingerprint values were not copied to the report.

## Scorecard

| Metric | Iteration 07 | Measurement |
|---|---:|---|
| Build Stability | 100% | Launcher build succeeded; the full regression suite passed immediately before the window. |
| Runtime Stability | 100% | Terminal remained connected for the exact 900-second window; MTC survived every sample. |
| LLM Reasoning | N/A | No Pilot, Reflex, or Strategic Review response was generated. |
| Prompt Quality | 100% source-contract coverage | Existing prompt-contract tests remained in force; no live response was available. |
| Autonomous Recovery | 0% | The worker did not reach a verified authenticated Command prompt. |
| Regression Rate | N/A | No previously fixed model-output contract was exercised. |
| Defect Closure Rate | 40.0% | 4 of 10 recorded defects are runtime-validated: DEFECT-0001, DEFECT-0006, DEFECT-0007, and DEFECT-0010. |

## Remaining risks and next-run checks

1. Keep raw MTC terminal text private. The five 44-character unknown surfaces represent at least two values; length-only diagnostics are insufficient for identity or classification.
2. Trace the boundary from MTC `isPrompt` lines through `ResolveGameAgentPromptSurface` and the login worker; add a recognized category only when evidence identifies the actual prompt type.
3. Keep the worker fail-closed for unknown login prompts; do not ask the model or send speculative input while authentication is unverified.
4. Continue the acceptance checklist only after authenticated game state is verified: acquire the Density Scanner, complete ship upgrades and Holo Scanner purchase at StarDock, then start `worldTrade` only after verified Holo ownership.
5. Continue anti-regression verification before the next autonomous window.
