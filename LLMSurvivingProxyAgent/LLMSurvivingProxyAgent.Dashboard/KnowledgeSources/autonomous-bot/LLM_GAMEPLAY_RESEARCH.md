# LLMs for TradeWars Decision-Making

Research notes captured 2026-10-22. This document records the design direction
and distinguishes implementation coverage from outcomes confirmed in a live
game session.

## Findings

The useful pattern in game-playing agent research is usually a hierarchy:
language models propose or plan at a high level, while environment-specific
skills perform actions and validate what is possible.

- **Voyager** demonstrates an LLM-driven Minecraft agent that builds and
  retrieves an executable skill library, uses an exploration curriculum, and
  improves proposed skills using environment feedback and self-verification.
  Its paper says it uses GPT-4; it is evidence for the architecture, not proof
  that a tiny LLM alone can learn a game.
- **SayCan** grounds language-model suggestions in a set of available skills
  and feasibility/value estimates. This is a useful precedent for letting a
  model select among known routines instead of inventing low-level commands.
- **ReAct** interleaves reasoning with actions and new observations. The
  decision loop should therefore return to observed game state after actions,
  rather than treating a long generated plan as guaranteed to succeed.
- **AgentBench** reports common LLM-agent difficulties with long-horizon
  reasoning, decision-making, and instruction following. It supports testing
  complete multi-step outcomes, not relying on plausible-sounding model
  explanations.

These sources do not establish that small LLMs outperform this bot's current
policy, nor that a particular model size will be sufficient. The model and
deployment choice should be measured on this game's state and action set.

## Early-Game Scanner-First Progression

When neither a Density Scanner nor a Holo Scanner is verified on the active
ship, prioritize reaching StarDock and acquiring scanner capability before
starting WorldTrade or exploring unknown sectors. Density is the minimum
requirement for WorldTrade and scan-before-warp safety. A Holo Scanner also
provides density scans and is an optional, faster and safer exploration
upgrade; never buy a separate Density Scanner when Holo ownership is verified.

Do not treat a script name, an old event, or a remembered purchase as proof of
ownership. Require a fresh synchronized ship-status snapshot for the current
ship after the purchase. If the scanner script is blocked by a prerequisite,
report the exact missing prerequisite and preserve the ship; do not bypass the
allowlist or deploy fighters in FedSpace to force the script to run.

Before any ordinary warp, use the installed scanner to preflight the target:
Density sends `sd`, while Holo sends `shsd`. Do not enter a sector whose scan
reports mines, limpets, armids, navhaz, or enemy fighters. Do not attempt
ordinary exploration without a scanner. If the current ship is an Escape Pod
or has one or fewer holds, reacquire a suitable ship before port trading or
WorldTrade.

Only after either scanner is verified on the active ship should the earnings
path move to the allowlisted `Pack1/1_Trade.ts` WorldTrade routine. Start from
the verified StarDock state, monitor the script, and verify fresh credits,
holds, and ship status after it stops. A log entry naming WorldTrade is not
proof that the routine ran or earned credits.

The TWX `Stop in this sector` prompt is an intermediate route decision, not a
request for another Enter. Respond with its single-key `Y` choice only when
that exact fresh prompt is observed, then recheck fresh sector and port state
before taking another turn. A stale or unauthenticated prompt is not
sufficient evidence to send that response.

## Model-Selected StarDock Shopper

Scanner acquisition is an allowlisted script-selection task, not a worker-owned
purchase macro. When scanner absence and affordability are verified, select the
Density Scanner as the minimum capability unless Holo ownership is already
verified or an explicitly approved Holo upgrade is affordable. Use only
freshly observed script-menu choices; `Z` is prohibited because it selects the
broad max-everything purchase. The worker only permits stage-matching input
while MTC reports the allowlisted script running. After it exits, request fresh
ship status and do not enable scanner-dependent play until ownership is
explicitly confirmed.

The shopper still has its own live prerequisites, including an owned or
corporate fighter in the script's starting sector and a verified route when a
StarDock round trip is required. StarDock sector 551 is confirmed FedSpace:
never deploy fighters there to satisfy a prerequisite. If a safe launch path
is unavailable, report the blocker instead of bypassing the gate. A missing
scanner remains a reason to route toward StarDock, not permission to enter
unknown sectors or start WorldTrade early.

## TWX Menu and Script Discovery

Use MTC's read-only `search_script_catalog` path to discover local TWX routine
filenames and categories. Queries about trading, World Trade, refurbishing,
Holds, Fighters, Shields, StarDock, Class 0 ports, or menus should search the
catalog. Catalog results are discovery hints only, not script source or proof
of behavior. Ground explanations in supplied knowledge and freshly observed
menu text; never infer unseen prompts or side effects from a filename.

The `$ S S` TWX script-selection menu is for discovering/selecting a script;
it is not evidence that the script has run. The separate
`$ S S directory\scriptfile.ts` form launches a script. Do not infer a key from
a remembered menu layout: first identify the current prompt, then use the exact
options in freshly observed menu text. Explain what the option does and its
credit/turn/state effects. Script execution remains restricted to reviewed,
allowlisted routines; the model must not emit arbitrary paths or raw input.

## Post-Scanner World Trade and Hardware Upgrade

After fresh ship status confirms Density or Holo Scanner ownership on the
active ship, the next economic objective is to earn with the fixed
`Pack1/1_Trade.ts` WorldTrade script, then upgrade ship components at a
verified Class 0 port or StarDock. Holo is not a WorldTrade prerequisite. The
autonomous worker stops WorldTrade only at a fresh Command prompt after
synchronized credits reach 1,000,000. The refurb manager preserves a
500,000-credit reserve and limits each quoted component purchase to one third
of the remaining budget.

The source-backed `TWX30-main/scripts/include/Refurb.ts` procedure distinguishes
the Class 0 and StarDock menus:

- Class 0 entry: `pty`; StarDock entry: `ps g y g q s p`.
- The quote labels are `A Cargo holds`, `B Fighters`, and `C Shield Points`.
- Holds use the `a` purchase option and a confirmation; Fighters use `b`;
  Shields use `c`. Exit the Class 0 menu with `q`, or the StarDock sequence
  with `qqq`.
- MTC reads all three fresh quote rows and passes the parsed prices and
  quantities to the model. The source-backed menu parsers read Cargo-hold
  price at word 5 and available quantity at word 10; Fighters use words 4 and
  8, and Shield Points use words 5 and 9. A missing or non-numeric price blocks
  that component rather than treating it as free.
- Purchase quantities are bounded by the quoted stock and conservative per-
  category budgets. Verify the selected component increased and synchronized
  credits decreased in fresh ship status before recording a purchase as
  successful.
- Verify the selected component increased and synchronized credits decreased
  in fresh ship status before recording a purchase as successful.

Do not confuse candidate scripts: `trading-cashing/class0supg.ts` searches for
likely Class 0 sectors; it does not upgrade a ship. `ship-upgrades/saar-furb.ts`
is a StarDock fleet/furbing routine, and `ship-upgrades/ls-ore-v162.ts` is a
planet-filling resource routine, not a hardware refurb procedure. SSM/SSM-upgrade
scripts include stealing or fighter/grid behavior and are not substitutes.

The stop-at-target and fixed Class 0/StarDock menu flow are now implemented as
bounded actions. No live purchase or World Trade stop has been performed; do
not claim the earnings goal or any upgrade is complete without fresh observed
status and a user-approved live trial.

This policy is implemented as bounded game actions and knowledge retrieval;
it does not establish that a live trade has succeeded. Only confirmed server
output or updated credits/holds count as a trade result. The embedded MTC
runtime exposes a live port-and-warp CIM refresh while connected and with
Meridian stopped. It sends each CIM request only from an observed TradeWars
Command prompt, waits for that prompt to return, and verifies the port CIM
timestamp before reporting completion; the TWXProxy AutoRecorder imports the
returned rows into the session database. Do not assume data is fresh until the
refresh completes successfully. Requests run through the read-only
`MTC_PortCIM.ts` and `MTC_WarpCIM.ts` TWX scripts in the configured proxy script
directory; do not inject raw Ctrl+R or Ctrl+I bytes through the terminal input
path. The worker now waits for fresh port CIM when cargo is aboard but no fresh
compatible buyer is known, rather than spending turns on an uninformed route.
The current code path still needs a successful live CIM and trade confirmation.

## Event-log lessons (2026-10-26 through 2026-10-30)

The five archived Meridian event files contain 161,984 events. They record
206 connection events and 154 disconnections, including 59 actual
`clear avoids` server-prompt events, 507 BBS-name prompts, and 252 password
prompts. Treat each as a new prompt-state transition; do not replay a cached
answer across reconnects or infer authentication from a connected socket.

The event sequence records a sector transition from 369 to 99 at
2026-10-29 13:57:49 UTC, followed by a synchronized status showing an Escape
Pod with one hold in sector 99. The preceding complete cruiser status was at
sector 369 with 40 holds and no scanner recorded. The log gap between those
ship-status snapshots means the exact hazard and scanner state at the instant
of entry are unknown; preserve that uncertainty while treating the ship loss
at sector 99 as confirmed.

The complete status snapshots contain 473 cruiser records with no scanner and
1,412 Escape Pod records with a Density Scanner; no Holo Scanner status was
observed. Eleven WorldTrade-related client-input events appear at sectors 783
and 369, but no corresponding evidence confirms a successful WorldTrade run
or positive earnings. Require scanner ownership on the active ship and
verified credit/hold changes rather than inferring success from a script name
or input event.

The event files contain no durable LLM prompt/reply transcript. Future test
windows must preserve prompt and final-response history before the MTC process
stops; do not infer model reasoning quality from terminal event counts.

## Fit With This Bot

The existing bot already has useful building blocks for a hybrid design:
state-based routine selection, deterministic prompt handlers, configured
actions, session/event logging, script and menu knowledge, and explicit
command-safety checks. Keep those as the execution and safety layer. An LLM
should initially advise the existing policy, not replace prompt handling,
macros, or the action scheduler.

Proposed decision boundary:

1. Supply a compact, structured snapshot of observed state and its freshness:
   sector and known warps, port profile, ship/holds/cargo, turns, synchronized
   credits, current phase, and recent outcomes.
2. Supply only the strategies currently eligible under deterministic rules.
   The model returns a structured choice such as a routine ID and bounded
   parameters, with a short rationale for diagnostics. The Dock Shopper uses
   an explicit staged action allowlist rather than arbitrary script paths or
   raw terminal input.
3. Validate the choice against the current state and allowlist. Re-check
   affordability, reserves, route/fuel, prompt context, and action-in-progress
   immediately before execution. Invalid or stale choices are rejected and
   handled through the existing deterministic policy; they are not converted
   into arbitrary commands.
4. Observe the server's response, record the result, and make the next
   decision from the new state.

## Safe Evaluation Path

1. **Decision adapter:** define a small schema for state, allowed routine IDs,
   and model output. Keep command text out of the model's action interface.
2. **Shadow mode:** log model proposals and compare them with the current
   policy without executing the proposals.
3. **Offline/replay tests:** compare complete outcomes on recorded or
   controlled scenarios. Track net credits, turns, successful recovery/trading,
   stale-state decisions, and invalid proposals. Unsafe or unauthorized
   actions must remain at zero.
4. **Constrained trial:** only after offline results are acceptable, allow
   validated routine selection in a controlled environment, with the
   deterministic policy as fallback and an operator stop path.
5. **Learning:** use reviewed, successful and failed trajectories to improve
   prompts, retrieval examples, or a locally hosted fine-tuned/distilled small
   model. Keep learned examples separate from authoritative live state; stale
   memories must not override current server observations.

Do not let the model directly issue raw telnet/TWX commands, confirm
destructive operations, spend unsynchronized credits, exceed configured
reserves, or start overlapping trade commands. Preserve all existing
recovery, confirmation, and operator-control gates. Prefer local inference if
state or logs contain account-sensitive information; do not send credentials
or private logs to an external model service.

## References

- Wang et al., “Voyager: An Open-Ended Embodied Agent with Large Language
  Models,” 2023: https://arxiv.org/abs/2305.16291
- Ahn et al., “Do As I Can, Not As I Say: Grounding Language in Robotic
  Affordances” (SayCan), 2022: https://arxiv.org/abs/2204.01691
- Yao et al., “ReAct: Synergizing Reasoning and Acting in Language Models,”
  2022: https://arxiv.org/abs/2210.03629
- Liu et al., “AgentBench: Evaluating LLMs as Agents,”
  2023: https://arxiv.org/abs/2308.03688
