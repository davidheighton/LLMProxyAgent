# Iteration 36

## Result

- The scheduled 900-second window ran from 05:15:19.913 to 05:30:19.913 local. Shutdown was requested at 05:30:19.010 and MTC exited at 05:30:24.199 (905.199 seconds elapsed); the run was not stopped early.
- The verified starting state was sector 290, 309 credits, and 20 empty holds. Meridian moved to sector 805, then made two Fuel Ore purchase attempts for 20 holds each. Neither offer was accepted or verified; credits stayed at 309 and holds remained empty.
- No sale earnings, Density Scanner purchase, or WorldTrade start was verified.
- Two transport reconnects completed automatically. The archived debug log contained 14 Reflex responses and no Pilot or Strategic entries; missing durable role history still prevents determining whether those lanes were not invoked.

## Issues and root cause

- Both attempts reached an untracked port-trade recovery path and left the quote unresolved. At 05:18:01.144 the worker selected 20 Fuel Ore holds; recovery began at 05:18:09.289, 8.145 seconds later. The source allowed only a 5-second match between the original quantity prompt and the quote's preceding quantity prompt. The strict window is too short for this observed recovery delay and can discard the selected-product context.
- Iteration 35 had the same measurable gap: recovery followed quantity selections by 8.562, 9.579, and 30.730 seconds. Its first two quotes were logged as unselected and declined; the third was accepted after correlation succeeded. This confirms the correlation-window defect as a contributor to the repeated zero responses and missed trade opportunities.
- The second iteration-36 attempt reached recovery 4.481 seconds after quantity selection but still produced no quote decision in the debug trace. That separate failure path remains unresolved.

## Repairs and validation

- Extended untracked quote correlation to the existing 45-second fresh-prompt limit, while retaining the current-prompt freshness, offer type, preceding quantity prompt, affordability, approval, and native-haggle checks.
- Changed the MTC safety regression case to correlate an affordable quote against its preceding quantity prompt after 35 seconds.
- The MTC RPC safety and dashboard harness passed all 151 checks. The Release LLM Control Center build succeeded with 0 warnings and 0 errors.
- The source repair was built only after iteration 36 ended; this run used the earlier executable and does not live-validate the change. Iteration 37 is the first live validation opportunity.

## Runtime metrics

- 28 samples covered 05:15:47.688–05:30:12.506, an 864.8-second sample interval.
- Average working set was 536.5 MiB (433.4–571.9 MiB); average private memory was 486.0 MiB (390.0–519.6 MiB); thread count ranged from 89 to 102 (92.9 average).
- Sampled CPU increased by 89.44 seconds, or 10.34% of one core over the sample interval.
- Container memory was stable: LiteLLM 324.9→325.1 MiB, API 47.71 MiB, OpenWebUI 691.6→691.3 MiB, Apache 6.977 MiB, and DB 32.05 MiB.
- The two reconnect gaps totaled 10.036 seconds, for 889.964 connected seconds (98.88% of the 900-second window). The completed archive is `files/campaign-36-run`.

## Remaining risks

- No trade transaction or positive credit delta was verified; scanner acquisition and WorldTrade remain outstanding.
- The 45-second correlation change still requires a live test. The second recovery stall also needs a timestamped branch trace.
- Pilot and Strategic prompt history remains unavailable in the durable archive. Do not equate an absent trace with an uninvoked lane.
