# TradeWars Login, Account Creation, Logout, and Reconnect

Reviewed 2026-10-24 from the readable legacy scripts and help files linked
below. These are source-backed examples, not a universal TWGS protocol or
ready-to-run macro. Server versions, game configuration, and current prompt
matter. Compiled `.cts` artifacts were not reverse-engineered for this review.

## Keep the identity and session layers distinct

A session can involve several different identities and states:

1. The BBS/TWGS name entered at the server's name prompt.
2. The selected game letter on that server.
3. The character/password for that game's player database.
4. The trader alias, which may differ from the BBS name.
5. Optional ship and home-planet names, which are requested only by some
   games.
6. The TWX/Telnet connection and the authenticated in-game prompt.

Do not treat a TCP connection, a server banner, a selected game, or a supplied
credential as proof of authenticated game state. Advance one observed prompt
at a time, and verify the next expected prompt before sending more input.

## Existing-character login

The readable TWX/Pink login scripts use a prompt-driven sequence: wait for the
name prompt, submit the configured BBS name, wait for the game-server banner,
select the configured game, and wait for the game module/password prompts.
They then wait for a sector display or an explicit pause/avoid prompt rather
than assuming that the first response means login is complete. The Pink script
also registers a connection-lost event so it can stop waiting on a dead
session. See [Pink `1_Login.ts`](../Pink-Scripts/scripts/1_Login.ts) and
[TWX Pack1 `1_Login.ts`](../TWX30-main/scripts/Pack1/1_Login.ts).

The scripts are examples for their stated TWGS versions. Their concatenated
send sequences should not be copied across unknown prompts: a menu key, a
line-submitted answer, and an intentional Enter at a pause are different
input types. In particular, send `Y`/`N` as a single key only when the observed
prompt uses single-key semantics; do not append Enter by habit.

## New-character creation

[TWX `ls_New_Game_v192.ts`](../TWX-Scripts/ls_New_Game_v192.ts) checks that
required profile values are present and that the script is not already
connected. It waits for the BBS name and game selection, handles an observed
pause, and branches on actual prompts for a new trader alias, ship name, home
planet name, or the command prompt. Its alias path explicitly chooses a new
alias and waits for the server's name confirmation. This is a specialized
new-game script, not an existing-account login routine.

[Z-Bot installation notes](../Zed/Z-Bot%20Installation%20Notes.txt) describe
separate TWX setup credentials and configurable alias/ship/planet/corporation
settings. They also document per-game `GAMENAME_z-globals.cfg` overrides and
say a planet is named only “if any.” Together, these sources support a
prompt-driven rule: do not assume that a game asks for a ship or creates a
personal home planet. Only answer the setup prompt that is actually present.

Before creating an account, require an explicit, recognized “new character”
prompt for the selected game and an explicit account-creation policy. A
generic username prompt, a login failure, or a timeout is not enough evidence
to create a character. Keep the BBS name, game, character, alias, and
credential-store entry associated with the correct server/game.

The embedded MTC existing-account login path declines new-character creation
by default. It can accept a new character only when the selected game's local
`game-login.json` profile explicitly sets `AllowNewCharacterCreation` to
`true`, and only after the recognized new-character prompt is observed. The
flow reuses that game's configured trader alias and password; it does not
register a new BBS identity. Keep this option false for normal reconnects and
enable it only for an explicitly authorized character reset. The setting is
local credential configuration and must not be copied into shared logs or
knowledge packages.

The embedded MTC input path accepts one line and at most one trailing Enter
per call. Split a legacy compound send such as `Y.*Y` into separate calls;
never pass an embedded Enter inside one login input.

## Logout and fast re-entry patterns

M()MBot documents `relog` as an attempt to log back into the game and
`logoff {minutes} {cloak}` as a logoff with optional timed re-entry.
Its logoff implementation distinguishes a Citadel start from other prompts,
may perform a cloak-out when requested, disconnects, counts down the timer,
and allows the spacebar to request an early relog. The connectivity include
then waits for connection acceptance, recognizes TWGS v1/v2 banners, selects
the configured game, and handles pause, closed-game, and new-character
prompts. See [M()MBot logoff help](../TWX30-main/scripts/mombot/help/logoff.txt),
[logoff/relog implementation](../TWX30-main/scripts/mombot/include/internal_commands.ts),
and [connectivity include](../TWX30-main/scripts/mombot/include/connectivity.ts).

M()MBot's `exit`/`xenter` is not a logout synonym: its help describes a quick
in-game exit and re-entry that clears mail and cleans mines. Do not use that
routine in a passive login/logout test. Likewise, the TWX
[Extern auto-login script](../TWX-Scripts/tt_auto_extern_login.ts) combines
bounded reconnect attempts with StarDock, cloak, and other gameplay behavior;
only its retry/timeout structure is relevant to session recovery.

The Node bot reconnects after a transport disconnect using its configured
delay and starts the prompt-handling flow again. That is different from a
verified in-game logout. A robust shutdown should first stop pending actions
and disable automatic reconnect, then use a known game-level exit only when
the current prompt and server support it, verify the server-side transition,
and finally close the transport. A reconnect must discard the previous live
menu cursor and wait for fresh prompts and synchronized state.

## Current Node-bot gaps to address before unattended provisioning

The current host config enables `autoCreateAccount` and has automatic replies
for new-character creation, alias/ship/planet prompts, and generic
“is what you want?” confirmations. Those are useful prototype hooks, but they
are broader than a safe account-creation policy: a generic confirmation must
not approve an unexpected name or state transition. Gate each response on the
expected game, exact prompt state, intended value, and one-time authorization.

The current generated-identity path writes the password to
`generated-account.json` as ordinary JSON. It marks the outgoing password send
as sensitive, but that does not protect the persisted file. Replace that
persistence with an OS-backed secret store before using generated credentials
for unattended live accounts. Never put passwords, credential-bearing logs,
or generated account files into this shared corpus.

The current reconnect loop uses a fixed delay and reschedules after failed
starts. A production session controller should add observable attempt counts,
bounded backoff with jitter, a clear operator stop, and tests proving that
retries do not replay account-creation or gameplay input. See the current
[host configuration](./config.192.168.4.75.json) and
[bot identity/reconnect implementation](./bot.js).

## Recommended prompt-state checklist

1. Connect and identify the server prompt; select a game only from an observed
   game list or configured, verified game letter.
2. Submit the BBS name and wait for the selected game's menu and authentication
   prompt. Do not infer character identity from the BBS name.
3. For a known character, submit its password only at the password prompt.
   For a new character, require the explicit new-character prompt and
   authorization. If the server explicitly says to repeat the password for
   verification, submit the same credential only at a fresh second prompt
   after the first submission; do not infer confirmation from a repeated
   generic prompt without that setup evidence.
4. Handle trader alias, ship name, and planet name as separate optional
   states. Confirm only the exact intended value. Stop if an unrecognized
   prompt appears.
5. Declare login complete only after a stable in-game command prompt and a
   trusted, fresh synchronized status snapshot. If synchronization is missing
   or stale, stop rather than recording a successful login or issuing actions.
6. For logout, stop the action loop first, use only a verified session-exit
   path appropriate to the current prompt, confirm disconnection, and prevent
   the reconnect watchdog from immediately undoing the logout.
7. On relogin, begin again from connection detection. Do not reuse stale
   prompt/menu state, and do not send a multi-prompt macro without validating
   each transition.

These procedures belong in the shared source-backed corpus. Live game
observations remain agent-owned experience until captured through the trusted
synchronized-state path; a one-session prompt observation is not universal
doctrine.
