# Iteration 28

## Result

- Productive activity was observed because the rebuilt worker moved the ship; the run did not achieve a trade, scanner purchase, or WorldTrade start.
- MTC Release `win-x64` build: succeeded with 0 warnings and 0 errors.
- `AutonomousTestActionProviderTests`: 27 passed, 0 failed.
- Runtime window: approximately 11 minutes of the planned 15 minutes; stopped early for safety.

## Issues and root cause

- **DEFECT-0026 regressed:** with no Density or Holo Scanner and 1 credit, verified snapshots showed movement between StarDock sector 551 and sector 99. A port-172 funding trade objective was visible during the window, but exact route targets and command acknowledgments were not captured. The specific cause of the 551/99 loop remains unconfirmed.
- **Status-action no-op:** the previous live attempt showed `RequestShipInformation` selected despite a fresh healthy snapshot. The Pilot schema now omits that action while the snapshot is healthy and no older than 30 seconds.
- **Strategic lane not observed:** the monitor showed Pilot and Reflex history, but no Strategic Review tab/cycle was visible during this run.
- **Insufficient verified funds:** the last captured state was sector 551, 1 credit, and no Holo Scanner. Dock Shopper readiness and any port transaction remained unverified.

## Repairs and validation

- Filtered `RequestShipInformation` out of the Pilot structured-action enum for a healthy fresh snapshot.
- Added a regression assertion for the fresh-state action enum.
- Re-ran the provider test class: 27/27 passed.
- Rebuilt the Release `win-x64` MTC: passed with 0 warnings and 0 errors.
- The new build was exercised live and produced verified movement, but the route loop was not resolved. Meridian was stopped when sector 99 reappeared, before another unscanned entry.

## LLM and runtime observations

- Latest visible history sample: 36 Pilot and 91 Reflex interactions.
- No Strategic Review tab/cycle was visible.
- The final Pilot call was still in progress when Meridian was stopped.
- No runtime metrics were sampled; runtime stability and LLM contract scores remain unavailable.

## Files modified

- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.Models/OllamaAutonomousTestActionProvider.cs`
- `LLMSurvivingProxyAgent/LLMSurvivingProxyAgent.TradeWars.Tests/AutonomousTestActionProviderTests.cs`
- `reports/defect-ledger.json`
- `reports/defect-ledger.md`
- `reports/iteration-27.md`
- `reports/iteration-28.md`
- `reports/lessons-learned.md`
- `reports/scorecard.json`

## Remaining risks and next steps

- Keep DEFECT-0026 REGRESSED. Capture every trade objective, adjacent target, MTC command submission/acknowledgment, and fresh sector snapshot to identify the route arbitration that sends the scannerless ship toward sector 99.
- Preserve the scan-first rule and avoid resuming autonomous movement through unverified sectors until the route is confirmed safe.
- The latest state is at StarDock sector 551, but scanner ownership is absent and credits are only 1. Do not start WorldTrade without verified Density or Holo Scanner ownership.
