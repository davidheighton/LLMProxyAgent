# Iteration 18

**Campaign status:** 18 of 20 iterations complete; 2 remain.  
**Autonomous window:** 2026-10-29 09:18:53 to 09:33:53 -06:00 (900.006 seconds).  
**Sampler:** 29 samples from 09:18:53.895 to 09:33:49.392 -06:00.

## Run summary

- MTC remained present in all 29 process samples. Docker statistics succeeded
  in all 29 samples for the five required containers.
- The dashboard showed an authenticated Command session in sector 97. It
  displayed `Ollama returned an empty test-action decision`; the exact model
  response cause was not durably available after the client stopped.
- The event window contained 243 events: 180 presence heartbeats, 56 server
  lines, and 7 server prompts. It recorded no sector changes or client-input
  actions. The ship did not demonstrate gameplay progress during this run.
- No scanner purchase, safe exploration, WorldTrade execution, or earnings
  were verified in the window. Exact cumulative connected seconds were not
  captured, so runtime stability is N/A despite process survival.

## Event-log analysis and root causes

The post-run analysis reviewed 161,984 Meridian game events from five daily
files and the available terminal/MTC logs. See
[event-log-learning.md](./event-log-learning.md) for the full evidence and
limitations.

| Finding | Evidence / root cause | Severity |
|---|---|---|
| An active ship was lost entering sector 99. | The synchronized status changed from a 40-hold Merchant Cruiser with no scanner in sector 369 to a one-hold Escape Pod in sector 99. The multi-hour status gap means the exact hazard and scanner state at entry are unknown. | CRITICAL |
| WorldTrade eligibility was stricter than the user's requirement. | The code/documentation required Holo Scanner despite the user's clarified rule that Density is sufficient and Holo also supports density scans. | HIGH |
| Trade safety lacked a deterministic Escape Pod/one-hold block. | The prior action gate did not prevent port trading when the active ship had one or fewer holds. | HIGH |
| Empty Ollama final content failed the Pilot action. | The provider rejected empty final content without retrying once with Qwen thinking disabled. The model's underlying response cause remains unconfirmed. | HIGH |
| Prompt and final-response history was not durable across shutdown. | LLM interactions were held in memory; archived game events do not include the model prompt/reply. | MEDIUM |

## Repairs and changes

- Scanner eligibility now accepts verified Density or Holo ownership for
  WorldTrade. Holo is optional, includes density scanning, and avoids a
  redundant Density purchase.
- Deterministic trade guards block Escape Pod and one-hold ships.
- Qwen empty-final-content handling now makes one retry with thinking disabled
  and records safe response metadata, while preserving fail-closed structured
  action validation. The precise upstream empty-response cause is still
  unconfirmed.
- Scanner-first, scan-before-warp, prompt-state, and trade-safety knowledge
  was updated from the event evidence. Historical logs were archived before
  the per-game state reset.
- The saved character snapshot and transient saved variables were reset;
  historical event logs, terminal logs, and the old per-game database were
  archived before iteration 19.

## Files changed

- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.Models/OllamaAutonomousTestActionProvider.cs`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.Models/OllamaReasoningProvider.cs`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.Runtime/AutonomousTestWorker.cs`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.TradeWars.Tests/AutonomousTestActionProviderTests.cs`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.TradeWars.Tests/AutonomousTestWorkerTests.cs`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.TradeWars.Tests/StrategicReviewTests.cs`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.Dashboard/KnowledgeSources/autonomous-bot/LLM_GAMEPLAY_RESEARCH.md`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.Dashboard/KnowledgeSources/autonomous-bot/ROUTINE_CATALOG.md`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.Dashboard/KnowledgeSources/autonomous-bot/README.md`
- `reports/event-log-learning.md`
- `reports/lessons-learned.md`

## Validation

- Focused action-provider, worker, and strategy tests: **89 passed, 0 failed**.
- Full Release agent solution build: **0 warnings, 0 errors**.
- Full TradeWars test suite: **242 passed, 0 failed, 0 skipped**.
- LLM Control Center Windows x64 Release build: **0 warnings, 0 errors**.
- The first launch attempt used the non-RID output, which lacked embedded
  Avalonia UI resources and could not host the dashboard. No autonomous test
  was run with that binary. The Windows x64 build was subsequently verified to
  contain `!AvaloniaResources`; use only its `win-x64` output.

## Resource metrics

Across 29 samples, MTC working set was **348.1-394.0 MiB** (average **361.2
MiB**), private memory averaged **308.4 MiB**, and thread count was **88-98**.
The sampled CPU delta was **18.14 seconds**. The five containers remained
available for every Docker sample. These measurements show process/container
availability, not autonomous gameplay progress.

## Scorecard

| Metric | Iteration 18 | Measurement |
|---|---:|---|
| Build Stability | 100% | Release agent solution and Windows x64 client builds succeeded; all 242 tests passed. |
| Runtime Stability | N/A | MTC survived all samples, but exact cumulative connected seconds were not captured. |
| LLM Reasoning | N/A | Empty-response failure was observed; final response transcript was unavailable for quality analysis. |
| Prompt Quality | 100% source-contract coverage | Either scanner is now supported; scanner/trade and ship-safety requirements have regression assertions. |
| Autonomous Recovery | N/A | The window did not exercise a verified recovery transition. |
| Regression Rate | N/A | No previously fixed defect was confirmed as regressed. |
| Defect Closure Rate | 23.8% | 5 of 21 recorded defects are runtime-validated; current source fixes await live confirmation. |

## Remaining risks

1. Start a fresh character only after the clean profile and database are
   verified; preserve the saved server/game connection settings.
2. Confirm live login completes through BBS name, game, and new-character
   prompts without replaying stale input.
3. Verify Density Scanner purchase at StarDock, then use fresh ship status to
   enable WorldTrade. Holo remains optional.
4. Confirm scanner preflight blocks the dangerous sector before every ordinary
   warp and that escape-pod trade guards remain active.
5. Capture LLM prompts and final responses before stopping MTC; hidden/internal
   model reasoning is not a substitute for the structured decision.
6. Complete exactly 15 minutes each for iterations 19 and 20, then write the
   final campaign analysis and defect audit.
