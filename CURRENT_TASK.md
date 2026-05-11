# Current Task

> Live state of in-flight work. Update after every meaningful chunk so a fresh
> session can resume cold. Companion to `PLANNING.md` (spec) and
> `SESSION_NOTES.md` (longer-lived context).

---

## Active phase

**Polish — Color palette, spool columns UX, grid scaling (2026-05-07)**

All changes on `master`, deployed to YarnTwist project. Latest package tag: `v0.5.7` (Layer 1). Layer 2 fixes committed on master.

### Added this session (2026-05-11)

**Bug fixes (Layer 2 — `YarnTopSectionPanel`, `YarnMasterLevelExporter`)**
- Spool color picker popup now opens adjacent to the right-clicked swatch instead of far off-screen. Root cause: `GUIToScreenPoint` was called manually before passing to `PopupWindow.Show`, which also calls `GUIToScreenRect` internally — double-conversion. Fix: pass `swatchRect` (content-local) directly to `PopupWindow.Show`.
- Tunnel cell export now includes `Direction` field (`OutputDirection` was never written to `BottomConfigs`). Arrow box already exported Direction correctly; tunnel was missed.

---

### Added previous session (2026-05-07)

**Color palette expanded to full YATColorType enum (Layer 2 — data assets)**
- `YarnTwistPalette.asset` replaced 6-color placeholder with all 13 YATColorType entries (Blue→PurpleBright).
- Colors sourced directly from `StaticManager.asset` exact RGB values.
- `YarnColorMapping.asset` corrected: turquoise fixed 10→12, added magenta(5), greenlime(11), purplebright(13), bluedark(10).

**Inspector color swatch popup now shows all colors (Layer 2 — `YarnBoxCellDefinition`, `YarnArrowBoxCellDefinition`)**
- `InspectorPreferredHeight` bumped 60→70 (Box) and 56→70 (ArrowBox) so the popup allocates enough height for 2 swatch rows at the popup's 228px inner width.

**Spool columns: scrollable, dynamic height, cross-column moves (Layer 2 — `YarnTopSectionPanel`)**
- Columns no longer have a fixed visible-row count; scroll area fills all space above the grid.
- Scroll activates only when column content exceeds available height (edge case: 15-20+ entries).
- Per-row ← → buttons move individual spool entries between adjacent columns.
- Per-column ← → buttons at the bottom swap entire column lists with adjacent columns (scroll positions travel with the data).
- Color picker popup fixed to use `GUIUtility.GUIToScreenPoint` (was off-screen inside scroll group).

**Grid canvas: bottom-anchored, dynamic cell size (Layer 1 — `GridCanvasPanel`, `LevelEditorWindow`)**
- `GridCanvasPanel.RequiredHeight(canvasW, session)` computes the exact canvas height needed to show the grid without scrolling, based on width-only cell-size calculation.
- `LevelEditorWindow` now uses bottom-up layout: canvas is anchored to the bottom at `RequiredHeight`; top section fills all remaining space above it.
- Cell size scales dynamically (min 20px, max 48px) to fill available canvas width.

**Deployment**
- Tags: → `v0.5.7`
- YarnTwist `manifest.json` bumped to `#v0.5.7`.
- Layer 2 files copied: `YarnTopSectionPanel.cs`, `YarnBoxCellDefinition.cs`, `YarnArrowBoxCellDefinition.cs`.

---

### Added previous session (2026-05-06)

**CTRL+click multi-cell selection (Layer 1)**
- `IColoredCell.ColorId` changed from `{ get; }` to `{ get; set; }` to enable batch color writes.
- `LevelEditorSession.MultiSelection` — `HashSet<CellRef>` tracking selected cells. `ClearMultiSelection()` helper.
- `GridCanvasPanel`: CTRL+click toggles a cell in/out of `MultiSelection`; non-CTRL click clears `MultiSelection` then does normal paint/select. Escape clears `MultiSelection` first, then exits Move tool. Multi-selected cells draw a blue outline (`MultiSelOutline = new Color(0.30f, 0.65f, 1.00f, 0.90f)`).
- `MultiSelectPanel` (new) — shown in the right-column summary slot when `MultiSelection.Count > 0`. Lets the designer batch-change color (all `IColoredCell` in selection) and batch-change cell type (preserves color when both old+new implement `IColoredCell`). "Deselect All" button.
- `LevelEditorWindow`: holds `private readonly MultiSelectPanel _multiSelect`; swaps Summary ↔ MultiSelectPanel based on `MultiSelection.Count`.

**Game Profile persistence (Layer 1)**
- `LevelEditorWindow._profile` is now `[SerializeField]` — Unity's EditorWindow serialization automatically restores it across close/reopen and domain reloads. Removed EditorPrefs GUID fallback code entirely.

**Bug fixes**
- `ValidationTests.cs`: `TestColoredCell.ColorId` updated to `{ get; set; }` to satisfy the updated `IColoredCell` interface. This was a silent compile failure that blocked both new features from loading.
- `MultiSelectPanel.cs.meta` was not committed to git, causing CS0246 in the YarnTwist package cache. Fixed by committing the meta file and releasing `v0.5.3`.
- Hit-testing offset (`GridCanvasPanel.ScreenToCell`): detection zones were 2 px offset from visual cells due to missing `CellGap` subtraction. Fixed in `v0.5.5`.
- MultiSelectPanel scroll: body had no scroll view; content exceeded panel height. Fixed in `v0.5.5`.
- **Level naming/indexing corruption** (Layer 2): `YarnMasterLevelExporter` was blindly writing to the filename-derived key on every save, overwriting the slot that `Apply Order` had assigned to another level. Fixed: now scans existing `LevelConfigs` for a matching `levelId` and updates in-place; only falls back to filename key for new levels.

**Deployment**
- Tags: `v0.5.0` → `v0.5.1` → `v0.5.2` → `v0.5.3` (meta fix) → `v0.5.4` (IHideableCell) → `v0.5.5` (hit-test + scroll)
- YarnTwist `manifest.json` bumped to `#v0.5.5`.
- Level index fix: Layer 2 only — no new tag needed. Commit `0548633` on master.

**Maintenance**
- McpPlugin NuGet upgraded 6.1.1 → 6.1.3 in `Assets/Plugins/NuGet/`.

---

### Added previous session (2026-05-05)

**Spool columns UI/UX (Layer 2 — `YarnTopSectionPanel`)**
- Drag-and-drop reorder within each spool column via IMGUI drag handle.
- Per-row `✕ Del` button replaces the old global `−` button.
- Color picker filtered to only colors present on the grid; falls back to full palette when grid is empty.
- `+` button default color: previous spool's color → first grid color → first palette color → "pink".

**Layer 1 package (ColorPalette)**
- `ColorSwatchDrawer` and `ColorPickerPopup` extended with optional `allowedIds` filter (v0.4.1).

**Level Order tab (Layer 2 — `YarnLevelOrderPanel`)**
- Each entry now shows a `#N` index label before the level name.

---

## Open items / known gaps

### Manual steps still needed
- [ ] In YarnTwist project: create `YarnLevelOrderPanel` asset → assign `YarnMasterLevelExporter` → assign to `YarnTwistProfile.asset` Order Panel field
- [ ] Smoke test: export `level_005.json` → confirm key `"5"` appears in `level_config.json`
- [ ] Smoke test: ⇅ Order tab → drag levels → Apply Order → verify keys renumbered

### Low-priority Jira gaps (not implemented)
- Arrow box / tunnel target cell highlight
- Top View (Preview) mini-panel in right column
- Copy Level ID button in toolbar/summary

### Remaining open questions
- [ ] Confirm `YATColorType` int values with game team and lock `YarnColorMapping.asset`
- [ ] Grid offset `−3.5f` fix (when grid dimensions become variable)
- [ ] ArrowBox + Tunnel full prefab implementation in game
- [ ] Camera auto-fit wiring
- [ ] Win/lose flow restoration

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
| Jira UI gaps (Medium) | ✅ Complete |
| Brush template + color swatches | ✅ Complete |
| Export key fix + Level Order Manager | ✅ Complete |
| Save/Open directory memory | ✅ Complete |
| Spool columns drag-reorder + per-row delete + grid color filter | ✅ Complete |
| Multi-select (CTRL+click) + profile persistence | ✅ Complete |
| Phase 6 — Second-game onboarding | Deferred |

---

## Key design notes (carry forward)

- `GridData` is bottomUp: y=0 = bottom row in data, drawn at bottom of canvas
- First cell type in `GameProfile.CellTypes` must be the "empty" type (erase + fill)
- `PushUndoSnapshot()` must be called BEFORE mutations (not after)
- `CloneBrushTemplate()` is the correct way to get a fresh painted cell — never store `BrushTemplate` reference directly in the grid
- `DrawInspector` must use absolute `GUI.*` / `EditorGUI.*` calls — never `GUILayout.BeginArea` (causes render leaks outside panel bounds)
- `ColorSwatchDrawer.Draw()` is the reusable swatch picker for any `ColorPaletteAsset`
- `YarnColorBalanceRule` emits Info entries (color table) + Error entries (imbalances) — both appear in `ValidationPanel`
- `ScriptableObjectExporter.Export()` only works when .json path is inside `Assets/`
- `LevelAsset.ApplyJson` is internal — only callable from Editor assembly
- `GameProfile.CreateTopSection()` uses `MonoScript.GetClass()` + `Activator.CreateInstance`
- `GridCanvasPanel.HoverCell` is public — read by `LevelEditorWindow` status bar
- `LevelEditorSession.MarkDirty()` is the safe way to flag unsaved state from panels
- `YarnMasterLevelExporter` searches existing `LevelConfigs` for a matching `levelId` first; only falls back to the filename-derived key (`level_005.json` → `"5"`) for brand-new levels
- `YarnLevelOrderPanel.WriteToFile()` is shared by Apply Order and the remove callback
- `LevelEditorWindow._profile` is `[SerializeField]` — persists across domain reloads automatically (do NOT use EditorPrefs for this)
- `MultiSelectPanel` is shown instead of `SummaryPanel` when `session.MultiSelection.Count > 0`
- Every new `.cs` file in the UPM package **must** have a committed `.meta` file or Unity silently excludes it from compilation in consumers
- Layer 1 UPM changes deploy via git tag + consumer `manifest.json` bump (currently `v0.5.3`); Layer 2 changes must be manually copied to `YarnTwist/Assets/_YAT/Scripts/Editor/` (preserve subdirectory structure)
