# Iteration 21

## Result

Productive but early-terminated. Meridian briefly moved within the FedSpace route and returned to sector 10, but it did not complete a verified port transaction, scanner purchase, or WorldTrade start. The worker was stopped after the same autopilot/port-trade stall recurred; this was not a 900-second run.

## Issues and root causes

- **Port-trade recovery stalled at AutopilotConfirmation (DEFECT-0023).** The worker saw an autopilot prompt while the command sender could not find a fresh matching prompt to decline. The trade path relied on the boolean command-prompt signal before opening a port menu, without proving that the newest timestamped server prompt was a fresh Command prompt.
- **Reflex contract failures recurred (DEFECT-0022).** Of 14 Reflex interactions observed after the preceding run, four failed: three requested Strategic Review without escalation, and one timed out after 100 seconds. One Strategic Review request also timed out after 100 seconds. No Pilot interactions were recorded.
- **Model latency remains an operational risk.** The timeouts were explicit `HttpClient.Timeout` cancellations; the underlying Ollama load/latency cause is not yet confirmed.

## Repairs and validation

The port-trade issue was repaired after this attempt by requiring a fresh timestamped Command prompt before sending the initial `P` input. The MTC safety harness now verifies both that a fresh AutopilotConfirmation blocks the trade and that a stale Command prompt blocks it; the ordinary valid trade fixtures begin with a fresh Command prompt.

The MTC safety harness passed. The full TradeWars suite initially had one Windows SQLite fixture-cleanup lock (`knowledge.db`); the isolated test passed, and the complete rerun passed **250/250**. The Windows x64 Release build passed with **0 warnings and 0 errors** after the running MTC process was stopped to release its locked executable.

## Files changed for the follow-up repair

- `LLMControlCenter/MtcGameCommandSender.cs`
- `Legacy/TWX30-main/TWX30-main-tests/MtcRpcSafetyTests/Program.cs`

## Remaining risks

- The Reflex prompt clarification did not eliminate live contract violations.
- Pilot remained unused, and the persistent survey/trade state may not correspond to the most recently verified game state.
- No Density Scanner or WorldTrade operation was verified.
- The runtime interval was short and no complete resource sample set was collected.

## Lessons

Do not trust a cached command-prompt boolean when an operation is about to submit a menu key. Require the latest timestamped server event to independently classify as a fresh Command prompt. Keep the autopilot decline gate fail-closed, and treat model timeouts and contract rejections as different failure categories.
