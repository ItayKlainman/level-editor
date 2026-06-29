# Bus Buddies — Sub-Phase 1a (Engine) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Build the pure-C# Bus Buddies simulator stack (`BusLevelModel / BusSimState / BusSolver / BusAveragePlayer`) plus its cell data + queue data classes, with headless EditMode NUnit tests that prove the §2 mechanic exactly.

**Architecture:** A new `Assets/BusBuddies/` folder mirrors `Assets/YAK/`. Layer-2 data classes (`BBPixelCell`, `BBEmptyCell`, `BusQueueData`) live in a `Hoppa.BusBuddies.Runtime` assembly that references only `Hoppa.LevelEditor.Core.Runtime`. The simulator lives under `Runtime/Sim/` in that same assembly (matching YAK, which keeps its `Sim/` inside `Hoppa.YAK.Runtime`) but is written as pure C# with no `UnityEngine` usage so it is fully headless-testable. Unlike YAK there is **no gravity**: the board is a full mutable 2D occupancy (`int[W*H]`), accessibility is a 4-way border flood, and a move pulls the top bus of a queue column into a stationary Active Row that auto-releases passengers to quiescence.

**Tech Stack:** Unity 6 (editor-core project), C# (no `UnityEngine` in Runtime/Sim or cells), Newtonsoft.Json (data attributes only), NUnit EditMode tests, Unity MCP CLI (`unity-mcp-cli`) for refresh + test runs.

## Global Constraints

- **Write all `.cs` and `.asmdef` files directly with the Write/Edit tools — NEVER via shell** (PowerShell/Bash). Shell-written files cause an approval bottleneck and encoding issues.
- **`Assets/BusBuddies/Runtime/Sim/*` is pure C#: NO `UnityEngine` / `UnityEditor` usings, no `MonoBehaviour`, no `ScriptableObject`.** The cell classes (`BBPixelCell`, `BBEmptyCell`) and `BusQueueData` are also pure C# (Newtonsoft attributes only). This mirrors YAK, whose `Sim/` files have no engine usings even though they share `Hoppa.YAK.Runtime` — the BusBuddies Sim files do the same so they stay headless-testable.
- **Grid origin is `y*W+x`, row 0 = BOTTOM** (`bottomUp`). This matches `GridData<TCell>.ToIndex` (`y*Width+x`) and the YAK exporter convention. Every grid index in this plan is `y*W+x`.
- **Accessibility is 4-way ORTHOGONAL only.** Diagonal neighbours never grant access. The implicit region outside the grid border counts as flooded (so a block on the literal grid edge is accessible); interior empty pockets not reachable from the border do NOT.
- **Deterministic seeded RNG only.** `BusAveragePlayer` uses `new System.Random(seed)` derived deterministically per run. No `DateTime`/`Guid`/unseeded randomness in logic paths under test.
- **Tests are NUnit EditMode** under `Assets/BusBuddies/Tests/Editor/`, assembly `Hoppa.BusBuddies.Editor.Tests`.
- **No absolute paths in any committed file.**

### Running tests (every "run the test" step references this)

After adding or editing any `.cs`/`.asmdef`, force Unity to recompile:

```
npx --yes unity-mcp-cli run-system-tool assets-refresh --json "{}"
```

Run the whole BusBuddies test assembly (EditMode):

```
npx --yes unity-mcp-cli run-system-tool tests-run --json "{\"testMode\":\"EditMode\",\"assemblyNames\":[\"Hoppa.BusBuddies.Editor.Tests\"]}"
```

Run a single test class (preferred while iterating — append `testNames`):

```
npx --yes unity-mcp-cli run-system-tool tests-run --json "{\"testMode\":\"EditMode\",\"assemblyNames\":[\"Hoppa.BusBuddies.Editor.Tests\"],\"testNames\":[\"Hoppa.BusBuddies.Editor.Tests.<ClassName>\"]}"
```

Equivalent alternative: invoke the `tests-run` Skill (and `assets-refresh` Skill) with the same arguments. Always `assets-refresh` first when a `.cs` changed; a stale compile makes tests-run report old results.

---

## File Structure

| File | Responsibility |
|---|---|
| `Assets/BusBuddies/Runtime/BusBuddies.Runtime.asmdef` | Runtime assembly `Hoppa.BusBuddies.Runtime`; references `Hoppa.LevelEditor.Core.Runtime`. Holds cells, queue data, and the Sim stack. |
| `Assets/BusBuddies/Runtime/BBPixelCell.cs` | `BBPixelCell : IColoredCell` — the coloured block (`CellTypeId="bb.pixel"`, `string ColorId`). |
| `Assets/BusBuddies/Runtime/BBEmptyCell.cs` | `BBEmptyCell : ICellData` — "no block" (`CellTypeId="bb.empty"`). |
| `Assets/BusBuddies/Runtime/BusQueueData.cs` | `BusEntry` / `BusColumn` / `BusQueueData` — the `LevelDocument.TopSection` payload (ordered buses per column). |
| `Assets/BusBuddies/Runtime/Sim/BusLevelModel.cs` | Immutable interned level snapshot; `Build(GridData,BusQueueData,activeSlots)`, `FromArrays(...)`, `IsColorBalanced()`. |
| `Assets/BusBuddies/Runtime/Sim/BusSimState.cs` | Mutable per-playthrough state: occupancy, accessibility flood, Active Row, `ApplyMove`/`ResolveReleases`/`IsWin`/`IsDeadlock`/`Clone`/`Key`. |
| `Assets/BusBuddies/Runtime/Sim/BusSolver.cs` | Budgeted exact DFS win-path finder; `Outcome{Solvable,Unsolvable,BudgetExceeded}`. |
| `Assets/BusBuddies/Runtime/Sim/BusAveragePlayer.cs` | Seeded Monte-Carlo greedy difficulty estimator → `WinRate`, `Aps`. |
| `Assets/BusBuddies/Tests/Editor/Hoppa.BusBuddies.Editor.Tests.asmdef` | EditMode test assembly. |
| `Assets/BusBuddies/Tests/Editor/BusScaffoldTests.cs` | Task 1 trivial passing test (proves the assemblies compile + run). |
| `Assets/BusBuddies/Tests/Editor/BusCellTests.cs` | Cell data contract tests. |
| `Assets/BusBuddies/Tests/Editor/BusQueueDataTests.cs` | Queue data defaults/shape tests. |
| `Assets/BusBuddies/Tests/Editor/BusLevelModelTests.cs` | `FromArrays`, `Build`, `IsColorBalanced` tests. |
| `Assets/BusBuddies/Tests/Editor/BusAccessibilityTests.cs` | Flood/accessibility incl. locked-diagonal + donut pocket. |
| `Assets/BusBuddies/Tests/Editor/BusDynamicsTests.cs` | No-gravity removal, ResolveReleases (partial bus, coexistence), win, deadlock, clone, key. |
| `Assets/BusBuddies/Tests/Editor/BusSolverTests.cs` | Solver win-path + replay→IsWin; unsolvable. |
| `Assets/BusBuddies/Tests/Editor/BusAveragePlayerTests.cs` | Determinism + win-rate sanity. |

**Reference conventions copied verbatim from YAK:** `Hoppa.YAK.Runtime` asmdef (`references:["Hoppa.LevelEditor.Core.Runtime"]`, `noEngineReferences:false`); `Hoppa.YAK.Editor.Tests` asmdef (`includePlatforms:["Editor"]`, `overrideReferences:true`, `precompiledReferences:["nunit.framework.dll","Newtonsoft.Json.dll"]`, `autoReferenced:false`, `defineConstraints:["UNITY_INCLUDE_TESTS"]`); cells implement `Hoppa.LevelEditor.Core.IColoredCell`/`ICellData` with `[JsonIgnore]` on `CellTypeId` and `[JsonProperty]` on data; the Sim namespace is `Hoppa.BusBuddies.Sim` (matching `Hoppa.YAK.Sim`).

---

## Task 1: Scaffold (assemblies + folders + trivial test)

**Files:**
- Create: `Assets/BusBuddies/Runtime/BusBuddies.Runtime.asmdef`
- Create: `Assets/BusBuddies/Tests/Editor/Hoppa.BusBuddies.Editor.Tests.asmdef`
- Test: `Assets/BusBuddies/Tests/Editor/BusScaffoldTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces: assembly `Hoppa.BusBuddies.Runtime` (namespace root `Hoppa.BusBuddies`) and test assembly `Hoppa.BusBuddies.Editor.Tests`.

- [ ] **Step 1: Write the failing test**

`Assets/BusBuddies/Tests/Editor/BusScaffoldTests.cs`:

```csharp
using NUnit.Framework;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BusScaffoldTests
    {
        [Test]
        public void Assembly_CompilesAndRuns()
        {
            Assert.AreEqual(4, 2 + 2);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

The test cannot even compile/run until both asmdefs exist. Create them now (Steps 3) before running. (A run before the asmdefs exist returns "no tests found for assembly Hoppa.BusBuddies.Editor.Tests" — that is the expected initial failure.)

Run:
```
npx --yes unity-mcp-cli run-system-tool assets-refresh --json "{}"
npx --yes unity-mcp-cli run-system-tool tests-run --json "{\"testMode\":\"EditMode\",\"assemblyNames\":[\"Hoppa.BusBuddies.Editor.Tests\"]}"
```
Expected: FAIL / no-tests-found (assembly does not exist yet).

- [ ] **Step 3: Create the two asmdefs**

`Assets/BusBuddies/Runtime/BusBuddies.Runtime.asmdef`:

```json
{
    "name": "Hoppa.BusBuddies.Runtime",
    "rootNamespace": "Hoppa.BusBuddies",
    "references": [
        "Hoppa.LevelEditor.Core.Runtime"
    ],
    "includePlatforms": [],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": false,
    "precompiledReferences": [],
    "autoReferenced": true,
    "defineConstraints": [],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`Assets/BusBuddies/Tests/Editor/Hoppa.BusBuddies.Editor.Tests.asmdef`:

```json
{
    "name": "Hoppa.BusBuddies.Editor.Tests",
    "rootNamespace": "Hoppa.BusBuddies.Editor.Tests",
    "references": [
        "Hoppa.LevelEditor.Core.Runtime",
        "Hoppa.BusBuddies.Runtime",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": [
        "Editor"
    ],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll",
        "Newtonsoft.Json.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": [
        "UNITY_INCLUDE_TESTS"
    ],
    "versionDefines": [],
    "noEngineReferences": false
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```
npx --yes unity-mcp-cli run-system-tool assets-refresh --json "{}"
npx --yes unity-mcp-cli run-system-tool tests-run --json "{\"testMode\":\"EditMode\",\"assemblyNames\":[\"Hoppa.BusBuddies.Editor.Tests\"],\"testNames\":[\"Hoppa.BusBuddies.Editor.Tests.BusScaffoldTests\"]}"
```
Expected: PASS (1 test).

- [ ] **Step 5: Commit**

```bash
git add Assets/BusBuddies/Runtime/BusBuddies.Runtime.asmdef Assets/BusBuddies/Tests/Editor/Hoppa.BusBuddies.Editor.Tests.asmdef Assets/BusBuddies/Tests/Editor/BusScaffoldTests.cs
git commit -m "feat(busbuddies): scaffold runtime + test assemblies (1a)"
```

---

## Task 2: Cell data classes

**Files:**
- Create: `Assets/BusBuddies/Runtime/BBPixelCell.cs`
- Create: `Assets/BusBuddies/Runtime/BBEmptyCell.cs`
- Test: `Assets/BusBuddies/Tests/Editor/BusCellTests.cs`

**Interfaces:**
- Consumes: `Hoppa.LevelEditor.Core.IColoredCell` (`string ColorId { get; set; }`, `string CellTypeId { get; }`), `Hoppa.LevelEditor.Core.ICellData` (`string CellTypeId { get; }`).
- Produces: `Hoppa.BusBuddies.BBPixelCell` (`CellTypeId=="bb.pixel"`, settable `ColorId`); `Hoppa.BusBuddies.BBEmptyCell` (`CellTypeId=="bb.empty"`).

- [ ] **Step 1: Write the failing test**

`Assets/BusBuddies/Tests/Editor/BusCellTests.cs`:

```csharp
using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.BusBuddies;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BusCellTests
    {
        [Test]
        public void PixelCell_HasTypeIdAndColor()
        {
            var c = new BBPixelCell { ColorId = "red" };
            Assert.AreEqual("bb.pixel", c.CellTypeId);
            Assert.AreEqual("red", c.ColorId);
            Assert.IsInstanceOf<IColoredCell>(c);
            Assert.IsInstanceOf<ICellData>(c);
        }

        [Test]
        public void EmptyCell_HasTypeId()
        {
            var e = new BBEmptyCell();
            Assert.AreEqual("bb.empty", e.CellTypeId);
            Assert.IsInstanceOf<ICellData>(e);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```
npx --yes unity-mcp-cli run-system-tool assets-refresh --json "{}"
npx --yes unity-mcp-cli run-system-tool tests-run --json "{\"testMode\":\"EditMode\",\"assemblyNames\":[\"Hoppa.BusBuddies.Editor.Tests\"],\"testNames\":[\"Hoppa.BusBuddies.Editor.Tests.BusCellTests\"]}"
```
Expected: FAIL — compile error, `BBPixelCell`/`BBEmptyCell` do not exist.

- [ ] **Step 3: Write minimal implementation**

`Assets/BusBuddies/Runtime/BBPixelCell.cs`:

```csharp
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
    }
}
```

`Assets/BusBuddies/Runtime/BBEmptyCell.cs`:

```csharp
using Hoppa.LevelEditor.Core;
using Newtonsoft.Json;

namespace Hoppa.BusBuddies
{
    public sealed class BBEmptyCell : ICellData
    {
        [JsonIgnore]
        public string CellTypeId => "bb.empty";
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```
npx --yes unity-mcp-cli run-system-tool assets-refresh --json "{}"
npx --yes unity-mcp-cli run-system-tool tests-run --json "{\"testMode\":\"EditMode\",\"assemblyNames\":[\"Hoppa.BusBuddies.Editor.Tests\"],\"testNames\":[\"Hoppa.BusBuddies.Editor.Tests.BusCellTests\"]}"
```
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add Assets/BusBuddies/Runtime/BBPixelCell.cs Assets/BusBuddies/Runtime/BBEmptyCell.cs Assets/BusBuddies/Tests/Editor/BusCellTests.cs
git commit -m "feat(busbuddies): BBPixelCell + BBEmptyCell data classes (1a)"
```

---

## Task 3: Queue data (`BusQueueData`)

**Files:**
- Create: `Assets/BusBuddies/Runtime/BusQueueData.cs`
- Test: `Assets/BusBuddies/Tests/Editor/BusQueueDataTests.cs`

**Interfaces:**
- Consumes: nothing.
- Produces:
  - `Hoppa.BusBuddies.BusEntry` — `string ColorId`, `int Capacity`, `bool Hidden`, `int ConnectedId` (default `-1`).
  - `Hoppa.BusBuddies.BusColumn` — `List<BusEntry> Buses` (head→back order).
  - `Hoppa.BusBuddies.BusQueueData` — `List<BusColumn> Columns`.

- [ ] **Step 1: Write the failing test**

`Assets/BusBuddies/Tests/Editor/BusQueueDataTests.cs`:

```csharp
using NUnit.Framework;
using Hoppa.BusBuddies;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BusQueueDataTests
    {
        [Test]
        public void Entry_Defaults()
        {
            var e = new BusEntry();
            Assert.AreEqual(-1, e.ConnectedId);
            Assert.IsFalse(e.Hidden);
        }

        [Test]
        public void Queue_ColumnsAndBusesOrdered()
        {
            var q = new BusQueueData();
            var col = new BusColumn();
            col.Buses.Add(new BusEntry { ColorId = "A", Capacity = 3 });
            col.Buses.Add(new BusEntry { ColorId = "B", Capacity = 2, Hidden = true, ConnectedId = 7 });
            q.Columns.Add(col);

            Assert.AreEqual(1, q.Columns.Count);
            Assert.AreEqual(2, q.Columns[0].Buses.Count);
            Assert.AreEqual("A", q.Columns[0].Buses[0].ColorId);
            Assert.AreEqual(3, q.Columns[0].Buses[0].Capacity);
            Assert.IsTrue(q.Columns[0].Buses[1].Hidden);
            Assert.AreEqual(7, q.Columns[0].Buses[1].ConnectedId);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```
npx --yes unity-mcp-cli run-system-tool assets-refresh --json "{}"
npx --yes unity-mcp-cli run-system-tool tests-run --json "{\"testMode\":\"EditMode\",\"assemblyNames\":[\"Hoppa.BusBuddies.Editor.Tests\"],\"testNames\":[\"Hoppa.BusBuddies.Editor.Tests.BusQueueDataTests\"]}"
```
Expected: FAIL — compile error, `BusQueueData`/`BusColumn`/`BusEntry` do not exist.

- [ ] **Step 3: Write minimal implementation**

`Assets/BusBuddies/Runtime/BusQueueData.cs`:

```csharp
using System.Collections.Generic;
using Newtonsoft.Json;

namespace Hoppa.BusBuddies
{
    public sealed class BusEntry
    {
        [JsonProperty("colorId")]
        public string ColorId { get; set; } = "blue";

        [JsonProperty("capacity")]
        public int Capacity { get; set; } = 10;

        [JsonProperty("hidden")]
        public bool Hidden { get; set; }

        // -1 = not part of a connected pair. Pairing modeling is deferred (1a treats
        // connected buses as independent); the field is carried so authoring/export survive.
        [JsonProperty("connectedId")]
        public int ConnectedId { get; set; } = -1;
    }

    public sealed class BusColumn
    {
        // Head (index 0) is the only tappable bus; back of the queue is the last element.
        [JsonProperty("buses")]
        public List<BusEntry> Buses { get; set; } = new List<BusEntry>();
    }

    public sealed class BusQueueData
    {
        [JsonProperty("columns")]
        public List<BusColumn> Columns { get; set; } = new List<BusColumn>();
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```
npx --yes unity-mcp-cli run-system-tool assets-refresh --json "{}"
npx --yes unity-mcp-cli run-system-tool tests-run --json "{\"testMode\":\"EditMode\",\"assemblyNames\":[\"Hoppa.BusBuddies.Editor.Tests\"],\"testNames\":[\"Hoppa.BusBuddies.Editor.Tests.BusQueueDataTests\"]}"
```
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add Assets/BusBuddies/Runtime/BusQueueData.cs Assets/BusBuddies/Tests/Editor/BusQueueDataTests.cs
git commit -m "feat(busbuddies): BusQueueData top-section payload (1a)"
```

---

## Task 4: `BusLevelModel` (interned snapshot + balance precheck)

**Files:**
- Create: `Assets/BusBuddies/Runtime/Sim/BusLevelModel.cs`
- Test: `Assets/BusBuddies/Tests/Editor/BusLevelModelTests.cs`

**Interfaces:**
- Consumes: `GridData<ICellData>` (`Width`,`Height`,`Get(x,y)`), `IColoredCell.ColorId`, `BusQueueData`/`BusColumn`/`BusEntry` (Task 3).
- Produces — `Hoppa.BusBuddies.Sim.BusLevelModel` (immutable, all fields `public readonly`):
  - `int W, H, ActiveSlots, NumColors; string[] ColorNames;`
  - `int[] Grid` (length `W*H`, colour index per cell, `-1` empty, index `y*W+x`); `int TotalBlocks;`
  - `int Columns; int[][] BusColor, BusCap; bool[][] BusHidden; int[][] BusConnected;` (each `[col]` head→back); `int TotalPassengers, TotalBuses;`
  - `static BusLevelModel Build(GridData<ICellData> grid, BusQueueData queue, int activeSlots)`
  - `static BusLevelModel FromArrays(int[] grid, int w, int h, int[][] busColors, int[][] busCaps, int activeSlots, int numColors, string[] colorNames)`
  - `bool IsColorBalanced()` — true iff for every colour, grid-block-count == total bus capacity of that colour.

- [ ] **Step 1: Write the failing test**

`Assets/BusBuddies/Tests/Editor/BusLevelModelTests.cs`:

```csharp
using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.BusBuddies;
using Hoppa.BusBuddies.Sim;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BusLevelModelTests
    {
        [Test]
        public void FromArrays_CountsBlocksBusesPassengers()
        {
            // grid 3x1: [A, empty, B]  -> 2 blocks
            var m = BusLevelModel.FromArrays(
                grid: new[] { 0, -1, 1 }, w: 3, h: 1,
                busColors: new[] { new[] { 0 }, new[] { 1 } },
                busCaps:   new[] { new[] { 2 }, new[] { 1 } },
                activeSlots: 5, numColors: 2, colorNames: new[] { "A", "B" });

            Assert.AreEqual(3, m.W);
            Assert.AreEqual(1, m.H);
            Assert.AreEqual(5, m.ActiveSlots);
            Assert.AreEqual(2, m.TotalBlocks);
            Assert.AreEqual(2, m.Columns);
            Assert.AreEqual(2, m.TotalBuses);
            Assert.AreEqual(3, m.TotalPassengers);
            Assert.AreEqual(-1, m.BusConnected[0][0]);
        }

        [Test]
        public void Build_FromGrid_InternsColorsAndCounts()
        {
            // 2x2: y0=[A,B], y1=[B,A]  -> 2 A, 2 B
            var grid = new GridData<ICellData>(2, 2);
            grid.Set(0, 0, new BBPixelCell { ColorId = "A" });
            grid.Set(1, 0, new BBPixelCell { ColorId = "B" });
            grid.Set(0, 1, new BBPixelCell { ColorId = "B" });
            grid.Set(1, 1, new BBPixelCell { ColorId = "A" });

            var q = new BusQueueData();
            var c0 = new BusColumn(); c0.Buses.Add(new BusEntry { ColorId = "A", Capacity = 2 });
            var c1 = new BusColumn(); c1.Buses.Add(new BusEntry { ColorId = "B", Capacity = 2 });
            q.Columns.Add(c0); q.Columns.Add(c1);

            var m = BusLevelModel.Build(grid, q, activeSlots: 3);

            Assert.AreEqual(4, m.TotalBlocks);
            Assert.AreEqual(2, m.NumColors);
            Assert.AreEqual(3, m.ActiveSlots);
            // index (1,1) = 1*2+1 = 3 must be a real colour index (>=0)
            Assert.GreaterOrEqual(m.Grid[3], 0);
            Assert.IsTrue(m.IsColorBalanced());
        }

        [Test]
        public void IsColorBalanced_RejectsMismatch()
        {
            // 3 A blocks but only 2 capacity of A -> unbalanced
            var m = BusLevelModel.FromArrays(
                grid: new[] { 0, 0, 0 }, w: 3, h: 1,
                busColors: new[] { new[] { 0 } },
                busCaps:   new[] { new[] { 2 } },
                activeSlots: 1, numColors: 1, colorNames: new[] { "A" });

            Assert.IsFalse(m.IsColorBalanced());
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```
npx --yes unity-mcp-cli run-system-tool assets-refresh --json "{}"
npx --yes unity-mcp-cli run-system-tool tests-run --json "{\"testMode\":\"EditMode\",\"assemblyNames\":[\"Hoppa.BusBuddies.Editor.Tests\"],\"testNames\":[\"Hoppa.BusBuddies.Editor.Tests.BusLevelModelTests\"]}"
```
Expected: FAIL — `BusLevelModel` does not exist.

- [ ] **Step 3: Write minimal implementation**

`Assets/BusBuddies/Runtime/Sim/BusLevelModel.cs`:

```csharp
using System;
using System.Collections.Generic;
using Hoppa.LevelEditor.Core;

namespace Hoppa.BusBuddies.Sim
{
    // Immutable, interned, engine-agnostic snapshot of a Bus Buddies level for the
    // simulator / solver / average-player. Plain C# (no UnityEngine) so it is
    // headless-unit-testable.
    //
    // Unlike YAK there is NO gravity, so the grid is kept as a FULL 2D occupancy
    // (int[W*H], -1 = empty) using the y*W+x, row-0-bottom origin convention.
    // Colours are interned to small int indices.
    public sealed class BusLevelModel
    {
        public readonly int W;
        public readonly int H;
        public readonly int ActiveSlots;     // max buses in the Active Row (default 5)
        public readonly int NumColors;
        public readonly string[] ColorNames; // colour index -> original colorId

        public readonly int[] Grid;          // [y*W+x] -> colour index, -1 = empty
        public readonly int   TotalBlocks;   // count of non-empty cells

        public readonly int      Columns;        // number of queue columns
        public readonly int[][]  BusColor;       // [col] -> colour index, head->back
        public readonly int[][]  BusCap;         // [col] -> capacity,     head->back
        public readonly bool[][] BusHidden;      // [col] -> hidden flag,  head->back
        public readonly int[][]  BusConnected;   // [col] -> connectedId (-1 none), head->back
        public readonly int      TotalPassengers;
        public readonly int      TotalBuses;

        private BusLevelModel(
            int w, int h, int activeSlots, int numColors, string[] colorNames,
            int[] grid, int totalBlocks,
            int columns, int[][] busColor, int[][] busCap, bool[][] busHidden, int[][] busConnected,
            int totalPassengers, int totalBuses)
        {
            W = w; H = h; ActiveSlots = activeSlots; NumColors = numColors; ColorNames = colorNames;
            Grid = grid; TotalBlocks = totalBlocks;
            Columns = columns; BusColor = busColor; BusCap = busCap; BusHidden = busHidden; BusConnected = busConnected;
            TotalPassengers = totalPassengers; TotalBuses = totalBuses;
        }

        // Build from the editor's working grid + parsed queue data. Cells are read
        // through IColoredCell (not a hardcoded BBPixelCell); anything that is not an
        // IColoredCell with a non-empty ColorId (e.g. BBEmptyCell) is "no block" (-1).
        public static BusLevelModel Build(GridData<ICellData> grid, BusQueueData queue, int activeSlots)
        {
            var map = new Dictionary<string, int>(StringComparer.Ordinal);
            int Intern(string c)
            {
                if (!map.TryGetValue(c, out var idx)) { idx = map.Count; map[c] = idx; }
                return idx;
            }

            int w = grid?.Width ?? 0;
            int h = grid?.Height ?? 0;
            var gridArr = new int[w * h];
            int totalBlocks = 0;
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                int i = y * w + x;
                var cell = grid.Get(x, y);
                if (cell is IColoredCell col && !string.IsNullOrEmpty(col.ColorId))
                {
                    gridArr[i] = Intern(col.ColorId);
                    totalBlocks++;
                }
                else
                {
                    gridArr[i] = -1;
                }
            }

            int columns = queue?.Columns?.Count ?? 0;
            var busColor = new int[columns][];
            var busCap = new int[columns][];
            var busHidden = new bool[columns][];
            var busConnected = new int[columns][];
            int totalPassengers = 0, totalBuses = 0;
            for (int c = 0; c < columns; c++)
            {
                var buses = queue.Columns[c]?.Buses ?? new List<BusEntry>();
                int nb = buses.Count;
                busColor[c] = new int[nb];
                busCap[c] = new int[nb];
                busHidden[c] = new bool[nb];
                busConnected[c] = new int[nb];
                for (int k = 0; k < nb; k++)
                {
                    var e = buses[k];
                    busColor[c][k] = Intern(e?.ColorId ?? string.Empty);
                    busCap[c][k] = e?.Capacity ?? 0;
                    busHidden[c][k] = e?.Hidden ?? false;
                    busConnected[c][k] = e?.ConnectedId ?? -1;
                    totalPassengers += busCap[c][k];
                    totalBuses++;
                }
            }

            var names = new string[map.Count];
            foreach (var kv in map) names[kv.Value] = kv.Key;

            return new BusLevelModel(
                w, h, activeSlots, map.Count, names,
                gridArr, totalBlocks,
                columns, busColor, busCap, busHidden, busConnected,
                totalPassengers, totalBuses);
        }

        // Test/utility factory: build directly from a flat grid + per-column bus
        // arrays. Hidden defaults all-false, connected all -1.
        public static BusLevelModel FromArrays(int[] grid, int w, int h,
            int[][] busColors, int[][] busCaps, int activeSlots, int numColors, string[] colorNames)
        {
            grid ??= Array.Empty<int>();
            busColors ??= Array.Empty<int[]>();
            busCaps ??= Array.Empty<int[]>();

            int totalBlocks = 0;
            for (int i = 0; i < grid.Length; i++) if (grid[i] >= 0) totalBlocks++;

            int columns = busColors.Length;
            var busHidden = new bool[columns][];
            var busConnected = new int[columns][];
            int totalPassengers = 0, totalBuses = 0;
            for (int c = 0; c < columns; c++)
            {
                int nb = busColors[c]?.Length ?? 0;
                busHidden[c] = new bool[nb];
                busConnected[c] = new int[nb];
                for (int k = 0; k < nb; k++)
                {
                    busConnected[c][k] = -1;
                    totalPassengers += busCaps[c]?[k] ?? 0;
                    totalBuses++;
                }
            }

            return new BusLevelModel(
                w, h, activeSlots, numColors, colorNames,
                grid, totalBlocks,
                columns, busColors, busCaps, busHidden, busConnected,
                totalPassengers, totalBuses);
        }

        // Balance precheck: a level is solvable ONLY IF every colour's block count
        // equals the total capacity of buses of that colour (sound, cheap). A
        // mismatch proves Unsolvable. Connected/hidden treated as known in 1a.
        public bool IsColorBalanced()
        {
            var blocks = new int[NumColors];
            for (int i = 0; i < Grid.Length; i++)
            {
                int c = Grid[i];
                if (c >= 0 && c < NumColors) blocks[c]++;
            }
            var cap = new int[NumColors];
            for (int col = 0; col < Columns; col++)
                for (int k = 0; k < BusColor[col].Length; k++)
                {
                    int c = BusColor[col][k];
                    if (c >= 0 && c < NumColors) cap[c] += BusCap[col][k];
                }
            for (int i = 0; i < NumColors; i++) if (blocks[i] != cap[i]) return false;
            return true;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```
npx --yes unity-mcp-cli run-system-tool assets-refresh --json "{}"
npx --yes unity-mcp-cli run-system-tool tests-run --json "{\"testMode\":\"EditMode\",\"assemblyNames\":[\"Hoppa.BusBuddies.Editor.Tests\"],\"testNames\":[\"Hoppa.BusBuddies.Editor.Tests.BusLevelModelTests\"]}"
```
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add Assets/BusBuddies/Runtime/Sim/BusLevelModel.cs Assets/BusBuddies/Tests/Editor/BusLevelModelTests.cs
git commit -m "feat(busbuddies): BusLevelModel interned snapshot + balance precheck (1a)"
```

---

## Task 5: `BusSimState` accessibility (border flood, 4-way)

**Files:**
- Create: `Assets/BusBuddies/Runtime/Sim/BusSimState.cs`
- Test: `Assets/BusBuddies/Tests/Editor/BusAccessibilityTests.cs`

**Interfaces:**
- Consumes: `BusLevelModel` (Task 4 — `W,H,Grid,TotalBlocks,ActiveSlots,Columns,BusColor,BusCap`).
- Produces — `Hoppa.BusBuddies.Sim.BusSimState`:
  - `public readonly BusLevelModel M;`
  - `public readonly int[] Cell;` (live colour per cell, `-1` = empty/removed; index `y*W+x`)
  - `public readonly int[] QHead;` (per column, next pullable bus index)
  - `public readonly int[] ActiveColor;` (length `ActiveSlots`, `-1` = empty slot)
  - `public readonly int[] ActiveRem;` (length `ActiveSlots`, remaining passengers)
  - `public int BlocksLeft;`
  - `public BusSimState(BusLevelModel m)` (initialises from `m.Grid`, runs `RecomputeAccess()`)
  - `public void RecomputeAccess()` (full 4-way border flood of empties; a block is accessible iff on the grid edge OR an orthogonal neighbour is a border-flooded empty)
  - `public bool IsAccessible(int x, int y)`
  - `public int FreeSlot()` / `public bool CanPull(int col)` / `public bool HasLegalMove()`
  - `private int FindAccessibleBlock(int color)`

- [ ] **Step 1: Write the failing test**

`Assets/BusBuddies/Tests/Editor/BusAccessibilityTests.cs`:

```csharp
using NUnit.Framework;
using Hoppa.BusBuddies.Sim;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BusAccessibilityTests
    {
        [Test]
        public void LockedDiagonal_OrthogonallyEnclosedBlockIsNotAccessible()
        {
            // 3x3: center (1,1) block A, its 4 orthogonal neighbours block A,
            // the 4 corners (diagonals) empty. Center must be LOCKED.
            //   y2: [-1, A, -1]
            //   y1: [ A, A,  A]
            //   y0: [-1, A, -1]
            var grid = new[] { -1, 0, -1,  0, 0, 0,  -1, 0, -1 };
            var m = BusLevelModel.FromArrays(grid, 3, 3,
                busColors: new int[0][], busCaps: new int[0][],
                activeSlots: 5, numColors: 1, colorNames: new[] { "A" });
            var s = new BusSimState(m);

            Assert.IsFalse(s.IsAccessible(1, 1), "center enclosed by 4 orthogonal blocks must be locked");
            Assert.IsTrue(s.IsAccessible(1, 0), "edge arm block is accessible (touches outside border)");
            Assert.IsTrue(s.IsAccessible(0, 1), "edge arm block is accessible");
        }

        [Test]
        public void InteriorPocket_DonutLiningIsNotAccessible()
        {
            // 5x5 all blocks except the centre (2,2) empty -> an isolated pocket.
            // The 4 interior blocks lining the pocket have no border-connected empty
            // neighbour and are not on the edge -> NOT accessible. Edge blocks are.
            var grid = new int[25];
            for (int i = 0; i < 25; i++) grid[i] = 0;
            grid[2 * 5 + 2] = -1; // (2,2) empty
            var m = BusLevelModel.FromArrays(grid, 5, 5,
                busColors: new int[0][], busCaps: new int[0][],
                activeSlots: 5, numColors: 1, colorNames: new[] { "A" });
            var s = new BusSimState(m);

            Assert.IsFalse(s.IsAccessible(2, 1), "pocket-lining interior block must be locked");
            Assert.IsFalse(s.IsAccessible(1, 2), "pocket-lining interior block must be locked");
            Assert.IsTrue(s.IsAccessible(0, 0), "corner edge block is accessible");
        }

        [Test]
        public void SimpleStrip_AllEdgeBlocksAccessible()
        {
            // 2x1: [A, B] both on the edge -> both accessible.
            var m = BusLevelModel.FromArrays(new[] { 0, 1 }, 2, 1,
                busColors: new int[0][], busCaps: new int[0][],
                activeSlots: 5, numColors: 2, colorNames: new[] { "A", "B" });
            var s = new BusSimState(m);

            Assert.IsTrue(s.IsAccessible(0, 0));
            Assert.IsTrue(s.IsAccessible(1, 0));
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```
npx --yes unity-mcp-cli run-system-tool assets-refresh --json "{}"
npx --yes unity-mcp-cli run-system-tool tests-run --json "{\"testMode\":\"EditMode\",\"assemblyNames\":[\"Hoppa.BusBuddies.Editor.Tests\"],\"testNames\":[\"Hoppa.BusBuddies.Editor.Tests.BusAccessibilityTests\"]}"
```
Expected: FAIL — `BusSimState` does not exist.

- [ ] **Step 3: Write minimal implementation**

`Assets/BusBuddies/Runtime/Sim/BusSimState.cs`:

```csharp
using System.Collections.Generic;
using System.Text;

namespace Hoppa.BusBuddies.Sim
{
    // Mutable simulation state for one Bus Buddies playthrough. Engine-agnostic plain C#.
    //
    // No gravity: removing a block just sets its cell to -1; nothing else moves.
    // Accessibility = a 4-way border flood of empty cells; a block is accessible iff
    // it sits on the grid edge (the outside counts as flooded) or an orthogonal
    // neighbour is a border-connected empty cell.
    //
    // The only player decision is "pull the top bus of some queue column into a free
    // Active Row slot" (ApplyMove); the row then auto-releases passengers to
    // quiescence (ResolveReleases).
    public sealed class BusSimState
    {
        public readonly BusLevelModel M;
        public readonly int[] Cell;        // [y*W+x] live colour, -1 = empty/removed
        public readonly int[] QHead;       // per column: next pullable bus index
        public readonly int[] ActiveColor; // per active slot: colour, -1 = empty slot
        public readonly int[] ActiveRem;   // per active slot: remaining passengers
        public int BlocksLeft;

        private readonly bool[] _accessible; // [y*W+x] true iff a block here is accessible
        private bool[] _floodScratch;        // reused empty-flood buffer

        public BusSimState(BusLevelModel m)
        {
            M = m;
            Cell = (int[])m.Grid.Clone();
            QHead = new int[m.Columns];
            ActiveColor = new int[m.ActiveSlots];
            ActiveRem = new int[m.ActiveSlots];
            for (int s = 0; s < ActiveColor.Length; s++) { ActiveColor[s] = -1; ActiveRem[s] = 0; }
            BlocksLeft = m.TotalBlocks;
            _accessible = new bool[m.W * m.H];
            RecomputeAccess();
        }

        private BusSimState(BusSimState src)
        {
            M = src.M;
            Cell = (int[])src.Cell.Clone();
            QHead = (int[])src.QHead.Clone();
            ActiveColor = (int[])src.ActiveColor.Clone();
            ActiveRem = (int[])src.ActiveRem.Clone();
            BlocksLeft = src.BlocksLeft;
            _accessible = (bool[])src._accessible.Clone();
            _floodScratch = null;
        }

        public BusSimState Clone() => new BusSimState(this);

        public bool IsAccessible(int x, int y) => _accessible[y * M.W + x];

        // Full 4-way border flood of empty cells, then mark each block accessible if
        // it is on the grid edge (outside == flooded) or touches a flooded empty.
        public void RecomputeAccess()
        {
            int w = M.W, h = M.H, n = w * h;
            var flood = _floodScratch ?? (_floodScratch = new bool[n]);
            System.Array.Clear(flood, 0, n);

            var stack = new Stack<int>();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (x != 0 && x != w - 1 && y != 0 && y != h - 1) continue; // border cells only
                int i = y * w + x;
                if (Cell[i] < 0 && !flood[i]) { flood[i] = true; stack.Push(i); }
            }
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                int x = i % w, y = i / w;
                TryFlood(x - 1, y, flood, stack);
                TryFlood(x + 1, y, flood, stack);
                TryFlood(x, y - 1, flood, stack);
                TryFlood(x, y + 1, flood, stack);
            }

            for (int i = 0; i < n; i++)
            {
                if (Cell[i] < 0) { _accessible[i] = false; continue; }
                int x = i % w, y = i / w;
                _accessible[i] =
                    x == 0 || x == w - 1 || y == 0 || y == h - 1 ||
                    IsFloodedEmpty(x - 1, y, flood) || IsFloodedEmpty(x + 1, y, flood) ||
                    IsFloodedEmpty(x, y - 1, flood) || IsFloodedEmpty(x, y + 1, flood);
            }
        }

        private void TryFlood(int x, int y, bool[] flood, Stack<int> stack)
        {
            if (x < 0 || x >= M.W || y < 0 || y >= M.H) return;
            int i = y * M.W + x;
            if (Cell[i] < 0 && !flood[i]) { flood[i] = true; stack.Push(i); }
        }

        private bool IsFloodedEmpty(int x, int y, bool[] flood)
        {
            if (x < 0 || x >= M.W || y < 0 || y >= M.H) return false;
            int i = y * M.W + x;
            return Cell[i] < 0 && flood[i];
        }

        // First active slot with the given accessible colour, or -1.
        private int FindAccessibleBlock(int color)
        {
            for (int i = 0; i < Cell.Length; i++)
                if (_accessible[i] && Cell[i] == color) return i;
            return -1;
        }

        public int FreeSlot()
        {
            for (int s = 0; s < ActiveColor.Length; s++) if (ActiveColor[s] < 0) return s;
            return -1;
        }

        public bool CanPull(int col) =>
            col >= 0 && col < M.Columns && QHead[col] < M.BusColor[col].Length;

        // A legal move exists iff an Active slot is free AND some column has a pullable bus.
        public bool HasLegalMove()
        {
            if (FreeSlot() < 0) return false;
            for (int c = 0; c < M.Columns; c++) if (CanPull(c)) return true;
            return false;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```
npx --yes unity-mcp-cli run-system-tool assets-refresh --json "{}"
npx --yes unity-mcp-cli run-system-tool tests-run --json "{\"testMode\":\"EditMode\",\"assemblyNames\":[\"Hoppa.BusBuddies.Editor.Tests\"],\"testNames\":[\"Hoppa.BusBuddies.Editor.Tests.BusAccessibilityTests\"]}"
```
Expected: PASS (3 tests).

- [ ] **Step 5: Commit**

```bash
git add Assets/BusBuddies/Runtime/Sim/BusSimState.cs Assets/BusBuddies/Tests/Editor/BusAccessibilityTests.cs
git commit -m "feat(busbuddies): BusSimState 4-way border-flood accessibility (1a)"
```

---

## Task 6: `BusSimState` dynamics (move, release, win, deadlock, key, clone)

**Files:**
- Modify: `Assets/BusBuddies/Runtime/Sim/BusSimState.cs`
- Test: `Assets/BusBuddies/Tests/Editor/BusDynamicsTests.cs`

**Interfaces:**
- Consumes: everything from Task 5 (`Cell`, `ActiveColor`, `ActiveRem`, `QHead`, `BlocksLeft`, `FreeSlot`, `CanPull`, `FindAccessibleBlock`, `RecomputeAccess`, `Clone`).
- Produces — added to `BusSimState`:
  - `public int ApplyMove(int col)` — pulls the head bus of `col` into the first free slot, then `ResolveReleases()`; returns blocks removed. Caller must ensure `CanPull(col)` and `FreeSlot()>=0`.
  - `public int ResolveReleases()` — loops to quiescence removing one accessible matching block per active bus per pass; returns total blocks removed.
  - `public bool IsWin()` — `BlocksLeft==0` AND all slots empty AND all `QHead` exhausted.
  - `public bool IsDeadlock()` — Active Row full AND no active colour has an accessible matching block.
  - `public string Key()` — canonical memo key `(occupancy bits, QHeads, sorted active {color,rem} multiset)`.

- [ ] **Step 1: Write the failing test**

`Assets/BusBuddies/Tests/Editor/BusDynamicsTests.cs`:

```csharp
using NUnit.Framework;
using Hoppa.BusBuddies.Sim;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BusDynamicsTests
    {
        [Test]
        public void Removal_HasNoGravity_OthersStayInPlace()
        {
            // 1x3 vertical strip: y0=A, y1=B, y2=A. One bus A cap1, 1 active slot.
            // Pulling A removes the first accessible A (i0); the top A must STAY at
            // index 2 (no gravity) and B at index 1 is untouched.
            var m = BusLevelModel.FromArrays(new[] { 0, 1, 0 }, 1, 3,
                busColors: new[] { new[] { 0 } }, busCaps: new[] { new[] { 1 } },
                activeSlots: 1, numColors: 2, colorNames: new[] { "A", "B" });
            var s = new BusSimState(m);

            int removed = s.ApplyMove(0);

            Assert.AreEqual(1, removed);
            Assert.AreEqual(-1, s.Cell[0], "bottom A removed");
            Assert.AreEqual(1, s.Cell[1], "B unchanged");
            Assert.AreEqual(0, s.Cell[2], "top A did NOT fall (no gravity)");
            Assert.AreEqual(2, s.BlocksLeft);
        }

        [Test]
        public void ResolveReleases_PartialBus_LeftInRow()
        {
            // 1x2 strip [A,A]; one bus A cap5. Removes both A, then 3 passengers
            // remain so the bus stays in the Active Row. Not a win.
            var m = BusLevelModel.FromArrays(new[] { 0, 0 }, 1, 2,
                busColors: new[] { new[] { 0 } }, busCaps: new[] { new[] { 5 } },
                activeSlots: 5, numColors: 1, colorNames: new[] { "A" });
            var s = new BusSimState(m);

            s.ApplyMove(0);

            Assert.AreEqual(0, s.BlocksLeft);
            Assert.AreEqual(0, s.ActiveColor[0], "partial bus still occupies slot 0");
            Assert.AreEqual(3, s.ActiveRem[0]);
            Assert.IsFalse(s.IsWin(), "active row not empty -> not a win");
        }

        [Test]
        public void ResolveReleases_Coexistence_UnlocksAndWins()
        {
            // 3x3: centre (1,1) = A locked behind 4 B arms; corners empty.
            //   y2: [-1, B, -1]
            //   y1: [ B, A,  B]
            //   y0: [-1, B, -1]
            // col0 = bus A cap1, col1 = bus B cap4, 5 active slots.
            // Pull A first (stuck: A locked). Pull B: B clears 4 arms, which unlocks
            // the centre A, which the still-resident A bus then collects -> WIN.
            var grid = new[] { -1, 1, -1,  1, 0, 1,  -1, 1, -1 };
            var m = BusLevelModel.FromArrays(grid, 3, 3,
                busColors: new[] { new[] { 0 }, new[] { 1 } },
                busCaps:   new[] { new[] { 1 }, new[] { 4 } },
                activeSlots: 5, numColors: 2, colorNames: new[] { "A", "B" });
            var s = new BusSimState(m);

            s.ApplyMove(0); // pull A — locked, stays
            Assert.AreEqual(0, s.ActiveColor[0]);
            Assert.AreEqual(1, s.ActiveRem[0]);
            Assert.AreEqual(5, s.BlocksLeft);

            s.ApplyMove(1); // pull B — clears arms, unlocks centre, A collects it

            Assert.AreEqual(0, s.BlocksLeft);
            Assert.IsTrue(s.IsWin(), "all blocks cleared, both buses emptied, queues exhausted");
        }

        [Test]
        public void IsDeadlock_RowFull_NoActiveColorCanRelease()
        {
            // 2x1 grid [A,B]; two columns each a single bus of colour C (index 2,
            // not present on the grid). 2 active slots. Pull both -> row full, no C
            // block exists -> deadlock.
            var m = BusLevelModel.FromArrays(new[] { 0, 1 }, 2, 1,
                busColors: new[] { new[] { 2 }, new[] { 2 } },
                busCaps:   new[] { new[] { 1 }, new[] { 1 } },
                activeSlots: 2, numColors: 3, colorNames: new[] { "A", "B", "C" });
            var s = new BusSimState(m);

            s.ApplyMove(0);
            s.ApplyMove(1);

            Assert.IsTrue(s.IsDeadlock());
            Assert.IsFalse(s.IsWin());
        }

        [Test]
        public void Clone_IsIndependent()
        {
            var m = BusLevelModel.FromArrays(new[] { 0, 0 }, 1, 2,
                busColors: new[] { new[] { 0 } }, busCaps: new[] { new[] { 2 } },
                activeSlots: 1, numColors: 1, colorNames: new[] { "A" });
            var s = new BusSimState(m);
            var clone = s.Clone();

            clone.ApplyMove(0);

            Assert.AreEqual(2, s.BlocksLeft, "original untouched by clone's move");
            Assert.AreEqual(0, clone.BlocksLeft);
        }

        [Test]
        public void Key_DeterministicForEqualStates()
        {
            var m = BusLevelModel.FromArrays(new[] { 0, 1 }, 2, 1,
                busColors: new[] { new[] { 0 } }, busCaps: new[] { new[] { 1 } },
                activeSlots: 2, numColors: 2, colorNames: new[] { "A", "B" });
            var a = new BusSimState(m);
            var b = new BusSimState(m);
            Assert.AreEqual(a.Key(), b.Key());

            a.ApplyMove(0);
            b.ApplyMove(0);
            Assert.AreEqual(a.Key(), b.Key());
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```
npx --yes unity-mcp-cli run-system-tool assets-refresh --json "{}"
npx --yes unity-mcp-cli run-system-tool tests-run --json "{\"testMode\":\"EditMode\",\"assemblyNames\":[\"Hoppa.BusBuddies.Editor.Tests\"],\"testNames\":[\"Hoppa.BusBuddies.Editor.Tests.BusDynamicsTests\"]}"
```
Expected: FAIL — `ApplyMove`/`ResolveReleases`/`IsWin`/`IsDeadlock`/`Key` do not exist.

- [ ] **Step 3: Write minimal implementation**

Add these methods to `BusSimState` (inside the class in `Assets/BusBuddies/Runtime/Sim/BusSimState.cs`, e.g. after `HasLegalMove`):

```csharp
        // Pull the head bus of `col` onto the first free Active slot, then resolve.
        // Returns blocks removed. Caller must ensure CanPull(col) and FreeSlot() >= 0.
        public int ApplyMove(int col)
        {
            int slot = FreeSlot();
            int head = QHead[col];
            ActiveColor[slot] = M.BusColor[col][head];
            ActiveRem[slot] = M.BusCap[col][head];
            QHead[col]++;
            return ResolveReleases();
        }

        // Loop to quiescence: while some active bus has an accessible block of its
        // colour, remove one, decrement that bus, free the slot if it hits 0, update
        // accessibility. (Order among same-colour buses does not change which blocks
        // ultimately clear, so a deterministic index-order greedy is sound.)
        public int ResolveReleases()
        {
            int removed = 0;
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int s = 0; s < ActiveColor.Length; s++)
                {
                    int color = ActiveColor[s];
                    if (color < 0) continue;
                    int cell = FindAccessibleBlock(color);
                    if (cell < 0) continue;
                    Cell[cell] = -1;
                    BlocksLeft--;
                    removed++;
                    ActiveRem[s]--;
                    if (ActiveRem[s] == 0) ActiveColor[s] = -1; // bus empty -> leaves, frees slot
                    RecomputeAccess();
                    changed = true;
                }
            }
            return removed;
        }

        // Win: every block removed, the Active Row empty, all queues exhausted.
        public bool IsWin()
        {
            if (BlocksLeft != 0) return false;
            for (int s = 0; s < ActiveColor.Length; s++) if (ActiveColor[s] >= 0) return false;
            for (int c = 0; c < M.Columns; c++) if (QHead[c] < M.BusColor[c].Length) return false;
            return true;
        }

        // Deadlock: Active Row full (no free slot) and no active colour has an
        // accessible matching block.
        public bool IsDeadlock()
        {
            if (FreeSlot() >= 0) return false;
            for (int s = 0; s < ActiveColor.Length; s++)
            {
                int color = ActiveColor[s];
                if (color < 0) continue;
                if (FindAccessibleBlock(color) >= 0) return false;
            }
            return true;
        }

        // Canonical memo key: occupancy bits + queue heads + a slot-order-independent
        // multiset of the active (colour,remaining) pairs. Exact (not a lossy hash)
        // so the solver's "unsolvable" verdict cannot be corrupted by a collision.
        public string Key()
        {
            var sb = new StringBuilder(Cell.Length + QHead.Length * 3 + 32);
            for (int i = 0; i < Cell.Length; i++) sb.Append(Cell[i] >= 0 ? '1' : '0');
            sb.Append('|');
            for (int c = 0; c < QHead.Length; c++) { sb.Append(QHead[c]); sb.Append(','); }
            sb.Append('|');
            int n = 0;
            for (int s = 0; s < ActiveColor.Length; s++) if (ActiveColor[s] >= 0) n++;
            var pairs = new long[n];
            int j = 0;
            for (int s = 0; s < ActiveColor.Length; s++)
                if (ActiveColor[s] >= 0) pairs[j++] = ((long)ActiveColor[s] << 20) | (uint)ActiveRem[s];
            System.Array.Sort(pairs);
            for (int i = 0; i < n; i++) { sb.Append(pairs[i]); sb.Append(';'); }
            return sb.ToString();
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```
npx --yes unity-mcp-cli run-system-tool assets-refresh --json "{}"
npx --yes unity-mcp-cli run-system-tool tests-run --json "{\"testMode\":\"EditMode\",\"assemblyNames\":[\"Hoppa.BusBuddies.Editor.Tests\"],\"testNames\":[\"Hoppa.BusBuddies.Editor.Tests.BusDynamicsTests\"]}"
```
Expected: PASS (6 tests).

- [ ] **Step 5: Commit**

```bash
git add Assets/BusBuddies/Runtime/Sim/BusSimState.cs Assets/BusBuddies/Tests/Editor/BusDynamicsTests.cs
git commit -m "feat(busbuddies): BusSimState move/release/win/deadlock/key (1a)"
```

---

## Task 7: `BusSolver` (budgeted exact win-path finder)

**Files:**
- Create: `Assets/BusBuddies/Runtime/Sim/BusSolver.cs`
- Test: `Assets/BusBuddies/Tests/Editor/BusSolverTests.cs`

**Interfaces:**
- Consumes: `BusLevelModel`, `BusSimState` (`IsWin`, `HasLegalMove`, `Key`, `CanPull`, `FreeSlot`, `Clone`, `ApplyMove`, `BlocksLeft`).
- Produces — `Hoppa.BusBuddies.Sim.BusSolver`:
  - `public enum Outcome { Solvable, Unsolvable, BudgetExceeded }`
  - `public sealed class Result { public Outcome Outcome; public int[] WinPath; public long Nodes; public long ElapsedMs; }`
  - `public BusSolver(long maxNodes, long timeoutMs)`
  - `public Result Solve(BusLevelModel model)` — DFS over which column to pull, children ordered by blocks-removed desc, visited-set keyed by `Key()`. Honesty rule: a budget cut returns `BudgetExceeded`, never `Unsolvable`. `WinPath` non-null only when `Solvable`.

- [ ] **Step 1: Write the failing test**

`Assets/BusBuddies/Tests/Editor/BusSolverTests.cs`:

```csharp
using NUnit.Framework;
using Hoppa.BusBuddies.Sim;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BusSolverTests
    {
        [Test]
        public void Solve_SmallBalancedLevel_SolvableAndReplaysToWin()
        {
            // 2x2: y0=[A,B], y1=[B,A]. col0 = A cap2, col1 = B cap2, 2 active slots.
            var grid = new[] { 0, 1, 1, 0 };
            var m = BusLevelModel.FromArrays(grid, 2, 2,
                busColors: new[] { new[] { 0 }, new[] { 1 } },
                busCaps:   new[] { new[] { 2 }, new[] { 2 } },
                activeSlots: 2, numColors: 2, colorNames: new[] { "A", "B" });

            var solver = new BusSolver(maxNodes: 100_000, timeoutMs: 5_000);
            var res = solver.Solve(m);

            Assert.AreEqual(BusSolver.Outcome.Solvable, res.Outcome);
            Assert.IsNotNull(res.WinPath);
            Assert.Greater(res.WinPath.Length, 0);

            // Replay the path through a fresh state -> must win.
            var s = new BusSimState(m);
            foreach (int col in res.WinPath)
            {
                Assert.IsTrue(s.CanPull(col), $"replay col {col} not pullable");
                Assert.GreaterOrEqual(s.FreeSlot(), 0, $"replay col {col} has no free slot");
                s.ApplyMove(col);
            }
            Assert.IsTrue(s.IsWin(), "replaying the win-path must win the level");
        }

        [Test]
        public void Solve_UnbalancedLevel_Unsolvable()
        {
            // 3 A blocks but only 2 capacity of A -> can never clear all -> Unsolvable
            // (the tiny state space is fully explored, not a budget cut).
            var m = BusLevelModel.FromArrays(new[] { 0, 0, 0 }, 3, 1,
                busColors: new[] { new[] { 0 } }, busCaps: new[] { new[] { 2 } },
                activeSlots: 1, numColors: 1, colorNames: new[] { "A" });

            var solver = new BusSolver(maxNodes: 100_000, timeoutMs: 5_000);
            var res = solver.Solve(m);

            Assert.AreEqual(BusSolver.Outcome.Unsolvable, res.Outcome);
            Assert.IsNull(res.WinPath);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```
npx --yes unity-mcp-cli run-system-tool assets-refresh --json "{}"
npx --yes unity-mcp-cli run-system-tool tests-run --json "{\"testMode\":\"EditMode\",\"assemblyNames\":[\"Hoppa.BusBuddies.Editor.Tests\"],\"testNames\":[\"Hoppa.BusBuddies.Editor.Tests.BusSolverTests\"]}"
```
Expected: FAIL — `BusSolver` does not exist.

- [ ] **Step 3: Write minimal implementation**

`Assets/BusBuddies/Runtime/Sim/BusSolver.cs`:

```csharp
using System.Collections.Generic;
using System.Diagnostics;

namespace Hoppa.BusBuddies.Sim
{
    // Depth-first search over move sequences (which queue column to pull next) to
    // find ONE winning order, or prove none exists within a node/time budget.
    //
    // The state space is a DAG: blocks are only removed, queue heads only advance,
    // and active buses only shrink, so states never repeat "backwards" — a
    // visited-set of proven-dead states (keyed by BusSimState.Key) is sufficient.
    // Branches are ordered by blocks-removed-desc so a winning line is usually
    // found early.
    //
    // Honesty rule: a budget cut returns BudgetExceeded, NEVER Unsolvable. Only a
    // fully-explored search with no win is Unsolvable.
    public sealed class BusSolver
    {
        public enum Outcome { Solvable, Unsolvable, BudgetExceeded }

        public sealed class Result
        {
            public Outcome Outcome;
            public int[] WinPath; // column indices to pull, in order (null unless Solvable)
            public long Nodes;
            public long ElapsedMs;
        }

        private readonly long _maxNodes;
        private readonly long _timeoutMs;
        private readonly HashSet<string> _dead = new HashSet<string>();
        private readonly Stopwatch _sw = new Stopwatch();
        private long _nodes;
        private bool _budgetHit;
        private List<int> _path;

        public BusSolver(long maxNodes, long timeoutMs)
        {
            _maxNodes = maxNodes > 0 ? maxNodes : 200_000;
            _timeoutMs = timeoutMs > 0 ? timeoutMs : 5_000;
        }

        public Result Solve(BusLevelModel model)
        {
            _sw.Restart();
            _nodes = 0; _budgetHit = false;
            _dead.Clear();
            _path = new List<int>(model.TotalBuses);

            var start = new BusSimState(model);
            bool found = Dfs(start);

            _sw.Stop();
            var r = new Result { Nodes = _nodes, ElapsedMs = _sw.ElapsedMilliseconds };
            if (found) { r.Outcome = Outcome.Solvable; r.WinPath = _path.ToArray(); }
            else if (_budgetHit) r.Outcome = Outcome.BudgetExceeded;
            else r.Outcome = Outcome.Unsolvable;
            return r;
        }

        // Returns true if a win is reachable from `s`; on success _path holds the
        // remaining moves appended in order.
        private bool Dfs(BusSimState s)
        {
            if (s.IsWin()) return true;
            if (_budgetHit) return false;
            if (_nodes >= _maxNodes || _sw.ElapsedMilliseconds > _timeoutMs) { _budgetHit = true; return false; }
            _nodes++;

            if (!s.HasLegalMove()) return false; // deadlock / stuck -> dead branch

            string key = s.Key();
            if (_dead.Contains(key)) return false;

            // Expand: one child per pullable column, ordered by blocks removed (desc).
            var children = new List<(int col, int removed, BusSimState st)>(s.M.Columns);
            for (int c = 0; c < s.M.Columns; c++)
            {
                if (!s.CanPull(c)) continue;
                if (s.FreeSlot() < 0) break; // no slot -> no move regardless of column
                var child = s.Clone();
                int removed = child.ApplyMove(c);
                children.Add((c, removed, child));
            }
            children.Sort((a, b) => b.removed.CompareTo(a.removed));

            foreach (var (col, _, child) in children)
            {
                _path.Add(col);
                if (Dfs(child)) return true;
                _path.RemoveAt(_path.Count - 1);
                if (_budgetHit) return false;
            }

            _dead.Add(key); // fully explored, no win, no budget cut -> dead
            return false;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```
npx --yes unity-mcp-cli run-system-tool assets-refresh --json "{}"
npx --yes unity-mcp-cli run-system-tool tests-run --json "{\"testMode\":\"EditMode\",\"assemblyNames\":[\"Hoppa.BusBuddies.Editor.Tests\"],\"testNames\":[\"Hoppa.BusBuddies.Editor.Tests.BusSolverTests\"]}"
```
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add Assets/BusBuddies/Runtime/Sim/BusSolver.cs Assets/BusBuddies/Tests/Editor/BusSolverTests.cs
git commit -m "feat(busbuddies): BusSolver budgeted exact win-path DFS (1a)"
```

---

## Task 8: `BusAveragePlayer` (seeded Monte-Carlo difficulty estimator)

**Files:**
- Create: `Assets/BusBuddies/Runtime/Sim/BusAveragePlayer.cs`
- Test: `Assets/BusBuddies/Tests/Editor/BusAveragePlayerTests.cs`

**Interfaces:**
- Consumes: `BusLevelModel`, `BusSimState` (`IsWin`, `HasLegalMove`, `CanPull`, `FreeSlot`, `Clone`, `ApplyMove`, `IsDeadlock`, `M`, `BlocksLeft`).
- Produces — `Hoppa.BusBuddies.Sim.BusAveragePlayer`:
  - `public struct Config { public float Epsilon; public int Lookahead; public int Runs; public int Seed; public static Config Default { get; } }`
  - `public struct Result { public float WinRate; public int Runs; public float Aps; }`
  - `public const float ApsCap = 99f;`
  - `public Result Estimate(BusLevelModel model, Config cfg)` — N seeded greedy rollouts; `WinRate = wins/runs`; `Aps = 1/WinRate` capped at `ApsCap`. Deterministic for a fixed `Seed`.

- [ ] **Step 1: Write the failing test**

`Assets/BusBuddies/Tests/Editor/BusAveragePlayerTests.cs`:

```csharp
using NUnit.Framework;
using Hoppa.BusBuddies.Sim;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BusAveragePlayerTests
    {
        private static BusLevelModel SolvableTwoByTwo()
        {
            // 2x2 [A,B / B,A]; col0 A cap2, col1 B cap2, 2 slots. Any order wins.
            return BusLevelModel.FromArrays(new[] { 0, 1, 1, 0 }, 2, 2,
                busColors: new[] { new[] { 0 }, new[] { 1 } },
                busCaps:   new[] { new[] { 2 }, new[] { 2 } },
                activeSlots: 2, numColors: 2, colorNames: new[] { "A", "B" });
        }

        [Test]
        public void Estimate_SolvableLevel_HasPositiveWinRate()
        {
            var player = new BusAveragePlayer();
            var res = player.Estimate(SolvableTwoByTwo(),
                new BusAveragePlayer.Config { Epsilon = 0.1f, Lookahead = 1, Runs = 100, Seed = 12345 });

            Assert.Greater(res.WinRate, 0f);
            Assert.AreEqual(100, res.Runs);
            Assert.Less(res.Aps, BusAveragePlayer.ApsCap + 0.001f);
        }

        [Test]
        public void Estimate_IsDeterministicForFixedSeed()
        {
            var player = new BusAveragePlayer();
            var cfg = new BusAveragePlayer.Config { Epsilon = 0.2f, Lookahead = 1, Runs = 200, Seed = 777 };

            var a = player.Estimate(SolvableTwoByTwo(), cfg);
            var b = player.Estimate(SolvableTwoByTwo(), cfg);

            Assert.AreEqual(a.WinRate, b.WinRate);
            Assert.AreEqual(a.Aps, b.Aps);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run:
```
npx --yes unity-mcp-cli run-system-tool assets-refresh --json "{}"
npx --yes unity-mcp-cli run-system-tool tests-run --json "{\"testMode\":\"EditMode\",\"assemblyNames\":[\"Hoppa.BusBuddies.Editor.Tests\"],\"testNames\":[\"Hoppa.BusBuddies.Editor.Tests.BusAveragePlayerTests\"]}"
```
Expected: FAIL — `BusAveragePlayer` does not exist.

- [ ] **Step 3: Write minimal implementation**

`Assets/BusBuddies/Runtime/Sim/BusAveragePlayer.cs`:

```csharp
using System;

namespace Hoppa.BusBuddies.Sim
{
    // Monte-Carlo "average player" difficulty estimator. Engine-agnostic plain C#.
    //
    // Difficulty is MEASURED, not labelled: run N seeded playouts of a myopic,
    // occasionally-careless bot and report the win-rate. APS (Attempts Per Solve)
    // ~= 1 / win-rate. Deadlock-proneness lowers win-rate naturally, so it folds
    // into APS.
    //
    // Policy (lookahead L, carelessness e): with probability e pull a random legal
    // column; otherwise pull the column that greedily removes the most blocks over
    // the next L plies, avoiding a pull that immediately self-deadlocks. Hidden /
    // connected buses are treated as known / independent in 1a.
    public sealed class BusAveragePlayer
    {
        public struct Config
        {
            public float Epsilon;   // 0..1 carelessness
            public int   Lookahead; // >=1 plies of greedy planning
            public int   Runs;      // playout count
            public int   Seed;      // base RNG seed (0 = time-based)

            public static Config Default =>
                new Config { Epsilon = 0.1f, Lookahead = 1, Runs = 400, Seed = 12345 };
        }

        public struct Result
        {
            public float WinRate; // wins / runs
            public int   Runs;
            public float Aps;     // 1/WinRate, capped at ApsCap (ApsCap when no win)
        }

        // Hard cap converting a zero win-rate to a finite APS for reporting.
        public const float ApsCap = 99f;

        public Result Estimate(BusLevelModel model, Config cfg)
        {
            int runs = cfg.Runs > 0 ? cfg.Runs : 400;
            int seed = cfg.Seed != 0 ? cfg.Seed : Environment.TickCount;
            int lookahead = cfg.Lookahead > 0 ? cfg.Lookahead : 1;
            float eps = cfg.Epsilon < 0f ? 0f : (cfg.Epsilon > 1f ? 1f : cfg.Epsilon);

            int wins = 0;
            for (int r = 0; r < runs; r++)
            {
                var rng = new Random(unchecked(seed * 1000003 + r));
                if (Playout(model, eps, lookahead, rng)) wins++;
            }

            float winRate = (float)wins / runs;
            float aps = winRate > 0f ? 1f / winRate : ApsCap;
            if (aps > ApsCap) aps = ApsCap;
            return new Result { WinRate = winRate, Runs = runs, Aps = aps };
        }

        private static bool Playout(BusLevelModel model, float eps, int lookahead, Random rng)
        {
            var s = new BusSimState(model);
            int maxMoves = model.TotalBuses + 2; // QHead is monotonic -> bounded
            for (int move = 0; move <= maxMoves; move++)
            {
                if (s.IsWin()) return true;
                if (!s.HasLegalMove()) return false; // deadlock / stuck

                Span<int> cand = stackalloc int[model.Columns];
                int nc = 0;
                for (int c = 0; c < model.Columns; c++)
                    if (s.CanPull(c) && s.FreeSlot() >= 0) cand[nc++] = c;
                if (nc == 0) return false;

                int chosen;
                if (rng.NextDouble() < eps)
                {
                    chosen = cand[rng.Next(nc)]; // careless pull
                }
                else
                {
                    int bestScore = int.MinValue, bestCount = 0;
                    Span<int> bestCols = stackalloc int[nc];
                    for (int j = 0; j < nc; j++)
                    {
                        var child = s.Clone();
                        int removed = child.ApplyMove(cand[j]);
                        int score = removed;
                        if (child.IsDeadlock()) score -= 1000;
                        if (lookahead > 1 && !child.IsWin())
                            score += GreedyScore(child, lookahead - 1);
                        if (score > bestScore) { bestScore = score; bestCount = 0; bestCols[bestCount++] = cand[j]; }
                        else if (score == bestScore) bestCols[bestCount++] = cand[j];
                    }
                    chosen = bestCols[rng.Next(bestCount)];
                }

                s.ApplyMove(chosen);
            }
            return s.IsWin();
        }

        // Greedy one-line lookahead: most blocks removed over `depth` more plies
        // (no carelessness inside the lookahead).
        private static int GreedyScore(BusSimState s, int depth)
        {
            if (depth <= 0 || !s.HasLegalMove()) return 0;
            int best = 0;
            for (int c = 0; c < s.M.Columns; c++)
            {
                if (!s.CanPull(c) || s.FreeSlot() < 0) continue;
                var child = s.Clone();
                int removed = child.ApplyMove(c);
                int score = removed + GreedyScore(child, depth - 1);
                if (score > best) best = score;
            }
            return best;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run:
```
npx --yes unity-mcp-cli run-system-tool assets-refresh --json "{}"
npx --yes unity-mcp-cli run-system-tool tests-run --json "{\"testMode\":\"EditMode\",\"assemblyNames\":[\"Hoppa.BusBuddies.Editor.Tests\"],\"testNames\":[\"Hoppa.BusBuddies.Editor.Tests.BusAveragePlayerTests\"]}"
```
Expected: PASS (2 tests).

- [ ] **Step 5: Final full-assembly run + commit**

Run the entire assembly to confirm all tasks are green together:
```
npx --yes unity-mcp-cli run-system-tool assets-refresh --json "{}"
npx --yes unity-mcp-cli run-system-tool tests-run --json "{\"testMode\":\"EditMode\",\"assemblyNames\":[\"Hoppa.BusBuddies.Editor.Tests\"]}"
```
Expected: PASS (all BusBuddies tests).

```bash
git add Assets/BusBuddies/Runtime/Sim/BusAveragePlayer.cs Assets/BusBuddies/Tests/Editor/BusAveragePlayerTests.cs
git commit -m "feat(busbuddies): BusAveragePlayer seeded Monte-Carlo APS estimator (1a)"
```

---

## Self-Review

**Spec coverage (§5 simulator + §10 tests):**

| Spec item | Task / test |
|---|---|
| asmdef + folder scaffold compiles, one trivial passing test | Task 1 (`BusScaffoldTests`) |
| `BBPixelCell` (`bb.pixel`, `ColorId`) / `BBEmptyCell` (`bb.empty`) | Task 2 (`BusCellTests`) |
| `BusQueueData` payload (`Columns`/`Buses`/`{ColorId,Capacity,Hidden,ConnectedId}`) | Task 3 (`BusQueueDataTests`) |
| `BusLevelModel.FromArrays` + `Build(GridData,BusQueueData,activeSlots)` interning | Task 4 (`BusLevelModelTests`) |
| Balance precheck rejects mismatched colour totals | Task 4 (`IsColorBalanced_RejectsMismatch`) |
| Accessibility flood incl. **locked-diagonal** + interior pocket/donut | Task 5 (`BusAccessibilityTests`) |
| No-gravity removal | Task 6 (`Removal_HasNoGravity_OthersStayInPlace`) |
| `ResolveReleases` to quiescence — multi-bus coexistence + partial-empty bus left in row | Task 6 (`ResolveReleases_PartialBus_LeftInRow`, `ResolveReleases_Coexistence_UnlocksAndWins`) |
| Win detection | Task 6 (coexistence win) + Task 7 replay |
| Deadlock detection (row full + none release) | Task 6 (`IsDeadlock_RowFull_NoActiveColorCanRelease`) |
| `Key()` canonical memo, `Clone()` independence | Task 6 (`Key_DeterministicForEqualStates`, `Clone_IsIndependent`) |
| `BusSolver` win-path on small level; replay → `IsWin`; `BudgetExceeded` honesty | Task 7 (`BusSolverTests`; honesty rule encoded in `Dfs`) |
| `BusAveragePlayer` determinism with a fixed seed; `WinRate`/`Aps` | Task 8 (`BusAveragePlayerTests`) |

**Type/signature consistency:** `RecomputeAccess()`, `IsAccessible(int,int)`, `FreeSlot()`, `CanPull(int)`, `HasLegalMove()`, `FindAccessibleBlock(int)`, `ApplyMove(int)→int`, `ResolveReleases()→int`, `IsWin()`, `IsDeadlock()`, `Clone()`, `Key()`, public field `M`, arrays `Cell/QHead/ActiveColor/ActiveRem`, `BlocksLeft` are used identically across Tasks 5–8. `BusLevelModel` fields (`W,H,ActiveSlots,Grid,TotalBlocks,Columns,BusColor,BusCap,BusHidden,BusConnected,TotalPassengers,TotalBuses`) and factories match between Task 4 and consumers. `BusSolver.Outcome`/`Result` and `BusAveragePlayer.Config`/`Result`/`ApsCap` match their tests.

**Placeholder scan:** none — every code step is complete.

**Notes for the implementer:**
- `RecomputeAccess()` is a full reflood after each removal (correctness-first). The spec mentions an incremental flood as an optimisation; it is intentionally deferred to a later perf pass and is NOT required for 1a correctness or any test here.
- Hidden / connected buses are carried as data (`BusHidden`, `BusConnected`) but modelled as known / independent in 1a, exactly per spec §5/§7.
