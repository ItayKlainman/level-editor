# Palette — Mechanic 3 (YarnTwist) — Design

## Context

Third and final (for now) new YarnTwist mechanic. A **Palette** covers a 3×3 area of boxes on the
grid, hides them from the player, and unlocks them only after the player opens enough boxes
elsewhere in the level (a per-palette countdown). This spec covers the **level-editor authoring**
side; the runtime/gameplay is Eliran's parallel game-side task.

Per the project's source-of-truth rule, the game schema is authoritative. The game code
(`E:/Projects/Hoppa/YarnTwist/.../YATLevelManager.cs`) currently has only the *stub*:
`YATBottomType.Palette = 7` and a new `BottomConfig.ExtraFeatureBottomType` field. There is **no**
`PaletteConfig`, no requirement-amount field, no palette-ID, and no covered-cell list.

### Decisions (with the user)

1. **No palette ID.** Palettes can't overlap, so a palette's center position is its identity.
2. **Covered cells are always the 3×3 around the center** — derived, never stored as an explicit list.
3. **Requirement amount is editable + stored editor-side now, but NOT exported** (the game field is a
   future addition). When the game adds it, export is a one-line change.
4. **Export marks the center box only** with `ExtraFeatureBottomType = Palette`; the game derives the
   3×3 cover and hides those boxes. No new game schema is required.
5. **Placement via right-click context actions** (not a dedicated "ADD PALETTE" button) — consistent
   with how Connected Boxes/Spools authoring works, and keeps the Layer-1 surface minimal.
6. **Analyzer/difficulty modelling is out of scope** this pass (gameplay + amount are game-side-future);
   the analyzer treats covered boxes as ordinary boxes.

### Scope

- **In:** authoring (Add/Remove Palette + requirement amount), 3×3 red overlay + amount badge on the
  canvas, placement rules + validation, editor-side storage, minimal export (center flag).
- **Out (deferred):** exporting the amount; analyzer/difficulty modelling of the gating.

## Layer split

- **Layer 1 (UPM → tag `v0.5.21`):** one additive hook. `GameProfile` gains an optional
  `CanvasOverlay` (abstract `CanvasOverlayAsset : ScriptableObject`). `GridCanvasPanel` calls
  `profile.CanvasOverlay?.DrawOverlay(session, cellRect)` after `DrawCells` (inside the scroll view),
  where `cellRect` maps a `CellRef` to its on-canvas `Rect`. Draw-only (placement is via context
  actions, so no canvas input hook is needed). Additive + optional → null for YAK/YarnKingdom (no
  overlay), so it is **not breaking** for other consumers.
- **Layer 2 (YarnTwist):** data model, authoring context actions, overlay renderer, validation, export.

## Editor data model

Palettes are non-cell document data stored in `LevelDocument.GameData["palettes"]` (the same
free-form slot used for `coinReward`/`levelType`/`conveyorCount`). Shape — a JSON array of:

```jsonc
{ "center": { "x": <int>, "y": <int> }, "amount": <int> }   // amount default 5
```

A `YarnPalettes` static helper (UI-agnostic, mirroring `YarnSpoolConnection`) owns all access:
- `IReadOnlyList<Palette> All(LevelDocument doc)` / `Write(doc, list)` (round-trips through `GameData`).
- `Palette? PaletteAt(doc, CellRef)` — the palette whose 3×3 contains the cell (or null).
- `IEnumerable<CellRef> CoveredCells(CellRef center)` — the 3×3 around the center.
- `bool CanPlace(GridData<ICellData> grid, CellRef center, IEnumerable<Palette> existing)` — full 3×3
  in-bounds, all 9 cells are boxes, no overlap with an existing palette's 3×3.
- `Add(doc, center)`, `Remove(doc, center)`, `SetAmount(doc, center, amount)`.

"Box" for coverage = `yt.box` (incl. connected) or `yt.arrowbox`; `yt.empty`/`yt.wall`/`yt.tunnel`
are not boxes.

## Authoring — right-click context actions on `YarnBoxCellDefinition`

`YarnBoxCellDefinition` already implements `ICellContextActions` (Connect/Un-connect). Add, using the
`CellActionContext` (Session + CellRef):
- **Add Palette (3×3 here)** — offered only when `YarnPalettes.CanPlace(grid, thisCell, existing)` is
  true (this box becomes the center). Creates the palette with `amount = 5`.
- On a box that is inside an existing palette:
  - **Palette Requirement Amount…** — opens a small `PaletteAmountPopup` (`PopupWindowContent` with an
    int field + Apply, modelled on `ColorPickerPopup`), pre-filled with the current amount; writes back
    via `SetAmount`.
  - **Remove Palette** — removes the palette covering this cell.

All mutations run under one `PushUndoSnapshot()` and write back through `YarnPalettes` →
`session.MarkDirty()`.

## Overlay rendering — `YarnPaletteOverlay : CanvasOverlayAsset`

Wired into `YarnTwistProfile.CanvasOverlay`. For each palette in `GameData`:
- a **red outline** around the bounding rect of the 3×3 (computed from `cellRect` of the corner cells),
- a light red **tint** on the 9 covered cells,
- the **amount number badge** drawn on the center cell.

Editor keeps the covered boxes **visible** (the designer must see/edit them); only the game hides them.

## Validation — `YarnPaletteRule` (Scope = Level)

Safety net for edits made after placement (e.g. a covered box deleted or repainted to wall/empty, or
the grid resized). For each palette: the 3×3 must be fully in-bounds, all 9 covered cells must still be
boxes, and no two palettes may overlap → `Error` otherwise. New `.cs` + `.asset` with stable hand-written
`.meta` GUIDs (`_id: yt.palette`), wired into `YarnTwistProfile._rules`.

## Export — `YarnMasterLevelExporter.BuildBottomConfigs`

For each palette, the **center** cell's `BottomConfig` gets an added field
`"ExtraFeatureBottomType": "Palette"` (enum-as-string, matching how `Direction` / `WinderType` are
written). No amount, ID, or covered list is emitted — the game derives the 3×3 from the center.
Non-center covered cells and cells with no palette are unchanged (no `ExtraFeatureBottomType` key).

(One detail to confirm with Eliran when the game spawner lands: center-marked vs. all-9-marked. We mark
the center because it is unambiguous and lets the game derive the rest.)

## Analyzer / Auto-fill

No change this pass. The win-path analyzer treats covered boxes as ordinary boxes (the "locked until N
opened" gating and the amount are game-side-future). Note left in `YarnTwistLevelAnalyzer` /
`YarnTwistSpoolAutofiller` that palettes are intentionally ignored for now.

## Tests (EditMode)

- `YarnPalettes` helper: `CanPlace` accepts a valid center; rejects off-grid (center too close to an
  edge), a 3×3 containing a non-box cell, and overlap with an existing palette. `CoveredCells` returns
  the 9 cells; `PaletteAt` finds the covering palette and returns null outside.
- Context actions (via `YarnBoxCellDefinition` like the Connected-Box tests): Add Palette offered only
  for a valid center; Requirement Amount + Remove Palette offered on a covered box and not elsewhere;
  Add stores `{center, amount:5}` in `GameData`; `SetAmount` updates it; Remove clears it;
  Add→undo restores (exercises the `GameData["palettes"]` round-trip).
- Export: a palette's center box gets `ExtraFeatureBottomType:"Palette"`; a covered non-center box and a
  box with no palette have no `ExtraFeatureBottomType` key.
- Validation: a valid palette passes; turning a covered cell into a wall/empty → Error; two overlapping
  palettes → Error.

Agent can't run Unity (unity-mcp-cli 401) → implement + write tests; **user runs EditMode tests**.

## Rollout

- Layer-1 change (the overlay hook) → **UPM tag bump `v0.5.21`**. Editor-core: commit + lightweight tag
  + push.
- Game sync (separate step, as with Mechanic 2): bump the game `manifest.json` pin to `#v0.5.21` and
  mirror the Layer-2 files into `Assets/_YAT/Scripts/` — `YarnBoxCellDefinition.cs` (context actions),
  new `YarnPalettes.cs`, new `YarnPaletteOverlay.cs`, new `PaletteAmountPopup.cs`, new
  `YarnPaletteRule.cs` (+`.asset`), `YarnMasterLevelExporter.cs`, and `YarnTwistProfile.asset`
  (`_canvasOverlay` + `_rules`). The overlay hook is additive → other consumers unaffected. User
  confirms the game compiles in Unity 2022.3.

## Watch-points

- The overlay must draw inside the canvas scroll view using the `cellRect` mapper (same coordinate space
  as cells); guard drawing to the Repaint event.
- Hand-written `.meta` GUIDs (new rule `.cs`+`.asset`, new helper/overlay/popup `.cs`) must be unique
  and importable.
- `CanPlace` and `YarnPaletteRule` must agree on the "box" definition (`yt.box`/`yt.arrowbox`).
- `ExtraFeatureBottomType` written as the string `"Palette"` (Newtonsoft parses enum from string or int;
  string chosen for parity with `Direction`/`WinderType`).
