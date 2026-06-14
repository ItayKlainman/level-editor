# YAK (YarnKingdom) Editor Migration & Restore — Design

**Date:** 2026-06-14
**Status:** Awaiting user review
**Scope:** Restore the deleted in-game level-editor mirror in YarnKingdom, migrate it from
editor-core `v0.5.16` → `v0.5.22`, and verify the existing author→export→import workflow
runs end-to-end. **No new generation features this phase** (image import / spool generator
deferred — see "Out of scope").

---

## Background

YAK is the level-editor Layer-2 for the **YarnKingdom** game (`E:/Projects/Hoppa/YarnKingdom`,
branch `Itay-Main`). The game consumes editor-core as a UPM package, currently pinned at
`#v0.5.16`, and embeds a manually-synced mirror of editor-core's `Assets/YAK/` Layer-2 code
under `Assets/_YAK/LevelEditor/`.

### Gameplay (from GDD, for context)
Colored wool grid (30×30 pixel-art) on top; 2–5 columns of capacity-bearing spools below;
a conveyor (max 5 active spools) circulates spools right → cave → reappear-left, matching
only the **bottom row** of the grid; matched wool unwinds into the spool and the grid
collapses with gravity. Win = all wool cleared + all spools completed. Lose = conveyor full
and nothing on it can match. Same genre as YarnTwist, but a distinct belt/lose model and a
fixed pixel-art grid.

### The core problem
Commit **`be34fc3` ("sparkle particle", ~May 20)** deleted the **entire**
`Assets/_YAK/LevelEditor/` tree from the game (cell defs, exporter, palette, validation
rules, order panel, `YAKProfile.asset`, `Editor/`, `Runtime/`). At HEAD the folder is empty;
only stray `.meta`, `.meta.local-backup`, and an empty `LevelEditor_DISABLED/` remain. The
deletion was bundled into an unrelated commit and is treated as **accidental** — restore
wholesale. The in-game editor has been non-functional since mid-May.

---

## Migration risk assessment (verified)

Layer-1 delta `v0.5.16 → v0.5.22` is **purely additive**: new `Editor/Analysis/*`
(analyzer/completer), new `Editor/Generator/*`, `CanvasOverlayAsset`, `CellActionContext`,
and new optional `GameProfile` slots. The only *changed interface* an existing game could
implement is `ICellContextActions` (the v0.5.20 Connected Boxes break).

**YAK's implemented surface** extends only: `CellTypeDefinition`, `ColorPaletteAsset`,
`EditorPanelAsset`, `LevelExporterAsset`, `TopSectionPanel`, `ICellData`, `IColoredCell`.
None of these changed in the delta, and YAK does **not** reference `ICellContextActions` /
`GetContextActions` / `CellActionContext` anywhere. → **Pin bump is non-breaking.** The new
subsystems stay unwired (no generator/analyzer for YAK this phase).

---

## Plan

| # | Step | Detail | Verify-by |
|---|------|--------|-----------|
| 1 | **Restore mirror** | In the game: `git checkout be34fc3^ -- Assets/_YAK/LevelEditor`. Recovers the full Layer-2 tree with **game-specific GUIDs intact** (incl. order panel). | `git ls-tree -r HEAD` shows the tree populated |
| 2 | **Cleanup cruft** | Remove the empty `LevelEditor_DISABLED/`, the `LevelEditor.meta.local-backup`(+`.meta`) untracked files. | working tree clean of backup files |
| 3 | **Bump pin** | `Packages/manifest.json`: `#v0.5.16` → `#v0.5.22`. Confirm tag `v0.5.22` is pushed to GitHub first (pin won't resolve otherwise). | manifest shows `#v0.5.22`; UPM resolves |
| 4 | **Forward-sync check** | Diff restored tree vs editor-core current `Assets/YAK/` with `git diff --ignore-all-space` (avoid CRLF/LF false positives). Only post-v0.5.16 YAK change was the order panel — already in the restore. Apply any genuine divergence, respecting the do-not-sync list. | diff is empty or only intentional drift |
| 5 | **User compiles** | Open YarnKingdom in **Unity 2022.3**; confirm clean compile + the Level Editor window opens. *(Agent cannot compile-verify the game.)* | user confirms |
| 6 | **Prove workflow** | Paint/load the 30×30 grid → add spools → run the color-balance validation rule → Export. Exporter already targets `Assets/_YAK/Configs/Resources/Configs/level_config.json`, which `YAKConfigManager` loads via `Resources.LoadAll<TextAsset>("Configs")`. Confirm a level round-trips into the game. | exported level appears in game config + loads |

### Do-not-sync / intentional drift (from prior sync notes)
- `Runtime/YAKStaticManagerScriptableObject.cs` — game has its own real type under `YAK.Gamelogic`.
- `Editor/YAK.Editor.asmdef` — references `YAK.Gamelogic` only in the game.
- `Data/Config/Palette/YAKStaticManagerColorSource.asset` — `_staticManager` points at the
  game's StaticManager GUID.
- Legacy editor-only artifacts: `StaticManager.asset`, `YAKPalette.asset`, `YAKColorMapping.asset`.

Restoring from `be34fc3^` (the game's own last-good tree) preserves all of the above
automatically — this is why restore-from-git beats a fresh re-copy from editor-core.

---

## Decisions

- **Restore-from-git, not fresh re-sync** — preserves game GUIDs and intentional drift.
  A fresh copy from editor-core would rebind asmdef/color-source to the wrong types/assets.
- **No manifest changes beyond the pin** — Layer-1 is additive; no Layer-2 source edits
  needed for compilation.
- **Push gate:** game `Itay-Main` only, and only after the user confirms compile + workflow
  in Unity. Editor-core is untouched by this work (no editor-core commit/tag).

## Out of scope (future phases)
- Image-import → wool-grid authoring (pixel-art PNG → 30×30 grid).
- YAK level **generator** (`LevelGeneratorAsset` subclass) + auto-spool fill.
- YAK-specific **analyzer** modelling the circulating-conveyor belt + lose condition.
- Any new cell types / mechanics beyond the current Empty + Wool.

## Open questions / risks
- Is `v0.5.22` actually pushed to GitHub? (Verify before step 3.)
- Does the restored `YAKProfile.asset` still reference valid game StaticManager/Palette
  GUIDs, or did those assets also change since May 20? (Check during step 5.)
- The 30×30 grid size vs the editor's grid UI — does the existing editor handle a grid that
  large acceptably? (Observe during step 6; not a blocker for restore.)
