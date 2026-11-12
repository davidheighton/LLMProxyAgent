# Running multiple agent sessions

MTC's TWX runtime keeps process-wide state, so use one MTC process per independent
game session. Do not try to host multiple game profiles inside one process.

Launch each process with a unique instance ID and agent ID. A shared team ID
enables durable status sharing between those agents. The Agent workspace in the
embedded dashboard can also select a saved profile and launch the isolated
process automatically:

```powershell
.\MTC.exe --instance-id meridian --agent-id meridian --game-profile "Game D.json" --team-id trade-team
.\MTC.exe --instance-id scout-e --agent-id scout-e --game-profile "Game E.json" --team-id trade-team
```

`--game-profile` is the saved profile's `.json` filename inside the configured
MTC games directory. The embedded MTC sidebar provides game-session tabs. Launching an assigned
agent starts a separate MTC process, then docks its actual MTC window into the
same workspace pane; sessions can be switched between tabs or undocked back to
separate windows. This window hosting is a presentation layer only: each game
still runs in a separate MTC process because the TWX runtime keeps process-wide
state. The dashboard also provides agent workspace tabs. Selecting an agent
opens its runtime workspace; if that agent is hosted by another MTC process,
the runtime page can launch it using an available saved profile. The existing
game-file lock remains an additional guard against
opening the same profile twice. Profiles held by another MTC process are shown
as in use and disabled in the assignment selector; the launch path checks the
lock again to handle races. If a lock is acquired between that check and
process startup, the session refuses to connect or start the agent without its
database.
Each agent ID can be active in only one MTC process at a time; the application
creates the persistent agent record on first use and resets only that agent's
status when its process starts.
Each terminal keeps its own MTC menu and controls, including Start/Pause and
Learn mode. The agent Runtime page also exposes the per-agent automatic-start
preference; changes apply the next time that MTC session launches and do not
start or stop an already-running worker. Session tabs show live worker status
for the current process and `MTC OPEN` when a secondary session process is
open; use that session's terminal toolbar to control its worker.
Each terminal also owns its game-status footer. The shared dashboard footer
remains below the workspace with tabs for the local agent and open MTC sessions;
it summarizes the active process, model-request counts, runtime queues, and
recent activity. Secondary-session worker telemetry remains inside that
session's own terminal.
Assigned-session launch can optionally create and save a unique login name,
trader alias, and password locally when the game offers account creation.

The default launch behavior is unchanged: it uses the `meridian` agent and
allows only one default MTC process. `--instance-id` selects the process mutex;
`--agent-id` selects the persistent agent identity and defaults to the instance
ID when omitted. `--team-id` is optional and must match across agents that should
coordinate.

Agents in a team publish their latest fresh authenticated sector, turns,
credits, and active goal to the shared agent database. Teammates receive status updates no
older than 15 minutes as decision context. These are separate game sessions:
an agent must not assume another member's map, resources, or prompts match its
own, and each agent can issue commands only to its own session. Experience and
journal ownership remain keyed by agent ID.

All embedded MTC processes use the shared
`%LOCALAPPDATA%\\LLMSurvivingProxyAgent` agent and knowledge databases. Keep
agent IDs unique when sessions are intended to represent different players.
