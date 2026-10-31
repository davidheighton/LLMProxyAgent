# Defect Ledger

Updated through iteration 31. The machine-readable source of truth is [defect-ledger.json](./defect-ledger.json).

| ID | Severity | Status | Title | Last seen |
|---|---|---|---|---:|
| DEFECT-0001 | HIGH | VALIDATED | Fixed CIM paths depend on interpreter base directory | 1 |
| DEFECT-0002 | MEDIUM | FIXED | Reflex confidence scale is unspecified | 1 |
| DEFECT-0003 | MEDIUM | FIXED | Strategic objective fields conflict with selected decision | 1 |
| DEFECT-0004 | MEDIUM | FIXED | Strategic prompt confuses Dock Shopper with port trading | 1 |
| DEFECT-0005 | HIGH | VALIDATED | Pilot remains idle without a recognized authenticated prompt | 15 |
| DEFECT-0006 | LOW | VALIDATED | Dashboard model telemetry disagrees with LLM history | 4 |
| DEFECT-0007 | LOW | VALIDATED | LiteLLM health status conflicts with local readiness | 2 |
| DEFECT-0008 | MEDIUM | FIXED | Authenticated movement prompt is omitted from login recovery | 2 |
| DEFECT-0009 | HIGH | FIXED | Bare BBS name prompt is not recognized | 3 |
| DEFECT-0010 | MEDIUM | VALIDATED | Unclassified login diagnostics omit prompt-text presence | 5 |
| DEFECT-0011 | HIGH | INVESTIGATING | Unhandled WebView2 focus exception terminates MTC | 15 |
| DEFECT-0012 | MEDIUM | FIXED | Cross-role history retention hides Pilot interactions | 16 |
| DEFECT-0013 | HIGH | FIXED | Game-event queue saturation drops actionable prompts | 16 |
| DEFECT-0014 | MEDIUM | FIXED | Reactive ship snapshots accumulate stale queued work | 16 |
| DEFECT-0015 | LOW | FIXED | Dock Shopper capability omits Density Scanner support | 16 |
| DEFECT-0016 | HIGH | FIXED | Stale queued prompt leaves authenticated worker idle | 27 |
| DEFECT-0017 | MEDIUM | FIXED | Monitor presents a stale ship snapshot as Healthy | 17 |
| DEFECT-0018 | HIGH | FIXED | WorldTrade eligibility incorrectly requires Holo Scanner | 18 |
| DEFECT-0019 | HIGH | FIXED | Port trading is not blocked for Escape Pod or one-hold ships | 18 |
| DEFECT-0020 | HIGH | REGRESSED | Empty Ollama final content fails the Pilot action | 25 |
| DEFECT-0021 | MEDIUM | INVESTIGATING | LLM prompt and response history is lost when MTC stops | 24 |
| DEFECT-0022 | MEDIUM | REGRESSED | Reflex misclassifies routine ship-state changes as urgent | 26 |
| DEFECT-0023 | MEDIUM | INVESTIGATING | Autopilot confirmation blocks port-trade recovery | 24 |
| DEFECT-0024 | MEDIUM | INVESTIGATING | Ollama inference requests time out during autonomous review | 21 |
| DEFECT-0025 | MEDIUM | FIXED | Pilot schema offers Dock Shopper when readiness is false | 24 |
| DEFECT-0026 | HIGH | REGRESSED | Stale trading objective caused scannerless route oscillation | 31 |
| DEFECT-0027 | MEDIUM | FIXED | Pilot pairs a non-movement action with a target sector | 25 |
| DEFECT-0028 | MEDIUM | INVESTIGATING | Stale ship snapshot blocks progress at StarDock | 27 |
| DEFECT-0029 | MEDIUM | INVESTIGATING | Repeated Fuel Ore haggles do not produce a verified trade | 27 |
| DEFECT-0030 | HIGH | VALIDATED | Pilot oneOf branches omit required shared response fields | 27 |
| DEFECT-0031 | MEDIUM | FIXED | Movement sender treats echoed MoveSector prompt as fatal | 30 |

## Recurring patterns

- **Unbounded model numeric fields:** 8 out-of-range Reflex confidence values in 14 captured responses. Bounds are now mirrored in prompt, schema, and validator.
- **Model output contract under-specification:** one Strategic Review combined `continue` with objective-transition data, and misassigned a script purpose. Prompt and validation changes are covered by regression tests.
- **Unavailable Pilot action exposed in structured schema:** uncounted attempt 24-01 repeatedly selected Dock Shopper despite false readiness and being outside the verified StarDock sector. The schema now omits that action until deterministic prerequisites are met; live validation remains pending.
- **Qwen3 thinking exhausts the structured-response budget:** iterations 24 and 25 again reached the 2,048-token limit with empty final content after an earlier 512-token failure. The no-thinking fallback and fail-closed parser remain; a larger budget alone is ineffective.
- **Agent readiness lost after connection/reconnect:** repeated in iterations 1–15. Iteration 15 identified the clear-avoids question as the unclassified prompt. Iteration 18's historical logs contained 59 clear-avoids prompt events, 507 BBS-name prompts, 252 password prompts, 206 connects, and 154 disconnects; these are repeated state transitions, not permission to replay stale input.
- **Unhandled WebView2 focus exception:** first observed in iteration 15; MTC terminated in `ICoreWebView2Controller.MoveFocus` during window activation. Root cause and narrow remediation are under investigation.
- **BBS name prompt parser is too restrictive:** found in iteration 3 by comparing the existing login flow with decoder requirements. The bare name prompt is now recognized in source and regression-tested; live verification is pending.
- **Unclassified prompt diagnostics are too coarse:** seen in iterations 3 and 4. The worker now reports safe text length and logs the first unclassified event per connection without raw text; the dashboard diagnostic was live-validated in iteration 5.
- **Healthcheck depends on a missing image executable:** LiteLLM's `curl` probe failed in its slim Python image. The probe now uses the image's Python runtime and the recreated container is healthy.
- **Cross-role interaction history crowding:** iteration 16 showed nonzero Pilot request counters but an empty Pilot history tab while Reflex history was high. Retention and dashboard retrieval are now role-scoped; verify live tab counts under load.
- **Bounded event queue saturation and prompt loss:** iteration 16 recorded 1,460 in-window dropped events and a full 128-item queue. Latest-event preservation and removal of ignored status traffic are implemented; validate drop rate and prompt freshness after rebuild.
- **Reactive snapshot backlog:** iteration 16 reached 64/64 reactive queue depth because every changed snapshot could enqueue stale work. Pending observations now coalesce to the latest state; verify queue depth and model-call freshness live.
- **Agent capability description drift:** iteration 16 found Dock Shopper's description omitted Density Scanner support. The description now matches the script, but purchase selection is not yet live-validated.
- **Stale queued prompt leaves worker idle:** iteration 17 discarded an old prompt but received no later actionable prompt, despite fresh presence heartbeats. MTC now includes a still-current prompt in the heartbeat, and the worker re-queues it only when the heartbeat is fresh; live verification is pending.
- **Stale queued prompt recovery recurrence:** iterations 24 and 25 again logged ignored stale prompts without subsequent fresh work before stop. Iteration 27 traced the race to synthesized heartbeat surfaces and timestamp fallback; the code is now regression-tested, but live port-prompt recovery remains pending.
- **Stale monitor situation shown as healthy:** iteration 17 displayed an old situation as Healthy after newer ship-status telemetry arrived. The monitor now labels situations older than 30 seconds as Stale and warns against using them for action.
- **Transient SQLite fixture cleanup locks:** intermittent Windows file locks recurred in iterations 5, 15, and 17. Individual failures passed in isolation and the full suite passed on rerun; no product cause is confirmed and no cleanup workaround was applied.
- **Unscanned movement through sector 99 risks ship loss:** iteration 18's state changed from a 40-hold Cruiser in sector 369 to a one-hold Escape Pod in sector 99. Iteration 29 again recorded 551-to-99-to-551 movement. A source guard now requires a verified scanner for sector 99 even when persistent map data says Explored; the rebuilt Release runtime still needs live verification.
- **WorldTrade actions lack scanner and earnings verification:** 11 WorldTrade-related inputs were observed, but no successful routine or positive credit delta was confirmed. Require Density or Holo Scanner ownership on the active ship and verify updated credits/holds.
- **Scanner-funding pair search was narrower than the campaign requires:** the iteration-31 CIM still produced no pair because the macro rejected a valid one-product profile and the general runtime provider searched only direct seller-to-buyer warps. Source changes now accept a single tradable product and discover fresh directed pairs up to three hops; 16 targeted macro tests and the MTC RPC safety harness pass, but the running MTC has not loaded this code.
- **LLM interaction history is unavailable after shutdown:** iteration 18's event logs contained no prompt/reply transcript and the in-memory history was unavailable after MTC stopped. Attempt 24-01 preserved 74 accessible Pilot card summaries, but not prompt bodies or responses. Persist or export prompts and final responses before shutdown.
- **Reflex urgency over-classifies routine state changes:** iteration 20's captured responses repeatedly called numeric ship-state updates urgent but did not escalate, violating the deterministic contract. The prompt distinguishes state changes from immediate threats; live verification is still required.
- **Reflex contract failures on routine state changes:** iteration 26 recorded 16 deterministic fallbacks after the schema update was deployed. The event cards exposed only a generic validation failure, so the exact invalid fields are unknown. Field-specific diagnostics passed focused tests but are not yet deployed.
- **Stale or inconsistent autopilot prompt telemetry:** iteration 20 reached sector 10 but stalled when a port-trade action encountered AutopilotConfirmation and the safe decline gate could not find a fresh matching prompt. Preserve the fail-closed guard until the worker/command-sender prompt mismatch is traced.
- **Scannerless stale-objective movement oscillation:** an uncounted attempt oscillated between sectors 933 and 556 while the active objective remained trading. The scanner-first StarDock objective and verified-hop restriction broke the loop by moving the ship to sector 805; arrival and scanner purchase remain unverified.
- **Scannerless route regression:** iteration 29 again moved between StarDock 551 and sector 99. Iteration 30 moved through sector 206 but failed before a trade. Iteration 31 moved 551→172→551 after the Pilot misread a Fuel Ore-selling port; no cargo or scanner was obtained. A new one-credit/no-cargo hold passed all 69 worker tests and was live-confirmed at StarDock after publishing. The hold prevents this dead-end loop but does not establish a funding route; the sector-99 guard and scanner purchase remain outstanding. DEFECT-0026 remains REGRESSED.
- **Movement sender rejects echoed destination prompt:** iteration 30 showed `To which Sector [551] ? 860` after destination submission. The sender now waits through a MoveSector echo for a fresh result; its 134-check RPC safety harness passes, but live validation is pending.
- **Action-dependent Pilot field mismatch:** iterations 24 and 25 recorded five non-movement actions with target sectors despite the prompt requiring zero. The parser rejected them; a mutually exclusive `oneOf` schema and regression assertions are now implemented, with live Qwen validation pending.
- **Reflex review/escalation contract remains unstable:** iteration 21 produced three additional Reflex failures requesting Strategic Review with escalation false, despite the prompt clarification. The validator remains fail-closed; prompt/schema or model-output contract work is still required.
- **Ollama inference timeouts:** iteration 21 recorded one Reflex and one Strategic Review request canceled at the 100-second HttpClient timeout. Correlate with Ollama queue and inference telemetry before altering timeout/retry behavior.
- **Port-trade sender freshness guard:** the sender now requires a fresh timestamped Command prompt before sending `P`. Safety-harness cases passed, and iteration 22 logged a successful Equipment transaction, but the post-trade ship snapshot was stale; runtime validation remains open.
- **Heartbeat prompt observation age:** heartbeat copies now preserve the original server-prompt observation timestamp, and expired prompt copies are rejected. The 255-test suite and Release build pass, but attempt 24-03 still stalled after an AutopilotConfirmation interruption; do not weaken the sender's fail-closed guard.
- **Stale ship-status refresh at authenticated Command:** iteration 26 remained at sector 551 with a stale snapshot and `RequestShipInformation` as the last decision despite a connected, authenticated Command session. Correlate prompt timestamps, status inputs, acknowledgments, and snapshots before changing the guard.
- **Stale ship-status refresh recurrence:** an uncounted post-build attempt again remained at sector 551 with 1 credit, no scanner, and `RequestShipInformation` as the last decision despite healthy synchronization. The Pilot context now excludes catalog entries that are not tied to approved script capabilities; live latency and movement verification remain pending. Correlate prompt timestamps, status inputs, acknowledgments, and snapshots before changing the guard.
- **Repeated no-transaction port haggle:** iteration 26 logged nine failed Fuel Ore outcomes at sector 172 with unchanged credits and cargo. Iteration 27's 12-hold quote exceeded the available 55 credits; determine trade affordability and verify a transaction before adjusting retry policy.

## Latest scorecard

See [scorecard.json](./scorecard.json) for definitions and historical values. Iteration 30 is counted as productive because verified snapshots changed from sector 551 through 206 and back to 551, but no trade, scan, scanner purchase, or WorldTrade start was verified. The scannerless guard blocked unexplored sector 172 in an uncounted follow-up; a source planner filter now removes such routes from Pilot context, but deployment and live validation are pending. The special sector-99 case and movement-prompt fix also await live validation. Six of 31 defects are runtime-validated (19.4%).

The prior campaign's iteration 19 evidence is incomplete, and iteration 23 has runtime samples but no scorecard entry. Iterations 21, 22, and 24–30 were productive but stopped early; none completed a 15-minute worker window. Iteration 22's native haggle log recorded a successful Equipment transaction, but a fresh ship snapshot did not verify cargo/credits. Attempts 24-01 through 24-03, the post-iteration-26 dashboard start attempt, the post-build schema-validation attempt, and the final post-rebuild stale-snapshot retry were uncounted stalls. See [iteration-24.md](./iteration-24.md), [iteration-25.md](./iteration-25.md), [iteration-26.md](./iteration-26.md), [iteration-27.md](./iteration-27.md), [iteration-28.md](./iteration-28.md), [iteration-29.md](./iteration-29.md), and [iteration-30.md](./iteration-30.md). Scanner purchase and WorldTrade remain unverified.
