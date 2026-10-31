# LLMs for TradeWars Decision-Making

Research notes captured 2026-10-22. This document records a design direction,
not an implemented or live-tested change.

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
   parameters, with a short rationale for diagnostics.
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
