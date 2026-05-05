# Current Task

> Live state of in-flight work. Update after every meaningful chunk so a fresh
> session can resume cold. Companion to `PLANNING.md` (spec) and
> `SESSION_NOTES.md` (longer-lived context).

---

## Active phase

**Post-ship polish — Level authoring UX improvements (2026-05-05)**

All changes on `master`, deployed to YarnTwist project. Latest package tag: `v0.4.1`.

### Added this session (2026-05-05)

**Spool columns UI/UX (Layer 2 — `YarnTopSectionPanel`)**
- Drag-and-drop reorder within each spool column via IMGUI drag handle (2×3 dot grid). `MoveVisual()` handles visual-index ↔ data-index conversion.
- Per-row `✕ Del` button replaces the old global `−` button; any specific spool can now be removed.
- Color picker (right-click swatch) now filtered to only colors present on the grid. Falls back to full palette when grid is empty.
- `+` button default color priority: previous spool's color → first color painted on the grid → first palette color → "pink".
- `GetFirstGridColor()` and `GetGridColors()` helpers scan `Grid.Cells` for `IColoredCell` and `YarnTunnelCell`.

**Level Order tab (Layer 2 — `YarnLevelOrderPanel`)**
- Each entry in the `ReorderableList` now shows a `#N` index label (30 px, `miniLabel`) before the level name.

**Layer 1 package (ColorPalette)**
- `ColorSwatchDrawer` and `ColorPickerPopup` extended with optional `allowedIds` filter (see CHANGELOG v0.4.1).

**Deployment**
- Layer 1 changes tagged `v0.4.1` and pushed to GitHub.
- YarnTwist `manifest.json` bumped from `#v0.4.0` → `#v0.4.1`.
- Layer 2 files copied to `YarnTwist/Assets/_YAT/Scripts/Editor/TopSection/YarnTopSectionPanel.cs` and `…/Editor/YarnLevelOrderPanel.cs`.

---

### Added previous session (2026-05-04)

**Export key fix (Layer 2 — `YarnMasterLevelExporter`)**
- Key into `level_config.json` is now derived from the **saved filename** (`level_005.json` → slot `"5"`) instead of `document.LevelId`. Fixes the bug where all levels overwrote slot 1 because the default `LevelId` is always `"level_001"`.
- Null or numberless filenames return `false` with a clear Console warning.
- Tests updated; two new edge-case tests added (`NullFilePath`, `FilenameWithNoNumber`).
- Added `public string OutputPath => _outputPath;` getter (used by `YarnLevelOrderPanel`).

**Level Order Manager tab (Layer 1 + Layer 2)**
- `EditorPanelAsset` (new, Layer 1): abstract `ScriptableObject` base implementing `IEditorPanel`.
- `GameProfile.OrderPanel` (new field): optional `EditorPanelAsset` for a game-provided order panel.
- `ToolbarPanel`: `⇅ Order` toggle button + `OnOrderToggle` event + `OrderMode` property.
- `LevelEditorWindow`: order mode renders the profile's `OrderPanel` full-window; falls back to a helpful message if none is configured.
- `YarnLevelOrderPanel` (new, Layer 2): reads `level_config.json`, shows a draggable `ReorderableList`, supports reorder (Apply Order) and per-level deletion (confirmation dialog). Auto-renumbers keys after both operations.

**Remember last save directory (Layer 1 — `LevelEditorWindow`)**
- Save As and Open dialogs now start in the last-used folder (`EditorPrefs` key `Hoppa.LevelEditor.LastSaveDir`).
- Falls back to `Application.dataPath` on first use.

**Deployment**
- Layer 1 changes tagged as `v0.2.0` then `v0.3.0` and pushed to GitHub.
- Yarn Sort `manifest.json` updated to `#v0.3.0`.
- Layer 2 files (`YarnMasterLevelExporter.cs`, `YarnLevelOrderPanel.cs`) copied to `Assets/_YAT/Scripts/Editor/` in the Yarn Sort project.

---

## Last session work (2026-04-28)

### Export button + integration docs
- `ToolbarPanel`: `OnExport` event + "Export ▸" button
- `LevelEditorWindow`: `HandleExport()` — runs all exporters, prompts to save if dirty, shows result dialog
- `docs/integration/new-game-exporter.md`: step-by-step guide for integrating a future game

---

## Open items / known gaps

### Manual steps still needed
- [ ] In YarnTwist project: create `YarnLevelOrderPanel` asset → assign `YarnMasterLevelExporter` → assign to `YarnTwistProfile.asset` Order Panel field
- [ ] Smoke test: export `level_005.json` → confirm key `"5"` appears in `level_config.json` (not `"1"`)
- [ ] Smoke test: ⇅ Order tab → drag levels → Apply Order → verify keys renumbered in `level_config.json`

### Low-priority Jira gaps (not implemented)
- Arrow box / tunnel target cell highlight (highlight destination cell when selected)
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
- `LevelEditorSession.MarkDirty()` is the safe way to flag unsaved state from panels (replaces `PushUndoSnapshot()` for non-grid mutations like Notes edits)
- `YarnMasterLevelExporter` key = trailing digits of the **filename** (`level_005.json` → `"5"`), not `document.LevelId` — save the file with the right name before exporting
- `YarnLevelOrderPanel.WriteToFile()` is shared by Apply Order and the remove callback — both paths renumber keys 1, 2, 3… sequentially after the operation
- Layer 1 UPM changes deploy via git tag + consumer `manifest.json` bump (currently `v0.4.1`); Layer 2 changes (`Assets/YarnTwist/Editor/`) must be manually copied to their matching paths in `YarnTwist/Assets/_YAT/Scripts/Editor/` (preserve subdirectory structure — e.g. `TopSection/YarnTopSectionPanel.cs`)
