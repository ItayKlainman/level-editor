# Bus Buddies — Hidden Buses · Hidden Pixels · Connected Buses Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let designers hand-author Hidden Buses, Hidden Pixels, and Connected Buses in the BB profile and round-trip them losslessly against the real BB game schema.

**Architecture:** A tiny generic Layer-1 addition (a "flag paint" grid tool + `ICellFlagPainter` profile hook) powers the Hidden-Pixels paint gesture; everything else is Bus-Buddies Layer-2 code that mirrors the existing YarnTwist Hidden/Connected-spool implementation. Hidden Buses already round-trip (verify only). All three export to the exact game field names discovered in `BUBLevelManager.cs`.

**Tech Stack:** Unity 6 editor C#, Newtonsoft.Json (`JObject`/`JArray`), the repo's EditMode test framework (run via the Unity MCP `tests-run` tool), the level-editor-core UPM package.

## Global Constraints

- **Game schema is the source of truth** (BB game `E:/Projects/Hoppa/BusBuddies`, `main` @ `73b0897`). Emit exactly: `BusConfig.BusType` (int enum, `Hidden=1`), `LevelConfig.HiddenPixels` (`int[]`), `LevelConfig.ConnectedBuses` (`BUBConnectedBuses[]` of `{BusA,BusB}`, each `{ColumnIndex,Index}`). Game JSON is PascalCase, **enums as int ordinals**.
- **Hidden-pixel export index = `x*width+y`** (the game's `HiddenPixels` formula in `BUBPixelService.cs:170`), NOT `PixelColors`' `y*width+x`. BB grids are square (W==H), so the inverse `x=p/width, y=p%width` round-trips.
- **Editor-internal JSON keys** stay camelCase (existing convention: `BusEntry` = `colorId/capacity/hidden/connectedId`, `BBPixelCell` = `colorId`). New pixel flag key = `"hidden"`.
- **`BusEntry.ConnectedId` is `int` with `-1` = unconnected** (NOT nullable like YarnTwist's `int?`). A pair = exactly two buses sharing an id ≥ 0.
- **Connected buses have NO adjacency constraint** (game pairs any two by coordinate). Same-column pairs ARE invalid (two buses in one column can never both be at head) → validation Error.
- **Layer-1 changes ship in the package** `com.hoppa.leveleditor.core` → minor version bump + tag (Task 16). Game-project re-mirror + compile-check + push is a **lead step** — the agent's Unity MCP is bound to editor-core and cannot compile the game or screenshot its custom window.
- **TDD every task:** write failing test → run to confirm fail → minimal impl → run to confirm pass → commit. Tests are EditMode. Run via MCP `tests-run` (EditMode, filtered by test class). Full suite must stay green (0 regressions).
- **IMGUI panels** (`GridCanvasPanel`, `PalettePanel`, `BusBuddiesQueuePanel`) are not unit-tested in this repo; their tasks are verified by the underlying logic tests plus an explicit manual/lead eyeball step, matching existing repo practice.

---

## Phase A — Generic Layer-1 flag-paint tool

### Task 1: `ICellFlagPainter` interface + `CellFlagPainterAsset` base + `GameProfile.FlagPainter`

**Files:**
- Create: `Packages/com.hoppa.leveleditor.core/Editor/Analysis/ICellFlagPainter.cs`
- Create: `Packages/com.hoppa.leveleditor.core/Editor/Analysis/CellFlagPainterAsset.cs`
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Registry/GridEditTool.cs`
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Infrastructure/GameProfile.cs` (add serialized field + property)
- Test: `Assets/BusBuddies/Tests/Editor/CellFlagPainterContractTests.cs` (a stub BB painter exercises the contract)

**Interfaces:**
- Produces: `ICellFlagPainter { string ToolLabel; string ToolTooltip; bool IsFlagged(ICellData cell); void SetFlag(LevelEditorSession session, CellRef cell, bool value); }`
- Produces: `abstract class CellFlagPainterAsset : ScriptableObject, ICellFlagPainter`
- Produces: `GameProfile.FlagPainter` → `CellFlagPainterAsset` (null when the profile has no flag mechanic)
- Produces: `GridEditTool.Hide` enum value

- [ ] **Step 1: Write the failing test**

```csharp
// Assets/BusBuddies/Tests/Editor/CellFlagPainterContractTests.cs
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class CellFlagPainterContractTests
    {
        // Minimal concrete painter over a fake cell, proving the base type + contract compile
        // and that SetFlag/IsFlagged operate through the interface.
        private sealed class FakeCell : ICellData
        {
            public string CellTypeId => "fake";
            public bool Flag;
        }

        private sealed class FakePainter : CellFlagPainterAsset
        {
            public override string ToolLabel => "Flag";
            public override string ToolTooltip => "Toggle a flag";
            public override bool IsFlagged(ICellData cell) => cell is FakeCell f && f.Flag;
            public override void SetFlag(LevelEditorSession session, CellRef cell, bool value)
            {
                if (session.Document.Grid.Get(cell.X, cell.Y) is FakeCell f) f.Flag = value;
            }
        }

        [Test]
        public void SetFlag_TogglesUnderlyingCell()
        {
            var grid = new GridData<ICellData>(2, 2);
            grid.Set(0, 0, new FakeCell());
            var doc = new LevelDocument { Grid = grid };
            var session = new LevelEditorSession(doc, null);

            var painter = ScriptableObject.CreateInstance<FakePainter>();
            Assert.IsFalse(painter.IsFlagged(grid.Get(0, 0)));
            painter.SetFlag(session, new CellRef(0, 0), true);
            Assert.IsTrue(painter.IsFlagged(grid.Get(0, 0)));

            Assert.AreEqual(GridEditTool.Hide, System.Enum.Parse(typeof(GridEditTool), "Hide"));
        }
    }
}
```

> NOTE: check `LevelEditorSession`'s real constructor signature before running (it may take `(LevelDocument, GameProfile)` or similar). Match it; the test only needs a session whose `Document.Grid` is the grid above.

- [ ] **Step 2: Run test to verify it fails**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.CellFlagPainterContractTests`.
Expected: FAIL — `ICellFlagPainter`/`CellFlagPainterAsset`/`GridEditTool.Hide` do not exist (compile error).

- [ ] **Step 3: Add the enum value**

```csharp
// Packages/com.hoppa.leveleditor.core/Editor/Registry/GridEditTool.cs
namespace Hoppa.LevelEditor.Core.Editor
{
    public enum GridEditTool { Paint, Select, Delete, Move, Hide }
}
```

- [ ] **Step 4: Create the interface**

```csharp
// Packages/com.hoppa.leveleditor.core/Editor/Analysis/ICellFlagPainter.cs
namespace Hoppa.LevelEditor.Core.Editor
{
    // A profile-supplied "flag paint" tool: when GridEditTool.Hide is active, the grid
    // canvas delegates click/drag to this painter to toggle a boolean flag on a cell
    // (e.g. Bus Buddies' hidden-pixel flag) WITHOUT replacing the cell or its color.
    // Layer-1 stays generic — it knows "there is a flag tool", not what the flag means.
    public interface ICellFlagPainter
    {
        // Tool button caption + tooltip shown in the TOOLS palette.
        string ToolLabel { get; }
        string ToolTooltip { get; }

        // Current flag value on a cell (used to decide a stroke's paint-vs-erase direction).
        bool IsFlagged(ICellData cell);

        // Set the flag to `value` on the cell at `cell`. Implementations mutate through
        // the session (so undo/dirty are handled by the caller's snapshot).
        void SetFlag(LevelEditorSession session, CellRef cell, bool value);
    }
}
```

- [ ] **Step 5: Create the ScriptableObject base**

```csharp
// Packages/com.hoppa.leveleditor.core/Editor/Analysis/CellFlagPainterAsset.cs
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Serializable base so a GameProfile can reference a flag painter as a project asset,
    // mirroring LevelCompleterAsset / LevelAnalyzerAsset.
    public abstract class CellFlagPainterAsset : ScriptableObject, ICellFlagPainter
    {
        public abstract string ToolLabel { get; }
        public abstract string ToolTooltip { get; }
        public abstract bool IsFlagged(ICellData cell);
        public abstract void SetFlag(LevelEditorSession session, CellRef cell, bool value);
    }
}
```

- [ ] **Step 6: Add the property to `GameProfile`**

Add near the other asset-reference fields (e.g. beside `_levelCompleter`):

```csharp
        [SerializeField] private CellFlagPainterAsset _flagPainter;

        // Optional per-profile "flag paint" tool (null = profile has no flag mechanic).
        // When set, the TOOLS palette shows a toggle and GridEditTool.Hide is enabled.
        public CellFlagPainterAsset FlagPainter => _flagPainter;
```

(`GameProfile` is in namespace `Hoppa.LevelEditor.Core.Editor`, so `CellFlagPainterAsset` resolves without a using.)

- [ ] **Step 7: Run test to verify it passes**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.CellFlagPainterContractTests`.
Expected: PASS.

- [ ] **Step 8: Commit**

```bash
git add Packages/com.hoppa.leveleditor.core/Editor Assets/BusBuddies/Tests/Editor/CellFlagPainterContractTests.cs
git commit -m "feat(core): add generic ICellFlagPainter tool hook + GridEditTool.Hide"
```

---

### Task 2: Grid canvas handles the Hide tool

**Files:**
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Panels/GridCanvasPanel.cs`

**Interfaces:**
- Consumes: `GameProfile.FlagPainter`, `GridEditTool.Hide`, `ICellFlagPainter.IsFlagged/SetFlag`

No unit test (IMGUI event handling; verified by Task 5's painter test + the manual eyeball in Task 6). Keep the diff minimal and mirror the existing `Paint` branch.

- [ ] **Step 1: Add a stroke-value field**

Beside the other private fields near the top of the class:

```csharp
        private bool _flagStrokeValue;
```

- [ ] **Step 2: Handle mouse-down for Hide**

In `HandleEvents`, inside the `case EventType.MouseDown ... e.button == 0:` block, in the `switch (session.ActiveTool)` (the `else` branch that already handles Paint/Select/Delete/Move), add:

```csharp
                            case GridEditTool.Hide:
                                if (session.Profile?.FlagPainter != null && _hoverCell.HasValue)
                                {
                                    session.PushUndoSnapshot();
                                    _isDragging = true;
                                    var painter = session.Profile.FlagPainter;
                                    var cur = session.Document.Grid.Get(_hoverCell.Value.X, _hoverCell.Value.Y);
                                    _flagStrokeValue = !painter.IsFlagged(cur);   // stroke sets the opposite of the first cell
                                    painter.SetFlag(session, _hoverCell.Value, _flagStrokeValue);
                                    session.MarkDirty();
                                }
                                break;
```

- [ ] **Step 3: Handle mouse-drag for Hide**

In the `case EventType.MouseDrag when _isDragging:` `switch (session.ActiveTool)`, add:

```csharp
                        case GridEditTool.Hide:
                            if (session.Profile?.FlagPainter != null && _hoverCell.HasValue)
                            {
                                session.Profile.FlagPainter.SetFlag(session, _hoverCell.Value, _flagStrokeValue);
                                session.MarkDirty();
                            }
                            break;
```

(The existing `MouseUp` case already ends the drag and runs validation — no change.)

- [ ] **Step 4: Compile check**

Run: MCP `assets-refresh` then `console-get-logs` (filter Error). Expected: no compile errors.

- [ ] **Step 5: Commit**

```bash
git add Packages/com.hoppa.leveleditor.core/Editor/Panels/GridCanvasPanel.cs
git commit -m "feat(core): GridCanvasPanel drives the Hide flag-paint tool"
```

---

### Task 3: TOOLS palette shows the Hide tool when the profile supplies a painter

**Files:**
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Panels/PalettePanel.cs:148-180` (the TOOLS row)

No unit test (IMGUI). Verified by manual eyeball in Task 6.

- [ ] **Step 1: Append the Hide tool to the TOOLS row when available**

Replace the `toolDefs` array construction (lines ~148-156) so a Hide entry is appended only when `session.Profile?.FlagPainter != null`:

```csharp
            var painter = session.Profile?.FlagPainter;
            var tools = new System.Collections.Generic.List<(GUIContent, GridEditTool)>
            {
                (ToolContent(ref _selectContent, "d_RectTool",       "Select", "Select cells without painting"), GridEditTool.Select),
                (ToolContent(ref _deleteContent, "d_P4_DeletedLocal", "Delete", "Click/drag to erase cells"),     GridEditTool.Delete),
                (ToolContent(ref _moveContent,   "d_MoveTool",        "Move",   "Click two cells to swap them"),   GridEditTool.Move),
            };
            if (painter != null)
                tools.Add((new GUIContent(painter.ToolLabel, painter.ToolTooltip), GridEditTool.Hide));
            var toolDefs = tools.ToArray();
```

(The loop below already lays out `toolDefs` by index; a 4th button wraps naturally. `btnW` stays `totalW/3f` — acceptable; if you want exact fit, change to `Mathf.Floor(totalW / Mathf.Max(3, toolDefs.Length))`. Do the latter to avoid overflow with 4 tools.)

- [ ] **Step 2: Compile + manual check**

Run: MCP `assets-refresh` + `console-get-logs`. Expected: no errors. (Visual placement verified in Task 6.)

- [ ] **Step 3: Commit**

```bash
git add Packages/com.hoppa.leveleditor.core/Editor/Panels/PalettePanel.cs
git commit -m "feat(core): TOOLS palette shows the profile flag-paint tool"
```

---

## Phase B — Hidden Pixels (Bus Buddies)

### Task 4: `BBPixelCell.Hidden` field + serialize round-trip

**Files:**
- Modify: `Assets/BusBuddies/Runtime/BBPixelCell.cs`
- Test: `Assets/BusBuddies/Tests/Editor/BBPixelCellTests.cs`

**Interfaces:**
- Produces: `BBPixelCell.Hidden` (bool, JSON `"hidden"`, default false)

- [ ] **Step 1: Write the failing test**

```csharp
// Assets/BusBuddies/Tests/Editor/BBPixelCellTests.cs
using Newtonsoft.Json;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BBPixelCellTests
    {
        [Test]
        public void Hidden_DefaultsFalse_AndRoundTrips()
        {
            var cell = new BBPixelCell { ColorId = "red", Hidden = true };
            var json = JsonConvert.SerializeObject(cell);
            var back = JsonConvert.DeserializeObject<BBPixelCell>(json);

            Assert.AreEqual("red", back.ColorId);
            Assert.IsTrue(back.Hidden);
            Assert.IsFalse(new BBPixelCell().Hidden, "Hidden must default to false");
        }
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.BBPixelCellTests`.
Expected: FAIL — `BBPixelCell` has no `Hidden`.

- [ ] **Step 3: Add the field**

```csharp
// Assets/BusBuddies/Runtime/BBPixelCell.cs
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json;

namespace Hoppa.BusBuddies
{
    public sealed class BBPixelCell : IColoredCell
    {
        [JsonIgnore]
        public string CellTypeId => "bb.pixel";

        [JsonProperty("colorId")]
        public string ColorId { get; set; } = "blue";

        // Concealed-until-revealed flag. The pixel keeps its color; Hidden only changes
        // how it renders in-editor and exports to LevelConfig.HiddenPixels.
        [JsonProperty("hidden")]
        public bool Hidden { get; set; }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.BBPixelCellTests`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/BusBuddies/Runtime/BBPixelCell.cs Assets/BusBuddies/Tests/Editor/BBPixelCellTests.cs
git commit -m "feat(bb): add Hidden flag to BBPixelCell"
```

---

### Task 5: `BusBuddiesHiddenPixelPainter` + profile wiring

**Files:**
- Create: `Assets/BusBuddies/Editor/Analysis/BusBuddiesHiddenPixelPainter.cs`
- Create asset: `Assets/BusBuddies/Data/Config/BusBuddiesHiddenPixelPainter.asset` (via CreateAssetMenu)
- Modify asset: `Assets/BusBuddies/Data/Config/BusBuddiesProfile.asset` (wire `_flagPainter`)
- Test: `Assets/BusBuddies/Tests/Editor/BusBuddiesHiddenPixelPainterTests.cs`

**Interfaces:**
- Consumes: `CellFlagPainterAsset`, `BBPixelCell.Hidden`
- Produces: `BusBuddiesHiddenPixelPainter : CellFlagPainterAsset` — `ToolLabel => "✦ Hide"`; toggles Hidden on `BBPixelCell` only

- [ ] **Step 1: Write the failing test**

```csharp
// Assets/BusBuddies/Tests/Editor/BusBuddiesHiddenPixelPainterTests.cs
using Hoppa.BusBuddies.Editor;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusBuddiesHiddenPixelPainterTests
    {
        private static LevelEditorSession MakeSession(GridData<ICellData> grid)
        {
            var doc = new LevelDocument { Grid = grid };
            return new LevelEditorSession(doc, null);   // adjust to real ctor
        }

        [Test]
        public void SetFlag_HidesAndUnhides_PixelCell()
        {
            var grid = new GridData<ICellData>(2, 1);
            grid.Set(0, 0, new BBPixelCell { ColorId = "red" });
            var session = MakeSession(grid);
            var painter = ScriptableObject.CreateInstance<BusBuddiesHiddenPixelPainter>();

            painter.SetFlag(session, new CellRef(0, 0), true);
            Assert.IsTrue(((BBPixelCell)grid.Get(0, 0)).Hidden);
            Assert.IsTrue(painter.IsFlagged(grid.Get(0, 0)));

            painter.SetFlag(session, new CellRef(0, 0), false);
            Assert.IsFalse(((BBPixelCell)grid.Get(0, 0)).Hidden);
        }

        [Test]
        public void SetFlag_NoOp_OnEmptyCell()
        {
            var grid = new GridData<ICellData>(1, 1);
            grid.Set(0, 0, new BBEmptyCell());
            var session = MakeSession(grid);
            var painter = ScriptableObject.CreateInstance<BusBuddiesHiddenPixelPainter>();

            Assert.DoesNotThrow(() => painter.SetFlag(session, new CellRef(0, 0), true));
            Assert.IsFalse(painter.IsFlagged(grid.Get(0, 0)));
        }
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.BusBuddiesHiddenPixelPainterTests`.
Expected: FAIL — painter type missing.

- [ ] **Step 3: Implement the painter**

```csharp
// Assets/BusBuddies/Editor/Analysis/BusBuddiesHiddenPixelPainter.cs
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Flag-paint tool for Hidden Pixels: toggles BBPixelCell.Hidden. No-op on empty
    // or non-pixel cells (an empty cell has nothing to conceal).
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Hidden Pixel Painter")]
    public sealed class BusBuddiesHiddenPixelPainter : CellFlagPainterAsset
    {
        public override string ToolLabel => "✦ Hide";
        public override string ToolTooltip => "Paint hidden pixels — click/drag colored cells to conceal them";

        public override bool IsFlagged(ICellData cell) => cell is BBPixelCell p && p.Hidden;

        public override void SetFlag(LevelEditorSession session, CellRef cell, bool value)
        {
            if (session?.Document?.Grid == null) return;
            if (session.Document.Grid.Get(cell.X, cell.Y) is BBPixelCell p)
                p.Hidden = value;
        }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.BusBuddiesHiddenPixelPainterTests`.
Expected: PASS.

- [ ] **Step 5: Create the asset + wire the profile**

Use MCP `assets-refresh`, then create the painter asset (menu `Hoppa/BusBuddies/Hidden Pixel Painter`) at `Assets/BusBuddies/Data/Config/BusBuddiesHiddenPixelPainter.asset` (via `assets-find`/`script-execute` `ScriptableObject.CreateInstance` + `AssetDatabase.CreateAsset` if no interactive menu). Then set `BusBuddiesProfile.asset`'s `_flagPainter` to reference it (MCP `assets-modify` / `object-modify` on the profile asset).

- [ ] **Step 6: Commit**

```bash
git add Assets/BusBuddies/Editor/Analysis/BusBuddiesHiddenPixelPainter.cs Assets/BusBuddies/Tests/Editor/BusBuddiesHiddenPixelPainterTests.cs "Assets/BusBuddies/Data/Config/BusBuddiesHiddenPixelPainter.asset" "Assets/BusBuddies/Data/Config/BusBuddiesProfile.asset"
git commit -m "feat(bb): hidden-pixel flag painter wired into the profile"
```

---

### Task 6: Hatch overlay rendering for hidden pixels + manual eyeball

**Files:**
- Modify: `Assets/BusBuddies/Editor/Cells/BBPixelCellDefinition.cs:35-41` (`DrawCell`)

No unit test (rendering). Ends with a manual/lead eyeball.

- [ ] **Step 1: Render a dim + hatch overlay when Hidden**

Replace `DrawCell`:

```csharp
        private static readonly Color HiddenDarken = new Color(0f, 0f, 0f, 0.45f);
        private static readonly Color HiddenHatch  = new Color(1f, 1f, 1f, 0.35f);

        public override void DrawCell(Rect rect, ICellData data)
        {
            if (data is not BBPixelCell pixel) return;
            var color = UnknownColor;
            if (_palette != null) _palette.TryGetColor(pixel.ColorId, out color);
            EditorGUI.DrawRect(rect, color);

            if (pixel.Hidden)
            {
                EditorGUI.DrawRect(rect, HiddenDarken);
                // Two diagonal hatch strokes so hidden reads clearly at any zoom.
                DrawHatch(rect, HiddenHatch);
            }
        }

        private static void DrawHatch(Rect r, Color c)
        {
            // Cheap "hatch": a thin cross of translucent lines (IMGUI has no line primitive
            // for diagonals without Handles, so use a small centered cross that reads as "masked").
            float t = Mathf.Max(1f, r.width * 0.10f);
            EditorGUI.DrawRect(new Rect(r.x, r.center.y - t * 0.5f, r.width, t), c);
            EditorGUI.DrawRect(new Rect(r.center.x - t * 0.5f, r.y, t, r.height), c);
        }
```

- [ ] **Step 2: Compile check**

Run: MCP `assets-refresh` + `console-get-logs`. Expected: no errors.

- [ ] **Step 3: Manual eyeball (lead/verifier step)**

Open the Level Editor on a BB level, select the `✦ Hide` tool, paint some colored pixels → they show the dim+cross overlay; painting again clears it; Undo (Ctrl+Z) reverts a stroke. (Cannot be screenshotted via MCP — custom editor window; note for the verifier/lead.)

- [ ] **Step 4: Commit**

```bash
git add Assets/BusBuddies/Editor/Cells/BBPixelCellDefinition.cs
git commit -m "feat(bb): render a hidden-pixel overlay in the grid canvas"
```

---

### Task 7: Exporter emits `HiddenPixels`

**Files:**
- Modify: `Assets/BusBuddies/Editor/Exporters/BusBuddiesGameLevelExporter.cs` (add `BuildHiddenPixels`, add key to the config `JObject`)
- Test: `Assets/BusBuddies/Tests/Editor/BusBuddiesHiddenPixelExportTests.cs`

**Interfaces:**
- Consumes: `BBPixelCell.Hidden`
- Produces: `LevelConfig.HiddenPixels` = `int[]` of `x*width+y` for each hidden cell

- [ ] **Step 1: Write the failing test**

```csharp
// Assets/BusBuddies/Tests/Editor/BusBuddiesHiddenPixelExportTests.cs
using System.Linq;
using Hoppa.BusBuddies.Editor;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusBuddiesHiddenPixelExportTests
    {
        [Test]
        public void BuildHiddenPixels_UsesXTimesWidthPlusY_ForHiddenCellsOnly()
        {
            // 3x2 grid. Hide the cell at (x=2, y=1) and (x=0, y=0).
            var grid = new GridData<ICellData>(3, 2);
            for (int y = 0; y < 2; y++)
                for (int x = 0; x < 3; x++)
                    grid.Set(x, y, new BBPixelCell { ColorId = "red" });
            ((BBPixelCell)grid.Get(2, 1)).Hidden = true;
            ((BBPixelCell)grid.Get(0, 0)).Hidden = true;

            var arr = BusBuddiesGameLevelExporter.BuildHiddenPixels(grid);
            var vals = arr.Select(t => (int)t).OrderBy(v => v).ToArray();

            // x*width+y : (2,1) -> 2*3+1=7 ; (0,0) -> 0
            CollectionAssert.AreEqual(new[] { 0, 7 }, vals);
        }

        [Test]
        public void BuildHiddenPixels_Empty_WhenNoneHidden()
        {
            var grid = new GridData<ICellData>(2, 2);
            for (int i = 0; i < 4; i++) grid.Cells[i] = new BBPixelCell { ColorId = "blue" };
            Assert.AreEqual(0, BusBuddiesGameLevelExporter.BuildHiddenPixels(grid).Count);
        }
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.BusBuddiesHiddenPixelExportTests`.
Expected: FAIL — `BuildHiddenPixels` missing.

- [ ] **Step 3: Implement + wire into the config**

Add the method (make it `public static` so the test and `Export` share it) and add the key to the `config` JObject in `Export` (after `PixelColors`):

```csharp
            var config = new JObject
            {
                ["SlotsAmount"] = slots,
                ["Width"] = grid.Width,
                ["Height"] = grid.Height,
                ["BusColumnConfigs"] = BuildBusColumnConfigs(document.TopSection),
                ["ConnectedBuses"] = BuildConnectedBuses(document.TopSection),   // Task 13
                ["PixelColors"] = BuildPixelColors(grid),
                ["HiddenPixels"] = BuildHiddenPixels(grid),
            };
```

> If Task 13 is not yet done when you implement this task, omit the `ConnectedBuses` line here and add it in Task 13. Order Task 13 before this line goes in, OR add the line now and stub `BuildConnectedBuses` returning `new JArray()`.

```csharp
        // Sparse hidden-pixel indices. MIRRORS the game's BUBPixelService, which tests
        // HiddenPixels membership against `position = x * width + y` — DELIBERATELY the
        // transpose of PixelColors' `y * width + x`. BB grids are square, so a hidden cell
        // at (x,y) maps to x*Width+y and the game conceals exactly that cell.
        public static JArray BuildHiddenPixels(GridData<ICellData> grid)
        {
            var array = new JArray();
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width;  x++)
            {
                if (grid.Get(x, y) is BBPixelCell p && p.Hidden)
                    array.Add(x * grid.Width + y);
            }
            return array;
        }
```

- [ ] **Step 4: Run to verify pass**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.BusBuddiesHiddenPixelExportTests`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/BusBuddies/Editor/Exporters/BusBuddiesGameLevelExporter.cs Assets/BusBuddies/Tests/Editor/BusBuddiesHiddenPixelExportTests.cs
git commit -m "feat(bb): export HiddenPixels (x*width+y, mirroring BUBPixelService)"
```

---

### Task 8: Importer reads `HiddenPixels`

**Files:**
- Modify: `Assets/BusBuddies/Editor/Exporters/BusBuddiesGameLevelImporter.cs` (`Import`)
- Test: `Assets/BusBuddies/Tests/Editor/BusBuddiesHiddenPixelRoundTripTests.cs`

**Interfaces:**
- Consumes: `BusBuddiesGameLevelExporter.BuildHiddenPixels`, `BBPixelCell.Hidden`

- [ ] **Step 1: Write the failing test (full export→import round-trip)**

```csharp
// Assets/BusBuddies/Tests/Editor/BusBuddiesHiddenPixelRoundTripTests.cs
using Hoppa.BusBuddies.Editor;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusBuddiesHiddenPixelRoundTripTests
    {
        [Test]
        public void HiddenPixels_SurviveImport_AtSameCells()
        {
            // Build a 4x4 game-schema JObject with two hidden cells, import it, assert Hidden.
            int w = 4, h = 4;
            var px = new JArray();
            for (int i = 0; i < w * h; i++) px.Add(9); // all Red
            // Hidden at (x=3,y=2) -> 3*4+2=14 ; (x=1,y=0) -> 4
            var hidden = new JArray { 14, 4 };

            var root = new JObject
            {
                ["SlotsAmount"] = 5, ["Width"] = w, ["Height"] = h,
                ["BusColumnConfigs"] = new JArray(),
                ["PixelColors"] = px,
                ["HiddenPixels"] = hidden,
            };

            var imported = BusBuddiesGameLevelImporter.Import(root.ToString(), "level_1");
            var grid = imported.Document.Grid;

            Assert.IsTrue(((BBPixelCell)grid.Get(3, 2)).Hidden);
            Assert.IsTrue(((BBPixelCell)grid.Get(1, 0)).Hidden);
            Assert.IsFalse(((BBPixelCell)grid.Get(0, 0)).Hidden);
        }
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.BusBuddiesHiddenPixelRoundTripTests`.
Expected: FAIL — importer ignores `HiddenPixels`.

- [ ] **Step 3: Read HiddenPixels after grid reconstruction**

In `Import`, after the `for (int i = 0; i < total; i++) { ... grid.Cells[i] = OrdinalToCell(ordinal); }` loop, add:

```csharp
            // Hidden pixels: sparse indices using the game's x*width+y stride (inverse:
            // x = p / width, y = p % width — exact for square grids).
            if (root["HiddenPixels"] is JArray hiddenArr)
            {
                foreach (var tok in hiddenArr)
                {
                    int p = (int)tok;
                    int x = p / width;
                    int y = p % width;
                    if (grid.InBounds(x, y) && grid.Get(x, y) is BBPixelCell cell)
                        cell.Hidden = true;
                }
            }
```

- [ ] **Step 4: Run to verify pass**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.BusBuddiesHiddenPixelRoundTripTests`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/BusBuddies/Editor/Exporters/BusBuddiesGameLevelImporter.cs Assets/BusBuddies/Tests/Editor/BusBuddiesHiddenPixelRoundTripTests.cs
git commit -m "feat(bb): import HiddenPixels back onto BBPixelCell.Hidden"
```

---

## Phase C — Hidden Buses (verify only)

### Task 9: Hidden-bus export/import round-trip test

**Files:**
- Test: `Assets/BusBuddies/Tests/Editor/BusBuddiesHiddenBusRoundTripTests.cs`

Hidden buses already work (exporter writes `BusType:1`, importer reads it). This task PROVES it and guards regressions. No production change expected; if the assertion fails, fix minimally in the exporter/importer.

- [ ] **Step 1: Write the test**

```csharp
// Assets/BusBuddies/Tests/Editor/BusBuddiesHiddenBusRoundTripTests.cs
using Hoppa.BusBuddies.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusBuddiesHiddenBusRoundTripTests
    {
        [Test]
        public void HiddenBus_ExportsBusType1_AndImportsHidden()
        {
            // One column: a normal red bus (cap 10) then a hidden blue bus (cap 5).
            var queue = new BusQueueData();
            var col = new BusColumn();
            col.Buses.Add(new BusEntry { ColorId = "red",  Capacity = 10, Hidden = false });
            col.Buses.Add(new BusEntry { ColorId = "blue", Capacity = 5,  Hidden = true  });
            queue.Columns.Add(col);
            var top = JObject.FromObject(queue);

            var busColumns = BusBuddiesGameLevelExporter.BuildBusColumnConfigsForTest(top);
            var busConfigs = (JArray)busColumns[0]["BusConfigs"];
            Assert.IsNull(busConfigs[0]["BusType"], "normal bus omits BusType");
            Assert.AreEqual(1, (int)busConfigs[1]["BusType"], "hidden bus → BusType 1");

            // Import a matching config and confirm Hidden survives.
            var root = new JObject
            {
                ["SlotsAmount"] = 5, ["Width"] = 1, ["Height"] = 1,
                ["PixelColors"] = new JArray { 0 },
                ["BusColumnConfigs"] = busColumns,
            };
            var imported = BusBuddiesGameLevelImporter.Import(root.ToString(), "level_1");
            var backTop = imported.Document.TopSection.ToObject<BusQueueData>();
            Assert.IsFalse(backTop.Columns[0].Buses[0].Hidden);
            Assert.IsTrue(backTop.Columns[0].Buses[1].Hidden);
        }
    }
}
```

- [ ] **Step 2: Expose the private builder for the test (minimal)**

`BuildBusColumnConfigs` is `private static`. Add a thin test hook next to it (keeps the test honest without reflection):

```csharp
        // Test hook — exercises the real column builder without duplicating it.
        public static JArray BuildBusColumnConfigsForTest(JObject topSection) => BuildBusColumnConfigs(topSection);
```

- [ ] **Step 3: Run**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.BusBuddiesHiddenBusRoundTripTests`.
Expected: PASS (if FAIL, fix the exporter/importer minimally, then re-run).

- [ ] **Step 4: Commit**

```bash
git add Assets/BusBuddies/Editor/Exporters/BusBuddiesGameLevelExporter.cs Assets/BusBuddies/Tests/Editor/BusBuddiesHiddenBusRoundTripTests.cs
git commit -m "test(bb): pin hidden-bus export/import round-trip"
```

---

## Phase D — Connected Buses (Bus Buddies)

### Task 10: `BusConnection` static ops (build/alloc/connect/disconnect)

**Files:**
- Create: `Assets/BusBuddies/Editor/TopSection/BusConnection.cs`
- Test: `Assets/BusBuddies/Tests/Editor/BusConnectionTests.cs`

**Interfaces:**
- Produces:
  - `void BusConnection.BuildConnInfo(BusQueueData, out Dictionary<int,List<(int col,int pos)>> members, out int? pendingId)`
  - `int BusConnection.AllocId(BusQueueData)`
  - `int BusConnection.DisplayNumber(Dictionary<int,List<(int col,int pos)>> members, int id)`
  - `void BusConnection.Connect(LevelEditorSession, BusQueueData, BusEntry, int id)`
  - `void BusConnection.DisconnectGroup(LevelEditorSession, BusQueueData, int id)`
- Note: BB uses `int ConnectedId` with `-1` sentinel (id ≥ 0 means connected).

- [ ] **Step 1: Write the failing test**

```csharp
// Assets/BusBuddies/Tests/Editor/BusConnectionTests.cs
using System.Collections.Generic;
using Hoppa.BusBuddies.Editor;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusConnectionTests
    {
        private static BusQueueData TwoColumns()
        {
            var q = new BusQueueData();
            var c0 = new BusColumn(); c0.Buses.Add(new BusEntry()); c0.Buses.Add(new BusEntry());
            var c1 = new BusColumn(); c1.Buses.Add(new BusEntry()); c1.Buses.Add(new BusEntry());
            q.Columns.Add(c0); q.Columns.Add(c1);
            return q;
        }

        [Test]
        public void AllocId_StartsAtZero_ThenIncrements()
        {
            var q = TwoColumns();
            Assert.AreEqual(0, BusConnection.AllocId(q));
            q.Columns[0].Buses[0].ConnectedId = 0;
            Assert.AreEqual(1, BusConnection.AllocId(q));
        }

        [Test]
        public void BuildConnInfo_GroupsMembers_AndFindsPending()
        {
            var q = TwoColumns();
            q.Columns[0].Buses[0].ConnectedId = 0; // pending single
            q.Columns[0].Buses[1].ConnectedId = 1;
            q.Columns[1].Buses[1].ConnectedId = 1; // complete pair
            BusConnection.BuildConnInfo(q, out var members, out var pending);

            Assert.AreEqual(2, members.Count);
            Assert.AreEqual(1, members[0].Count);
            Assert.AreEqual(2, members[1].Count);
            Assert.AreEqual(0, pending);
        }

        [Test]
        public void DisconnectGroup_ClearsBothMembers_ToSentinel()
        {
            var q = TwoColumns();
            q.Columns[0].Buses[0].ConnectedId = 3;
            q.Columns[1].Buses[0].ConnectedId = 3;
            BusConnection.DisconnectGroup(null, q, 3);
            Assert.AreEqual(-1, q.Columns[0].Buses[0].ConnectedId);
            Assert.AreEqual(-1, q.Columns[1].Buses[0].ConnectedId);
        }
    }
}
```

> `Connect`/`DisconnectGroup` take a `LevelEditorSession` to snapshot undo + write back `Document.TopSection`. The test passes `null` and the impl must guard null session (skip snapshot/dirty) so the pure logic stays testable — see impl.

- [ ] **Step 2: Run to verify fail**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.BusConnectionTests`.
Expected: FAIL — `BusConnection` missing.

- [ ] **Step 3: Implement (mirrors `YarnSpoolConnection`, adapted to `int` sentinel)**

```csharp
// Assets/BusBuddies/Editor/TopSection/BusConnection.cs
using System.Collections.Generic;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;

namespace Hoppa.BusBuddies.Editor
{
    // UI-agnostic operations for Connected Buses, shared by BusBuddiesQueuePanel and
    // exercised by tests. A connection is a stable ConnectedId (>= 0) on BusEntry; two
    // buses sharing an id form a pair. Positions shift as the designer edits, so the id —
    // not a position pointer — is the authoring handle; the exporter resolves each bus's
    // live (column, index) at export. Mirrors YarnSpoolConnection but uses BB's int
    // sentinel (-1 = unconnected) instead of a nullable id, and imposes NO adjacency
    // (the game pairs any two buses by coordinate).
    public static class BusConnection
    {
        public static void BuildConnInfo(BusQueueData queue,
            out Dictionary<int, List<(int col, int pos)>> members, out int? pendingId)
        {
            members = new Dictionary<int, List<(int col, int pos)>>();
            for (int c = 0; c < queue.Columns.Count; c++)
            {
                var buses = queue.Columns[c]?.Buses;
                if (buses == null) continue;
                for (int p = 0; p < buses.Count; p++)
                {
                    int id = buses[p].ConnectedId;
                    if (id < 0) continue;
                    if (!members.TryGetValue(id, out var list))
                        members[id] = list = new List<(int, int)>();
                    list.Add((c, p));
                }
            }

            pendingId = null;
            foreach (var kv in members)
                if (kv.Value.Count == 1 && (pendingId == null || kv.Key < pendingId.Value))
                    pendingId = kv.Key;
        }

        // Contiguous 1..N label for a connection id (ordinal rank among ids present).
        public static int DisplayNumber(Dictionary<int, List<(int col, int pos)>> members, int id)
        {
            int n = 1;
            foreach (var key in members.Keys)
                if (key < id) n++;
            return n;
        }

        // Lowest free id >= 0.
        public static int AllocId(BusQueueData queue)
        {
            int max = -1;
            foreach (var col in queue.Columns)
                if (col?.Buses != null)
                    foreach (var b in col.Buses)
                        if (b.ConnectedId > max) max = b.ConnectedId;
            return max + 1;
        }

        public static void Connect(LevelEditorSession session, BusQueueData queue, BusEntry bus, int id)
        {
            session?.PushUndoSnapshot();
            bus.ConnectedId = id;
            if (session != null)
            {
                session.Document.TopSection = JObject.FromObject(queue);
                session.MarkDirty();
            }
        }

        public static void DisconnectGroup(LevelEditorSession session, BusQueueData queue, int id)
        {
            session?.PushUndoSnapshot();
            foreach (var col in queue.Columns)
                if (col?.Buses != null)
                    foreach (var b in col.Buses)
                        if (b.ConnectedId == id) b.ConnectedId = -1;
            if (session != null)
            {
                session.Document.TopSection = JObject.FromObject(queue);
                session.MarkDirty();
            }
        }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.BusConnectionTests`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/BusBuddies/Editor/TopSection/BusConnection.cs Assets/BusBuddies/Tests/Editor/BusConnectionTests.cs
git commit -m "feat(bb): BusConnection static ops for connected buses"
```

---

### Task 11: `BusConnection.ConnectionsDeadlock` soft-lock model

**Files:**
- Modify: `Assets/BusBuddies/Editor/TopSection/BusConnection.cs` (add `ConnectionsDeadlock`)
- Test: `Assets/BusBuddies/Tests/Editor/BusConnectionDeadlockTests.cs`

**Interfaces:**
- Produces: `bool BusConnection.ConnectionsDeadlock(BusQueueData)` — color-blind structural soft-lock check.

**Model:** heads advance monotonically; an unconnected head clears freely; a connected head clears only when its partner is simultaneously at ITS column head; same-column pairs are never linkable (they can never both be head) so they remain "locked" → deadlock. If any head can never reach the top → deadlock.

- [ ] **Step 1: Write the failing test**

```csharp
// Assets/BusBuddies/Tests/Editor/BusConnectionDeadlockTests.cs
using Hoppa.BusBuddies.Editor;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusConnectionDeadlockTests
    {
        private static BusColumn Col(int n)
        {
            var c = new BusColumn();
            for (int i = 0; i < n; i++) c.Buses.Add(new BusEntry());
            return c;
        }

        [Test]
        public void NoConnections_NotDeadlocked()
        {
            var q = new BusQueueData(); q.Columns.Add(Col(2)); q.Columns.Add(Col(2));
            Assert.IsFalse(BusConnection.ConnectionsDeadlock(q));
        }

        [Test]
        public void HeadsPair_NotDeadlocked()
        {
            // Both partners at their column heads (pos 0) → clear together.
            var q = new BusQueueData(); q.Columns.Add(Col(2)); q.Columns.Add(Col(2));
            q.Columns[0].Buses[0].ConnectedId = 0;
            q.Columns[1].Buses[0].ConnectedId = 0;
            Assert.IsFalse(BusConnection.ConnectionsDeadlock(q));
        }

        [Test]
        public void CrossingPairs_Deadlocked()
        {
            // col0: [A(id0), B(id1)]  col1: [C(id1), D(id0)]
            // A(head col0) needs D(pos1 col1); C(head col1) needs B(pos1 col0). Neither
            // partner is at its head, and neither column can advance → soft-lock.
            var q = new BusQueueData(); q.Columns.Add(Col(2)); q.Columns.Add(Col(2));
            q.Columns[0].Buses[0].ConnectedId = 0;
            q.Columns[0].Buses[1].ConnectedId = 1;
            q.Columns[1].Buses[0].ConnectedId = 1;
            q.Columns[1].Buses[1].ConnectedId = 0;
            Assert.IsTrue(BusConnection.ConnectionsDeadlock(q));
        }

        [Test]
        public void SameColumnPair_Deadlocked()
        {
            // Two buses in one column connected → can never both be head.
            var q = new BusQueueData(); q.Columns.Add(Col(2));
            q.Columns[0].Buses[0].ConnectedId = 0;
            q.Columns[0].Buses[1].ConnectedId = 0;
            Assert.IsTrue(BusConnection.ConnectionsDeadlock(q));
        }
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.BusConnectionDeadlockTests`.
Expected: FAIL — method missing.

- [ ] **Step 3: Implement (adapts `YarnSpoolConnection.ConnectionsDeadlock`)**

Add to `BusConnection`. Note the ONE change from the Yarn version: a same-column complete pair is NOT skipped — instead it is marked permanently unsatisfiable so it deadlocks (Yarn couldn't produce same-column pairs; BB can, and they're always soft-locks).

```csharp
        public static bool ConnectionsDeadlock(BusQueueData queue)
        {
            int n = queue.Columns.Count;
            if (n == 0) return false;

            var len  = new int[n];
            var pCol = new int[n][];
            var pPos = new int[n][];
            var sameColLocked = new bool[n][];   // a member whose partner is in its own column
            for (int c = 0; c < n; c++)
            {
                int count = queue.Columns[c]?.Buses?.Count ?? 0;
                len[c]  = count;
                pCol[c] = new int[count];
                pPos[c] = new int[count];
                sameColLocked[c] = new bool[count];
                for (int p = 0; p < count; p++) { pCol[c][p] = -1; pPos[c][p] = -1; }
            }

            BuildConnInfo(queue, out var members, out _);
            foreach (var kv in members)
            {
                if (kv.Value.Count != 2) continue;   // incomplete/over-linked handled by the rule
                var a = kv.Value[0];
                var b = kv.Value[1];
                if (a.col == b.col)
                {
                    // Two buses in one column can never both be head → permanent lock.
                    sameColLocked[a.col][a.pos] = true;
                    sameColLocked[b.col][b.pos] = true;
                    continue;
                }
                pCol[a.col][a.pos] = b.col; pPos[a.col][a.pos] = b.pos;
                pCol[b.col][b.pos] = a.col; pPos[b.col][b.pos] = a.pos;
            }

            var head = new int[n];
            bool progress = true;
            while (progress)
            {
                progress = false;
                for (int c = 0; c < n; c++)
                {
                    while (head[c] < len[c])
                    {
                        int p = head[c];
                        if (sameColLocked[c][p]) break;                 // never clears
                        int pc = pCol[c][p];
                        if (pc < 0) { head[c]++; progress = true; continue; }               // unconnected
                        if (head[pc] == pPos[c][p]) { head[c]++; head[pc]++; progress = true; continue; } // pair ready
                        break;                                          // partner not at its head yet
                    }
                }
            }

            for (int c = 0; c < n; c++)
                if (head[c] < len[c]) return true;
            return false;
        }
```

- [ ] **Step 4: Run to verify pass**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.BusConnectionDeadlockTests`.
Expected: PASS (all 4).

- [ ] **Step 5: Commit**

```bash
git add Assets/BusBuddies/Editor/TopSection/BusConnection.cs Assets/BusBuddies/Tests/Editor/BusConnectionDeadlockTests.cs
git commit -m "feat(bb): connected-bus soft-lock detector (ConnectionsDeadlock)"
```

---

### Task 12: `BBConnectedBusRule` validation + profile wiring

**Files:**
- Create: `Assets/BusBuddies/Editor/Validation/BBConnectedBusRule.cs`
- Create asset: `Assets/BusBuddies/Data/Config/Rules/BBConnectedBusRule.asset`
- Modify asset: `Assets/BusBuddies/Data/Config/BusBuddiesProfile.asset` (append to `_rules`)
- Test: `Assets/BusBuddies/Tests/Editor/BBConnectedBusRuleTests.cs`

**Interfaces:**
- Consumes: `BusConnection.ConnectionsDeadlock`, `ValidationRuleBase`, `ValidationScope.Level`, `ValidationEntry`, `ValidationSeverity`
- Produces: `BBConnectedBusRule : ValidationRuleBase` (Scope = Level)

- [ ] **Step 1: Write the failing test**

```csharp
// Assets/BusBuddies/Tests/Editor/BBConnectedBusRuleTests.cs
using System.Linq;
using Hoppa.BusBuddies.Editor;
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BBConnectedBusRuleTests
    {
        private static ValidationContext Ctx(BusQueueData queue)
        {
            var doc = new LevelDocument { TopSection = JObject.FromObject(queue) };
            // Match the real ValidationContext constructor/shape used by BBColorBalanceRuleTests.
            return new ValidationContext { Document = doc, Grid = doc.Grid };
        }

        private static BBConnectedBusRule Rule()
        {
            var r = ScriptableObject.CreateInstance<BBConnectedBusRule>();
            r.Configure("bb.connected");
            return r;
        }

        private static BusColumn Col(int n)
        {
            var c = new BusColumn();
            for (int i = 0; i < n; i++) c.Buses.Add(new BusEntry());
            return c;
        }

        [Test]
        public void ValidPair_NoErrors()
        {
            var q = new BusQueueData(); q.Columns.Add(Col(1)); q.Columns.Add(Col(1));
            q.Columns[0].Buses[0].ConnectedId = 0;
            q.Columns[1].Buses[0].ConnectedId = 0;
            var errs = Rule().Evaluate(Ctx(q)).Where(e => e.Severity == ValidationSeverity.Error).ToList();
            Assert.IsEmpty(errs);
        }

        [Test]
        public void IncompletePair_Errors()
        {
            var q = new BusQueueData(); q.Columns.Add(Col(1));
            q.Columns[0].Buses[0].ConnectedId = 0;
            var errs = Rule().Evaluate(Ctx(q)).Where(e => e.Severity == ValidationSeverity.Error).ToList();
            Assert.IsNotEmpty(errs);
        }

        [Test]
        public void CrossingPairs_ErrorsWithSoftLock()
        {
            var q = new BusQueueData(); q.Columns.Add(Col(2)); q.Columns.Add(Col(2));
            q.Columns[0].Buses[0].ConnectedId = 0; q.Columns[0].Buses[1].ConnectedId = 1;
            q.Columns[1].Buses[0].ConnectedId = 1; q.Columns[1].Buses[1].ConnectedId = 0;
            var errs = Rule().Evaluate(Ctx(q)).Where(e => e.Severity == ValidationSeverity.Error).ToList();
            Assert.IsNotEmpty(errs);
        }
    }
}
```

> Before running, open `BBColorBalanceRuleTests.cs` and copy its exact `ValidationContext` construction (field names / ctor). Use the same shape here.

- [ ] **Step 2: Run to verify fail**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.BBConnectedBusRuleTests`.
Expected: FAIL — rule missing.

- [ ] **Step 3: Implement (mirrors `YarnConnectedSpoolRule`, minus adjacency, plus same-column error)**

```csharp
// Assets/BusBuddies/Editor/Validation/BBConnectedBusRule.cs
using System.Collections.Generic;
using System.Linq;
using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Validates Connected Buses: every bus carrying a ConnectedId (>= 0) must be one of
    // exactly two members forming a pair. The game pairs any two buses by coordinate, so
    // there is NO adjacency constraint — but same-column pairs (can never both be head)
    // and link-crossing soft-locks are rejected. Scope = Level.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Validation/Connected Bus")]
    public sealed class BBConnectedBusRule : ValidationRuleBase
    {
        public override ValidationScope Scope => ValidationScope.Level;

        public override IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx)
        {
            var queue = ctx.Document?.TopSection?.ToObject<BusQueueData>();
            if (queue?.Columns == null) yield break;

            BusConnection.BuildConnInfo(queue, out var members, out _);
            if (members.Count == 0) yield break;

            var displayNum = members.Keys.OrderBy(k => k)
                .Select((id, i) => (id, n: i + 1))
                .ToDictionary(t => t.id, t => t.n);

            foreach (var kv in members.OrderBy(k => k.Key))
            {
                int n = displayNum[kv.Key];
                var mem = kv.Value;

                if (mem.Count == 1)
                {
                    yield return new ValidationEntry(Id, ValidationSeverity.Error,
                        $"Connected Bus Pair {n} is incomplete. Select a second bus to connect.");
                    continue;
                }
                if (mem.Count > 2)
                {
                    yield return new ValidationEntry(Id, ValidationSeverity.Error,
                        $"Connected Bus Pair {n} has {mem.Count} members; a pair must have exactly two.");
                    continue;
                }
                if (mem[0].col == mem[1].col)
                {
                    yield return new ValidationEntry(Id, ValidationSeverity.Error,
                        $"Connected Bus Pair {n} links two buses in the same column; they can never both reach the front. Connect buses in different columns.");
                }
            }

            if (BusConnection.ConnectionsDeadlock(queue))
                yield return new ValidationEntry(Id, ValidationSeverity.Error,
                    "Connected buses form a soft-lock: a connected pair can never both reach the front (links cross). Re-order or re-connect so the links don't cross.");
        }
    }
}
```

- [ ] **Step 4: Run to verify pass**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.BBConnectedBusRuleTests`.
Expected: PASS.

- [ ] **Step 5: Create the asset + append to the profile `_rules`**

Create `BBConnectedBusRule.asset` (menu `Hoppa/BusBuddies/Validation/Connected Bus`) at `Assets/BusBuddies/Data/Config/Rules/`, set its `_id` to `"bb.connected"` (via `Configure` at author time / the `_id` serialized field). Append it to `BusBuddiesProfile.asset`'s `_rules` array (MCP `assets-modify` — add the element, don't replace the existing `BBColorBalanceRule`).

- [ ] **Step 6: Commit**

```bash
git add Assets/BusBuddies/Editor/Validation/BBConnectedBusRule.cs Assets/BusBuddies/Tests/Editor/BBConnectedBusRuleTests.cs "Assets/BusBuddies/Data/Config/Rules/BBConnectedBusRule.asset" "Assets/BusBuddies/Data/Config/BusBuddiesProfile.asset"
git commit -m "feat(bb): BBConnectedBusRule validates pairs + blocks soft-locks"
```

---

### Task 13: Exporter emits `ConnectedBuses`

**Files:**
- Modify: `Assets/BusBuddies/Editor/Exporters/BusBuddiesGameLevelExporter.cs` (add `BuildConnectedBuses`; ensure the `config` JObject includes it — see Task 7 Step 3)
- Test: `Assets/BusBuddies/Tests/Editor/BusBuddiesConnectedBusExportTests.cs`

**Interfaces:**
- Consumes: `BusConnection.BuildConnInfo`
- Produces: `LevelConfig.ConnectedBuses` = `[{ "BusA":{"ColumnIndex":c,"Index":i}, "BusB":{...} }]`

- [ ] **Step 1: Write the failing test**

```csharp
// Assets/BusBuddies/Tests/Editor/BusBuddiesConnectedBusExportTests.cs
using Hoppa.BusBuddies.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusBuddiesConnectedBusExportTests
    {
        [Test]
        public void BuildConnectedBuses_EmitsPairCoordinates()
        {
            var q = new BusQueueData();
            var c0 = new BusColumn(); c0.Buses.Add(new BusEntry()); c0.Buses.Add(new BusEntry { ConnectedId = 5 });
            var c1 = new BusColumn(); c1.Buses.Add(new BusEntry { ConnectedId = 5 });
            q.Columns.Add(c0); q.Columns.Add(c1);

            var arr = BusBuddiesGameLevelExporter.BuildConnectedBuses(JObject.FromObject(q));
            Assert.AreEqual(1, arr.Count);
            var pair = arr[0];
            // members: (col0,pos1) and (col1,pos0), in BuildConnInfo column-then-pos order.
            Assert.AreEqual(0, (int)pair["BusA"]["ColumnIndex"]);
            Assert.AreEqual(1, (int)pair["BusA"]["Index"]);
            Assert.AreEqual(1, (int)pair["BusB"]["ColumnIndex"]);
            Assert.AreEqual(0, (int)pair["BusB"]["Index"]);
        }

        [Test]
        public void BuildConnectedBuses_SkipsIncompleteGroups()
        {
            var q = new BusQueueData();
            var c0 = new BusColumn(); c0.Buses.Add(new BusEntry { ConnectedId = 2 });
            q.Columns.Add(c0);
            Assert.AreEqual(0, BusBuddiesGameLevelExporter.BuildConnectedBuses(JObject.FromObject(q)).Count);
        }
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.BusBuddiesConnectedBusExportTests`.
Expected: FAIL — method missing.

- [ ] **Step 3: Implement**

```csharp
        // BusQueueData connected-id groups → ConnectedBuses[] of {BusA,BusB} coordinate
        // pairs. Only complete pairs (exactly two members) are emitted; incomplete groups
        // are dropped (BBConnectedBusRule errors on them before export).
        public static JArray BuildConnectedBuses(JObject topSection)
        {
            var result = new JArray();
            BusQueueData queue = null;
            if (topSection != null)
            {
                try { queue = topSection.ToObject<BusQueueData>(); }
                catch { queue = null; }
            }
            if (queue?.Columns == null) return result;

            BusConnection.BuildConnInfo(queue, out var members, out _);
            foreach (var kv in members)
            {
                if (kv.Value.Count != 2) continue;
                var a = kv.Value[0];
                var b = kv.Value[1];
                result.Add(new JObject
                {
                    ["BusA"] = new JObject { ["ColumnIndex"] = a.col, ["Index"] = a.pos },
                    ["BusB"] = new JObject { ["ColumnIndex"] = b.col, ["Index"] = b.pos },
                });
            }
            return result;
        }
```

Ensure `Export`'s `config` JObject includes `["ConnectedBuses"] = BuildConnectedBuses(document.TopSection)` (Task 7 Step 3 shows the full block).

- [ ] **Step 4: Run to verify pass**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.BusBuddiesConnectedBusExportTests`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/BusBuddies/Editor/Exporters/BusBuddiesGameLevelExporter.cs Assets/BusBuddies/Tests/Editor/BusBuddiesConnectedBusExportTests.cs
git commit -m "feat(bb): export ConnectedBuses coordinate pairs"
```

---

### Task 14: Importer reads `ConnectedBuses`

**Files:**
- Modify: `Assets/BusBuddies/Editor/Exporters/BusBuddiesGameLevelImporter.cs` (`Import`, after the bus queue is built)
- Test: `Assets/BusBuddies/Tests/Editor/BusBuddiesConnectedBusRoundTripTests.cs`

**Interfaces:**
- Consumes: `BusBuddiesGameLevelExporter.BuildConnectedBuses`, `BusEntry.ConnectedId`

- [ ] **Step 1: Write the failing test (export→import round-trip)**

```csharp
// Assets/BusBuddies/Tests/Editor/BusBuddiesConnectedBusRoundTripTests.cs
using Hoppa.BusBuddies.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace Hoppa.BusBuddies.Tests
{
    public sealed class BusBuddiesConnectedBusRoundTripTests
    {
        [Test]
        public void ConnectedBuses_SurviveRoundTrip_AsAPair()
        {
            // Two columns, one bus each, connected.
            var q = new BusQueueData();
            var c0 = new BusColumn(); c0.Buses.Add(new BusEntry { ColorId = "red",  Capacity = 5, ConnectedId = 0 });
            var c1 = new BusColumn(); c1.Buses.Add(new BusEntry { ColorId = "blue", Capacity = 5, ConnectedId = 0 });
            q.Columns.Add(c0); q.Columns.Add(c1);
            var top = JObject.FromObject(q);

            var root = new JObject
            {
                ["SlotsAmount"] = 5, ["Width"] = 1, ["Height"] = 1,
                ["PixelColors"] = new JArray { 0 },
                ["BusColumnConfigs"] = BusBuddiesGameLevelExporter.BuildBusColumnConfigsForTest(top),
                ["ConnectedBuses"] = BusBuddiesGameLevelExporter.BuildConnectedBuses(top),
            };

            var imported = BusBuddiesGameLevelImporter.Import(root.ToString(), "level_1");
            var back = imported.Document.TopSection.ToObject<BusQueueData>();

            int idA = back.Columns[0].Buses[0].ConnectedId;
            int idB = back.Columns[1].Buses[0].ConnectedId;
            Assert.GreaterOrEqual(idA, 0, "bus A should be connected");
            Assert.AreEqual(idA, idB, "both buses share one connection id");
        }
    }
}
```

- [ ] **Step 2: Run to verify fail**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.BusBuddiesConnectedBusRoundTripTests`.
Expected: FAIL — importer ignores `ConnectedBuses`.

- [ ] **Step 3: Read ConnectedBuses after the queue is built**

In `Import`, after `doc.TopSection = JObject.FromObject(queue);` add (before the `return`):

```csharp
            // Connected buses: each {BusA,BusB} pair gets a fresh shared ConnectedId,
            // set on both referenced (ColumnIndex, Index) buses. Rebuild TopSection after.
            if (root["ConnectedBuses"] is JArray connections)
            {
                int nextId = 0;
                foreach (var conn in connections)
                {
                    var a = conn["BusA"]; var b = conn["BusB"];
                    if (a == null || b == null) continue;
                    var busA = BusAt(queue, a.Value<int?>("ColumnIndex"), a.Value<int?>("Index"));
                    var busB = BusAt(queue, b.Value<int?>("ColumnIndex"), b.Value<int?>("Index"));
                    if (busA == null || busB == null) continue;
                    busA.ConnectedId = busB.ConnectedId = nextId++;
                }
                doc.TopSection = JObject.FromObject(queue);
            }
```

Add the helper to the class:

```csharp
        private static BusEntry BusAt(BusQueueData queue, int? col, int? index)
        {
            if (col == null || index == null) return null;
            if (col < 0 || col >= queue.Columns.Count) return null;
            var buses = queue.Columns[col.Value]?.Buses;
            if (buses == null || index < 0 || index >= buses.Count) return null;
            return buses[index.Value];
        }
```

- [ ] **Step 4: Run to verify pass**

Run: MCP `tests-run`, EditMode, testClass `Hoppa.BusBuddies.Tests.BusBuddiesConnectedBusRoundTripTests`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/BusBuddies/Editor/Exporters/BusBuddiesGameLevelImporter.cs Assets/BusBuddies/Tests/Editor/BusBuddiesConnectedBusRoundTripTests.cs
git commit -m "feat(bb): import ConnectedBuses back into shared ConnectedId pairs"
```

---

### Task 15: Connect/disconnect UI in `BusBuddiesQueuePanel`

**Files:**
- Modify: `Assets/BusBuddies/Editor/TopSection/BusBuddiesQueuePanel.cs`

No unit test (IMGUI); logic already covered by `BusConnectionTests`/`BusConnectionDeadlockTests`. Ends with a manual/lead eyeball.

- [ ] **Step 1: Read the panel to learn its layout + session access**

Open `BusBuddiesQueuePanel.cs` fully. Identify: how it iterates columns/buses to draw each bus rect, how it reaches the `LevelEditorSession` and the live `BusQueueData`, and where the per-bus controls (color swatch, capacity, Hidden toggle) are drawn.

- [ ] **Step 2: Add connect-mode state + a Connect/Disconnect affordance**

Add a `_pendingAnchor` state `(int col, int pos)?` on the panel. Per bus, draw a small "🔗" button:
- If the bus is unconnected and no anchor is pending → clicking sets `_pendingAnchor = (col,pos)`.
- If an anchor is pending and this is a DIFFERENT bus → clicking completes: `int id = BusConnection.AllocId(queue); BusConnection.Connect(session, queue, anchorBus, id); BusConnection.Connect(session, queue, thisBus, id); _pendingAnchor = null;` — guard with `BusConnection.ConnectionsDeadlock` on a trial copy and refuse (log + keep anchor) if it would soft-lock, mirroring YarnTwist's `CompletingDeadlocks` intent.
- If the bus is already connected → the button becomes "Disconnect": `BusConnection.DisconnectGroup(session, queue, bus.ConnectedId)`.

Draw the pair's display number (`BusConnection.BuildConnInfo` + `DisplayNumber`) as a small badge on each connected bus, so the designer can see which two are linked.

Use this exact deadlock-guard pattern for completion (trial on a serialized clone so a rejected attempt doesn't mutate live data):

```csharp
            // Trial: would connecting anchor↔target soft-lock? Test on a clone.
            var clone = session.Document.TopSection.ToObject<BusQueueData>();
            int trialId = BusConnection.AllocId(clone);
            clone.Columns[anchor.col].Buses[anchor.pos].ConnectedId = trialId;
            clone.Columns[col].Buses[pos].ConnectedId = trialId;
            if (BusConnection.ConnectionsDeadlock(clone))
            {
                Debug.LogWarning("[BusBuddies] That connection would create a soft-lock; pick a different bus.");
            }
            else
            {
                int id = BusConnection.AllocId(queue);
                BusConnection.Connect(session, queue, queue.Columns[anchor.col].Buses[anchor.pos], id);
                BusConnection.Connect(session, queue, queue.Columns[col].Buses[pos], id);
                _pendingAnchor = null;
            }
```

- [ ] **Step 3: Compile check**

Run: MCP `assets-refresh` + `console-get-logs`. Expected: no errors.

- [ ] **Step 4: Manual eyeball (lead/verifier step)**

In the editor: click 🔗 on bus A, then bus B in another column → both show the same pair badge; export and confirm the JSON has a `ConnectedBuses` entry; try to connect two buses that cross → the warning fires and the connection is refused. (Custom window — not MCP-screenshot-able.)

- [ ] **Step 5: Commit**

```bash
git add Assets/BusBuddies/Editor/TopSection/BusBuddiesQueuePanel.cs
git commit -m "feat(bb): connect/disconnect UI for connected buses in the queue panel"
```

---

## Phase E — Ship

### Task 16: Version bump, full-suite green, deploy notes

**Files:**
- Modify: `Packages/com.hoppa.leveleditor.core/package.json` (version)
- Modify: `CURRENT_TASK.md`, `SESSION_NOTES.md` (deploy + the HiddenPixels index flag for the game team)

- [ ] **Step 1: Bump the package version**

Edit `package.json` `"version"` from `0.9.0` to `0.10.0` (additive Layer-1 API: `ICellFlagPainter`, `GameProfile.FlagPainter`, `GridEditTool.Hide`).

- [ ] **Step 2: Run the FULL EditMode suite**

Run: MCP `tests-run`, EditMode, no class filter (all assemblies).
Expected: PASS, 0 failures beyond any pre-existing known-failing YAK prompt test (confirm the count matches the pre-change baseline + the new tests).

- [ ] **Step 3: Record deploy state + the game-team flag**

In `SESSION_NOTES.md` note: (a) editor-core changes for the 3 mechanics shipped; (b) package `0.10.0` — BB game must re-pin + re-mirror the BB Layer-2 stack (`BBPixelCell`, `BusBuddiesHiddenPixelPainter`, `BusConnection`, `BBConnectedBusRule`, queue panel, exporter/importer, profile `_flagPainter` + `_rules`) and the Layer-1 additions travel via the package; (c) **flag for the game team:** `BUBPixelService` reads `HiddenPixels` with `x*width+y` but `PixelColors` with `y*width+x` — we mirror the former; confirm this is intended (square grids only) and reconcile if it's a bug; (d) needs one in-game spot-check that a hidden pixel conceals the intended cell.

- [ ] **Step 4: Commit + tag**

```bash
git add Packages/com.hoppa.leveleditor.core/package.json SESSION_NOTES.md CURRENT_TASK.md
git commit -m "chore(package): 0.10.0 — hidden pixels, connected buses, flag-paint tool"
git tag v0.10.0
```

(Push + game re-mirror + game compile-check = lead step, per the push gate.)

---

## Self-Review

**Spec coverage:**
- Hidden Buses (verify) → Task 9. ✓
- Hidden Pixels: model → Task 4; render → Task 6; paint gesture (generic Layer-1) → Tasks 1–3, 5; export → Task 7; import → Task 8. ✓
- Connected Buses: static ops → Task 10; deadlock → Task 11; validation + wiring → Task 12; export → Task 13; import → Task 14; UI → Task 15. ✓
- Deployment (package bump, re-mirror, game flag) → Task 16. ✓
- Testing (per-feature round-trips + validation branches + soft-lock fixture) → Tasks 4,5,7,8,9,10,11,12,13,14. ✓
- Out-of-scope items (no autofill-gen, no analyzer modeling, no adjacency) are respected — no task adds them. ✓

**Placeholder scan:** No TBD/"handle edge cases"/"similar to Task N". Two explicit "check the real signature" notes (LevelEditorSession ctor, ValidationContext shape) are deliberate — the worker must match existing local types; the tests show exactly what behavior is required.

**Type consistency:** `ICellFlagPainter`/`CellFlagPainterAsset` (`ToolLabel`/`ToolTooltip`/`IsFlagged`/`SetFlag`) used identically in Tasks 1,2,3,5. `BusConnection` method names (`BuildConnInfo`/`AllocId`/`DisplayNumber`/`Connect`/`DisconnectGroup`/`ConnectionsDeadlock`) consistent across Tasks 10–15. Exporter statics (`BuildHiddenPixels`/`BuildConnectedBuses`/`BuildBusColumnConfigsForTest`) match their call sites in Tasks 7,9,13,14. `BusEntry.ConnectedId` int/`-1` sentinel used uniformly.

**Known risk:** Tasks 2/3/6/15 are IMGUI and rely on manual verification — flagged in each and gathered for the verifier/lead. All pure logic is unit-tested.
