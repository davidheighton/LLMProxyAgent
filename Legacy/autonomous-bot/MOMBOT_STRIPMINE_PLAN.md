# Mombot Strip-Mining Plan

This file tracks the ongoing extraction of useful framework patterns from Mombot into the Node autonomous bot.

## Goal

Continuously port high-value behavior from the Mombot framework while preserving current live stability.

## Cadence

1. Pick one Mombot include or mode.
2. Map behavior to Node runtime.
3. Port smallest safe slice.
4. Validate with syntax checks and live status/log verification.
5. Keep, tune, or roll forward.

## Phase Backlog

### Phase 1 - Runtime Supervisor
- Status: In progress (first slice complete)
- Ported:
  - Watchdog freeze recovery
  - Recovery cooldown and escalation
  - Supervisor telemetry in status payload

### Phase 2 - Event Bus and Routing
- Status: In progress (first slice complete)
- Ported:
  - Runtime event publication/counters/recent ring buffer
  - Status transition/mode change/TX/CIM events
  - Startup CIM skip events

### Phase 3 - Mode and Command Orchestration
- Status: In progress (first slice complete)
- Ported:
  - Mode derivation from phase/action
  - Mode telemetry surfaced in status
- Remaining:
  - Explicit mode registry with per-mode handlers
  - Isolate startup, sync, recovery, offense/defense loops by mode

### Phase 4 - Data Services Layer
- Status: In progress (first slice complete)
- Ported:
  - Data services snapshot (intel age, CIM age, persistence timestamps)
- Remaining:
  - Extract map/port/fighter/stardock services into independent modules
  - Add deterministic freshness policy objects

### Phase 5 - Safety and Reliability Hardening
- Status: Planned
- Targets:
  - Deterministic startup state machine transitions
  - Prompt deadlock breakers by menu context
  - Recovery policy per phase/mode

### Phase 6 - Continuous Mombot Mining
- Status: Active
- Targets:
  - Pull one module at a time from `TWX30-main/scripts/mombot/include`
  - Convert to testable Node service or policy
  - Track compatibility and gaps

## Source Modules To Mine Next

1. `include/switchboard.ts` for richer output routing and notification policy
2. `include/update.ts` for delta-based data refresh reports
3. `include/map.ts` for stardock discovery and adjacency intelligence utilities
4. `include/grid.ts` and `include/connectivity.ts` for route and resilience upgrades
5. `modes/resource` and `modes/offense` for modular mode handlers

## Compatibility Matrix

- Supervisor watchdog: Partial (active)
- Event bus: Partial (active)
- Mode registry: Partial (derived, not explicit)
- Data freshness services: Partial (snapshot only)
- Grid/fighter delta reporting: Not ported
- Switchboard style formatting/routing: Not ported
- Connectivity relog policy: Not ported
- Modular mode packages: Not ported

## Definition of Done for Each Slice

- Compiles cleanly (`node --check`)
- Config JSON validates
- Status payload includes new telemetry fields
- Runtime behavior visible in logs/status without regressions
