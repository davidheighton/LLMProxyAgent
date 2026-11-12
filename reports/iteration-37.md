# Iteration 37

## Result

- The observation window ran from 05:37:30.237 to 05:52:31.171 local (900.934 seconds). The sampler's 30-second polling overshot the 900-second target by 0.934 seconds; the next run should cap its final sleep to the remaining window.
- Meridian started in sector 805 with 309 credits and 20 empty holds. It selected 20 Fuel Ore holds in three attempts. The offers were 153, 215, and 153 credits, all within the verified 309-credit balance, but none was answered. The final state remained sector 805, 309 credits, and 20 empty holds; no earnings, Density Scanner purchase, or WorldTrade start was verified.
- Two server reconnects recovered automatically. Each reconnect returned the agent to Command and another trade attempt, but did not complete the pending trade.
- The archived debug log contained eight Reflex entries and no Pilot or Strategic entries. The durable game-event archive does not contain model prompt/response history, so lane invocation remains ambiguous.

## Root cause and repair

- The 45-second quantity-to-offer correlation change did not resolve the live failure. The captured game events show each actual `PortTradeOffer` was followed by confirmed presence-heartbeat events that repeated the active surface. `GetRecentPromptObservations` promoted those heartbeats to new prompt timestamps. `CompletePortTradeAsync` could therefore treat a heartbeat as an additional pending prompt and abort; untracked recovery could then see the synthetic `PortTradeOffer` surface rather than the actual quoted text and preceding quantity prompt.
- After the run, trade recovery and pending-prompt processing were changed to exclude presence-heartbeat records from actionable trade prompts. Actual server prompt text remains the source for product and quote correlation; unmatched or stale prompts still fail closed.
- The MTC safety harness now includes heartbeat-interleaving cases for both normal trade handling and untracked quote recovery.

## Validation

- MTC RPC safety/dashboard harness: 151 checks passed.
- Release build: succeeded with 0 warnings and 0 errors.
- The heartbeat repair was applied after this run; it is not live-validated. Iteration 38 must verify that an affordable quote is accepted and a fresh ship snapshot confirms the cargo and credit deltas.

## Runtime metrics

- Connected time was 890.912 seconds of the 900-second target (98.99%); two reconnect gaps totaled 10.022 seconds.
- Thirty samples covered 05:37:30.248–05:52:01.132 (870.9 seconds): average working set 532.4 MiB (407.4–572.4 MiB), average private memory 481.3 MiB (353.5–519.9 MiB), and sampled CPU increase 94.9 seconds (10.9% of one core over the sample interval).
- The raw debug and daily game logs, a time-filtered game-event archive, and runtime metrics are stored in the `campaign-37-run` session archive.

## Remaining risks

- No port trade was verified. Keep DEFECT-0029 open until fresh ship telemetry confirms a transaction and positive earnings.
- Extra zero responses and skipped products remain a concern. Iteration 37 selected the full 20-hold Fuel Ore capacity, but the quote was never answered; confirm the rebuilt run also avoids declining an affordable offer.
- Scanner acquisition and WorldTrade remain outstanding. Require verified scanner ownership before starting WorldTrade.
- Pilot and Strategic history is still not durably archived.
