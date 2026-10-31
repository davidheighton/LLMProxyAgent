# Iteration 25

## Result

Productive but early-terminated. Meridian reached known StarDock sector 551 from sector 172. The last verified snapshot at StarDock showed 65,463 turns, 17 credits, no Density or Holo Scanner, and no scanner purchase. The last decision proposed returning to the known port in sector 172 to fund a scanner, but no fresh post-move snapshot or completed trade was verified. The worker was stopped around 11:58 after stale-prompt behavior returned; the worker did not complete the 900-second autonomous window.

## Issues found and root causes

- **Pilot output contract violations recurred.** Of 24 Pilot interactions, 19 completed and 5 failed. Four failures paired a non-movement action with a target sector; the remaining failure returned empty final content after Qwen3 used the 2,048-token budget in its thinking channel. The parser rejected invalid output before execution.
- **Stale queued prompt recovery recurred.** After reconnect, fresh prompts initially led to progress, but the worker later returned to stale-prompt and stale-snapshot behavior. The heartbeat-to-requeue transition was not captured.
- **Scanner and trade progression remained incomplete.** StarDock arrival was verified, but scanner ownership, a completed port transaction, and WorldTrade start were not. A decision to return to a port is not evidence that the ship moved or traded.
- **Reflex and Strategic Review were active.** The captured history contained 18 Reflex and 2 Strategic Review interactions. No reliable contract-compliance score could be calculated from the retained summaries.

## Repairs applied

- Encode the action/target dependency in the Pilot JSON schema with mutually exclusive `oneOf` branches: movement requires an adjacent verified target, and non-movement actions require `targetSector=0`.
- Clarify the same contract in the Pilot prompt and retain fail-closed parsing.
- Expand the action-provider regression test to assert the schema branches and reject a malformed non-movement decision with an otherwise valid adjacent target.

## Validation and deployment

- Release `win-x64` build: **succeeded, 0 warnings, 0 errors**.
- Full TradeWars suite: **256 passed, 0 failed** on rerun.
- The first full-suite run hit a transient Windows SQLite fixture cleanup lock in `MacroExecutionTests`; the full rerun passed. No product-code change was made for this known intermittent test-fixture failure.
- The updated MTC was started as PID 70172. Its embedded dashboard returned HTTP 200 on loopback port 53649.
- The 31-sample MTC process sampler completed 901.9 seconds with no process or dashboard failures. This measured MTC health, not 900 seconds of worker runtime: the worker had already been stopped before the sampler ended.
- Live Qwen validation of the full `oneOf` schema remains pending at this iteration's cutoff.

## Files changed

- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.Models/OllamaAutonomousTestActionProvider.cs`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.TradeWars.Tests/AutonomousTestActionProviderTests.cs`
- `reports/defect-ledger.json`
- `reports/defect-ledger.md`
- `reports/lessons-learned.md`
- `reports/scorecard.json`
- `reports/iteration-25.md`

## Prompt changes

Clarified that only `MoveToAdjacentSector` may use a nonzero `targetSector`, and that its value must be an adjacent verified sector. Other Pilot actions require `targetSector=0`.

## Performance changes

No performance optimization was made. During the MTC-only sample window, working set averaged 409.5 MiB (377.3–478.9 MiB), private memory averaged 348.1 MiB, thread count ranged from 89 to 97, and CPU increased by 42.37 seconds over 901.9 seconds. All 31 dashboard checks returned HTTP 200 and the MTC process remained alive. These results do not establish worker runtime stability or rule out a leak.

## Remaining risks

- Density/Holo Scanner acquisition, a verified port trade, and WorldTrade remain incomplete.
- Empty Qwen3 final content recurred at the 2,048-token limit.
- Stale queued prompt recovery remains unverified and recurred after reconnect.
- The new action/target schema was built and regression-tested but had not yet been exercised in live inference at the iteration cutoff.
- The live process may produce only summary-level history; preserve prompt/response artifacts before future MTC restarts.
