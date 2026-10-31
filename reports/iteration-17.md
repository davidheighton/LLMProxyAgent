# Iteration 17

**Campaign status:** 17 of 20 iterations complete; 3 remain.  
**Autonomous window:** 2026-10-29 08:41:01.4668801-06:00 to 08:56:01.5672249-06:00 (900.102 seconds elapsed).  
**Sampler completion:** 08:56:01.5672249-06:00.

## Run summary

- MTC PID 91744 remained running in all 30 process samples. Docker statistics succeeded in all 30 samples for the five required containers.
- The monitor was Connected and Authenticated at cutoff, but the Pilot had made **0 requests**. Its last activity was “Ignored a stale queued game prompt.” The authenticated worker did not resume useful gameplay.
- The worker queues were empty and reported zero dropped game events at cutoff. The last Reflex cycle was at 08:39; no Strategic Review cycle or active TWX script was observed during the window. Watchdog progress remained 0/3.
- The displayed verified snapshot was older than the latest synchronized ship-status event: the snapshot was captured around 08:37, while a Density Scanner status was observed around 08:39. The monitor still showed the old situation as Healthy.
- At the cutoff snapshot, sector 97, 63,781 turns, and 21,856 credits were reported. Density Scanner presence was observed, but Holo Scanner ownership, StarDock location, and WorldTrade execution were not verified.
- The 15-minute run used the pre-repair runtime. The stale-prompt recovery, freshness display, and Qwen3 thinking changes below were made and tested afterward; they are not live-validated by this run.

## Findings and root causes

| Finding | Root cause | Severity |
|---|---|---|
| Authenticated Pilot remained idle after a stale prompt was discarded. | The 45-second stale-event guard correctly prevented acting on queued prompt data, but there was no safe recovery path if no new `ServerPrompt` arrived. MTC heartbeats confirmed connectivity but did not include the current prompt surface. | HIGH |
| Monitor showed old synchronization as Healthy. | Heartbeats updated worker telemetry while the retained `GameSituationReport` remained unchanged; the UI displayed its synchronization value without checking the capture age. | MEDIUM |
| Qwen3 Pilot traces lacked reasoning output. | Pilot requests explicitly set `think=false`; the captured transport body already supports retaining the response payload, but the trace mode was disabled. | LOW |
| Full-test runs intermittently failed while deleting temporary SQLite files. | Several independent fixtures hit transient Windows file locks during cleanup. The failed tests passed in isolation and the full suite passed on a later complete rerun; no product-code root cause was confirmed. | LOW / test reliability |

## Repairs applied after the timed run

- Kept the stale-prompt safety guard. When it drops a stale prompt, the worker now waits for a prompt-bearing heartbeat no older than 15 seconds and re-queues that current prompt with its fresh heartbeat timestamp. It does not recover from a heartbeat while another command retry is pending.
- MTC now carries the most recently observed prompt surface in its five-second presence heartbeat. The cached prompt is cleared on client input, non-prompt server output, connect, and disconnect, so a heartbeat cannot revive a prompt after intervening activity.
- The monitor now labels a situation stale when its capture is older than 30 seconds and warns that stale game state will not be used for autonomous action.
- Qwen3 Pilot requests now enable `think=true`; response capture retains the transport response, including any returned thinking field. The response token ceiling was raised from 256 to 512 to leave room for both trace and structured decision output. No system-prompt wording changed.

## Files changed

- `LLMControlCenter/GameAgentIntegration.cs`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.Runtime/AutonomousTestWorker.cs`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.TradeWars.Tests/AutonomousTestWorkerTests.cs`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.Models/OllamaAutonomousTestActionProvider.cs`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.TradeWars.Tests/AutonomousTestActionProviderTests.cs`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.Dashboard/Components/Pages/AgentMonitor.razor`

## Validation

- Focused worker and Ollama provider tests: **74 passed, 0 failed**.
- Full solution Release build: **0 warnings, 0 errors**.
- Full TradeWars test suite: **237 passed, 0 failed, 0 skipped** on the final rerun.
- Earlier full-suite attempts encountered transient SQLite fixture cleanup locks (different temporary databases); the isolated failure passed and the complete suite later passed. No cleanup workaround was added without a confirmed product cause.
- The timed run's privacy-safe debug-log window contained 725 timestamped lines, with no error/exception or warning matches. This does not substitute for dashboard counters or prove absence of all runtime faults.

## Resource metrics

The sampler recorded 30 samples from 08:41:03.636 to 08:55:33.621, spanning 869.99 seconds between the first and last samples. MTC was present in 30/30 samples; Docker statistics succeeded in 30/30.

- Working set: **369.3–394.4 MiB**, average **387.2 MiB**.
- Private memory: **318.3–342.3 MiB**, average **335.1 MiB**.
- Thread count: **88–95**.
- CPU delta: **21.219 seconds** over the 869.99-second sample span, or **2.44% of one core**.
- Container memory was nearly unchanged from first to last sample: LiteLLM 325.1→325.2 MiB, API 47.71→47.71 MiB, Open WebUI 691.2→691.0 MiB, Apache 6.977→6.977 MiB, and database 31.93→31.94 MiB.
- These sampled values show no abrupt resource growth during the window; they are not a complete leak analysis.

## Scorecard

| Metric | Iteration 17 | Measurement |
|---|---:|---|
| Build Stability | 100% | Full Release build and all 237 tests passed after repairs. |
| Runtime Stability | N/A | MTC survived all samples, but exact cumulative connected seconds were not captured. |
| LLM Reasoning | N/A | No Pilot model response occurred in the window; available role-history counts are not a defensible quality sample. |
| Prompt Quality | 100% source-contract coverage | No prompt template changed; existing structured-output tests pass. Qwen3 thinking is enabled but awaits live verification. |
| Autonomous Recovery | N/A | No reconnect challenge occurred; stale-prompt recovery is tracked as a separate defect and was not live-tested after repair. |
| Regression Rate | N/A | No previously fixed defect was demonstrably reintroduced. |
| Defect Closure Rate | 29.4% | 5 of 17 defects are runtime-validated; new source fixes await a live run. |

## Remaining risks and iteration-18 checks

1. Verify a stale prompt is recovered only from a fresh heartbeat carrying a still-current prompt; confirm the clear-avoids prompt still receives the safe default `N`.
2. Confirm a fresh authenticated Command prompt produces Pilot requests and that Qwen3 response traces are retained without invalid or truncated decisions.
3. Check that the monitor changes a stale situation to Stale and returns to Healthy only after new verified state is captured.
4. Confirm game-event drops remain zero/low and the queue stays responsive under live activity.
5. Continue to require verified Holo Scanner ownership at StarDock before WorldTrade starts. Density Scanner ownership alone is not evidence that WorldTrade is ready.
6. DEFECT-0011 (WebView2 `MoveFocus` crash) remains under investigation; this run did not exercise the relevant UI activation path.

Persisted evidence:

- [iteration-17-dashboard-events.txt](./iteration-17-dashboard-events.txt) — privacy-safe cutoff state and agent counters.
- [iteration-17-metrics.jsonl](./iteration-17-metrics.jsonl) — 30 process and container samples.
- [iteration-17-debug.log](./iteration-17-debug.log) — privacy-safe log-window counts; no raw terminal text.
