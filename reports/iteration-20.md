# Iteration 20

## Result

**Productive but stopped early.** The Release Control Center moved the authenticated ship from sector 1 through the FedSpace sweep to sector 10. After the run remained at sector 10 with unchanged turns and credits for several minutes, the worker was stopped through Runtime controls. The terminal stayed connected. The run lasted about six minutes after the first observed progress and did not complete a 900-second window.

## Issues found

1. **FedSpace first-login sweep stall was not reproduced with the Release runtime.** The prior Debug process had remained at sector 1 waiting for a fresh route. After rebuilding and restarting on Release, the ship advanced through the sweep to sector 10.
2. **Port-trade recovery stalled on AutopilotConfirmation.** A `TradeAtCurrentPort` action was interrupted by an autopilot prompt. The movement prompt was safely declined, but subsequent decline attempts were rejected because the command sender did not see a fresh matching prompt. Verified state remained sector 10, 65,421 turns, and 300 credits.
3. **Reflex contract rejection persisted at lower frequency.** The new prompt was active in live history. Of 18 Reflex interactions, one was rejected because it requested strategic review while `escalate=false`; the remainder passed the deterministic gate. At least one accepted response still called a routine ship-state update urgent.
4. **Pilot did not run during the observed portion.** The monitor showed zero Pilot requests, one Strategic Review interaction, and 18 Reflex interactions. The deterministic sweep/trading path remained active and had not reached an eligible script prompt.

## Root causes and repairs

- **Reflex over-classification:** The prompt did not distinguish ordinary numerical ship-state updates from immediate threats. Added explicit default urgency guidance for `WorldObservationChanged` and numeric changes, without weakening the deterministic escalation gate. Added prompt-contract assertions.
- **Autopilot prompt mismatch:** The worker's displayed prompt state and the command sender's latest fresh prompt evidence disagreed. Root cause remains unconfirmed. No input-safety guard was weakened.
- **Sweep:** The source already contained the stale-terminal FedSpace route handling; the active Debug binary did not demonstrate it. Building and restarting the Release executable allowed the sweep to proceed.

## Validation

- Full TradeWars test suite: **250 passed, 0 failed**. Two existing xUnit analyzer warnings were reported in `StrategicReviewTests.cs`.
- Windows x64 Release build: **0 warnings, 0 errors**.
- Defect ledger JSON parsed successfully after update.
- Live evidence: movement to sector 10; unchanged credits/turns during the stalled period; connected terminal after stopping.
- Live success is **not** claimed for port trading, StarDock discovery, Density Scanner purchase, `2_worldtrade`, or 15-minute runtime stability.

## Files changed or captured

- `LLMSurvivingProxyAgent.Models/OllamaReflexReasoningProvider.cs`
- `LLMSurvivingProxyAgent.TradeWars.Tests/ReactiveIntelligenceTests.cs`
- `reports/defect-ledger.json`
- `reports/defect-ledger.md`
- `reports/lessons-learned.md`
- `reports/iteration-20-metrics.jsonl` (six valid samples; partial window)
- `reports/iteration-20-llm-history.txt` (captured before the process history is lost)

## Scores

- Build stability: **100%** (test suite and Release build passed)
- Runtime stability: **N/A** (early-terminated; less than 900 seconds)
- LLM contract proxy: **94.7%** (18 of 19 sampled Reflex/Strategic results passed the available contract checks; not a semantic-quality score)
- Prompt contract coverage: **100%** (source and regression assertion updated; live semantic adherence remains incomplete)
- Autonomous recovery, regression rate: **N/A**
- Validated defect closure: **5 of 23 (21.7%)**

## Remaining risks and next steps

- Trace why session state still reports `AutopilotConfirmation` when the command sender has no fresh matching prompt. Keep all command freshness checks fail-closed.
- Verify whether the decline returns the game to Command and whether one-hop route selection resumes after the prompt mismatch is fixed.
- Continue the sector 2–10 compatible-port trade plan, then locate StarDock, purchase a Density Scanner, and start/monitor `2_worldtrade`.
- Run a complete 900-second window and collect continuous process/container telemetry before scoring runtime stability.
- Verify Pilot request counts after deterministic sweep/trading completes; do not treat Reflex/Strategic activity as evidence of Pilot execution.
