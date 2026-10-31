# Iteration 27

## Result

Productive but early-terminated. Meridian made verified movement to sector 206. The agent did not complete a port transaction, buy a Density or Holo Scanner, or start WorldTrade. A 12-hold Fuel Ore quote was 58 credits against 55 available. A later trade attempt stalled at a quantity prompt. After the prompt-recovery fix was built and MTC reconnected, no further gameplay action was verified.

## Issues found and root causes

- **A port-trade prompt was not recovered reliably.** The source trace found two related timestamp defects: `GameAgentRuntime.NormalizeEvent` filled empty presence-heartbeat prompt surfaces from the terminal buffer, and `AutonomousTestWorker` could substitute the heartbeat timestamp for a missing prompt-observation timestamp. MTC command waiters also compared heartbeat event timestamps in place of the prompt's observation time. This could make a stale screen prompt appear fresh or make a valid current prompt appear stale.
- **The attempted trade was not affordable.** The quoted 12-hold Fuel Ore buy cost 58 credits while the ship had 55. No purchase or credit/cargo delta was verified; repeated haggle causes from iteration 26 remain under investigation.

## Repairs applied

- Heartbeats no longer receive synthesized prompt surfaces from the terminal buffer.
- MTC marks a cached prompt as current explicitly and carries its original observation timestamp.
- The worker rejects heartbeat prompts without trustworthy observation metadata, except when MTC explicitly confirms the cached prompt remains current.
- MTC command-sender prompt checks now normalize heartbeat events to a trusted prompt-observation time before applying freshness and in-flight ordering.
- Added worker regressions for rejecting stale prompt echoes without metadata and accepting an explicitly confirmed current prompt.

## Validation

- Focused heartbeat recovery regressions: 3 passed, 0 failed.
- Full `AutonomousTestWorkerTests`: 64 passed, 0 failed.
- Release `win-x64` LLM Control Center build: succeeded with 0 warnings and 0 errors.
- Rebuilt MTC reconnected to the game and refreshed ship state at sector 206. Live recovery through a port quantity prompt and transaction verification remain pending.

## Runtime and LLM observations

- The worker was stopped early because the live session was not progressing; the 900-second window was not completed.
- No runtime metric samples or complete Pilot/Reflex/Strategic contract totals were available for this iteration.
- Current verified state after reconnect: sector 206, 55 credits, 20 empty holds, and no scanner purchase or WorldTrade start verified.

## Files modified

- `LLMControlCenter/GameAgentRuntime.cs`
- `LLMControlCenter/GameAgentIntegration.cs`
- `LLMControlCenter/DashboardIntegration.cs`
- `LLMControlCenter/MtcGameCommandSender.cs`
- `LLMControlCenter/EmbeddedMtcAgentSessionController.cs`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.Models/OllamaAutonomousTestActionProvider.cs`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.Runtime/AutonomousTestWorker.cs`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.TradeWars.Tests/AutonomousTestActionProviderTests.cs`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.TradeWars.Tests/AutonomousTestWorkerTests.cs`
- `reports/defect-ledger.json`
- `reports/defect-ledger.md`
- `reports/lessons-learned.md`
- `reports/scorecard.json`

## Remaining risks and lessons

- Do not infer a completed trade from an action decision, a quantity response, or a return to Command; verify fresh credits and cargo.
- The prompt fix is source-, test-, and build-validated but not yet live-validated through a completed port transaction.
- Trade quantity selection must account for available credits before submitting a purchase.
- Scanner acquisition and `2_worldtrade` remain outstanding. Start WorldTrade only after Density or Holo Scanner ownership is freshly verified.

## Follow-up: Pilot structured response and uncounted live attempt

- **Root cause found:** the Pilot schema's `oneOf` branches contained only `action` and `targetSector`; root-required `rationale`, `citedKnowledgeNodeId`, and conditional `scriptInputResponse` were not part of each branch. Ollama could return a branch-shaped response without the shared required fields, causing Pilot validation failures.
- **Repair:** added the shared fields and required lists to both branches, made rationale non-empty, and included `scriptInputResponse` in both branches only when the current approved script prompt requires it.
- **Validation:** all 27 `AutonomousTestActionProviderTests` passed. Release `win-x64` LLM Control Center build passed with 0 warnings and 0 errors. The rebuilt MTC returned a complete Pilot movement response with action, adjacent target, rationale, and citation.
- **Uncounted runtime attempt:** MTC reconnected and remained authenticated at Command, but the ship stayed at sector 551 with 1 credit and no verified scanner. By the latest monitor snapshot, Pilot had 62 interactions, Strategic Review 2, and Reflex 100; one Pilot output-limit failure was recovered by its retry. No movement, trade, scanner purchase, or WorldTrade start was verified, so the worker was stopped and this attempt was not counted as a productive iteration.
- **Remaining issue:** the monitor continued to report `RequestShipInformation` despite healthy synchronization. The valid model movement response did not establish that MTC submitted the command or that the game reached its target. DEFECT-0028 remains under investigation; trace prompt observation, command submission/acknowledgment, and the post-move snapshot before relaxing any safety gate.
- **Follow-up context reduction:** the Pilot request was also forwarding the unreviewed 506-entry script catalog despite having two approved script capabilities. The provider now retains only catalog entries matching those approved references. The provider test class passed 27/27 and the rebuilt Release runtime passed; its effect on live inference latency and movement remains unverified.
- **Latest uncounted worker window:** Meridian ran for roughly 15 minutes, but the verified state remained sector 551, 1 credit, and no Holo Scanner. At 5:41 PM the session was connected, authenticated, synchronized, and at Command, yet the latest decision was still `RequestShipInformation`. Dashboard history showed 44 Pilot, 2 Strategic Review, and 94 Reflex interactions, including two empty-output token-limit failures recovered by retry. No movement, trade, scan, scanner purchase, or WorldTrade start was verified; Meridian was stopped at 5:43 PM. The attempt is not a productive iteration.
- **Remaining diagnosis:** the fresh in-window snapshot rules out staleness as the immediate explanation for the last observed decision, but it does not establish why no route or trade action was executed. After shutdown the snapshot aged normally beyond the 30-second guard. Keep DEFECT-0028 open and capture the route target, action schema, command submission, and postcondition on the next attempt.
- **Action-schema repair:** removed `RequestShipInformation` from the Pilot enum when synchronization and the ship snapshot are healthy and the snapshot is no older than 30 seconds. The provider test class passed 27/27 and the Release `win-x64` build passed. Iteration 28 exercised the new build and verified movement, but a scannerless route loop remained.
