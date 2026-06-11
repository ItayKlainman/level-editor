# YarnTwist — unified spool popup + color-pick stutter fix

**Date:** 2026-06-11
**Layer:** 2 (YarnTwist) — no Layer-1 / UPM change, **no tag bump**. Needs sync to the game repo mirror.
**Files:** `Assets/YarnTwist/Editor/TopSection/YarnTopSectionPanel.cs` (+ a new
`YarnSpoolPopup` popup), tests where feasible.

---

## Problem

1. **Two clicks to edit a spool.** Right-clicking a spool row opens a `GenericMenu`
   ("Change Color…" / "Add Connect"). Picking "Change Color…" then opens a *second*
   window (`ColorPickerPopup`). The grid, by contrast, opens a single "Yarn Box" popup
   (`GridCellPopup`) with swatches + actions inline. The spool should match that.

2. **Stutter on every spool color pick.** The color-apply callback
   (`YarnTopSectionPanel.cs`, the deferred picker) calls `session.PushUndoSnapshot()`,
   which runs `JsonLevelSerializer.Save(Document)` — a full reflection JSON serialize of
   **every grid cell**, pretty-printed (`Formatting.Indented`), via a per-cell
   `JObject.FromObject` converter. That O(grid-size) serialize on the GUI thread is the
   visible hitch. The grid's swatch path deliberately does **not** snapshot, so it's instant.

---

## Design

### Part 1 — `YarnSpoolPopup : PopupWindowContent` (one window)

Mirror `GridCellPopup`. Opened on right-click of a spool row (replacing both the
`GenericMenu` and the deferred `ColorPickerPopup`). Top to bottom:

- **Header** — "Spool" label + accent bar (same styling as `GridCellPopup`).
- **Color swatches** — `ColorSwatchDrawer.Draw(rect, palette, spool.ColorId, pickerFilter)`
  (same palette + filter used today). Picking a swatch applies the color live (see Part 2)
  and closes the popup, matching the current single-pick behavior.
- **Connect button** — the existing connect logic from `ShowRowMenu`, rendered as one
  button whose label + enabled state reflect the current state:
  - unconnected, no pending pair → **"Add Connect"** (starts a pair)
  - unconnected, pending pair, adjacent + no deadlock → **"Add Connect (complete Pair N)"**
  - pending but not adjacent → disabled **"… needs an adjacent column"**
  - pending but would cross → disabled **"… would soft-lock — links can't cross"**
  - already connected → **"Disable Connect (Pair N)"** / **"Cancel Connect (Pair N)"**
  Connect/disconnect are structural and **keep** their undo snapshot (via
  `YarnSpoolConnection.Connect`/`DisconnectGroup`, unchanged). The button closes the popup.

The popup is constructed with everything it needs (session, topData, palette, the spool +
its (col, idx), the connection-member map, pending id, picker filter). Window size derived
like `GridCellPopup.GetWindowSize`.

**Hidden stays on the row, not in the popup.** The spool row already has an inline "Hidden"
toggle that persists cheaply through the panel's `BeginChangeCheck`/`EndChangeCheck`
(top-section-only write, no full-document snapshot). The right-click menu we're replacing only
had Change Color + Connect, so the popup mirrors exactly that. Duplicating Hidden into the
popup would create two controls for one field — kept it on the row.

**Removed:** `ShowRowMenu`, the `_colorPickerCol/_colorPickerIdx` deferred-picker fields,
and the on-repaint `PopupWindow.Show(ColorPickerPopup…)` block. The row right-click handler
calls `PopupWindow.Show(swatchRect, new YarnSpoolPopup(...))` instead.

### Part 2 — kill the stutter (match the grid)

Color and Hidden changes become **cheap, no full-document snapshot** — exactly how the grid
swatch path works (`GridCellPopup` inspector change: mutate + `MarkDirty` + `RunValidation`,
no `PushUndoSnapshot`). Concretely, the apply does:

```
spool.ColorId = id;                                   // (or spool.Hidden = v)
session.Document.TopSection = JObject.FromObject(topData);  // small — top section only
session.MarkDirty();
session.RunValidation();                               // keep color-balance validation fresh
```

No `PushUndoSnapshot()` for color/Hidden. The only remaining serialize is
`JObject.FromObject(topData)` over the **top section alone** (a handful of columns/spools) —
orders of magnitude cheaper than the whole grid. Connect/disconnect keep undo.

**Trade-off (accepted by the user):** a spool color/Hidden change is no longer on the Ctrl+Z
stack — identical to the grid's color swatches today. Structural connect/disconnect remains
undoable.

---

## Testing

This is IMGUI-heavy, so coverage is mostly indirect:

- **Existing analyzer/exporter/connection tests must stay green** — the connect logic is
  unchanged (still routed through `YarnSpoolConnection`), so `YarnConnectedSpoolTests` and
  the exporter connection tests cover it.
- If `YarnSpoolPopup` grows any non-trivial pure helper (e.g. computing the connect-button
  label/enabled state), extract it as a `static` method and unit-test the state machine
  (unconnected / pending-adjacent / pending-cross / connected). Otherwise no new tests.
- Manual verification in Unity (must run): right-click a spool → single window with swatches
  + Hidden + Connect; pick a color → applies instantly with **no stutter**; connect two
  spools across adjacent columns → pair badge appears, Ctrl+Z reverts the connect.

---

## Rollout

1. Implement; full EditMode suite stays green (self-run via Unity MCP).
2. Sync `YarnTopSectionPanel.cs` (+ new `YarnSpoolPopup` file/meta) to the game repo mirror
   under `Assets/_YAT/Scripts/Editor/TopSection/`. Layer-2 only → **no manifest pin bump.**
3. User confirms the game compiles in Unity 2022.3 + does the manual UX check.
4. Commit + push both repos.

## Out of scope

- Changing the grid popup or the undo architecture.
- Adding per-color undo to the grid or spools (explicitly not wanted).
- The YAK spool panel (`YAKSpoolSectionPanel`) — Yarn only.
