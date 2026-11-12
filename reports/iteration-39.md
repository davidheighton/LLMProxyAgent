# Iteration 39

## Result

- The autonomous observation window ran from 06:37:06.096 to 06:52:07.884 local. The runtime sampler captured 900.006 seconds over 30 samples; shutdown metadata reports 901.788 seconds. MTC survived the window.
- The run began at StarDock sector 551 with 4,072 credits, 20 Fuel Ore, and no Density Scanner. It ended at sector 172 with 5,752 credits, 20 empty holds, and no Density Scanner.
- A 20-unit Fuel Ore purchase for 102 credits and a 20-unit sale for 849 credits were accepted during the observation. A boundary sale quote was also accepted immediately before the timed window, with the increased balance reflected shortly afterward.
- Another 20-unit Fuel Ore attempt received a 209-credit quote, but later recovery had lost the selected product and synchronized credit context. It declined the quote and subsequently submitted `0` at an Organics quantity prompt.
- No Density Scanner purchase or WorldTrade start was verified. The run did not meet the scanner/WorldTrade objective.
- The debug log contained 330 Reflex entries and no Pilot or Strategic entries. This does not prove those lanes were never invoked; durable prompt/reply history remains unavailable.

## Root cause and repair

- Iteration 39 confirmed that storing the active selected opportunity before `SendAsync` helps only while that call is still pending. The worker cleared the active attempt when the sender returned, even though no fresh ship snapshot had yet verified whether the trade completed. A later untracked prompt therefore had no selected product or original credit balance and could fall back to `0`.
- The worker now retains the active trade attempt through fresh trade verification and clears it only after a verified transaction or after its bounded recovery-context age expires. A new delayed-prompt regression simulates a sender that returned, a fresh no-change snapshot, and a later untracked quantity prompt; it asserts that Organics and the original 1,000-credit balance are preserved.
- The original same-sector, freshness, affordability, and fail-closed checks remain in place. The code repair is unit-tested but still needs a live run.

## Validation

- The targeted delayed-prompt and interrupted-send tests passed: 2/2.
- All `AutonomousTestWorkerTests` passed: 73/73.
- MTC Release build succeeded with 0 warnings and 0 errors.
- MTC RPC safety/dashboard harness passed all 152 checks.
- The full TradeWars regression suite passed after this change: 277/277.

## Runtime metrics

- Thirty samples covered 900.006 seconds, from 06:37:06.105 to 06:52:06.102. The average working set was 499.8 MiB (434.2–587.6 MiB); average private memory was 443.9 MiB (381.2–531.6 MiB).
- Sampled CPU increased by 420.3 seconds over approximately 900 seconds (46.7% of one core).
- Container metrics and application/debug logs are archived under `campaign-39-run` in the session workspace.
- Connected duration was not captured, so runtime stability and autonomous-recovery scores are not inferred from process survival.

## Remaining risks

- DEFECT-0029 remains INVESTIGATING. The latest run made progress and earned credits but still lost recovery context on a late prompt. The source change that retains context through verification needs live validation.
- The agent ended with empty holds, but its final location was sector 172; there is no evidence it reached or purchased the scanner at StarDock.
- WorldTrade was not started or monitored, and Pilot/Strategic prompt history remains unavailable.
- The sampler completed its 900-second capture, but shutdown metadata overshot the intended window by 1.788 seconds. Recompute the remaining time after each metrics/container sample on future runs.

## Files changed after the live window

- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.Runtime/AutonomousTestWorker.cs`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.TradeWars.Tests/AutonomousTestWorkerTests.cs`
- `reports/iteration-39.md`
- `reports/scorecard.json`
- `reports/defect-ledger.json`
- `reports/defect-ledger.md`
- `reports/lessons-learned.md`

## Campaign fields

- **Issues found:** Late untracked recovery lost trade intent; scanner/WorldTrade objectives remained unmet; Pilot/Strategic trace evidence remained unavailable; runtime shutdown metadata exceeded the target window.
- **Repairs applied:** Retained active trade intent until verified transaction or bounded expiry; added delayed-prompt regression coverage.
- **Prompt changes:** None.
- **Performance changes:** None. Resource samples are recorded above; no performance regression threshold was established or evaluated.
- **Lessons learned:** Sender completion is not transaction verification. Keep the original request context until fresh cargo/credit evidence arrives, and do not infer connected time from process survival.
