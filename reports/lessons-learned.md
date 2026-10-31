# Lessons Learned

Updated through iteration 28. Read before diagnosing or repairing later runs.

## Iteration 27 follow-up findings

- A prompt instruction is not an enforcement mechanism: the Pilot schema still allowed `RequestShipInformation` with a fresh healthy snapshot. Remove actions that are invalid for the current state from the structured enum, and retain the worker's independent freshness check.
- During the roughly 15-minute follow-up worker window, the ship remained in StarDock sector 551 with 1 credit and no Holo Scanner. The monitor recorded 44 Pilot, 2 Strategic Review, and 94 Reflex interactions, but no verified movement, trade, scan, scanner purchase, or WorldTrade start. Stop stalled windows and do not count them as productive.
- Two Pilot calls exhausted the 2,048-token thinking budget and returned empty content; the configured retry recovered each one. Keep the retry and fail-closed parsing; do not treat recovery as gameplay progress.
- Schema gating for fresh status requests passed all 27 provider tests and the Release build. Iteration 28 exercised the new build and verified movement, but scannerless route oscillation remained; do not close the broader routing defect.

## Iteration 28 run findings

- The rebuilt worker moved between StarDock sector 551 and sector 99 with no scanner and only 1 credit. Stop rather than allow another unscanned entry into sector 99; report the objective failure even when the movement itself is verified.
- A funding trade objective for port 172 appeared during the route loop. Correlate that opportunity with the exact adjacent target, command submission/acknowledgment, and resulting sector snapshot before changing route arbitration.
- Sector snapshots alternated 551/99/551; the latest captured state was back at StarDock, with no scanner or verified trade. The episode is a regression of the earlier scannerless-route loop, not proof the ship survived safely in sector 99.
- The fresh-snapshot schema gate passed the provider tests and Release build and was exercised live, but it does not ensure a selected trade route is scanner-safe.
- No Strategic Review tab/cycle was visible during the sampled runtime. Prompt lane activity and game progress must be tracked separately; Pilot and Reflex calls alone do not satisfy the strategy decision requirement.

## Uncounted attempt 29 findings

- The final movement guard blocked a scannerless move from StarDock sector 551 to uncharted sector 172 before any sector change. Keep the guard; do not loosen it to force movement.
- The ship had 1 credit, 20 empty holds, and no Holo Scanner. It remained at StarDock, so no trade, scan, scanner purchase, or WorldTrade start was verified. This attempt does not count toward the 20 productive cycles.
- After MTC restarted, the terminal was connected and Meridian ran, but the ship snapshot became stale and Pilot history remained at 0 calls. Strategic Review and Reflex showed 1 and 5 interactions. Track prompt freshness separately from worker heartbeats; a running agent is not proof of Pilot decision activity.
- The full TradeWars suite had timing failures under suite execution (263/266, then 264/266). The affected worker and runtime tests passed individually; treat these as test-harness timing instability until a repeatable product failure is isolated.
- A release build passed with no warnings or errors, and the focused AutonomousTestWorkerTests passed 65/65. Keep the attempt uncounted because no game action completed.

## Iteration 26 run findings

- Meridian made verified movement to StarDock, but the nine native Fuel Ore haggle attempts at sector 172 produced no transaction: credits stayed at 17 and all 20 holds stayed empty. A trade decision or return to Command is not a completed trade; verify the fresh post-trade snapshot before recording progress or retrying.
- The Reflex schema update did not eliminate live contract failures: 16 world-observation events used deterministic fallback during the worker window. The UI exposed only a generic gate error, so adding field-specific validation messages is preferable to guessing which constraint failed.
- A healthy authenticated Command prompt did not imply a fresh ship snapshot. The worker remained on `RequestShipInformation`; correlate the raw prompt timestamp, `I` and quick-status submissions, acknowledgments, and the resulting snapshot before relaxing any safety guard.
- No active TWX interpreter script was observed, and no scanner was verified. Do not start WorldTrade until the Density or Holo Scanner is confirmed in a fresh ship snapshot.
- Dashboard HTTP 200 and process survival describe MTC health, not autonomous worker progress. Keep runtime stability unavailable when the exact connected duration is unknown and the 900-second window was cut short.
- The focused Reflex suite passed 16/16 after adding field-specific failure diagnostics. This makes the next live run more informative but does not yet fix or validate Qwen's contract compliance; deploy the new build before using those diagnostics.

## Uncounted restart attempt after iteration 26

- The Start Meridian dashboard control did not change the worker from Stopped. No new gameplay progress was observed, so this control attempt is not a productive test cycle.

## Iteration 25 run findings

- Verified arrival at StarDock sector 551 is productive movement, but a proposal to return to a port is not a verified trade. Require a fresh post-move snapshot, transaction outcome, and updated credits/cargo before recording a trade.
- Four of five Pilot failures paired a non-movement action with a sector target. Encoding the action/target relationship in mutually exclusive JSON Schema branches is stronger than repeating the instruction in the prompt; retain parser validation as the final guard.
- Qwen3 still exhausted the 2,048-token budget in its thinking channel and returned empty final content. Do not keep raising the token limit without model-response evidence; preserve the no-thinking fallback and fail-closed behavior.
- Disconnect/reconnect briefly produced fresh prompts and productive movement, but stale-prompt behavior returned. A successful reconnect is not evidence that recovery is durable; correlate server-prompt observation time, heartbeat prompt metadata, queue state, input acknowledgment, and the next authenticated prompt.
- The 31-sample MTC health window continued after the worker stopped. Process uptime and a healthy dashboard do not substitute for the worker's exact connected duration; keep the runtime score unavailable when that duration is unknown.
- The first full-suite run hit a Windows SQLite cleanup lock; the immediate full-suite rerun passed. Preserve this as a test-fixture flake unless evidence establishes a product defect; do not make unrelated cleanup changes.
- Release deployment and dashboard health are not live-schema validation. Exercise the exact updated provider against Qwen and verify the resulting action/target pair before closing the live contract defect.

## Iteration 24 run findings

- Count the run as productive because verified movement reached StarDock, but do not infer a trade from the 64-to-17 credit change without a transaction record and fresh cargo/credit snapshot.
- The scannerless route reached StarDock but then moved to sector 172 and returned. Preserve scanner acquisition as the objective at the destination as well as en route; compatible port-pair trading is only for verified funding.
- Raising Qwen3's Pilot generation budget from 512 to 2,048 tokens did not prevent empty final content. One response again exhausted the budget in the thinking channel. Do not keep increasing the budget without evidence; inspect the request options and model-specific behavior while retaining fail-closed parsing.
- The Pilot also returned a non-movement action with `targetSector=551`. The parser rejected it, as intended. A prompt statement alone does not enforce this action-dependent field relationship; investigate schema support and add a focused regression before changing validation.
- The stale queued prompt was ignored, but no new strategic cycle followed before the worker was stopped. Preserve the stale-prompt guard and correlate the prompt-bearing heartbeat, queue state, and worker status before changing recovery behavior.
- Focused scanner-routing tests passed 2/2 and the full TradeWars suite passed 256/256. The running Release process predated the latest objective change, so live verification is still required.
- No runtime sampler ran for this attempt. Record runtime/resource scores as unavailable rather than deriving them from the approximate stop time.

## Uncounted attempt 24-01

- The agent remained authenticated at Command in sector 933 with 65,520 turns and 64 credits. It did not move, trade, or scan, so stop the attempt and do not increment the productive iteration count.
- The synchronized ship-status event bridge was active during the attempt; the event log contained fresh ship snapshots through 09:28. Do not attribute this stall to the snapshot bridge without new evidence.
- The live Pilot model was `qwen3:1.7b`. Its first response exhausted the 512-token generation cap in the thinking channel (`done_reason=length`, `eval_count=512`, empty final content). Increasing the budget is appropriate when the trace proves truncation; retain the no-thinking fallback and fail-closed parser.
- The Pilot repeatedly selected RunStarDockShopper while readiness was false and the ship was not in verified StarDock sector 551. The worker correctly rejected the action. Encode dynamic action availability in the structured schema as well as the prompt and validator so the model cannot repeatedly select an unavailable action.
- The schema now omits RunStarDockShopper unless game actions are enabled, readiness is true, the action is not already attempted at this sector, and the shopper stage is inactive. The response budget is now 2048 tokens.
- Focused action-provider tests passed 25/25 and the full TradeWars suite passed 252/252. Release MTC rebuild and live validation remain pending.
- Preserve the partial telemetry as [iteration-24-attempt-01-metrics.jsonl](./iteration-24-attempt-01-metrics.jsonl); do not add a scorecard row for this attempt.
- Before restarting MTC, preserve accessible interaction-card summaries when a full transcript export is unavailable. Attempt 24-01 saved 74 Pilot card summaries, but prompt bodies and model responses remained unavailable; DEFECT-0021 remains open and durable transcript export is still needed.

## Iteration 20 pre-run finding

- Captured Reflex responses classified routine `WorldObservationChanged` events with numeric ship-state deltas as urgent while returning `escalate=false`. The deterministic gate correctly rejected these responses and used its safe fallback; do not weaken that gate to hide model contract failures.
- Clarify that changes in sector, turns, credits, fighters, shields, or cargo are informational/routine unless the event itself contains evidence of immediate threat or time-sensitive danger. Keep the escalation rule for genuine urgent/emergency events and communication unchanged.
- Prompt-contract tests, the full TradeWars suite (250 passed), and the Windows x64 Release build passed before the live restart. Recheck Reflex failure history after this run; live prompt adherence remains unverified.

## Iteration 20 run findings

- Restarting the already-running Debug Control Center was necessary to load the fixed Release runtime. The new runtime moved from sector 1 through the FedSpace sweep to sector 10, confirming the prior no-route stall was not reproduced in this run.
- Do not equate movement or a `TradeAtCurrentPort` decision with completed trading. Iteration 20 kept credits at 300 and did not verify a port transaction, scanner, or WorldTrade start.
- A trade operation encountered an `AutopilotConfirmation` prompt and safely aborted. The worker's current situation still said autopilot confirmation, while the command sender reported no fresh matching prompt and refused to send `N`. Keep that guard; investigate event freshness and cross-component prompt consistency before changing command behavior.
- The run was stopped early after about six minutes without further sector, turn, or credit progress. Count it as productive because movement occurred, but record its runtime score as unavailable rather than comparing a partial run to a 900-second window.
- The new Reflex prompt appeared in live history. The sampled history showed 18 Reflex interactions, one rejected contract response, and an accepted response that escalated a routine world-state change as urgent. Validation failures reduced, but urgency semantics still need improvement.

## Iteration 21 run findings

- The autopilot/port-trade stall recurred: the worker's prompt state and the command sender's latest fresh event were inconsistent. Do not send the initial port key on a cached command Boolean alone; require the newest timestamped server prompt to classify as a fresh Command.
- Of 14 Reflex interactions added during the attempt, 10 completed, three again requested Strategic Review without escalation, and one timed out at 100 seconds. A Strategic Review request also timed out. The prompt clarification did not fully repair the Reflex contract; keep the deterministic validator fail-closed and distinguish model timeouts from invalid responses.
- Pilot remained unused. Movement within the FedSpace route occurred, but the worker did not verify a trade, scanner, or WorldTrade result.

## Iteration 22 run findings

- The fresh-Command guard passed its stale-prompt and AutopilotConfirmation safety cases, and valid port-trade harness cases continued to pass. The complete TradeWars suite passed 250/250 after an isolated Windows SQLite cleanup-lock failure.
- Native haggle diagnostics recorded a failed Equipment attempt followed by a successful trade outcome. The dashboard snapshot was stale and still showed 300 credits, so do not mark the cargo/credit delta verified until a fresh ship status confirms it.
- Stopping the autonomous worker does not necessarily cancel a TWX/native haggle already in progress. Inspect transaction logs after stopping, and separate action completion evidence from the dashboard's last synchronized snapshot.
- Restarting the rebuilt Release runtime restored a connected terminal, but the browser circuit briefly failed to rejoin. A fresh page restored the controls; no MTC process crash was observed.

## Successful repair strategies

- Resolve fixed TWX script references at the MTC boundary using the configured scripts root; do not trust a mutable interpreter working directory.
- Mirror every deterministic validator range in both the structured-output schema and the model prompt.
- Validate model output before marking its interaction successful. A transport/JSON success is not an accepted decision.
- State mutually exclusive decision fields explicitly instead of relying on “null where appropriate.”
- Describe approved scripts by operational purpose and verified prerequisites, not only by identifier.
- Preserve prompt, raw response, outcome, and runtime status together so rejected output can be diagnosed.

## Uncounted attempt 24-03 findings

- The scannerless route override broke the earlier sector 933/556 loop and reached sector 805, but this attempt did not verify StarDock arrival or scanner ownership.
- Do not count LLM calls or a trade decision as in-game progress. Attempt 24-02 recorded 41 Pilot, 2 Strategic Review, and 39 Reflex interactions but no verified movement, trade, or scan.
- The rebuilt Release runtime and all 255 TradeWars tests passed, but the live port trade still stalled at `AutopilotConfirmation`. The worker showed that prompt and selected `DeclineAutopilotEngagement`, then waited for a fresh authenticated Command prompt without verifying a trade.
- Heartbeat metadata now retains the original prompt observation time. This prevents a heartbeat from making an old prompt appear new, but the live failure remains unresolved; preserve the fail-closed command guard and capture correlated server-prompt, heartbeat, client-input acknowledgment, and post-decline events before changing it.
- The monitor showed zero Pilot calls during the smoke attempt and no strategic cycle during the worker run. Strategic history held one older invalid assessment; Reflex history continued to grow. Diagnose prompt eligibility and role invocation separately from game-command failures.
- The Events view also captured a Reflex `requestStrategicReview` intent with `escalate=false` for routine ship-state changes; deterministic fallback rejected it. The system prompt now states the conditional contract explicitly and its regression test passes, but the currently running Release MTC process predates that prompt edit.
- Stop an attempt that is repeatedly waiting at the same prompt without sector, cargo, credits, or scanner progress. Keep the terminal connected and the worker stopped until the prompt-transition cause is understood.
- Treat a recognized gameplay prompt such as `MoveSector` as authenticated-session evidence only when the existing-account attach path explicitly permits it; do not send input while attaching.
- Surface only a classified prompt name in login-wait diagnostics. Never expose unclassified terminal text, which may contain credentials or other sensitive values.
- Replace container healthcheck dependencies that are absent from the base image with tools guaranteed by that image, then recreate and verify the actual container.
- Make dashboard counter scope explicit when a panel excludes other agent roles; keep cross-role interaction history in its dedicated review pages.
- Compare prompt decoders against the exact supported login flow, including bare and suffixed forms. A small syntax assumption can leave a healthy connection permanently unauthenticated.
- When raw prompt text must remain private, log a bounded diagnostic such as text length and prompt classification so empty telemetry can be distinguished from an unrecognized surface.
- Keep interaction-history retention and retrieval role-scoped end to end. A role-filtered dashboard page can look empty if one busy role consumes the shared retention/page limit.
- Before sending a port menu key, cross-check the command-state Boolean against the newest fresh timestamped prompt classification. Fail closed on missing, stale, or non-Command events, and cover each case with an input-not-sent regression.
- Preserve native transaction outcomes and dashboard synchronization evidence as separate facts. A success line can show the transaction completed, while only a fresh ship snapshot can confirm resulting credits and cargo.
- Under bounded queue pressure, distinguish actionable events from state telemetry. Remove events the consumer ignores, preserve fresh arrivals, count evictions, and reject stale prompts rather than acting on delayed state.
- Coalesce superseded state observations before submitting them to a sequential model queue; do not allow fire-and-forget calls to turn transient telemetry into stale work.
- Keep agent capability descriptions synchronized with the scripts they select. Density Scanner support in Dock Shopper does not change WorldTrade's Holo Scanner gate.

## Ineffective or unsafe approaches

- A broad JSON type such as `number` is insufficient when downstream code enforces a narrower range.
- Generic script names and descriptions invite semantic misrouting; do not let an LLM infer script scope from names.
- Do not add a guessed response to an unknown login or game prompt. Capture the exact prompt and verified state first.
- Do not treat successful transport reconnection as proof that the agent is authenticated and ready to act.
- Do not interpret one health signal as definitive when container health and endpoint readiness disagree.
- Do not score a lack of LLM output as poor reasoning quality. Report the metric as not applicable and score agent readiness separately.
- Do not claim a prompt-parser change fixed a live stall unless the next run advances through the corresponding authenticated transition; an unclassified diagnostic deliberately does not preserve raw terminal text.
- A prompt-state mismatch can be obscured by a stale monitor snapshot. Correlate timestamps across worker status, server prompt events, native haggle logs, and ship-status snapshots before attributing a trade failure.
- A parser gap found from scripts is not automatically the live root cause. Iteration 4 still reported an unclassified surface with the bare-name fix in the binary; keep the exact live cause unconfirmed until a safe event trace narrows it.
- Do not infer connected-time percentages from process survival or a single healthy cutoff snapshot; record cumulative connection time or mark that score unavailable.
- A source-level queue or history repair is not runtime-validated by a test run that used the pre-fix binary.

## Architectural weaknesses

- Prompt recognition, login/authentication, and Pilot readiness need correlated event traces. Iterations 2–4 still did not retain a classified incoming prompt or authenticated state transition; the worker now records safe text length for unclassified surfaces.
- The Pilot-only provider counters and role-specific LLM history measure different scopes; labels now make this distinction explicit.
- The monitor history is in memory and is lost on restart. Export it before restarting MTC.
- Resource samples began four minutes into iteration 2 and covered 639 of 900 seconds; schedule sampling before the agent window.
- Structured action schemas should expose only actions whose deterministic prerequisites are currently satisfied; prompts alone did not prevent the small Pilot model from repeating an ineligible Dock Shopper action.

## Changes to carry forward

- The Reflex and Strategic Review contract tests remain passing in the full 232-test suite.
- Keep the Dock Shopper -> Density scanner path, ship upgrades -> Holo scanner -> WorldTrade sequence as a live acceptance checklist; iteration 01 did not exercise it.
- Score prompt/output contract compliance separately from semantic quality. Do not report a small schema subset as a complete LLM-quality score.
- Iteration 2 produced no Pilot, Reflex, or Strategic Review interactions, so the prior LLM contract repairs were not exercised in live inference.
- Iteration 3 also produced no LLM interactions. The parser now recognizes the bare BBS name prompt in tests, but because the active binary predated that change and the raw surface was not retained, live causality remains unconfirmed.
- Iteration 4 ran the rebuilt parser and remained unclassified. The bare-name parser gap is now a valid source-level repair but not a validated explanation for the live stall.
- Iteration 5 live-validated that the unclassified surface is nonempty (safe text length 44), but did not identify its contents or reach authentication. Preserve privacy; do not infer a login response from length alone.
- The safe length diagnostic improved observability but did not itself recover the session. Keep cause and mitigation separate, and continue tracing the event source without speculative terminal input.
- Iteration 6's privacy-safe event aggregation found 17 unclassified prompt events among 27 prompt events. Five length-44 entries represented two distinct fingerprints, so equal length does not imply the same prompt; do not deduplicate or classify by length alone.
- The source trace confirms MTC may publish a prompt flagged by its own `isPrompt` signal even when the shared decoder cannot classify it. The worker intentionally ignores unknown login surfaces; add a recognized category only after the actual surface is safely identified.
- Iteration 6 recorded seven initial login-sequence inputs, all within five seconds, then no later inputs during the 15-minute window. Count-only telemetry distinguishes initial login progress from the later fail-closed stall without exposing input contents.
- Iteration 7 reproduced the same stalled pattern: six login-sequence inputs during the first five seconds, then none; 17 unclassified prompt events and five length-44 surfaces with two fingerprints. The failure is repeatable, but the terminal prompt's meaning remains unverified.
- Iteration 27 traced stale port-prompt recovery to heartbeat events synthesizing prompt surfaces from terminal-buffer content and substituting heartbeat time for prompt-observation time. Preserve empty heartbeat surfaces, carry explicit current-prompt confirmation, and normalize timestamps in every waiter; keep live recovery unverified until a transaction is confirmed.
- Iteration 27's Pilot `oneOf` schema exposed only action-specific fields inside each branch, even though the root required rationale and citation. Duplicate all required common fields into every structured-output branch, test conditional fields in both branches, and confirm the live model response separately from command execution.
- The post-build uncounted attempt produced a valid Pilot movement response but no verified game action. Treat model output, command submission, server acknowledgment, and observed game-state change as separate validation stages.
- The Pilot context contained a 506-entry unreviewed script catalog although action selection only needs the allowlisted script capabilities. Send only catalog metadata matching approved capabilities; measure prompt latency and confirm the movement postcondition live before attributing progress to this reduction.
- Cross-run hashing confirmed that both distinct length-44 prompt surfaces from iteration 6 recurred in iteration 7. The stable fingerprints prove recurrence, not meaning; continue to fail closed rather than infer a response.
- Iteration 8 again remained connected but unauthenticated for the full window. It recorded 31 server-prompt events, five length-44 surfaces, and eight client-input events in the first five seconds, then none. The repeated stall supports a stable login-recovery failure but does not identify the prompt; do not classify by length or inject guessed input.
- Keep evidence from every run count-only when terminal text may contain credentials. The dashboard status and event timing establish the stall boundary, but a safe, trusted way to identify the prompt category is still needed before a parser change can be justified.
- Iteration 9 again recorded 35 prompt events, five length-44 surfaces, and eight login inputs within about six seconds, then no further inputs for the rest of the 15-minute window. The Pilot, Strategic Review, and Reflex histories all showed zero interactions; the runtime pipeline itself stayed up with empty queues and no cycles.
- Repeated runs now distinguish a live transport/runtime from a usable authenticated agent: build and process health remain stable, while autonomous recovery remains 0%. Continue to measure these separately and keep the unknown-prompt path fail-closed.
- Iteration 10 reproduced the same pattern again: 31 prompt events, five length-44 surfaces, and eight login inputs within about five seconds; no later inputs occurred. Pilot, Strategic Review, and Reflex all remained at zero, while the pipeline stayed healthy with empty queues and no cycles.
- A tenth successful build and full 233-test run confirms the known code contracts still pass; this does not resolve the live login stall. Keep the two facts separate and do not claim gameplay progress until authenticated game state is observed.
- Iteration 11 remained connected but unauthenticated for the exact window. It logged 33 prompt events, five length-44 surfaces, and eight initial inputs; all three LLM roles remained at zero, with no reactive or strategic cycles. This is a recurring readiness failure rather than a model-quality result.
- Iteration 12 again produced no authenticated session or LLM calls: 30 prompt events, five length-44 surfaces, and eight inputs in the first five seconds. The build, test suite, transport, and runtime pipeline remained healthy, while autonomous recovery stayed at zero.
- Iteration 13 repeated the same pattern: 31 prompt events, five length-44 surfaces, and eight initial inputs, followed by no activity from the Pilot, Strategic Review, or Reflex lanes. Treat the repeated login stall as the dominant unresolved defect; do not confuse healthy process telemetry with task completion.
- Iteration 14 again showed a connected, running process but no authenticated game state. The live monitor reported an unclassified length-44 surface and zero Pilot, Strategic Review, and Reflex interactions. Unlike iterations 8–13, safe event counts were not available, so do not extrapolate prompt or input counts.
- Iteration 15 resolved the login ambiguity: the unclassified surface was the clear-avoids question. Since the prompt default is N, classify it and explicitly send N so existing avoids are preserved; the rebuilt live session reached authenticated Command. The watchdog is downstream of authentication, so it cannot recover this pre-login stall.
- Iteration 15 also recorded a fatal Avalonia WebView2 `MoveFocus` ArgumentException during window activation. Track it separately from the login defect; do not hide it with a broad exception handler.
- The first full test pass in iteration 15 hit the known transient SQLite fixture cleanup lock; the suite passed on immediate rerun (234/234). Preserve the successful rerun evidence and avoid changing unrelated fixture cleanup without reproduction.
- Iteration 5 had one transient SQLite fixture cleanup file-lock failure; the specific test passed in isolation and the full suite passed on rerun. Record the event, but do not claim a product root cause without reproduction.
- Iteration 16 found the Pilot history tab empty despite 2 completed and 3 failed Pilot requests; 49 visible Reflex traces and cross-role history limits explained the observability gap. The UI now fetches each role independently; verify tab switching and retention under a live high-volume run.
- Iteration 16 reached the game-event queue capacity of 128 and reactive queue capacity of 64. The old runtime logged 1,460 event drops, including 1,031 server prompts. Queue eviction, ignored-event removal, stale-prompt filtering, and latest-state coalescing are implemented but need live confirmation.
- Iteration 16's process and containers remained present in every sample, and the monitor was authenticated/healthy near cutoff, but exact connected seconds were not measured. Keep runtime process survival distinct from connected-time score.
- Iteration 16 did not verify Density Scanner ownership, StarDock, Holo Scanner ownership, or WorldTrade execution. Continue to require evidence for each acceptance gate.
- Iteration 17 confirmed that rejecting a stale prompt is necessary but insufficient: without a new prompt, an authenticated worker can remain idle. Preserve fail-closed handling and recover only from a fresh heartbeat that carries a still-current prompt, invalidated by intervening input or server output.
- Keep monitor situation freshness separate from heartbeat freshness. A recent heartbeat must not make an older ship snapshot appear current or Healthy.
- Qwen3 thinking traces are now enabled for Pilot requests and retained in the transport response; the larger response-token budget needs live validation for latency and structured-decision completeness.
- Iteration 17 repeated transient Windows SQLite fixture cleanup locks. Individual failures passed in isolation and the full suite passed on rerun; treat this as a test-harness reliability observation, not a product database defect, unless a reproducible root cause is found.
- Iteration 17 did not verify Holo Scanner ownership or WorldTrade execution. A Density Scanner sighting is not sufficient evidence to start WorldTrade.
- Iteration 18's 161,984 archived events confirm a transition from a 40-hold, no-scanner Cruiser in sector 369 to a one-hold Escape Pod in sector 99. The exact hazard and scanner state at entry are not captured; require scanner preflight on every ordinary warp and fail closed on stale state.
- The same event set records 11 WorldTrade-related inputs but no verified earnings, and no Holo Scanner status. Density alone is sufficient for WorldTrade; Holo also satisfies density scans and remains optional. Verify ownership and credit/hold changes from fresh ship status rather than counting script-name events.
- Login history contains 59 clear-avoids prompt events, 507 BBS-name prompts, 252 password prompts, 206 connects, and 154 disconnects. Treat repeated login prompts as reconnect/state transitions, not permission to replay cached input.
- Event JSONL does not preserve the LLM prompt and final response. Preserve model interaction history during each live window before shutting down MTC; do not infer LLM quality from game-event totals.
- Before resetting a deleted character, archive the event/terminal logs for learning, clear only the active character snapshot and transient saved game variables, and archive the old per-game database so the next run cannot use stale ship/map state.

## Iteration 29 follow-up

- Refreshing port/warp CIM does not make an old sector exploration flag safe for the current character. Sector 99 is a confirmed prior ship-loss sector; require a verified Density or Holo Scanner before entering it even when persistent map data marks it Explored.
- The live monitor recorded sectors 551, 99, and 551 during this run. Stop immediately when sector 99 appears; the return to StarDock is not evidence that scannerless entry is safe.
- The run made verified movement, so it counts as productive iteration 29, but no trade, scan, scanner purchase, or WorldTrade start was verified. Do not equate movement or status refresh with progress toward scanner acquisition.
- The new sector-99 guard passes the autonomous worker tests and is in the rebuilt MTC Release app. The dashboard host did not start after app restart because the terminal did not reconnect; restore the terminal connection and verify the guard before restarting Meridian.

## Iteration 30 follow-up

- After a movement destination is submitted, MTC can surface the echoed `MoveSector` prompt before the next Command prompt. Do not treat this echo as proof of movement or as a fatal prompt; wait for a fresh result and keep the existing timeout fail-closed.
- The MoveSector echo fix passed the 134-check MTC RPC safety harness and the Release publish. It has not yet been live-validated.
- The rebuilt agent later stayed at sector 551 with `RequestShipInformation` and no fresh snapshot. Stop such a stalled retry early; status refresh alone is not productive gameplay.
- The latest live run moved from 551 through 206 and back to 551 but did not complete a trade. Scanner purchase and WorldTrade remain outstanding; do not equate movement with objective completion.
- An uncounted restart observed 1 credit, 30 fighters, no shields, 20 empty holds, and no scanner at sector 551. The Pilot proposed unexplored sector 172 for trade, but the scannerless movement guard blocked it; stop rather than repeatedly retrying an unsafe route. Confirm the one-credit state from a fresh ship-information response before planning further purchases.
- A final movement guard prevents unsafe commands, but leaving an impossible remote trade opportunity in the Pilot context still encourages repeated invalid selections. Filter destinations and trade opportunities using the same scannerless eligibility rule before prompting, while keeping the final guard as defense in depth. The 68-test worker suite passes; deploy before claiming live improvement.

## Iteration 31 follow-up

- A verified `Explored` flag makes a scannerless move eligible under the safety policy, but does not make the destination a valid trade. Sector 172 sells Fuel Ore; the Pilot incorrectly described it as a place to sell the ship's cargo.
- The live route was 551→172→551 with 1 credit and empty holds. No trade, scan, scanner purchase, or WorldTrade start was verified; count the run only because actual movement occurred, and stop the repeat loop early.
- At StarDock, no scanner, no cargo, at most 1 credit, and no provider-verified trade is a hard no-progress state. The runtime now holds instead of allowing an unsupported model trade rationale to initiate another trip.
- All 69 `AutonomousTestWorkerTests` passed, the MTC Release `win-x64` publish succeeded, and the restarted runtime recorded the hold at sector 551. That restart check made no game progress and is uncounted.
- A manual CIM refresh completed, but the read-only Pair Port Trade plan still found no complementary pair. A successful map refresh does not imply the current world has a verified scanner-funding route.
- MTC can automatically resume Meridian after a process restart. Check the runtime status and stop it explicitly before assuming the worker is idle.
- The ship still has no verified scanner or funding route. Do not bypass scanner checks or start WorldTrade without verified Density or Holo Scanner ownership.

## Uncounted source/test follow-up

- The no-pair result had two code causes: the macro required at least two profile differences, rejecting a valid single-good buy/sell pair, and the general runtime provider searched only direct warps between seller and buyer ports.
- The macro now accepts any known complementary product, and the provider searches fresh directed paths up to three hops. The pair remains a planning candidate, not a confirmed profitable trade; live stock, purchase affordability, and route safety must still be verified.
- The 16 focused MacroExecutionTests and the MTC RPC safety harness passed, including a three-hop non-FedSpace funding pair. No in-game command was issued, so this follow-up is not a productive campaign run and must not increment the scorecard.
- MTC automatically resumes Meridian after restart. Do not replace the active process until a safe stop/restart path is verified; keep the scannerless and sector-99 guards in force.
