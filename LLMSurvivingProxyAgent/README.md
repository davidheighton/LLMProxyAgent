# LLMSurvivingProxyAgent

A greenfield .NET 10 solution for a persistent TradeWars player. Its application projects
separate the player domain, TradeWars integration, model providers, agent runtime,
macros, and dashboard.

## Current vertical slice

- The Blazor dashboard creates and lists player identities.
- Agent identity, model selection, goals, configuration, journal, and
  statistics have a SQLite persistence boundary.
- The default `Meridian` agent is created once and remains in the database across
  application restarts.
- On startup, any previously active status is recovered as `Offline`; a crashed
  process cannot leave the UI claiming the player is still running.
- The Ollama strategic-review provider returns structured progress assessments,
  cited knowledge, and objective decisions. It receives no raw command, protocol,
  navigation, or macro-execution interface.
- The TradeWars project now persists universe metadata, sectors, directed warp
  edges, port/planet/trader/ship observations, and corporations in SQLite.
- It provides sector queries, shortest directed paths, nearest StarDock lookup,
  unexplored-adjacent-sector queries, and validated port/warp CIM line imports.
- The world store can load universe metadata and a sector batch atomically; the
  navigation and CIM behavior has focused offline tests.
- The macro registry/runtime includes deterministic pair-port planning,
  adjacent-sector exploration planning, nearest-StarDock routing, and an
  explicitly blocked CIM-refresh macro when no live session exists.
- Macro attempts persist start/end time, status, result, notes, and planned
  sectors in SQLite; the dashboard exposes a plan runner and recent activity.
- The Knowledge project performs deterministic, non-executing analysis of
  configured TWX scripts and guidance. It stores version-aware source hashes, relative
  provenance, line-level redacted evidence, confidence, and relationships for
  world claims, mechanics, tactical/strategic guidance, macros, and construction
  patterns. The Knowledge Catalog page supports mining and review; only
  allowlisted roots from dashboard configuration can be scanned.
- Knowledge mining also catalogs TWX trigger registrations by normalized
  trigger type, with line-level condition, handler, explicit urgency-comment,
  and possible recovery/failure evidence. Catalog frequency counts distinct
  source documents by configured root; urgency, runtime firing, outcome, and
  operational doctrine are not inferred.
- Knowledge extraction is intentionally heuristic: comments and code structure
  produce reviewable leads, not verified game facts or executable macros. Live
  CIM/world data remains authoritative for current universe state.
- The new CorpusBuilder console project inventories explicitly supplied roots
  by extension and size, records source-kind and extraction counts, and builds
  versioned seed packages from staged SQLite data. Runtime-artifact roots are
  inventory-only; logs, status, telemetry, credentials, generated output, and
  cache directories are excluded from mining. Inventory records file metadata,
  not file contents or local absolute paths.
- The initial local corpus package (`artifacts/trade-wars-v1.seedpkg`) and its
  inventory (`artifacts/trade-wars-v1.inventory.json`) cover seven roots:
  TWX-Scripts, Pink-Scripts, MOMBot, TWX30, unencrypted scripts, Zed, and the
  autonomous Node bot. The v1.0.0 build contains 1,987 source documents and
  9,402 extracted knowledge nodes. Extraction remains heuristic and the
  resulting claims require review; it is not verified gameplay doctrine.
  Runtime/log/status files under the Node bot root were not mined; only file
  counts, extensions, and sizes appear in its inventory summary.
- The CorpusBuilder coverage summary reports trigger registrations and kinds,
  trigger evidence, code artifacts, subroutine definitions, explicit macro
  context labels, explicit urgency annotations, labeled stopping/failure
  conditions, precondition checks, result-verification patterns, and
  documentation-file counts per source. Menu coverage is explicitly
  `notMeasured` unless a saved-game log root is supplied; ordinary logs remain
  inventory-only.
- `SavedGameLogs` roots use a separate streaming extractor. It considers
  repeated, bordered option rows from incoming `RX-LINE` records and stores
  only menu key/label evidence. Outgoing commands, conversations, and raw log
  text are not added to the seed; menu transitions and availability are not
  inferred. This is partial menu coverage, not a complete menu graph.
- Corpus v1.4.0 includes the saved-game captures: 1,989 documents and 9,405
  nodes total, with three observed menu layouts, 61 distinct key/label options,
  and 685 repeated menu displays across two captured logs. The main command
  menu appears in both captures; an onboard-computer menu appears in one.
  Package schema v2 adds the menu-structure category while retaining schema v1
  import support.
- Script comments with explicit labels such as `Purpose:`, `Typical usage:`,
  `Trigger:`, `Stopping condition:`, and `Failure condition:` are retained as
  separately signaled evidence on script knowledge nodes. These remain cited
  source claims, not validated doctrine. The package manifest includes the
  corpus revision and per-root file/extraction summaries.
- The corpus inventory compares recognized trigger registrations and explicitly
  labeled macro-context evidence across source families. These are heuristic
  observations: multiple roots of one kind count as one family, unobserved
  evidence does not establish absence, and handler/recovery candidates are not
  causally linked to individual registrations. Repetition is not treated as
  verified doctrine.
- Corpus v1.5.1 keeps the v1.4 corpus size (1,989 documents and 9,405 nodes)
  while classifying Zed as its own source family. Its inventory reports eight
  recognized trigger tags; bust-registration evidence appears in TWX, Pink, and
  TWX30. The six macro-context comparisons currently come only from explicit
  purpose comments in TWX30. These are mined observations, not evidence that
  other families lack the behavior or that matching or differing filenames
  identify the same macro.
- Corpus v1.6.0 adds pattern comparisons for handler response candidates,
  possible recovery handling, explicit urgency annotations, labeled stopping
  and failure conditions, and structural construction patterns. Its inventory
  contains 19 observed pattern groups: eight handler-response tags, five
  recovery tags, and six construction-pattern types. The current roots yielded
  no explicit urgency annotations or labeled stopping/failure-condition
  comments. The corpus still has 1,989 documents and 9,405 nodes; this report
  adds analysis without promoting candidates to verified behavior.
- Response and recovery evidence is compared as separate observations, not
  joined to a specific trigger. Urgency comments are not escalation policies,
  and construction patterns such as `Failure recovery` or `Result
  verification` show code-structure signals—not that a recovery or verification
  succeeded at runtime. See `artifacts/trade-wars-v1.6.inventory.json` for
  family counts and `artifacts/trade-wars-v1.6.seedpkg` for the versioned seed.
- Corpus v1.7.1 recognizes additional explicit decision-context labels:
  observed goals, preconditions, validation checks, verification loops, retry
  and resume patterns, abort/escalation conditions, recovery checkpoints, and
  recovery patterns. These labels are retained as cited evidence and counted
  per source; unlabeled code is not promoted to an author-stated purpose.
- The v1.7.1 inventory adds a family-coverage strength to trigger, macro
  context, and pattern comparisons. `High` requires at least four families and
  two-thirds of scanned families; `Moderate` requires at least two families
  and one-third; otherwise it is `Low`. Every result explicitly says this is
  observed coverage, not validated consensus or doctrine.
- In the current v1.7.1 roots, none of the new explicit decision-context labels
  were present; the six macro-context comparisons remain explicit `Purpose`
  observations. Structural pattern comparisons still identify six
  construction-pattern types across all six scanned families, including
  precondition checks, route validation, and result verification. The report
  has 33 comparison rows total (6 High, 18 Moderate, 9 Low), all with
  observation-only interpretation. See
  `artifacts/trade-wars-v1.7.1.inventory.json` and
  `artifacts/trade-wars-v1.7.1.seedpkg`.
- Corpus v1.8.0 adds a documentation-oriented `DecisionContext` category for
  cited passages about intent, objectives, usage and avoidance conditions,
  assumptions, prerequisites, risks, benefits, expected outcomes, alternatives,
  counterexamples, stopping/escalation conditions, validation, retry/resume,
  abort, and verification patterns. Matching is heuristic and favors explicit
  decision phrasing; generic help keywords are covered by negative tests to
  avoid treating ordinary command labels as strategic context.
- The v1.8.0 build contains 1,989 source documents and 9,408 knowledge nodes,
  with 431 decision-context evidence observations across 116 source documents.
  Its comparison report has 59 signal/topic rows across six scanned
  documentation-bearing families (MOMBot, NodeBot, Pink, TWX, TWX30, and Zed).
  These are source observations, not verified tactics or validated consensus;
  a signal not observed in a family does not establish that the family lacks
  equivalent guidance. Package schema v3 carries the new category while import
  support for schemas v1 and v2 remains. See
  `artifacts/trade-wars-v1.8.inventory.json` and
  `artifacts/trade-wars-v1.8.seedpkg`.
- Corpus v1.8.1 adds per-root documentation-role coverage to both inventory
  reports and package source summaries. Files are classified from relative
  paths and file names only as TacticalGuide, HelpReference, DesignNotes,
  ForumMaterial, PersonalNotes, RuntimeHistory, or GeneralDocumentation. The
  report keeps inventoried-file counts separate from mined-document counts and
  decision-context evidence, and includes a few relative-path samples per role.
  This classifier does not inspect document content and is explicitly
  heuristic. In the current roots, 337 files are classified as HelpReference,
  67 as GeneralDocumentation, one as DesignNotes, two as PersonalNotes, and one
  as RuntimeHistory; none are path-classified as TacticalGuide or
  ForumMaterial. That is a source-discovery signal, not proof those materials
  are absent. The build remains at 1,989 documents and 9,408 nodes; its
  decision-context comparisons remain observations aggregated across document
  roles. See `artifacts/trade-wars-v1.8.1.inventory.json` and
  `artifacts/trade-wars-v1.8.1.seedpkg`.
- Corpus v1.9.0 adds candidate claim networks to the inventory comparison
  report. It uses leading uppercase acronym-like terms in code artifact
  filenames as candidate entities, then connects a documentation decision
  observation only when the exact term appears in its cited evidence text or
  document filename. Each link carries the evidence node/document IDs, source
  path, line, family, documentation role, and match basis, along with code
  artifact references. The current corpus produces six candidate networks and
  16 linked observations; the SST network has eight code artifacts across five
  code families and three context observations across two documentation
  documents. These remain lexical co-reference candidates: a filename-only
  match suggests document subject, and no link proves implementation intent,
  tactic correctness, or successful outcomes. Claim networks are inventory
  analysis only; they do not create persistent knowledge-graph relations.
  See `artifacts/trade-wars-v1.9.inventory.json` and
  `artifacts/trade-wars-v1.9.seedpkg`.
- Corpus v1.10.0 is the next combined local snapshot. It rebuilds the prior
  eight roots (including the filtered saved-game-log root) and adds `TRIBAL`
  as a `Documentation` root. The package contains 2,025 source documents and
  9,564 knowledge nodes; TRIBAL contributes 29 documents and 191 nodes. Its
  13 decision-context observations come only from `SLICE-10.TXT`; they remain
  heuristic authored claims, not verified doctrine. Help-reference material
  contributes other cited observations but is excluded from decision-context
  extraction to reduce menu/manual false positives. See
  `artifacts/trade-wars-v1.10.inventory.json` and
  `artifacts/trade-wars-v1.10.seedpkg`.
- The CorpusBuilder `url-inventory` command accepts an explicit JSON list of
  HTTP(S) URLs and records normalized URL, host, optional title/author/operator-
  supplied retrieval date, source role, classification basis, and optional
  priority tier (1 is highest, 5 lowest). Roles can be supplied or heuristically
  classified from host/path tokens. Each entry records a candidate evidence
  class (`AuthoredClaimCandidate`, `ImplementedBehaviorCandidate`,
  `ObservedOutcomeCandidate`, or `MixedCandidate`) and `NotFetched`; these are
  inventory hints, not verified evidence. This stage performs no discovery,
  network requests, page extraction, or claim mining. Invalid URLs, embedded
  credentials, duplicate normalized URLs, malformed role values, and existing
  output paths are rejected. Example input:

  ```json
  {
    "sources": [
      {
        "url": "https://example.org/strategy/sst",
        "title": "SST strategy notes",
        "author": "Example author",
        "role": "tacticalGuide",
        "evidenceClassCandidate": "authoredClaimCandidate",
        "priorityTier": 2
      }
    ]
  }
  ```

  Run with `corpus-builder url-inventory --input urls.json --output
  web-sources.inventory.json`. This inventory is a source-discovery aid, not
  evidence that any page recommends a tactic or supports a particular claim.
  The initial 17-source candidate list and generated inventory are stored in
  `artifacts/web-source-seeds.v1.json` and
  `artifacts/web-sources-v1.inventory.json`. The Bottomless Abyss URL is
  represented once at its higher priority tier; the ZiSKiND archive is kept
  as a discovery pointer for Slice's War Manual because no direct manual URL
  was supplied.
- A first public-source review was performed on 2026-10-24 for those 17 URLs.
  Sixteen were accessible; the TradeWars Bible wiki URL returned an empty 404
  stub. Short paraphrased observations, visible source metadata, access status,
  limitations, and provenance are recorded in
  `artifacts/web-source-review.v1.json`. This run did not crawl links, download
  repositories or archives, or collect runtime outcomes. Repository pages were
  reviewed at README/landing-page level only; their implementation claims
  remain authored statements, not verified code behavior.
- The initial v1.0 source set excludes the empty Bible URL and retains the 16
  accessible pages in `artifacts/web-source-knowledge.v1.json`: eight
  knowledge-evidence candidates, seven discovery-only sources, and one
  metadata-only software catalog. Discovery pages are retained as leads, not
  promoted to tactical claims. `corpus-builder web-seed` packages the nine
  knowledge/metadata sources into a separate seed package with 13
  `WorldKnowledge` observations. Each node uses the `ExternalSourceReview`
  extraction basis, a source URL, and a review-note locator; its summary says
  the paraphrase is not independently validated as game doctrine. The
  discovery-only sources do not create knowledge nodes. This package is
  available as `artifacts/trade-wars-web-v1.seedpkg`, separate from the
  existing local corpus seed, and is not auto-imported.
  Example:

  ```powershell
  dotnet run --project .\LLMSurvivingProxyAgent.CorpusBuilder\LLMSurvivingProxyAgent.CorpusBuilder.csproj -- `
    web-seed --input .\artifacts\web-source-knowledge.v1.json `
    --output .\artifacts\trade-wars-web-v1.seedpkg --version 1.0.0
  ```
- A bounded ClassicTW follow-up inspected `index.php`, the first listing page
  for forums 7 and 15, and three selected threads (six page loads total). It
  did not crawl the forum, follow pagination, or download attachments.
  `artifacts/web-source-forum-followup.v1.json` records the index/listing
  metadata, candidate thread links, opened-thread summaries, and scope limits.
  The index is navigation-only; the thread samples yielded authored claims
  about TWX/M()MBot releases, death-limit podding, and historical pod-destination
  rules. Those claims are not verified against current server behavior.
- The active v1.1 web set adds those three reviewed threads to the original 16
  accessible pages: 19 active sources total, comprising 11
  knowledge-evidence candidates, seven discovery-only sources, and one
  metadata-only catalog. `artifacts/web-source-knowledge.v1.1.json` is the
  input; `artifacts/trade-wars-web-v1.1.seedpkg` contains 12 source documents
  and 19 paraphrased `WorldKnowledge` observations with URL provenance.
  Discovery-only listings remain outside the knowledge nodes, and the empty
  Bible URL remains excluded.
  Build with:

  ```powershell
  dotnet run --project .\LLMSurvivingProxyAgent.CorpusBuilder\LLMSurvivingProxyAgent.CorpusBuilder.csproj -- `
    web-seed --input .\artifacts\web-source-knowledge.v1.1.json `
    --output .\artifacts\trade-wars-web-v1.1.seedpkg --version 1.1.0
  ```
- Knowledge relationships are typed, directed edges with their own confidence,
  extraction basis, evidence, and evidence-document provenance. The store can
  traverse bounded paths and add cross-document reviewed claims such as
  `Supports`, `Enables`, or `MitigatesRisk`. Mining currently emits only
  structural `CallsSubroutine` and `DemonstratesPattern` edges; it does not
  automatically assert strategic causality.
- Shared seed knowledge and per-agent runtime knowledge now use separate SQLite
  files. The default files are `knowledge.seed.db` and `knowledge.runtime.db`
  beside `agents.db`; configure them with `Knowledge:SeedDatabasePath` and
  `Knowledge:RuntimeDatabasePath`. Normal mining writes only to the runtime
  store. The composite read service exposes both stores to catalog and review
  consumers, while seed-package imports write only to the seed store.
- Startup copies existing knowledge documents, nodes, and relations from the
  legacy `agents.db` knowledge tables into an empty seed database in one
  transaction. It retains the old tables. On later starts, document identity
  and source hashes are checked; a partial or conflicting migration stops
  startup rather than overwriting knowledge.
- The Packaging project exports/imports versioned ZIP packages containing a
  JSON manifest and payload. Seed packages preserve source documents, evidence,
  and cross-document relations; imports validate references, reject collisions,
  and apply atomically. A SHA-256 payload checksum detects corruption but is
  not a signature or proof of publisher identity.
- Agent export packages preserve identity/preferences, journal entries,
  proposals, objectives, macro telemetry, reactive history, and strategic
  reviews, plus explicitly agent-owned runtime knowledge. They record the
  required seed content fingerprint and counts without copying the seed.
  Unowned runtime mining results are not misattributed as personal knowledge.
  Configuration keys that appear to contain credentials are omitted. The
  export explicitly reports that trust networks, relationships, and standalone
  communications are not stored yet. Current history APIs cap each operational
  history at 500 items, so exports at that limit are marked as potentially
  truncated. Package services are library APIs for controlled packaging tools;
  no package-import/export dashboard workflow is exposed yet, and normal
  dashboard writes cannot mutate the seed store.
- The strategic-review runtime compares manually entered snapshots with previous
  review snapshots, recent journal entries, the active objective, pending goal
  candidates, and bounded knowledge results. It asks the configured Ollama model
  to assess progress, evidence for and against progress, stuck signals, uncertain
  assumptions, overlooked opportunities, and alternative objectives.
- A deterministic failure detector flags repeated synchronized-credit values
  and unchanged active objectives after three consecutive prior review cycles.
  It reports the repetition count and elapsed duration, suppresses trend claims
  when the supplied snapshot predates saved history, and explicitly marks
  unavailable telemetry such as new-sector counts and route profitability.
- Each review also receives up to ten prior assessments, exact normalized
  summary/recommendation repetition counts, consecutive stalled-review count,
  and local macro telemetry since the previous review. This supports
  meta-review without claiming semantic repetition or action completion:
  macro records are offline/local telemetry and are not linked to arbitrary
  free-text recommendations or live in-game actions.
- The Reactive project classifies typed events deterministically into fast,
  deliberative, or informational outcomes. Confirmed hostile/bust/disconnect
  signals produce response intents without invoking an LLM; untrusted
  communications retain sender, channel, receipt time, and trust-at-receipt and
  are escalated without changing verified world state. Fast candidates older
  than 30 seconds are treated as stale and escalated rather than used for
  emergency planning. Event provenance and decisions are persisted in SQLite.
- The Reactive project defines a constrained reflex-model contract and validation
  gate. Model-shaped output can only classify, prioritize, assess trust, escalate,
  and produce allowlisted audited intents; messages and urgent assessments cannot
  bypass deliberative review. The gate exposes an explicit fallback that
  escalates ambiguous events on timeout or invalid output. The Ollama reflex
  provider handles deliberative events; deterministic fast-path rules bypass it.
- The Runtime project hosts separate bounded work lanes: a deterministic
  reactive worker and a strategic worker that invokes the existing review
  provider. Dashboard-submitted events are journaled, and strategic reviews
  include attributed recent reactive activity with per-type/path counts.
- In the embedded MTC client, the initial synchronized ship state and distinct
  verified ship-state changes are submitted to the reactive queue. Live terminal
  chat is not automatically classified because the event stream does not provide
  a reliable sender/channel boundary; attributed communication events can still
  be submitted from the Reactive Intelligence page. Queues are in memory and
  queued work is not recovered after process restart. Reflex output remains
  advisory and cannot dispatch gameplay actions.
- Reactive actions are currently audited intents only. There is no live TWX
  trigger adapter, command sender, macro cancellation, disconnect/reconnect
  handler, or movement/combat executor. Deliberative event intents are not
  automatically converted into review requests; a verified snapshot is required.
- The model may continue the active objective, select one of the supplied pending
  candidates, or create and select a new candidate. Candidate IDs and knowledge
  citations are validated against the exact review context before an objective
  transition. Objective selection is atomic; replacing an objective marks its
  previous objective superseded. Completion does not silently select another
  candidate; a later review makes that decision.
- Each completed review stores the timestamp, model/provider, snapshot, full
  bounded context, structured assessment, and objective-decision outcome in an
  append-only SQLite review journal. These records become progress history for
  later reviews. Proposal and review state are not live game telemetry, and
  objective transitions do not execute macros.
- In-game communications are not yet connected. Future message intake should
  preserve channel, sender, receipt time, and trust-at-receipt as evidence;
  reported claims must not silently overwrite verified world state.

Agent memory has been renamed conceptually to a journal; any entries saved by
the initial slice are migrated once into the journal. TWXProxy's legacy memory
table is retained as historical data.

The world and macro engines are offline data/query/planning layers, not yet an
operational player: live TradeWars session handling, prompt parsing, automatic
world-state capture, actual trading/movement execution, and supervised runtime
resumption are not implemented. The reasoning integration requires a reachable
Ollama server and the agent's configured model. The dashboard does not connect
to a game server or issue game commands.

## Learning trajectories and reward

The autonomous Pilot worker records every decision as a trajectory step so runs can be compared,
scored, and later used for reflection, skill statistics, or fine-tuning.

- **Episodes.** One episode spans one worker run. It ends when the worker stops, or when a new worker
  run for the same agent supersedes it.
- **Steps.** Each decision is stored with the synchronized snapshot at decision time, the action,
  target sector, rationale, active objective, and the ID of the LLM interaction that produced it.
  `source` records who chose the action: `Pilot`, `Policy` (deterministic rule),
  `PilotFallbackToPolicy` (Pilot chose `None`), or `PilotOverriddenByPolicy`.
- **Reward.** A step is closed when the next decision is recorded or the episode ends. Its reward is
  computed from the newest valid synchronized snapshot (`GameplayRewardCalculator`): change in
  estimated net worth (credits plus holds, fighters, shields, scanners, and cargo) per 1,000 credits,
  minus 0.02 per turn used, plus 0.05 for a sector new to the episode, plus 5 for acquiring a
  scanner, minus 50 for losing the ship to an Escape Pod. Without a newer valid snapshot the reward is
  marked unverified and excluded from totals. Weights are in `GameplayRewardWeights`; asset values are
  estimates.
- **LLM history.** Prompts and replies for every role are written through to SQLite and reloaded on
  startup. Calls still in progress at shutdown are marked `Interrupted before completion.`

Both are stored in `trajectories.db` beside `agents.db` (override with
`Learning:TrajectoryDatabasePath`). Loopback-only endpoints expose the data:

```text
GET /api/learning/episodes?agentId=meridian&limit=50
GET /api/learning/episodes/{episodeId}/steps
```

Example analysis queries:

```sql
-- How often does the Pilot actually choose the action that runs?
SELECT source, COUNT(*) FROM trajectory_steps GROUP BY source;

-- Average verified reward by action.
SELECT action, COUNT(*) AS n, AVG(reward_total) AS avg_reward
FROM trajectory_steps WHERE reward_verified = 1 GROUP BY action ORDER BY avg_reward DESC;
```

## Episode harness

`LLMSurvivingProxyAgent.Harness` runs repeatable learning episodes. Each episode is a brand-new character in
its own MTC process, scored automatically from the trajectory data above.

```powershell
# From the repository root. Builds must be current: build LLMControlCenter first.
dotnet run --project LLMSurvivingProxyAgent\LLMSurvivingProxyAgent.Harness -- run `
  --base-profile "LLMControlCenter\games\exiledTWGS  - Game E.json" --episodes 4 --parallel 2 `
  --pilot-model qwen3:1.7b
dotnet run --project LLMSurvivingProxyAgent\LLMSurvivingProxyAgent.Harness -- status
dotnet run --project LLMSurvivingProxyAgent\LLMSurvivingProxyAgent.Harness -- resume
dotnet run --project LLMSurvivingProxyAgent\LLMSurvivingProxyAgent.Harness -- report --batch <id>
dotnet run --project LLMSurvivingProxyAgent\LLMSurvivingProxyAgent.Harness -- cleanup
```

- **Models.** `--pilot-model` defaults to `qwen3:1.7b`. New-character naming always uses the fast reactive model
  (`Brains:Reactive:Model`, default `qwen3:1.7b`) with thinking off, and only receives the game letter. Model replies
  are reduced to their leading letters, retried, and replaced by a random callsign or alias-based name if still
  invalid.
- **Cleanup.** When an episode finishes (not when paused), the learner is removed from the agent registry and its
  cloned profile, `.xdb`, and game folder are deleted. Event and debug logs are archived to
  `%LOCALAPPDATA%\LLMSurvivingProxyAgent\harness\<batch>\<episode>\`. Trajectories, LLM history, reports, and saved
  login credentials are kept; the characters stay on the server. `--cleanup false` keeps everything, and the
  `cleanup` command applies the same rules to existing batches.

- **Isolation.** MTC locks each profile, so every episode gets a cloned profile
  (`<base name> - learner-<batch>-<n>.json`), its own `.xdb` (a copy of the base map by default; `--map empty`
  starts blank) and game folder. The clone keeps the base "Game X" name, so the game letter is unchanged, and the
  new agent ID makes MTC generate a new login and character (`--create-account-if-needed`).
- **Goal.** `FullyUpgradedShip`: Holo Scanner plus maximum holds, fighters, and shields for the current ship class.
  Milestones are recorded along the way: ReachedStarDock, DensityScanner, FirstProfitableTrade, HoloScanner,
  MaxHolds, MaxFighters, MaxShields, FullyUpgraded. Milestones use only fresh synchronized snapshots.
- **Ship limits** come from Mombot's universe catalog `games/<game>/ships.cfg`, copied into each clone. Start
  Mombot once on the base profile to capture it. Without it, a "max" milestone is awarded only from a fresh Refurb
  quote that offers zero more units while credits exceed the unit price.
- **Episode end.** Completed, ShipLost (Escape Pod), PausedOutOfTurns (fewer than 5 turns; resumed after the next
  `--game-day-reset`, default local midnight), TimedOut (`--wall-clock-minutes`, default 480 per segment), Stopped,
  or Error (worker fault or blocked login). MTC writes a result JSON and exits with codes 10–15
  (`EpisodeExitCodes`). The harness kills MTC after the cap plus `--grace-minutes` (default 15) and reconciles the
  episode status.
- **Reports.** `reports/harness/<batch>.md` and `.json`: completion and ship-loss rates, milestone funnel with
  median turns and minutes, net worth per turn, decision sources, verified-step rate, and the characters created
  (old characters are not removed from the server).
- **State.** `harness_batches` and `harness_episodes` live in `trajectories.db`, so `resume` works after the
  harness itself restarts. Stopping the harness with Ctrl+C closes its MTC sessions; killing the harness process
  does not.

MTC accepts the matching options directly: `--episode-id`, `--episode-goal`, `--episode-wall-clock-minutes`,
`--episode-minimum-turns`, `--episode-result-path`, `--episode-pilot-model`, `--memory-id`, and
`--exit-on-episode-end`.

## Playbook: cross-life memory and coaching

The playbook is how the agent gets better from one life to the next, and how a veteran player coaches it. It
lives in `playbook.db` beside `agents.db` (override with `Learning:PlaybookDatabasePath`), outside any one
game's folder, so it survives character deaths, game resets, and harness cleanup.

**Memory lineage.** Entries and life records belong to a *memory id*, not to one character. It defaults to the
agent id. Harness learners share one lineage, `--memory-id veteran` by default, so a stream of short-lived
`learner-*` agents grows into a single veteran. Pass a new id to start a fresh lineage. For a manually launched
MTC, set `LLMCONTROL_MEMORY_ID`. Agents that share a memory id (for example teammates) learn together.

**Entry kinds.**

| Kind | Author | Effect |
| --- | --- | --- |
| Rule | Coach | Enforced. The worker refuses the forbidden action while the rule's situation matches; the step is recorded as `CoachRuleVeto`. |
| Tip | Coach | Strong advice. The Pilot may deviate only by naming the tip in `overriddenPlaybookEntryId` with an `overrideReason`; every override is logged and scored. |
| Lesson | Agent | Learned from its own past lives. Starts at low confidence and must earn it in play. |

Entries apply in **situation tags**: coarse, universe-independent tags derived from the synchronized snapshot.
Examples are `credits:<1k`, `holds:30-59`, `scanner:none`, `location:fedspace`, `location:stardock` and
`cargo:loaded`; prefix `!` to negate a tag. Sector numbers differ between games, but "no scanner, under 1k
credits" does not, so tags let lessons transfer between games. An entry is either universal or scoped to the
current game (the MTC session name).

**Learning loop.**

1. Each decision, the worker selects the applicable rules, tips, and lessons and puts them first in
   `relevantKnowledge` (layers `CoachRule`, `CoachTip`, `Lesson`) with their track record. Strategic Review gets
   the same items plus `veteranMemory`: recent life summaries and progression benchmarks.
2. When the Pilot relies on an entry it cites `playbook:<id>`. When that step's reward is verified, the entry's
   counters update: help above +0.01, hurt below −0.05 (ordinary move costs are neutral). Confidence is a Beta
   estimate with a source prior: coach 0.8, endorsed lesson ≈0.73, new lesson 0.4. An unendorsed lesson retires
   automatically after 8 scored uses below 0.25. Coach entries never auto-retire; they are flagged *evidence
   disagrees* below 0.4 for the coach to review.
3. When an episode ends, it is remembered as a **life**: a numbered record with its milestone timeline (turns
   to each milestone), reward by action, and outcome. Comparing milestone turns across lives is how the agent
   discovers an efficient early-game progression without a hard-coded curriculum.
4. The local strategic model then reflects on the life's best and worst verified decisions (`OllamaLessonDistiller`)
   and proposes at most three lessons. Each proposal must cite steps it was shown, use known tags, and restate an
   existing lesson by reinforcing it instead of duplicating it. Reflection runs only against a loopback model
   endpoint; set `Learning:DisableDistillation=true` to turn it off. Credit assignment still runs.

**Coaching.**

- *Coaching page* (`/agents/<id>/coaching`, also in the MTC workspace navigation). Add tips and rules with tag
  pickers. Review, endorse, edit, or retire learned lessons. See each entry's track record and overrides, and
  the progression across lives. Mark recent decisions good or poor with a note; the note becomes a tip scoped to
  that decision's situation.
- *In the game terminal*, type `!coach ...` and press Enter. TradeWars acts on single keystrokes, so MTC holds a
  `!` only while the next keys still spell `coach`; anything else goes to the game unchanged. Escape cancels.

  ```text
  !coach Sell carried cargo before docking at StarDock.
  !coach here : Pair-trade Organics/Equipment next to FedSpace until you can afford more holds.
  !coach rule never worldtrade when scanner:none : World Trade needs a scanner first.
  !coach game : The port pair at 412/413 pays well in this universe.
  !coach help
  ```

  `here` scopes the entry to the current credits, holds, scanner, and location tags. `game` limits it to this
  universe. Rules need `never <action>`; accepted actions are `move`, `trade`, `worldtrade`, `shopper`,
  `buyholds`, `buyfighters`, `buyshields`, and `refurb`, or a full action name.
- *API (loopback only)*: `POST /api/agents/<id>/coach` with `{"command":"..."}`, and `GET /api/agents/<id>/playbook`.

## Run

```powershell
dotnet run --project .\LLMSurvivingProxyAgent.Dashboard
```

The database is stored at
`%LOCALAPPDATA%\LLMSurvivingProxyAgent\agents.db` by default. Set
`AgentDatabasePath` in configuration to use another path.

The dashboard's `Knowledge:Roots` configuration contains the only directories
the Knowledge Catalog can mine. Paths are resolved relative to the dashboard
content root. Add or remove explicit roots there when running against another
workspace; the page does not accept arbitrary filesystem paths.
The checked-in defaults point to curated snapshots in
`LLMSurvivingProxyAgent.Dashboard/KnowledgeSources/`, so the agent does not
depend on the workspace's `Legacy/` tree. These sources are copied alongside
the dashboard at build and publish time. The autonomous-bot snapshot includes
operational docs and source modules, but intentionally excludes game
configuration, logs, and runtime status. Other configured roots contain the
script/core source trees used for knowledge mining.

Build a corpus inventory or seed package from explicit local roots:

```powershell
dotnet run --project .\LLMSurvivingProxyAgent.CorpusBuilder -- inventory `
  --output .\corpus-inventory.json `
  --root twx:TwxScripts=C:\Projects\TWBot\Legacy\TWX-Scripts `
  --root runtime:RuntimeArtifacts=C:\Projects\TWBot\Legacy\autonomous-bot\logs `
  --root tribal:Documentation=C:\Projects\TWBot\Legacy\TRIBAL

dotnet run --project .\LLMSurvivingProxyAgent\LLMSurvivingProxyAgent.CorpusBuilder -- build `
  --output .\trade-wars-v1.seedpkg --version 1.0.0 `
  --source "Local TradeWars corpus" `
  --root twx:TwxScripts=C:\Projects\TWBot\Legacy\TWX-Scripts `
  --root runtime:RuntimeArtifacts=C:\Projects\TWBot\Legacy\autonomous-bot\logs `
  --root tribal:Documentation=C:\Projects\TWBot\Legacy\TRIBAL
```

Repeat `--root` for additional script, documentation, Node-bot, or data roots.
The scanner inventories files without following reparse points and does not
read runtime-artifact roots. ZIP archives are read in place, never extracted
to disk or executed; only allowlisted text/code entries are considered.
Traversal paths are rejected, with limits of 4 MiB per entry, 128 MiB per
archive, 64 MiB total supported expansion per archive, and 2,048 entries.
Exact entry copies are deduplicated against loose files and earlier archive
entries within the same source root. Archive evidence retains a virtual path
such as `archive.zip!/folder/source.ts`.

Knowledge mining supports Markdown and text, HTML/HTM with markup removed
while preserving source line locations, text-encoded legacy `.doc` files,
TiddlyWiki `.tid` pages, Telix `.slt` comments and `.key` mappings in the
TRIBAL root, and historical `.ts.backup` / `.ts.msfucked` files as TWX
includes. Tiddler title, tags, and type are retained in evidence source
locators while header lines are excluded from claim extraction. Binary Word
`.doc` files fail with conversion guidance; `.docx`, PDF, and RTF are
inventory-only. A generated seed package contains mined evidence and metadata,
not the complete source files.

The current local seed is the combined
`artifacts/trade-wars-v1.12.seedpkg`, with inventory in
`artifacts/trade-wars-v1.12.inventory.json`; it contains 2,344 documents and
10,903 knowledge nodes across ten roots. It retains TRIBAL in the combined
package and adds the standalone legacy Mombot source. The expanded ingestion
includes 236 TiddlyWiki pages, 67 unique text/code files from ZIP archives,
10 Telix scripts, two Telix key maps, and two historical Pink scripts. Twenty
one exact archive-entry copies were deduplicated within their source roots.

Each full corpus build reports disjoint knowledge-class rollups in the
inventory: facts, procedures, tactics, cooperative doctrine, communication,
strategy, and structure. CommunicationPattern nodes are counted separately
from cooperative doctrine, and the class totals are checked against exported
nodes. Inventory-only scans leave these counts unmeasured rather than reporting
misleading zeros. The original v1.12 inventory's 92 cooperative nodes included
61 CommunicationPattern nodes; the disjoint split is 31 cooperative and 61
communication nodes. Earlier inventory artifacts retain their original counts.

Version 1.12 adds evidence-backed cooperative doctrine categories:
CommunicationPattern (61 observations), CooperativeProcedure (6),
CorpProtocol (13), EmergencyResponse (3), ResourceTransfer (9), and
TacticalPattern (71). No explicit role/responsibility statements qualified
for RoleDefinition in this corpus pass. Structured evidence preserves the
source line and only records expected responses or rationale when stated.
FURB provisioning and explicitly numbered cooperative steps are kept
separate; the miner does not synthesize missing rescue steps. Tactical branch
extraction is limited to explicit safety-oriented conditions and targets.
These are static source observations, not validated game doctrine or evidence
of successful outcomes. Credential and channel-setting examples such as
`CORPPW` and `SSCHANNEL` are skipped or redacted.

Corpus v1.13.3 adds a disjoint communication rollup to conservative extraction
of explicit outbound Sub-Space message templates from TWX source. Dynamic
values are represented as
`{dynamic}`; generic help text and fighter-status messages are excluded, and
generic photon references are labeled as status rather than a threat report.
The build contains 2,344 source documents and 11,025 nodes. Its disjoint
rollups include 183 communication nodes and 31 cooperative nodes; v1.13.2's
214 cooperative nodes included the 183 communication nodes. The package
records 271 template evidence observations across 91 documents and 137
case-insensitively distinct normalized templates. These are implemented
message templates, not captured player conversations; required actions and
expected responses are not inferred. See
`artifacts/trade-wars-v1.13.3.inventory.json` and
`artifacts/trade-wars-v1.13.3.seedpkg`.

Corpus v1.14.0 adds the source-backed
[`LOGIN_SESSION_PROCEDURES.md`](../Legacy/autonomous-bot/LOGIN_SESSION_PROCEDURES.md)
guide to the Node-bot source root. It synthesizes legacy account-creation,
prompt-driven login, M()MBot logoff/relog, and reconnect patterns, and records
current Node-bot provisioning/security gaps. The full local package contains
2,345 documents and 11,039 nodes (one new source document and 14 additional
nodes). It contains no passwords or live Game D/E session transcripts; those
observations remain separate from shared seed knowledge. See
`artifacts/trade-wars-v1.14.0.inventory.json` and
`artifacts/trade-wars-v1.14.0.seedpkg`.

Knowledge categories now map to eight disjoint layers: Facts, Procedural,
Tactical, Communication, Cooperative, Strategic, Structural, and Experience. Strategic
reviews retrieve up to four matching nodes per layer rather than allowing the
largest procedural category to dominate the context (up to 32 total). Each
supplied knowledge item carries its layer label; the reasoning prompt explicitly treats message
templates as distinct from captured exchanges and cooperative workflows as
distinct from evidence of successful coordination. This improves retrieval
balance and interpretation, but does not validate the underlying source claims.

AgentExperience nodes are created in agent-owned runtime knowledge from
recorded macro telemetry, retrieved only for their owning agent's strategic
reviews, and included in that agent's export. Their status is
the macro engine's status (including Planned or Blocked), not proof that the
intended game outcome occurred. When an `ISynchronizedGameSnapshotProvider` is
supplied, an experience may also preserve the before/after `GameSnapshot`
observations with their timestamps. They are recorded as observations only;
no deltas or causal conclusions are derived. The dashboard can host an
optional loopback-only Telnet proxy, so a normal game client connects to the
LLM proxy and the proxy forwards bytes to the configured TradeWars server:

```json
{
  "GameProxy": {
    "Listeners": [
      {
        "AgentId": "meridian",
        "ListenAddress": "127.0.0.1",
        "ListenPort": 2023,
        "UpstreamHost": "192.168.4.75",
        "UpstreamPort": 2002,
        "EventLogDirectory": "data/proxy-events/meridian"
      }
    ]
  }
}
```

The listener is disabled by default. It accepts one client per configured
listener, binds only to loopback, and relays the original Telnet bytes in both
directions without logging client keystrokes or raw terminal text. It records
connection lifecycle, five-second presence heartbeats, recognized prompts,
sector changes, and complete ship-information screens observed from the
server. A client may request the ordinary in-game `I` ship-information screen;
the proxy itself does not issue game commands unless the separately gated
autonomous smoke test is enabled. Event records are written to
`EventLogDirectory` as `events-*.jsonl`.

For the standalone dashboard, `AutonomousTesting.Enabled` remains false by
default and `AutonomousGameplay.Enabled` remains a separate, Development-only
opt-in. The embedded LLMControlCenter enables the agent runtime when the
operator starts the agent. While active, gameplay has no per-session action
limit and movement does not require a separate approval for every hop. The
worker still requires healthy synchronized state, positive turns, a fresh
Command prompt, and fresh directed warp data, then revalidates immediately
before sending each move. The command sender accepts typed requests only and
never forwards model-provided command text. Current gameplay supports verified
port trades and the explicitly approved built-in scripts; arbitrary catalog
scripts are not automatically executable. Both the selector and strategic
review use the validated loopback Ollama endpoint. The action prompt receives
a bounded, source-derived selection of relevant procedures, tactics,
mechanics, and recent agent experience, with citations checked against the
supplied knowledge nodes. The action selector receives the agent's active
objective; when none exists and the snapshot is healthy, strategic review gets
a chance to select one before a gameplay action.

Autonomous login is separately opt-in and Development-only. It connects as a
local client to the configured loopback proxy only when that agent has no active
proxy session; an already-connected manual client is never displaced. At the
first BBS name prompt, the local LLM selects a server login name and a distinct
trader alias for the selected game profile, then the application persists both
before sending them. It only creates a character after the server explicitly
offers the new-character prompt; the LLM then generates a fresh per-game
password, which is persisted before the confirmation is sent. Password input
is limited to the expected creation/confirmation prompts. Existing-character
login requires a previously saved game password. Unexpected prompts, invalid
LLM output, or an unconfirmed password stop the worker instead of falling into
account creation or retrying credentials.

Identity generation uses the configured local Ollama model, receives only the
server endpoint and selected game profile, and never receives raw terminal
output. Generated names and passwords are stored in the local profile file;
they are not written to proxy telemetry or sent to the later gameplay action
selector.

The autonomous client responds to Telnet option requests before processing
login prompts, refusing options it does not implement.
Game-letter and new-character choices use the server's single-key input;
the daily-log prompt accepts its default No response.

The embedded MTC Control Center uses the already-connected MTC terminal instead
of this separate client. Its Meridian login path is existing-character-only:
when the server offers a new character, it sends `N` and halts without sending
a password or entering account setup. If MTC is already at a current authenticated
`Command` prompt, Meridian attaches to that session without replaying credentials.

For local testing, create
`%LOCALAPPDATA%\LLMSurvivingProxyAgent\game-login.json` (plaintext, outside the
source tree), configure the verified game letters, and set `Enabled` only when
ready:

```json
{
  "AutonomousLogin": {
    "Enabled": false,
    "AgentId": "meridian",
    "LoginName": "",
    "SelectedGame": "default",
    "Games": {
      "default": { "GameLetter": "D", "TraderAlias": "", "Password": "" },
      "enhanced": { "GameLetter": "E", "TraderAlias": "", "Password": "" }
    }
  }
}
```

The example letters match the local test bed: D is default and E is enhanced.
The LLM-generated login name, trader aliases, and per-game
passwords are filled into this local file at runtime. `AutonomousLogin:AgentId`
must match `AutonomousTesting:AgentId`, and both automatic login and the bounded
`RequestShipInformation` smoke test must be enabled. Do not copy generated
credentials into `appsettings*.json`, source control, or logs, and do not feed
stored credentials back to the model.

`GameState:EventLogPaths:<agentId>` remains available for an existing MTC event
log or another compatible event source. The generic event-log provider accepts
a snapshot only when the log shows a connected session, a full
`SynchronizedShipStatus` event newer than the latest connection and
sector-change events, and a status timestamp no more than 30 seconds old.
Incremental turn, credit, or fighter updates are not treated as complete
snapshots. Disconnected, stale, incomplete, or not-yet-synchronized state
returns unavailable; it never fills missing values from local guesses. The
registered `IGameSituationProvider` also reports connection presence, whether
a recognized in-game prompt verifies authentication, synchronization health,
the latest observed prompt, and fields that remain unknown. Snapshot fields
include holds, experience, alignment, and corporation only when present in the
full status event. The proxy flushes connection, sector, complete status, and
heartbeat events so the provider can observe them promptly. Presence is stale
without a heartbeat in the last 15 seconds. A situation observation is
persisted only for a connected, authenticated, healthy report, and it remains
private to its owning agent. MTC remains an optional client/event source, not
a required control surface. Event logs are operational telemetry, not a
credential store.
Request a capture with `POST /api/agents/meridian/situation` and JSON body
`{"sessionId":"alpha-1a"}`; the endpoint returns `409` with the report and an
explicit reason when the state is not trusted, and otherwise returns the
persisted observation. The provider reads observations only and does not send
game commands.
Profitability, after-action causes, and trust changes are not measured.

The experience recorder also accepts explicit, consecutive turn observations
for a named session. Each entry retains the supplied before/after snapshots,
is stored in that agent's runtime knowledge, and rejects gaps or duplicate turn
numbers. This records an adapter-reported sequence; it does not prove a turn
was spent, that the agent survived, or that an operator was absent. The
event-log provider supplies a trusted source boundary, but no live solo-game loop is
registered; the 100-turn Alpha-1 acceptance gate remains unverified.

The v1.13.3 seed is an automated beta candidate. Its acceptance test imports
the actual package and checks package integrity, redaction, evidence presence,
disjoint layer totals, retrieval from every layer, and the absence of inferred
actions or responses on outbound message templates. Run it with:

```powershell
dotnet test .\LLMSurvivingProxyAgent.TradeWars.Tests `
  --filter "FullyQualifiedName~V1133SeedPassesAutomatedBetaEvidenceAndLayerGate"
```

A passing gate establishes packaging and retrieval quality checks, not that
historical claims are correct, tactics succeed in live play, or the corpus
contains real corporate conversations. Human source review remains necessary
before treating individual doctrine claims as verified.

The reviewed external-web package remains separate because it uses a distinct
source-review and authored-claim provenance path. `TLXTW204.DOC` is
text-encoded rather than a binary Word document, so it can be mined; other
binary `.doc` files require conversion.

Rebuild the current local seed with the explicit source roots:

```powershell
dotnet run --project .\LLMSurvivingProxyAgent.CorpusBuilder -- build `
  --output .\LLMSurvivingProxyAgent\artifacts\trade-wars-v1.14.0.seedpkg `
  --inventory .\LLMSurvivingProxyAgent\artifacts\trade-wars-v1.14.0.inventory.json `
  --version 1.14.0 --source "Local TradeWars corpus" `
  --root twx:TwxScripts=C:\Projects\TWBot\Legacy\TWX-Scripts `
  --root pink:PinkScripts=C:\Projects\TWBot\Legacy\Pink-Scripts `
  --root mombot:MomBot=C:\Projects\TWBot\Legacy\mombot `
  --root twx30:Twx30=C:\Projects\TWBot\Legacy\TWX30-main `
  --root legacy-mombot:MomBot=C:\Projects\TWBot\Legacy\__mom_bot3_1045 `
  --root unencrypted:Other=C:\Projects\TWBot\Legacy\unencrypted-scripts `
  --root zed:Zed=C:\Projects\TWBot\Legacy\Zed `
  --root node:NodeBot=C:\Projects\TWBot\Legacy\autonomous-bot `
  --root saved-logs:SavedGameLogs=C:\Projects\TWBot\Legacy\autonomous-bot\logs `
  --root tribal:Documentation=C:\Projects\TWBot\Legacy\TRIBAL
```

The ClassicTW forum follow-up is currently a bounded manual review, not a
crawler. Any crawler should require explicit allowed forums, pagination and
thread caps, conservative request pacing, canonical-URL deduplication, and
provenance for both listing discovery and opened-thread content. It should not
log in, fetch attachments, or treat listing titles as authored claims, and a
full-forum crawl should never be the default.

`Reasoning:OllamaEndpoint` configures the Ollama service URL (default
`http://localhost:11434/`). Brain model settings are independent:

- `Brains:Pilot:Model` selects the action model; it falls back to the legacy
  `AutonomousTesting:Model` setting.
- `Brains:Reactive:Model` selects the fast reflex model (default `qwen3:1.7b`).
  Deterministic fast-path rules bypass the model; ambiguous events use it for
  audited classification and escalation only.
- `Brains:Strategic:Model` selects the larger objective/review model (default
  `qwen3:8b`).

The Agent Settings page can override each role per agent; those values take
precedence over the controller-wide defaults above.

Strategic review seeds the Alpha-1 learning curriculum, keeps one objective
active, and schedules review on session start, requested synchronization or
failure review, significant verified state changes, and a five-minute interval.
The Pilot receives the active objective and cannot create one. Review may
complete an objective; a successor is selected or generated before the Pilot
acts again. Legacy `PreferredProvider` and `PreferredModel` remain the fallback
for direct, non-autonomous strategic reviews when no brain-specific model is
configured. The endpoint must serve Ollama's `/api/chat` API.
