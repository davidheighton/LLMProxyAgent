# LLMProxyScripts

A source-only library of readable, unencrypted TWX scripts harvested from `Legacy/`. `Legacy/` remains unchanged; library copies have repaired include paths where needed. Scripts were not run and no live game action was taken.

## Structure

Scripts are grouped by task domain: `bots`, `combat-defense`, `includes`, `intel-data`, `login-session`, `navigation-grid`, `planet-citadel`, `ship-upgrades`, `trading-cashing`, and `utilities`. Placement is a filename/source-header classification aid, not a safety or server-compatibility guarantee.

All output files use the canonical `.ts` extension. Filenames use lowercase kebab-case; explicit source version markers are retained. Distinct implementations with the same basename get source-derived suffixes.

## Provenance, versions, and duplicates

`catalog.json` records each canonical output path, SHA-256, byte count, selected source path, and every merged equivalent source path and hash. Exact copies and token-equivalent copies are consolidated. The comparison preserves quoted literals and command-line boundaries while ignoring full-line comments, whitespace, and case differences in unquoted TWX tokens.

Some scripts originally referenced include files in section-local `include/` folders. Their implementations are now available under `includes/aliases/<source-family>/`. These derived copies preserve the original include basename (and therefore the namespace expected by TWX scripts); include directives point to those aliases. Alias hashes, byte counts, and source-family names are recorded separately in `catalog.json`.

For equivalent copies, an explicit higher version marker is preferred where available; otherwise the source priority recorded in the catalog selects the representative. Distinct token streams are retained rather than silently overwritten. Same-basename groups mark a preferred variant using that version/source priority and include normalized statement/token difference counts and complete provenance. Those source-level differences do not prove behavior differences; scripts were not executed to compare behavior.

The desktop Scripts and Quick menus use `catalog.json` to omit nonpreferred same-basename variants and display the selected variant under its logical name. The Quick menu excludes the `includes/` helper library from runnable menu entries. Source and include files remain on disk because retained scripts may depend on them; menu deduplication does not delete or rewrite files. Scripts not listed in the catalog remain available as local additions.

## Coverage and exclusions

The catalog covers every top-level tree under `Legacy/`, including candidate source counts by root. Readable `.ts` files, TWX-like code stored as `.txt`, and readable script backups are considered. Encrypted `.cts`/`.es`, unreadable `.inc`/`.ets`, documentation/help text, configs/secrets, runtime status/logs, generated/build artifacts, and unrelated language source/assets are excluded.

## Include repair status

The static include resolver found 834 of 850 include directives across the library. Sixteen references remain unresolved because their target files are absent from the readable `Legacy/` source corpus; no incompatible substitute was invented:

- `planet-citadel/ls-planet-creation-v10a.ts`: `STATS.ts`, `POP.ts`, `FURB.ts`, `LIB.ts`, and `CATALOG.ts`
- `planet-citadel/ph-pdrop.ts` and `planet-citadel/phs-pdrop.ts`: `9_comms.ts` and `9_cprompt.ts`
- `ship-upgrades/saar-furb.ts`: `_ckinc_CNsettings.ts`
- `trading-cashing/rin-team-sdt.ts`: `_ckinc_CNsettings.ts`, `_ckinc_getInfo.ts`, `_ckinc_getPlanetInfo.ts`, and `_ckinc_swathoff.ts`
- `trading-cashing/turbosst.ts`: `worldSST`
- `trading-cashing/xbot.ts`: `shipStats`
