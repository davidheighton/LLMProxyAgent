# Iteration 15

**Campaign status:** 15 of 20 iterations complete; 5 remain.  
**Autonomous window:** 2026-10-29 07:41:36.9070395-06:00 to 07:56:36.9070395-06:00 (900 seconds).  
**Sampler completion:** 07:56:36.9296829-06:00 (900.0226434 seconds elapsed).

## Run summary

- The standard launcher built the pre-fix solution successfully with 0 warnings and 0 errors and started MTC PID 91696 before the window.
- At 07:44:21, MTC terminated due to an unhandled `ArgumentException` from Avalonia WebView2 `ICoreWebView2Controller.MoveFocus` during window activation. A replacement MTC process, PID 65428, appeared at 07:46:39; the cause of the restart was not established.
- The replacement process continued using the pre-fix binary. At 07:46:43 the .NET Runtime log recorded that the login worker ignored an unclassified terminal surface of length 75 and sent no input. The monitor still reported an unclassified surface of length 44; raw terminal contents were not logged or copied.
- At the window cutoff, MTC PID 65428 reported Terminal Connected and Agent Running, but the verified game state was still unavailable. Watchdog progress was 0/3 unchanged cycles, with no cooldown. Pilot history showed 0 calls.
- After the timed window, the fix was rebuilt and the application relaunched. The fresh session reached **Authenticated / Command** with a Healthy verified snapshot: sector 99, 65,520 turns, 21,856 credits, and no Holo Scanner. This validates the login recovery transition, but no Pilot action, scanner purchase, or WorldTrade start was verified.
- The post-fix monitor showed Pilot history 0, Strategic Review 1, and Reflex 2. The last decision was `DisplayCurrentSector`, with a summary that local sector and warp information was being refreshed before route selection.

## Confirmed defect and repair

| Defect | Finding | Root cause / status |
|---|---|---|
| DEFECT-0005 | Login remained stuck before the watchdog-managed gameplay loop. | The prompt `Do you wish to clear some avoids? (Y/N) [N]` was not classified. The login worker ignored the unknown surface and never sent the default-preserving `N` response. The decoder now recognizes `ClearAvoids`, and the login sequence sends `N`. **Live-validated:** after rebuilding, the monitor reached authenticated Command and no longer reported the unclassified login surface. |

## New runtime defect

| Defect | Finding | Root cause / status |
|---|---|---|
| DEFECT-0011 | MTC exited unexpectedly at 07:44:21, interrupting the test window. | The .NET Runtime recorded an unhandled `ArgumentException` in Avalonia WebView2 `MoveFocus` during `NativeWebView.OnGotFocus` / window activation. The exact invalid focus state is unknown. **Investigating.** |

## Repairs and validation

- Added the `ClearAvoids` prompt pattern to the telemetry decoder and `KnownPromptSurfaces`.
- The existing-account login sequence explicitly sends `N`, preserving existing sector avoids.
- Added decoder coverage and an end-to-end login-worker test that verifies the exact prompt receives `N`.
- Focused login tests passed: **23 passed, 0 failed**.
- The first full-suite run had one transient SQLite fixture cleanup lock in `WorldEngineTests`; the full rerun passed: **234 passed, 0 failed, 0 skipped**.
- The first post-fix solution build was blocked because the active MTC PID 65428 held output DLLs. After stopping Meridian and MTC at the end of the timed window, the complete solution build succeeded with **0 warnings and 0 errors**.
- The rebuilt application launched as MTC PID 92192 and reached an authenticated Command snapshot. This is live validation for the login fix; the subsequent Pilot/gameplay progression remains under observation in iteration 16.

## Metrics and artifacts

The sampler produced **30 samples** and Docker statistics succeeded in all 30. It was bound to the original MTC PID 91696: that process was present for 6 samples, covering 150 seconds, then terminated. The 24 later `mtcRunning: false` samples refer to that original PID, not to the replacement PID 65428.

- Original PID 91696 working set during its sampled interval: **263.4–280.9 MiB**, average **268.1 MiB**.
- Private bytes: **222.0–234.4 MiB**, average **226.6 MiB**.
- Thread count: **93–102**.
- CPU delta: **6.204 seconds over 150 seconds**, or **4.14% of one core** while that PID was alive.
- Container metrics succeeded in all samples; all five required containers remained up.
- Based on the crash at 07:44:21 and replacement process creation at 07:46:39, MTC was unavailable for about 138 seconds and available for approximately 762/900 seconds (**84.7%**) in the timed window. The replacement PID was not sampled by the original-PID-bound sampler.
- A separate post-window MTC PID 92192 was built and launched for live login validation. Its resource samples are recorded under iteration 16.

Persisted evidence:

- [iteration-15-dashboard-events.txt](./iteration-15-dashboard-events.txt) — cutoff monitor state, watchdog status, safe login diagnostics, and post-fix smoke result.
- [iteration-15-metrics.jsonl](./iteration-15-metrics.jsonl) — PID-bound MTC and container samples.
- [iteration-15-debug.log](./iteration-15-debug.log) — safe summary of the login warning and WebView2 crash; no raw terminal output.

## Scorecard

| Metric | Iteration 15 | Measurement |
|---|---:|---|
| Build Stability | 100% | Launcher and post-fix solution builds succeeded with 0 warnings/errors. |
| Runtime Stability | 84.7% | MTC exited unexpectedly, then a replacement process appeared; the timed window was not continuously available. |
| LLM Reasoning | N/A | No Pilot response was recorded; post-window history showed Strategic Review 1 and Reflex 2. |
| Prompt Quality | 100% source-contract coverage | The exact prompt and safe default response are now covered by decoder and login-worker tests. |
| Autonomous Recovery | 100% login readiness | Post-fix live session reached authenticated Command; gameplay progression is not yet verified. |
| Regression Rate | N/A | No previously fixed model-output contract was exercised. |
| Defect Closure Rate | 45.5% | 5 of 11 recorded defects are runtime-validated, including DEFECT-0005. |

## Remaining risks and next-run checks

1. Keep raw terminal text private. The .NET log records only the unclassified surface length; the prompt text in this report came from the operator.
2. Track DEFECT-0011 separately; do not mask the WebView2 activation exception with a broad exception handler.
3. Confirm the Pilot begins acting after the authenticated Command state and that fresh ship/warp data is delivered.
4. Continue the acceptance checklist: acquire the Density Scanner, complete ship upgrades and Holo Scanner purchase at StarDock, then start `worldTrade` only after verified Holo ownership.
5. Continue anti-regression verification during iteration 16.
