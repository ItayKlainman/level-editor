# Current Task

> Live state of in-flight work. Update after every meaningful chunk so a fresh
> session can resume cold. Companion to `PLANNING.md` (spec) and
> `SESSION_NOTES.md` (longer-lived context).

---

## Active phase

**Yarn Twist test data** — *COMPLETE — awaiting user review*

All `Assets/YarnTwist/` files created as a team-presentation demo of the framework.

### What was built
- **Runtime types** (7 files): `YarnDirection`, `YarnEmptyCell`, `YarnWallCell`, `YarnBoxCell`, `YarnTunnelCell`, `YarnArrowBoxCell`, `YarnTopSectionData`
- **Editor cell definitions** (5 files): Empty, Wall, Box, Tunnel, ArrowBox — all with `DrawCell` + `DrawInspector`
- **TopSection panel**: `YarnTopSectionPanel` — 4-column spool editor with color picker, hidden toggle, +/− buttons
- **Validation rules** (3 files): `YarnColorBalanceRule`, `YarnArrowBoxTargetRule`, `YarnTunnelOutputRule`
- **Exporter**: `YarnLevelAsset` + `YarnSortExporter`
- **Level data** (2 files): `YT_001.json` (tutorial, 2-color, balanced) and `YT_025.json` (intermediate, 6-color, all features, balanced)
- Assembly definitions: `Hoppa.YarnTwist.Runtime` + `Hoppa.YarnTwist.Editor`

### Framework gaps found during Yarn Twist work
1. **`LevelEditorSession.IsDirty` private setter** — `YarnTopSectionPanel` cannot mark the session dirty after TopSection mutations. Workaround: call `PushUndoSnapshot()` before mutations. Real fix: expose `MarkDirty()` on session, or make setter internal.
2. **`ColorBalanceRule` only reads grid cells** — Generic rule has no awareness of TopSection. Yarn Twist needs a custom `YarnColorBalanceRule` that reads both `ctx.Grid.Cells` and `ctx.Document.TopSection`. Consider adding a `ValidationContext.GameData` accessor or documenting that balance rules are always game-specific.

### Still needed (manual Inspector setup)
- Create `YarnTwistPalette.asset` (ColorPaletteAsset) with 6 colors: pink, blue, teal, green, yellow, purple
- Create `YarnTwistProfile.asset` (GameProfile) pointing at all 5 cell definitions + `YarnTopSectionPanel`
- Create 5 `CellTypeDefinition.asset` instances (one per cell type) with palette wired
- Create 3 validation rule `.asset` instances: ColorBalance, ArrowBoxTarget, TunnelOutput
- Create `YarnSortExporter.asset`
- All `#TEMP` prefix values (author fields, etc.) to be replaced before final demo

---

**Phase 4.5 — UI/UX polish** — *COMPLETE — awaiting user approval to start Phase 5*

### Phase 4.5 changes
- [x] **3-column layout** — Palette (left 160px) | Canvas+TopSection (centre) | Validation+Summary (right 230px)
- [x] **Status bar** — bottom strip showing: `● Unsaved`, cursor `(x, y)`, `schema: {version}`
- [x] **GridCanvasPanel** — exposed `HoverCell` property (read by status bar)
- [x] **PalettePanel** — redesigned as list rows: swatch preview + full display name + blue accent on active item
- [x] **ToolbarPanel** — tooltips on every button, level name label shown after Test Play
- [x] **CellTypeDefinition** — `[Tooltip]` with examples on all 4 `[SerializeField]` fields
- [x] **GameProfile** — `[Tooltip]` with examples on all 8 `[SerializeField]` fields
- [x] **ColorPaletteAsset / ColorEntry** — `[Tooltip]` with examples on all public fields

### Layout structure (matches PLANNING.md §3 mock)
```
┌─ Toolbar (28px) ──────────────────────────────────────────────────┐
│  New  Open  Save  Save As  |  Undo  Redo  |  ▶ Test     LevelName │
├────────────┬─────────────────────────────┬────────────────────────┤
│  Palette   │  Top Section (if any)        │  Validation (60%)      │
│  160px     │  ─────────────────────────── │                        │
│  list rows │  Grid Canvas                 │  Summary (40%)         │
│  w/ swatch │                             │                        │
├────────────┴─────────────────────────────┴────────────────────────┤
│  Status: ● Unsaved  (x, y)  schema: demo.v1               (20px)  │
└───────────────────────────────────────────────────────────────────┘
```

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
| **Phase 4.5 — UI/UX polish** | **✅ Complete** |
| Phase 5 — Yarn Sort Layer 2 (separate game repo) | Not started |
| Phase 6 — Second-game onboarding | Deferred |

## Phase 5 scope (from PLANNING.md §5)
- Separate game repo that depends on `com.hoppa.leveleditor.core`
- Yarn Sort cell types (`YarnCell`, `EmptyCell`, `TunnelCell`, `ArrowBoxCell`)
- `YarnSortTopSectionPanel` (spool columns above grid)
- `YarnLevelAsset` + `YarnSortExporter`
- Sort-ID prefix (`YRN` vs `YS`) — deferred decision
- Validation rules for Yarn Sort constraints
- GameProfile for Yarn Sort registered in game repo

## Key design notes (carry forward)
- Write/Edit tools work directly for all files — gateguard hook removed 2026-04-24
- GridData is bottomUp: y=0 = bottom row in data, drawn at bottom of canvas
- `ValidationPanel.OnGUI` takes `ValidationReport` (not session) — existing API, keep as-is
- `DemoBoxCellDefinition._palette` is assigned via `DemoBoxCellDef.asset` in Inspector
- First cell type in GameProfile.CellTypes must be the "empty" type (used for erase + fill)
- `LevelEditorSession.CreateEmpty` fills all cells with CellTypes[0].CreateDefault()
- `AutoActivateCellType()` in LevelEditorWindow auto-selects CellTypes[1] (first non-empty)
- `PushUndoSnapshot()` must be called BEFORE mutations (not after)
- Undo/redo stack is cleared on new paint after an undo (standard behavior)
- `ScriptableObjectExporter.Export()` only works when .json path is inside Assets/ folder
- `LevelAsset.ApplyJson` is internal — only callable from Editor assembly
- `GameProfile.CreateTopSection()` uses MonoScript.GetClass() + Activator.CreateInstance
- `GridCanvasPanel.HoverCell` is public — read by LevelEditorWindow status bar

## Known open questions
- [ ] Yarn Sort ID prefix (`YRN` vs `YS`) — deferred to Phase 5
- [ ] CoderGamester MCP entry in settings.json — can be removed or left (it doesn't connect)
