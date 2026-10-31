# TradeWars World Engine Migration Plan

This plan records the legacy evidence and the boundary used by the new engine.
The new projects do not reference TWXProxy assemblies; the old system's module
lifecycle, GUI, and binary persistence are intentionally left behind.

| Concept | Existing implementation | Dependencies / reuse decision | Modern equivalent |
|---|---|---|---|
| Universe | `TWX30-main/Source/Core/Database.cs`: `DataHeader` stores sector count and landmark IDs; `ModDatabase` owns the active data. There is no standalone universe aggregate. | The header fields are reusable as imported facts. The TWX module, global module, and file lifecycle are coupled to the legacy proxy and should not be reused. | `Universe` stores sector count, StarDock sector, and update time. |
| Sector | `Core.cs`: `Sector` stores six warps and exploration state. `Database.cs`: `SectorData` adds port, hazards, traders, ships, planets, and reverse warps; `_sectors` is a concurrent dictionary. | Preserve the distinction between unknown, warp-only (`ExploreType.Calc`), and fully explored (`ExploreType.Yes`). Replace mutable inheritance and in-memory dictionaries with typed records and SQLite rows. | `Sector` with `ExplorationStatus`, `WarpDataKnown`, observations, and an explicit warp list. |
| Port | `Database.cs`: `Port` stores class, dead/build state, buy flags, amounts, percentages, and update time. `ProductType` distinguishes Fuel Ore, Organics, and Equipment. | Product profile semantics are reusable. The mutable dictionaries are replaced with a typed product map. | `Port` and `PortProduct`, persisted with the sector observation; port class is indexed for StarDock queries. |
| Planet | `Database.cs`: ID-keyed `_planets`, `Planet.LastSector`, observed order, owner, level, shield state, fighters, and commodities; `GetPlanetsInSector` and reconciliation methods handle mobile/provisional planets. | Preserve registry ID and last-observed location. Do not port reconciliation side effects until live observations and ordering requirements are captured in tests. | Typed `Planet` attached to its last-seen `Sector`; extend with explicit reconciliation if live data requires it. |
| Trader | `Database.cs`: `Trader` is stored in each `SectorData` with display label, name, ship type/name, and fighters. | The fields are reusable; sector nesting is a legacy storage choice. | Typed `Trader` observation attached to a sector, with optional corporation ID. |
| Corporation | No first-class `Corporation` data model or indexed corporation store was found in the inspected `Core/Database.cs` database layer. | Do not infer or fabricate fields from UI text. The new type is deliberately minimal and should be expanded only when a verified source/parser supplies attributes. | `Corporation` with ID, name, members, and observation time. |
| Ship | `Database.cs`: `Ship` is nested in `SectorData` with owner, type, and fighters. | Preserve these observation fields and decouple them from UI/proxy state. | Typed `Ship` observation attached to a sector. |
| Navigation | `Database.cs`: `CalculateShortestPath`, `CalculateBidirectionalShortestPath`, `GetAllCoursesFrom`, and nearest-warp helpers operate over the sector warp arrays and cached graph. | Reuse directed warp semantics and tested route outcomes, not the `ModDatabase` graph caches, thread-static scratch buffers, or global state. | `NavigationService` runs BFS over the persisted directed warp graph; paths include both endpoints and honor avoid lists. |
| Pathfinding | `CalculateBidirectionalShortestPath` documents shortest-hop guarantees on the directed warp graph; tie-breaking may differ from the legacy Pascal implementation. | The new implementation favors a simple auditable queue-based BFS for unweighted hops. Its deterministic neighbor ordering is explicit; it does not promise legacy tie selection. | `NavigationPath`, shortest route and nearest-StarDock queries with offline tests. |
| CIM data | `AutoRecorder.cs` receives live lines through a stateful parser. `ParsePortCIMLine` documents sector + three amount/percent pairs with `-` buy markers; `ParseWarpCIMLine` accepts a sector and up to six destinations. | The line formats and validation rules are reusable. Live prompt state, global correction logging, and automatic side effects are not. Extract parsing into pure functions and apply observations through the world-store boundary. | `CimImportService` parses and validates port/warp rows, persists observations, retains explored status, and reports accepted/rejected lines. |

## Implemented boundary

- `IWorldStateStore` exposes universe metadata, sector paging/lookups, bulk
  updates, warp graph, StarDock candidates, and corporation persistence.
- `SqliteWorldStateStore` stores universe metadata, sector observations, directed
  warp edges, port lookup fields, and corporation records in SQLite. Sector
  details remain strongly typed and are serialized as a versionable JSON payload.
- `NavigationService` implements shortest directed paths, avoids listed sectors,
  selects nearest known StarDock by hop count, and returns adjacent sectors not
  fully explored.
- `CimImportService` extracts the verified port and warp row formats from the
  legacy parser into pure line parsers and applies validated rows to the store.

## Deliberate deferrals

- Importing the full binary TWX26 file is not part of this first engine slice.
  `ModDatabase.LoadFromTWX26` is available as a reference, but its binary codec
  is coupled to legacy header and sector structures; add a separate converter
  with fixtures before accepting those files as authoritative.
- Live prompt/telnet parsing, sector correction/reconciliation, reverse-warp
  consistency repair, and dynamic universe updates remain outside the offline
  world engine. Do not connect or issue gameplay commands from this service.
- Corporation details are minimal because the inspected database has no
  authoritative first-class corporation record.
