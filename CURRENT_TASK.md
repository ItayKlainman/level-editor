# Current Task

> Live state of in-flight work. Update after every meaningful chunk so a fresh
> session can resume cold. Companion to `PLANNING.md` (spec) and
> `SESSION_NOTES.md` (longer-lived context).

---

## Active phase

**Phase 3 — EditorWindow + grid canvas** — *not started, awaiting user approval*

Spec: PLANNING.md §5 → "Phase 3 — EditorWindow + grid canvas".

### What Phase 3 will produce
- `LevelEditorSession.cs` (Editor) — in-memory state: current LevelDocument, dirty flag, validation cache, undo stack reference.
- `IEditorPanel.cs` (Editor) — `OnGUI(Rect rect, LevelEditorSession session)` contract.
- `LevelEditorWindow.cs` (Editor) — top-level `EditorWindow`; carves rects, delegates to panels.
- `PalettePanel.cs` — grouped cell-type browser (IMGUI).
- `GridCanvasPanel.cs` — IMGUI grid; left-click place, right-click erase, R-rotate, Delete, drag-paint, Shift+drag select.
- `ToolbarPanel.cs` — New/Open/Save/Save As/Test Play buttons.
- `SummaryPanel.cs` — level stats (cell counts, schema version, etc.)
- Wire `ValidationPanel` to `IEditorPanel` now that `LevelEditorSession` exists.
- `GridUndoScope.cs` (Editor/Undo) — wraps `Undo.RegisterCompleteObjectUndo`; groups drag-paint into one undo step.
- **Demo:** using the bundled DemoColorGridGame sample, author a 3-color grid puzzle, save, reopen, continue editing.

### Phase 3 exit protocol
1. `LevelEditorWindow` opens via `Window → Level Editor` menu item.
2. With DemoColorGridGame profile selected: palette populates, grid renders, left-click places a cell, right-click erases, Ctrl+Z undoes.
3. Save produces a `.json` file; opening it re-hydrates the session.
4. **Stop. Report. Wait for explicit user approval before starting Phase 4.**

---

## Done this session
- Phase 1 complete — 21 source files (Runtime data model, serialization stack, Editor SO types).
- Phase 2 complete — validation engine:
  - `IColoredCell` + `IColorPalette` Runtime interfaces
  - `ValidationRuleBase` abstract SO base
  - 3 data-rule templates: `PaletteColorsExistRule`, `GridNonEmptyRule`, `ColorBalanceRule`
  - `ValidationRuleRegistry` (Editor)
  - `ValidationPanel` IMGUI component
  - `ColorPaletteAsset` implements `IColorPalette`
  - `GameProfile` updated with rules list + `BuildValidationRegistry()`
  - `ValidationTests.cs` — 6 NUnit tests covering all 3 rules (pass + fail cases)

## Known compile-time notes
- Runtime asmdef: `overrideReferences: false` — Newtonsoft auto-referenced via package `autoReferenced: true`. If Unity complains, add `"Unity.Newtonsoft.Json"` to references.
- Test asmdefs: `overrideReferences: true` — `Newtonsoft.Json.dll` in precompiledReferences.
- `ValidationPanel` uses a switch expression (C# 8+) and `is not` pattern (C# 9+) — both fine in Unity 2022.3.

## Blocked / open questions
- [ ] Yarn Sort ID prefix (`YRN` vs `YS`) — deferred to Phase 5.
- [ ] DemoColorGridGame sample content — needed for Phase 3 demo. Should be a minimal 2-3 color game (2 cell types: colored box + empty). Create alongside EditorWindow work.

---

## Completed phases
| Phase | Status |
|-------|--------|
| Planning | ✅ Complete |
| Phase 0 — Package skeleton | ✅ Complete |
| Phase 1 — Core data + serialization | ✅ Complete |
| Phase 2 — Validation engine | ✅ Complete |
| **Phase 3 — EditorWindow + grid canvas** | **Not started — awaiting user approval** |
| Phase 4 — Top-section + export | Pending |
| Phase 5 — Yarn Sort Layer 2 (in separate game repo) | Pending |
| Phase 6 — Second-game onboarding | Deferred |
