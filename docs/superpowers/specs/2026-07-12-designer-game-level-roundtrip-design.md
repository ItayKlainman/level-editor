# Designer game-level round-trip (open + edit + save game levels)

**Date:** 2026-07-12
**Goal:** Let Bus Buddies designers, working only in the BB game project, open the game's
real levels in the Level Editor, touch them up, and save them straight back — with zero
knowledge of the editor's internal format. Fixes the "Open Failed" error they hit today.

## Problem
- Game levels live at `Assets/_BUB/Resources/Configs/Levels/level_N.json` in the **game
  schema**: `{SlotsAmount, Width, Height, BusColumnConfigs[BusConfigs[ColorType,Capacity,BusType?]], PixelColors[]}`.
- The editor's **Open** loads its own `LevelDocument` JSON; the game schema fails to parse
  → "Open Failed".
- A `BusBuddiesGameLevelImporter` exists but (a) isn't wired to Open and (b) rebuilds only
  the grid, not the bus queue (`TopSection`) → buses would be empty/uneditable.
- The `BusBuddiesGameLevelExporter` is wired to **Export ▸** but writes to a staging dir.
- **Hazard:** opening a game file then hitting **Save** would overwrite it with the
  editor's internal `LevelDocument` format and break the game level.

## Approved decisions
- **Game-in / game-out:** opening a game level makes the game format the working format;
  Save writes game JSON back to the same `level_N.json`. No internal-format file.
- **Auto-detect on Open:** designers use the normal Open button; the editor detects the
  game schema and imports it (picture **+** editable buses).

## Design

### Layer 1 (package — generic, backward-compatible)
1. **`LevelImporterAsset`** (abstract `ScriptableObject`, sibling of `LevelExporterAsset`):
   - `string Name`
   - `bool CanImport(string json)` — cheap schema sniff.
   - `LevelDocument Import(string json, CellTypeRegistry registry)`.
2. **`GameProfile._importers`** (`List<LevelImporterAsset>`) + `IReadOnlyList<LevelImporterAsset> Importers`.
   Null/empty ⇒ current behavior (YAK/YarnTwist unaffected).
3. **`LevelEditorWindow.LoadFromPath`** — auto-detect: read json; if a profile importer
   `CanImport(json)`, use it and mark the session **foreign-format**; else load native
   `LevelDocument` as today.
4. **`LevelEditorWindow.SaveToPath`** — if the level was opened **foreign-format**, SKIP the
   native `JsonExporter` write (never stamp internal format onto the game file) and run the
   profile **exporters** only. Native levels: unchanged (native write + exporters).
   Foreign-format flag lives on the window, reset on New/Open.

### Layer 2 (Bus Buddies)
5. **Enhance `BusBuddiesGameLevelImporter.Import`** to also rebuild `BusQueueData`
   (columns → buses, preserving column structure) and set `doc.TopSection`, so imported
   levels open with the editable bus queue.
6. **`BusBuddiesGameLevelImporterAsset : LevelImporterAsset`** — `CanImport` = json has
   `PixelColors` + `Width` + `BusColumnConfigs`; `Import` delegates to the static importer.
7. Wire it into `BusBuddiesProfile._importers`.
8. **Exporter output dir:** in the **BB game project** mirror, set the exporter asset's
   `_outputDir` to `Assets/_BUB/Resources/Configs/Levels` so Save/Export round-trips into
   the game. (editor-core keeps its staging dir so its tests never touch a game folder.)

### Guide
9. Add a **"Designer workflow (Bus Buddies)"** section to
   `Documentation~/level-editor-guide.md` (open → touch up → save back), shown by ? Guide.

### Deploy
10. Package `0.8.1 → 0.8.2`, tag `v0.8.2`, push; re-mirror the BB Layer-2 changes into the
    BB game project; set the BB exporter `_outputDir`; re-pin BB manifest to `#v0.8.2`.

## Tests (editor-core, TDD)
- Importer rebuilds grid **and** bus queue: import a known game JSON → assert grid pixels +
  `TopSection` columns/buses (colors, capacities, hidden, column grouping) match.
- `CanImport` true on game schema, false on a native `LevelDocument`.
- Round-trip: game JSON → import → export → semantically equal (PixelColors, buses, dims).
- `GameProfile.Importers` empty by default (back-comaptibility for other profiles).

## Out of scope / parked
Image-prompt kawaii iteration, unified Image Generation window, "Run Batch (20)" removal,
"0 colors"/"OFF-TARGET" label fixes, batch perf fix. (Resume after onboarding is unblocked.)
