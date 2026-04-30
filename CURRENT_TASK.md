# Current Task

> Live state of in-flight work. Update after every meaningful chunk so a fresh
> session can resume cold. Companion to `PLANNING.md` (spec) and
> `SESSION_NOTES.md` (longer-lived context).

---

## Active phase

**Post-phase polish — Export button + integration docs (2026-04-28)**

Branch `feat/phase5-data-pipeline` is pushed to GitHub and ready for review/merge.
Open a PR at: https://github.com/ItayKlainman/level-editor/pull/new/feat/phase5-data-pipeline

### Added this session
- `ToolbarPanel`: new `OnExport` event + "Export ▸" button (64px, in the Save group, disabled when no session)
- `LevelEditorWindow`: `HandleExport()` — runs all `_profile.Exporters` on demand; prompts to Save first if dirty; shows per-exporter success/error dialog; calls `AssetDatabase.Refresh()` after
- `GameProfile`: updated `_exporters` tooltip to document the Export button and the swap-in pattern
- `docs/integration/new-game-exporter.md`: step-by-step guide for integrating any future game

---

## Last session work (2026-04-26)

### Jira UI gaps implemented

**Gap 1 — Cell Inspector panel (MEDIUM)**
- `LevelEditorSession.MarkDirty()` exposed (was private-setter only)
- `CellInspectorPanel` (new): renders `DrawInspector` for the selected grid cell; sits between Validation (50%) and Summary in the right column (130px fixed)
- Right column layout: Validation (50%) → Cell Inspector (130px) → Summary (remaining)

**Gap 2 — Per-color validation breakdown (MEDIUM)**
- `YarnColorBalanceRule` now emits `ValidationSeverity.Info` entries per color (always shown as a balance table); Error entries still fire for imbalanced colors
- `ValidationPanel` already rendered Info with blue rows — no panel change needed

**Gap 3 — Notes field + improved Summary (MEDIUM)**
- `LevelDocument.LevelMetadata.Notes` (string, serialized as `"notes"`)
- `SummaryPanel` shows `DisplayName` from registry instead of raw TypeId for cell counts; Notes textarea pinned to bottom (editable, calls `MarkDirty`)

### Brush template system
- `LevelEditorSession.BrushTemplate` — pre-configured cell template for next paint stroke
- `LevelEditorSession.CloneBrushTemplate()` — round-trips template through `JsonLevelSerializer` to produce a fresh instance per painted cell
- `PalettePanel` split into cell list (top, scrollable) + BRUSH section (96px bottom); clicking a cell type resets `BrushTemplate`; `DrawInspector` runs on the brush so color/direction/queue are configurable before painting
- `GridCanvasPanel.PlaceAt` uses `CloneBrushTemplate()` instead of `CreateDefault()`

### Color swatch pickers
- `ColorSwatchDrawer` (new, core Editor): reusable wrapping swatch grid with selected/hover borders and tooltip
- `YarnBoxCellDefinition.DrawInspector`: swatches + Hidden toggle
- `YarnArrowBoxCellDefinition.DrawInspector`: swatches + direction EnumPopup
- `YarnTunnelCellDefinition.DrawInspector`: rewritten with absolute rects (removes `GUILayout.BeginArea` that caused right-panel text overflow); queue entries show click-to-cycle color swatch
- `YarnTopSectionPanel`: spool rows show swatch strip + hidden toggle; `PreferredHeight` is now dynamic (was hardcoded for 9 spools)

---

## Open items / known gaps

### Manual steps still needed
- [x] `_schemaId` is already `yarn-twist` in asset — was stale note
- [x] `YarnTwistPalette` already assigned on `YarnTunnelCellDef.asset` — was stale note
- [ ] Smoke test: open Level Editor → YarnTwistProfile → paint boxes + spools → Save → verify `level_config.json` updated in YarnTwist project
- [ ] Merge `feat/phase5-data-pipeline` → `master` and push
- [ ] Tag release: `git tag v0.1.0 && git push origin v0.1.0`

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
