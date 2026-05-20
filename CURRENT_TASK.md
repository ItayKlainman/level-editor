# Current Task

> Live state of in-flight work. Update after every meaningful chunk so a fresh
> session can resume cold. Companion to `PLANNING.md` (spec) and
> `SESSION_NOTES.md` (longer-lived context).

---

## Active phase

**YAK Layer 2 onboarding (2026-05-18)**

YarnTwist's editor work is complete for now (tag `v0.5.14`). Now bringing up a
sibling Layer 2 for the studio's next game, codenamed **YAK** — a pixel-art
wool-grid puzzle. The framework already supports multi-profile coexistence
(`LevelEditorWindow.DrawProfileSelector`), so YarnTwist stays untouched in
`Assets/YarnTwist/` and YAK lives alongside it at `Assets/YAK/`. Switching
games = drag a different `GameProfile` asset into the editor window field.

### Added this session (2026-05-18)

**YAK Layer 2 — initial scaffold (`Assets/YAK/`)**

Runtime (`Hoppa.YAK.Runtime`):
- `YAKEmptyCell` (`ICellData`, `yak.empty`) — no-wool sentinel.
- `YAKWoolCell` (`IColoredCell`, `yak.wool`) — holds `ColorId`.
- `YAKSpoolEntry` (color + capacity + hidden), `YAKSpoolColumn`, `YAKTopSectionData`.

Editor (`Hoppa.YAK.Editor`):
- `YAKEmptyCellDefinition` — checker pattern in canvas, empty inspector.
- `YAKWoolCellDefinition` — palette-coloured fill, `ColorSwatchDrawer` in inspector.
- `YAKSpoolSectionPanel` — adapted from `YarnTopSectionPanel`. Variable column
  count (2–5) via `+ / −` header buttons; per-spool capacity int field next to
  the color swatch; per-spool `Hidden` toggle; drag-reorder + per-row delete +
  cross-column move + column swap (same UX as Yarn).
- `YAKColorBalanceRule` — counts wool tiles per color vs spool capacity sum per
  color (including hidden); Info row when balanced, Error when not.
- `YAKLevelExporter` — writes one YAK `LevelConfig` JSON per level. Color values
  serialised as ints via `YAKColorMapping`. `PixelColors` is a flat int array
  of length `Width*Height` in row-major bottom-up (`index = y*Width + x`), with
  `0` = empty cell. Configurable `_outputDir` field on the asset (left empty
  until the game repo is connected). Adds Columns / Spools / Wool to the
  Summary panel, plus a Conveyor int field stored in `doc.GameData["conveyorCount"]`.
- `YAKLevelImporter` — parses a YAK LevelConfig JSON back into a `LevelDocument`
  using reverse-lookup on `YAKColorMapping`. Validates `PixelColors.Length == Width*Height`.
- `YAKImportMenu` — `Tools › Hoppa Level Editor › Import YAK Level…`. Picks a
  source YAK JSON, asks where to save the editor's working file, writes the
  `LevelDocument` JSON, and auto-opens it in the Level Editor window.

Framework (Layer 1) additions:
- `NewLevelDialog` (modal) — pops on **New Level** with Width/Height fields
  pre-filled from profile defaults. Lets YAK levels start at any size (30×30,
  40×40, …) without changing the profile.
- `LevelEditorWindow.Profile` — public getter so Layer 2 tooling can read the
  active profile.
- `LevelEditorWindow.OpenLevelFile(path)` — public hook used by `YAKImportMenu`
  to auto-load the freshly imported file.
- `LevelEditorSession.CreateEmpty(profile, width, height)` — overload accepting
  per-level dimensions.

Data assets (`Assets/YAK/Data/Config/`):
- `YAKProfile.asset` — schema `yak`, default grid 30×30, wires empty+wool cell
  defs, color balance rule, exporter, and the spool section panel script. No
  order panel (one file per level, no master config to renumber).
- `YAKPalette.asset` — 13 colors copied from `YarnTwistPalette` as starting set.
- `YAKEmptyCellDef.asset`, `YAKWoolCellDef.asset`, `YAKColorBalanceRule.asset`.
- `YAKColorMapping.asset` — 13 entries (`blue→1`, `cyan→2`, …, `purplebright→13`).
  Reserves `0` for empty cells.
- `YAKLevelExporter.asset` — color mapping wired; output dir empty pending game repo.

---

## Open items / known gaps

### Manual verification still pending (must run in Unity)
- [ ] Recompile scripts; confirm no compile errors in `Hoppa.YAK.Runtime` /
      `Hoppa.YAK.Editor` / `Hoppa.LevelEditor.Core.Editor`.
- [ ] Switch active profile to `YAKProfile` in `LevelEditorWindow`; confirm
      grid renders, palette shows Empty + Wool, spool section panel renders
      with two empty columns.
- [ ] Click **New Level** → confirm the size dialog pops and 30×30 default is
      pre-filled; create a level and paint a few wool cells.
- [ ] Add spool columns / spools / capacities; confirm the Color Balance rule
      shows one row per color and turns Error on mismatch.
- [ ] Save level (LevelDocument JSON). Open it again via Open → round-trip works.
- [ ] Set `_outputDir` on `YAKLevelExporter.asset` to a real path; click Export;
      confirm `level_NNN.json` is written with int color values + correct
      `Width`/`Height`/`ConveyorCount`/`SpoolColumnConfigs`/`PixelColors`.
- [ ] **Regression** — switch profile back to `YarnTwistProfile`; confirm Yarn
      flows (open existing `YT_*.json`, edit, save, export) still work.
- [ ] **Importer** — once Eliran shares his `level_001.json`, run
      `Tools › Hoppa Level Editor › Import YAK Level…` and round-trip it.

### Decisions still pending input
- [ ] Game repo output path for `YAKLevelExporter._outputDir`
- [ ] Confirm `YAKColorType` int values once the game's enum is settled (current
      mapping copied from YarnTwist's order: blue=1 … purplebright=13).
- [ ] Whether YAK needs an Order panel later (currently filesystem-driven order;
      can add later if a master config is introduced).

---

## Done (completed phases)

| Phase | Status |
|-------|--------|
| Planning | ✅ Complete |
| Phase 0 — Package skeleton | ✅ Complete |
| Phase 1 — Core data + serialization | ✅ Complete |
| Phase 2 — Validation engine | ✅ Complete |
| Phase 3 — EditorWindow + grid canvas | ✅ Complete |
| Phase 4 — Top-section abstraction + export | ✅ Complete |
| Phase 4.5 — UI/UX polish | ✅ Complete |
| Phase 5 — Data pipeline integration | ✅ Complete |
| YarnTwist Jira UI gaps | ✅ Complete |
| YarnTwist brush template + color swatches | ✅ Complete |
| YarnTwist export key fix + Level Order Manager | ✅ Complete |
| YarnTwist save/open directory memory | ✅ Complete |
| YarnTwist spool columns drag-reorder | ✅ Complete |
| YarnTwist multi-select + profile persistence | ✅ Complete |
| YarnTwist summary panel enhancements + coin reward (v0.5.14) | ✅ Complete |
| Phase 6 — Second-game onboarding | 🔵 **In progress (YAK)** |

---

## Key design notes (carry forward)

- `GridData` is bottomUp: y=0 = bottom row in data, drawn at bottom of canvas.
  YAK's `PixelColors` follows the same convention so export needs no inversion.
- YAK reserves `0` in its color mapping for the empty/no-wool sentinel; wool
  colors start at `1`.
- First cell type in `GameProfile.CellTypes` must be the "empty" type (erase + fill).
- `PushUndoSnapshot()` must be called BEFORE mutations (not after).
- `CloneBrushTemplate()` is the correct way to get a fresh painted cell — never
  store `BrushTemplate` reference directly in the grid.
- `DrawInspector` must use absolute `GUI.*` / `EditorGUI.*` calls — never
  `GUILayout.BeginArea` (causes render leaks outside panel bounds).
- `LevelEditorSession.MarkDirty()` is the safe way to flag unsaved state from panels.
- Game switching = drag a different `GameProfile` asset into the
  `LevelEditorWindow`'s profile selector. The selector remembers the choice
  per project via `EditorPrefs` (`Hoppa.LevelEditor.ProfileGuid`).
- Every new `.cs` file in the package or under `Assets/` **must** have a
  committed `.meta` file with a stable GUID, or Unity silently excludes it from
  compilation in package consumers.
- Layer 1 UPM changes deploy via git tag + consumer `manifest.json` bump
  (currently `v0.5.16`). Tag history:
  - `v0.5.15` — `NewLevelDialog`, public `Profile`/`OpenLevelFile`,
    `CreateEmpty(profile, w, h)` overload, framework bottom-section support,
    YAK single-source color refactor.
  - `v0.5.16` — `GridCanvasPanel` mouse hit-test now follows the scroll
    offset; previously, once the canvas scrolled (e.g. when the YAK bottom
    spool panel shrunk the canvas below the grid's required height), the
    bottom `_scroll.y` pixels of the visible viewport silently rejected
    clicks. Layer 2: `YAKProfile.asset` rewired from the legacy
    `YAKPalette.asset` to `YAKStaticManagerColorSource.asset` (single
    source of truth per `design_yak_colors_single_source.md`) — fixes the
    empty color picker popup when right-clicking spool swatches.
