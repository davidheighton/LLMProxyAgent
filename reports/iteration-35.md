# Iteration 35

## Result

- The scheduled window was 04:48:19.091–05:03:19.091 local. Shutdown was requested at the planned end and the process exited at 05:03:24.262 (905.171 seconds elapsed). The test was not stopped early.
- The run produced one verified full-hold purchase. At 05:02:46.648 the recovered Fuel Ore quote of 191 credits was accepted against 500 credits. A fresh snapshot at 05:02:51.118 confirmed 20 Fuel Ore, 20/20 holds occupied, and 309 credits.
- The ship then moved from sector 805 to sector 290. At 05:03:09 it started selling all 20 Fuel Ore. The port quoted 876 credits, but the offer remained unanswered when the window ended; no sale proceeds were verified.
- No Density Scanner purchase or WorldTrade start was recorded. No `0` quote-decline was recorded for the accepted purchase.
- Two server disconnects each recovered automatically in about five seconds. The debug log recorded 12 Reflex responses and no Pilot or Strategic responses; this does not prove whether those lanes were not invoked or their history was not persisted.

## Issues and root cause

- The sender attempted to buy the full 20-hold Fuel Ore capacity three times. The first two untracked-recovery attempts did not produce a recorded quote decision. The third accepted the 191-credit quote and was verified from the cargo/credit snapshot.
- The prior quote-selection repair can succeed live, but recovery remains intermittent and late: the first verified purchase occurred only about 14 minutes into the window. The available logs do not establish why the first two attempts stalled.
- Source review found a related prompt-state gap: `Your offer [amount] ?` was not classified as a distinct TradeWars prompt, and recovery could reject an otherwise fresh, correlated offer if the MTC command-prompt flag was stale. These are plausible contributors to the earlier stalls, not confirmed causes from this run.
- The sale quote was still active at shutdown, so this run confirmed cargo acquisition but not earnings.

## Repairs and validation

- Classified port quote prompts as `PortTradeOffer`, so telemetry no longer leaves the previous prompt surface in place.
- Updated recovery to accept a fresh, correlated quote even when the command-prompt indicator is stale, while retaining age, correlation, affordability, approval, and native-haggle checks.
- Added telemetry coverage for decoded quote prompts and changed the MTC harness to exercise a correlated quote while the command-prompt indicator reports true.
- The TradeWars suite passed: 276 passed, 0 failed. It emitted two pre-existing xUnit analyzer warnings in `StrategicReviewTests.cs`.
- The MTC safety harness passed all 151 checks. The Release LLM Control Center build succeeded with 0 warnings and 0 errors.
- These source changes were made after the live window; iteration 36 must validate them. DEFECT-0029 remains INVESTIGATING until a recovered sale or other positive earnings are verified.

## Runtime metrics

- 26 samples covered 04:49:51.117–05:03:11.851, an 800.734-second interval. Sampling began about 90 seconds after the scheduled window began.
- Average working set was 468.7 MiB (398.7–543.1 MiB); average private memory was 415.8 MiB (346.0–489.0 MiB); thread count ranged from 89 to 97 (92.9 average).
- Sampled CPU increased by 94.61 seconds, or 11.82% of one core over the sampling interval.
- Container memory was stable: LiteLLM 326.1→325.1 MiB, API 47.71 MiB, OpenWebUI 691.2 MiB, Apache 6.977 MiB, and DB 32.05 MiB.
- The 900-second window had 889.969 connected seconds (98.89%); the two disconnect gaps totaled 10.031 seconds. The session archive, including start/end metadata and logs, is `files/campaign-35-run`.
- The only shutdown warning in the captured stderr was Chromium error 1412 while unregistering `Chrome_WidgetWin_0`; the run metadata confirms the MTC process exited.

## Remaining risks

- The successful purchase was verified, but the sale quote was not accepted or verified before shutdown. Do not treat the 191-credit purchase as earnings.
- Two untracked recovery attempts stalled before the third succeeded. Their specific failure branch was not durably logged.
- Pilot and Strategic prompt history remains absent from the debug archive. Add durable per-lane request/response capture before deciding whether their schedulers are inactive.
- Scanner acquisition and WorldTrade remain outstanding.
