# Iteration 22

## Result

Productive but early-terminated. The rebuilt Release MTC process reconnected to the existing game session and attempted the sector-10 Equipment trade. Native haggle diagnostics recorded an initial no-transaction outcome followed by a `trade-success-line:great` result at 08:44:23. Meridian was stopped at 08:44:22 while the dashboard still showed 300 credits and a stale PortTradeQuantity snapshot, so the resulting cargo and credit delta were not verified from a fresh ship snapshot. The run did not approach the 900-second target.

## Issues and root causes

- **The dashboard snapshot lagged the terminal/haggle stream.** At stop time, the monitor still showed sector 10, 300 credits, no scanner, and PortTradeQuantity; the native log then recorded the return to Command and a successful haggle one second later. The source of this delayed UI/runtime synchronization remains under investigation.
- **Pilot remained inactive.** The captured monitor history showed 0 Pilot calls, 14 Reflex interactions, and 1 Strategic Review interaction after the process restart.
- **The browser circuit briefly failed to rejoin.** A reload restored a fresh runtime page, and MTC remained responsive; no process crash was observed.

## Repairs and validation

- `StartPortTradeAsync` now requires both the existing command-prompt signal and a latest, fresh timestamped server prompt classified as `Command`; otherwise it throws before sending any trade input.
- The safety harness verified that a fresh AutopilotConfirmation and a stale Command prompt both prevent initial trade input, while valid buy/sell trade cases still pass.
- MTC RPC safety harness: **passed**.
- TradeWars suite: **250 passed, 0 failed** after isolating and rerunning a transient SQLite cleanup failure.
- Windows x64 Release build: **passed, 0 warnings, 0 errors** after stopping the MTC PID that held `MTC.exe`.
- The follow-up live retry moved through sectors 10, 8, and 4 and showed credits decreasing from 300 to 64 by 08:50; that continued activity is recorded under iteration 23.

## Files changed

- `LLMControlCenter/MtcGameCommandSender.cs`
- `Legacy/TWX30-main/TWX30-main-tests/MtcRpcSafetyTests/Program.cs`

## Remaining risks

- The trade-success log is not yet corroborated by a fresh ship-status snapshot.
- StarDock, scanner purchase, and WorldTrade remain unverified.
- Prompt freshness and dashboard synchronization must be observed under the restarted runtime.

## Lessons

A transaction log can confirm a trade outcome even when the dashboard snapshot is stale, but it is not a substitute for verifying cargo and credits afterward. Record the two evidence sources separately and do not infer a final ship state from a stale monitor.
