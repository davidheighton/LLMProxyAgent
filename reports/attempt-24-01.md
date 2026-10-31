# Uncounted attempt 24-01

## Result

Stalled and stopped early; not counted as a productive iteration. The agent remained authenticated at the Command prompt in sector 933 with 65,520 turns and 64 credits. No movement, trade, or scan was observed, and the 15-minute window was not completed. The worker was stopped while the MTC terminal stayed connected.

The ship had 20 holds, 30 fighters, and no scanner. StarDock is sector 551. No route was verified in this attempt; do not authorize an unknown-sector move without the scanner safety checks.

## Issues and root causes

- **Qwen Pilot returned empty final content.** The live history showed `qwen3:1.7b` consuming the configured 512-token generation limit in its thinking channel (`done_reason=length`, `eval_count=512`, `contentLength=0`). The existing no-thinking retry then produced invalid decisions rather than a usable action.
- **Pilot repeatedly selected an unavailable Dock Shopper action.** The model chose `RunStarDockShopper` despite `starDockShopperReady=false`, current sector 933 not matching StarDock sector 551, and other prerequisites being unverified. The deterministic worker rejected these decisions, preventing unsafe execution but leaving the agent stalled. The structured schema had listed this action unconditionally.
- **Snapshot-event forwarding was not the cause.** The attempt's event log contained fresh synchronized ship-status events through 09:28; the model-output and action-availability failures explain the observed stall.

## Repairs applied

- Increased the Pilot Ollama response-token limit from 512 to 2048 while retaining the no-thinking fallback and fail-closed validation.
- Made the structured action schema omit `RunStarDockShopper` unless game actions are enabled, readiness is verified, the action has not already been attempted at this sector, and the shopper stage is inactive.
- Kept the worker-side readiness and execution checks unchanged.

## Validation

- Focused action-provider tests: **25 passed, 0 failed**.
- Full TradeWars suite: **252 passed, 0 failed**.
- The test build reported two existing xUnit2031 analyzer warnings in `StrategicReviewTests.cs`; they are unrelated to this change.
- Before restarting MTC, captured 74 accessible Pilot interaction-card summaries in [iteration-24-attempt-01-pilot-history.txt](./iteration-24-attempt-01-pilot-history.txt). Prompt bodies and model responses were not exposed in those summaries, so the full transcript-retention defect remains open.
- Release MTC build and live validation remain pending.

## Files changed

- [OllamaAutonomousTestActionProvider.cs](../LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.Models/OllamaAutonomousTestActionProvider.cs)
- [AutonomousTestActionProviderTests.cs](../LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.TradeWars.Tests/AutonomousTestActionProviderTests.cs)
- [defect-ledger.json](./defect-ledger.json)
- [defect-ledger.md](./defect-ledger.md)
- [lessons-learned.md](./lessons-learned.md)
- [iteration-24-attempt-01-pilot-history.txt](./iteration-24-attempt-01-pilot-history.txt)

Partial minute-by-minute telemetry is preserved in [iteration-24-attempt-01-metrics.jsonl](./iteration-24-attempt-01-metrics.jsonl). This attempt is excluded from the iteration scorecard.

## Remaining risks and next step

- The Release MTC runtime has not yet been rebuilt with the changes.
- The current ship remains outside FedSpace, lacks a scanner, and has only 64 credits. Revalidate the starting snapshot and a safe route before movement.
- StarDock, scanner purchase, and WorldTrade remain unverified.
- Full LLM prompt/response history remains memory-only; only accessible interaction-card summaries were preserved for this attempt.

After rebuilding and restarting MTC, recheck the fresh ship snapshot and active objective. Count the next iteration only after observable movement, trading, or scanning.
