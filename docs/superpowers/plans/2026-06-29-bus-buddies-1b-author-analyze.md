# Bus Buddies — Sub-Phase 1b (Author & Analyze) Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Stand up the Bus Buddies **analyze path** as a sibling of YAK's Layer-2: a new `Hoppa.BusBuddies.Editor` assembly with the pixel/empty cell **definitions**, a `BusBuddiesPalette.asset`, a `BBColorBalanceRule`, and a `BusBuddiesAnalyzer` (+ `BusBuddiesAnalyzerConfig`) that wraps the shipped 1a simulator stack (`BusLevelModel` / `BusSolver` / `BusAveragePlayer`) onto the generic `ILevelAnalyzer` contract — returning honest `AnalysisStatus`, a measured APS, a difficulty Band, and a `WinPath` on small grids. A `BusBuddiesProfile.asset` wires the slots available now (palette, cells, balance rule, analyzer; default 30×30; `_spoolsBelowGrid = true`). Headless EditMode NUnit tests prove the analyzer, balance rule, cell definitions, and `BusQueueData`↔`TopSection` serialization.

**Scope note — 1b is split.** This plan is **1b-i: the analyze path** only. The **autofiller** (`BusBuddiesAutofiller` + config) and the **queue editor panel** (`BusBuddiesQueuePanel`) — the "author by generation" and "author by hand in-editor" halves — are a coherent, separable follow-on, **1b-ii**, outlined at the end of this document. Rationale: the autofiller is a meaty unit (mirrors `YAKSpoolAutofiller`, ~260 lines, its own partition/assignment + analyzer-gated reroll search) that *depends on* the analyzer this plan delivers, and the queue panel is a large IMGUI surface that is **manual/integration, not TDD-able** — keeping it out of the TDD plan avoids inventing hollow UI tests. The analyzer is fully proven by unit tests here; the in-editor hand-author→analyze loop is closed in 1b-ii (it needs the panel to place buses). **Recommendation: take the split.**

**Architecture:** `Assets/BusBuddies/` already holds the 1a runtime (`Hoppa.BusBuddies.Runtime`: `BBPixelCell`, `BBEmptyCell`, `BusQueueData`, and `Sim/*`) and the EditMode test assembly (`Hoppa.BusBuddies.Editor.Tests`). This plan adds `Assets/BusBuddies/Editor/` with a new **`Hoppa.BusBuddies.Editor`** assembly (Editor platform; references `Hoppa.LevelEditor.Core.Runtime` + `Hoppa.LevelEditor.Core.Editor` + `Hoppa.BusBuddies.Runtime`), exactly mirroring `Hoppa.YAK.Editor`. The analyzer is a `LevelAnalyzerAsset` subclass that reads `doc.GameData["conveyorCount"]` (YAK's key, repurposed as the Active Bus Row slot count), parses `doc.TopSection` into `BusQueueData`, builds a `BusLevelModel`, runs the 1a balance precheck → exact solver (small grids) → Monte-Carlo estimator (all sizes) with rollout-rescue, and fills a `LevelAnalysisResult`. A `GameProfile` is **not subclassed** — Bus Buddies is a configured `BusBuddiesProfile.asset` wiring these Layer-2 assets into its slots.

**Tech Stack:** Unity 6 (editor-core project), C# (Editor assembly may use `UnityEngine`/`UnityEditor`; the 1a Runtime/Sim stays pure C# and is **consumed unchanged**), Newtonsoft.Json (`JObject` round-trip for `TopSection`), NUnit EditMode tests, the **direct** `mcp__ai-game-developer__tests-run` / `assets-refresh` MCP tools for refresh + test runs.

## Global Constraints

- **Write all `.cs`, `.asmdef`, and `.asset` files directly with the Write/Edit tools — NEVER via shell** (PowerShell/Bash). Shell-written files cause an approval bottleneck and encoding issues.
- **Editor code goes in the new `Hoppa.BusBuddies.Editor` asmdef** (`includePlatforms:["Editor"]`, references `Hoppa.LevelEditor.Core.Runtime` + `Hoppa.LevelEditor.Core.Editor` + `Hoppa.BusBuddies.Runtime`). The test asmdef `Hoppa.BusBuddies.Editor.Tests` additionally references `Hoppa.BusBuddies.Editor` and the precompiled `nunit.framework.dll` + `Newtonsoft.Json.dll`. The 1a `Hoppa.BusBuddies.Runtime` is **not modified** by this plan.
- **Every new Unity asset commit must `git add` the containing folder so the `.meta` sidecars are included.** Unity generates a `.meta` for every `.cs`/`.asmdef`/`.asset` and for every new folder; a commit that adds the source but not its `.meta` (or the new folder's `.meta`) breaks every teammate's import and the profile's GUID references. **This is called out explicitly in EVERY commit step below** — `git add` the folder, then verify `.meta` files are staged before committing.
- **Grid origin is `y*W+x`, row 0 = BOTTOM** (`bottomUp`), matching `GridData<TCell>.ToIndex` and the 1a `BusLevelModel`. Every grid index in this plan is `y*W+x`.
- **The canonical passenger-target rule (nearest-to-hole) already lives in 1a** (`BusSimState.FindAccessibleBlock`). Do **NOT** re-implement or re-derive it here — the analyzer consumes the 1a sim as-is.
- **Tests are NUnit EditMode**, run via the **direct** `mcp__ai-game-developer__tests-run` tool with `{ "testMode": "EditMode", "testAssembly": "Hoppa.BusBuddies.Editor.Tests" }` (optionally add `"testFilter"`/class name while iterating). **Do NOT use the `npx unity-mcp-cli` path — the CLI currently 500s.** Always call `mcp__ai-game-developer__assets-refresh` first when a `.cs`/`.asmdef`/`.asset` changed; a stale compile makes the run report old results.
- **After adding a new asmdef (Task 1), expect a domain-reload window where Editor-API MCP calls return 500.** Retry `assets-refresh` / `tests-run` after a few seconds until they respond — this is the reload settling, not a real failure.
- **No absolute paths in any committed file** (profile/exporter path fields must be project-relative).

### Running tests (every "run the test" step references this)

After adding or editing any `.cs`/`.asmdef`/`.asset`, refresh then run:

1. `mcp__ai-game-developer__assets-refresh` with `{}`
2. `mcp__ai-game-developer__tests-run` with `{ "testMode": "EditMode", "testAssembly": "Hoppa.BusBuddies.Editor.Tests" }`

While iterating on one class, narrow with the runner's class filter (e.g. `BusBuddiesAnalyzerTests`). If a call returns 500 right after an asmdef/script change, wait for the domain reload and retry.

---

## File Structure

| File | Responsibility |
|---|---|
| `Assets/BusBuddies/Editor/BusBuddies.Editor.asmdef` | **New** Editor assembly `Hoppa.BusBuddies.Editor`; references core runtime+editor and `Hoppa.BusBuddies.Runtime`. Holds all Layer-2 editor code below. |
| `Assets/BusBuddies/Editor/Cells/BBPixelCellDefinition.cs` | `CellTypeDefinition` for the coloured Pixel Block — palette-swatch brush; `CreateDefault()` → `BBPixelCell`. Mirrors `YAKWoolCellDefinition`. |
| `Assets/BusBuddies/Editor/Cells/BBEmptyCellDefinition.cs` | `CellTypeDefinition` for "no block" — checker render; `CreateDefault()` → `BBEmptyCell`. Mirrors `YAKEmptyCellDefinition`. |
| `Assets/BusBuddies/Editor/Validation/BBColorBalanceRule.cs` | `ValidationRuleBase` — per-colour block count vs per-colour total bus capacity. Mirrors `YAKColorBalanceRule`. |
| `Assets/BusBuddies/Editor/Analysis/BusBuddiesAnalyzerConfig.cs` | `ScriptableObject` knobs (active slots, solver budget, estimator params, APS bands). Mirrors `YAKAnalyzerConfig`. |
| `Assets/BusBuddies/Editor/Analysis/BusBuddiesAnalyzer.cs` | `LevelAnalyzerAsset` — wraps the 1a sim onto `ILevelAnalyzer`. Mirrors `YAKLevelAnalyzer`. |
| `Assets/BusBuddies/Data/Config/Palette/BusBuddiesPalette.asset` | `ColorPaletteAsset` with the bus colour list (authoritative YAML). |
| `Assets/BusBuddies/Data/Config/CellDefs/BBPixelCellDef.asset` | `BBPixelCellDefinition` instance (`_typeId="bb.pixel"`, `_palette`→BusBuddiesPalette). Created via menu. |
| `Assets/BusBuddies/Data/Config/CellDefs/BBEmptyCellDef.asset` | `BBEmptyCellDefinition` instance (`_typeId="bb.empty"`). Created via menu. |
| `Assets/BusBuddies/Data/Config/Rules/BBColorBalanceRule.asset` | `BBColorBalanceRule` instance (`_id="busbuddies.color_balance"`). Created via menu. |
| `Assets/BusBuddies/Data/Config/Analysis/BusBuddiesAnalyzerConfig.asset` | Config instance (defaults). Created via menu. |
| `Assets/BusBuddies/Data/Config/Analysis/BusBuddiesAnalyzer.asset` | Analyzer instance (`_config`→config). Created via menu. |
| `Assets/BusBuddies/Data/Config/BusBuddiesProfile.asset` | `GameProfile` instance wiring schemaId/palette/cells/rule/analyzer; 30×30; `_spoolsBelowGrid=true`. Created via menu + Inspector. |
| `Assets/BusBuddies/Tests/Editor/Hoppa.BusBuddies.Editor.Tests.asmdef` | **Edited** — add `Hoppa.LevelEditor.Core.Editor` + `Hoppa.BusBuddies.Editor` references. |
| `Assets/BusBuddies/Tests/Editor/BusQueueSerializationTests.cs` | `BusQueueData`↔`TopSection` JObject round-trip guard. |
| `Assets/BusBuddies/Tests/Editor/BBCellDefinitionTests.cs` | `CreateDefault()` returns the right cell types. |
| `Assets/BusBuddies/Tests/Editor/BBColorBalanceRuleTests.cs` | Balanced → Info, unbalanced → Error. |
| `Assets/BusBuddies/Tests/Editor/BusBuddiesAnalyzerTests.cs` | Solvable+APS+Band, proven Unsolvable, no-buses Unknown, seed determinism. |

**Reference conventions copied verbatim from YAK:** `Hoppa.YAK.Editor` asmdef references (`Core.Runtime`+`Core.Editor`+`<game>.Runtime`, `includePlatforms:["Editor"]`, `autoReferenced:true`, `noEngineReferences:false`); the analyzer/config split (config is its own `ScriptableObject` referenced from the analyzer's `[SerializeField] _config`, **not** from `GameProfile`); `CreateAssetMenu` paths under `Hoppa/BusBuddies/...`; cell-def `_typeId` equals the cell-data `CellTypeId` (`bb.pixel` / `bb.empty`); analyzer reads `doc.GameData["conveyorCount"]` first, `req.ConveyorCapacityOverride` second, config default third; tests inject the private `_config`/analyzer fields via reflection (`BindingFlags.NonPublic|Instance`), exactly as `YakAutofillTestFixtures` does.

**Verbatim 1a signatures this plan consumes (do not redefine):**
- `BusLevelModel.Build(GridData<ICellData> grid, BusQueueData queue, int activeSlots)` → immutable model; public readonly `W,H,ActiveSlots,NumColors,ColorNames,Grid,TotalBlocks,Columns,BusColor,BusCap,BusHidden,BusConnected,TotalPassengers,TotalBuses`; `bool IsColorBalanced()`.
- `BusSolver(long maxNodes, long timeoutMs)`; `BusSolver.Result Solve(BusLevelModel)`; `enum Outcome { Solvable, Unsolvable, BudgetExceeded }`; `Result { Outcome Outcome; int[] WinPath; long Nodes; long ElapsedMs; }`.
- `BusAveragePlayer.Estimate(BusLevelModel, Config)`; `Config { float Epsilon; int Lookahead; int Runs; int Seed; }`; `Result { float WinRate; int Runs; float Aps; }`; `const float ApsCap = 99f`.
- `BBPixelCell : IColoredCell` (`CellTypeId="bb.pixel"`, `string ColorId`), `BBEmptyCell : ICellData` (`CellTypeId="bb.empty"`), `BusEntry { string ColorId; int Capacity; bool Hidden; int ConnectedId=-1; }`, `BusColumn { List<BusEntry> Buses; }`, `BusQueueData { List<BusColumn> Columns; }`.

**Verbatim Layer-1 signatures this plan implements against:**
- `abstract LevelAnalysisResult LevelAnalyzerAsset.Analyze(LevelDocument doc, GameProfile profile, AnalysisRequest req)`.
- `LevelAnalysisResult` fields: `bool Solvable; long WinPathCount; bool CountWasCapped; long StatesExplored; long ElapsedMs; string FailureReason; AnalysisStatus Status; double WinRate; long RolloutsRun; float ApsEstimate; bool ApsCalibrated; float ComplexityEstimate; int Band; IReadOnlyList<int> WinPath; List<string> SolutionSteps;`.
- `enum AnalysisStatus { Unknown, Solvable, Unsolvable, TimedOut, Faulted }`.
- `AnalysisRequest { ... int? ConveyorCapacityOverride; int RolloutCount; long NodeBudget; long TimeoutMs; int Seed; ... }`.
- `abstract class ValidationRuleBase : ScriptableObject, IValidationRule { string Id; void Configure(string id); abstract ValidationScope Scope; abstract IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx); }`; `ValidationEntry(string ruleId, ValidationSeverity severity, string message, CellRef? offendingCell=null, Color? swatch=null)`; `enum ValidationScope { Level, Cell, Color, Stack }`; `enum ValidationSeverity { Info, Warning, Error }`; `ValidationContext(LevelDocument document, IColorPalette palette=null)` with `Grid`, `Document`, `Palette`.
- `abstract class CellTypeDefinition : ScriptableObject { string TypeId; ...; virtual float InspectorPreferredHeight; abstract ICellData CreateDefault(); abstract void DrawCell(Rect, ICellData); abstract void DrawInspector(Rect, ref ICellData); }`.
- `ColorPaletteAsset : ScriptableObject, IColorPalette { TryGetColor(string,out Color); IEnumerable<string> ColorIds; ... }` (script GUID `232eb0793e72a1c48876d9d723e20bf2`).
- `GameProfile` is **sealed** (script GUID `705a0ca41a11b1a4eaf43d6aee9a89d1`); slots `_schemaId,_colorPalette,_gridWidth,_gridHeight,_cellTypes,_rules,_exporters,_topSectionScript,_bottomSectionScript,_orderPanel,_levelGenerator,_generatorConfig,_levelAnalyzer,_levelCompleter,_canvasOverlay,_imageToGrid,_extensions,_spoolsBelowGrid`.

---

## Task 1: New editor assembly + test-assembly references

**Files:**
- Create: `Assets/BusBuddies/Editor/BusBuddies.Editor.asmdef`
- Edit: `Assets/BusBuddies/Tests/Editor/Hoppa.BusBuddies.Editor.Tests.asmdef`

**Interfaces:**
- Consumes: `Hoppa.BusBuddies.Runtime`, `Hoppa.LevelEditor.Core.Runtime`, `Hoppa.LevelEditor.Core.Editor`.
- Produces: assembly `Hoppa.BusBuddies.Editor` (namespace root `Hoppa.BusBuddies.Editor`); the test assembly can now reference it.

- [ ] **Step 1: Create the editor asmdef**

`Assets/BusBuddies/Editor/BusBuddies.Editor.asmdef`:

```json
{
    "name": "Hoppa.BusBuddies.Editor",
    "rootNamespace": "Hoppa.BusBuddies.Editor",
    "references": [
        "Hoppa.LevelEditor.Core.Runtime",
        "Hoppa.LevelEditor.Core.Editor",
        "Hoppa.BusBuddies.Runtime"
    ],
    "includePlatforms": [
        "Editor"
    ],
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

(An asmdef with no `.cs` yet compiles fine as an empty assembly; cells/rule/analyzer land in later tasks.)

- [ ] **Step 2: Extend the test asmdef references**

Edit `Assets/BusBuddies/Tests/Editor/Hoppa.BusBuddies.Editor.Tests.asmdef` so its `references` array becomes (adds `Hoppa.LevelEditor.Core.Editor` and `Hoppa.BusBuddies.Editor`):

```json
{
    "name": "Hoppa.BusBuddies.Editor.Tests",
    "rootNamespace": "Hoppa.BusBuddies.Editor.Tests",
    "references": [
        "Hoppa.LevelEditor.Core.Runtime",
        "Hoppa.LevelEditor.Core.Editor",
        "Hoppa.BusBuddies.Runtime",
        "Hoppa.BusBuddies.Editor",
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

- [ ] **Step 3: Recompile and verify the existing 1a suite is still green**

This is a wiring task with no test of its own; the verification is "the new assembly compiles and breaks nothing." Run `assets-refresh`, then `tests-run` `{ "testMode": "EditMode", "testAssembly": "Hoppa.BusBuddies.Editor.Tests" }`. **Expect the domain-reload 500 window** — retry until it responds. Expected: all pre-existing 1a tests still PASS (no new tests yet).

- [ ] **Step 4: Commit (include `.meta` sidecars + new folder meta)**

```bash
git add Assets/BusBuddies/Editor Assets/BusBuddies/Tests/Editor/Hoppa.BusBuddies.Editor.Tests.asmdef
git status --porcelain Assets/BusBuddies/Editor   # verify BusBuddies.Editor.asmdef AND its .meta (and the Editor folder .meta) are staged
git commit -m "feat(busbuddies): add Hoppa.BusBuddies.Editor assembly + test refs (1b)"
```

---

## Task 2: `BusQueueData` ↔ `TopSection` serialization guard

**Files:**
- Test: `Assets/BusBuddies/Tests/Editor/BusQueueSerializationTests.cs`

**Interfaces:**
- Consumes: `Hoppa.BusBuddies.BusQueueData`/`BusColumn`/`BusEntry` (1a), `Newtonsoft.Json.Linq.JObject`.
- Produces: a guard test proving `JObject.FromObject(queue)` (emit, as `LevelDocument.TopSection`) round-trips through `top.ToObject<BusQueueData>()` (parse, as the analyzer does). The analyzer and balance rule both rely on this contract.

> **Testability honesty:** `BusQueueData` was annotated with Newtonsoft attributes in 1a, so this test **passes on first run** — there is no production code to add. It is a *characterization/guard* test, the documented exception to the red-first rhythm. If it ever FAILS, the 1a JSON attributes regressed and must be restored (do **not** "fix" it by editing the test).

- [ ] **Step 1: Write the guard test**

`Assets/BusBuddies/Tests/Editor/BusQueueSerializationTests.cs`:

```csharp
using NUnit.Framework;
using Hoppa.BusBuddies;
using Newtonsoft.Json.Linq;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BusQueueSerializationTests
    {
        [Test]
        public void BusQueueData_RoundTripsThroughTopSectionJObject()
        {
            var q = new BusQueueData();
            var c0 = new BusColumn();
            c0.Buses.Add(new BusEntry { ColorId = "red", Capacity = 12, Hidden = true, ConnectedId = 3 });
            c0.Buses.Add(new BusEntry { ColorId = "blue", Capacity = 7 });
            q.Columns.Add(c0);
            q.Columns.Add(new BusColumn()); // empty column survives

            JObject top = JObject.FromObject(q);     // emit (LevelDocument.TopSection payload)
            var back = top.ToObject<BusQueueData>();   // parse (exactly what the analyzer does)

            Assert.AreEqual(2, back.Columns.Count);
            Assert.AreEqual(2, back.Columns[0].Buses.Count);
            Assert.AreEqual("red", back.Columns[0].Buses[0].ColorId);
            Assert.AreEqual(12, back.Columns[0].Buses[0].Capacity);
            Assert.IsTrue(back.Columns[0].Buses[0].Hidden);
            Assert.AreEqual(3, back.Columns[0].Buses[0].ConnectedId);
            Assert.AreEqual("blue", back.Columns[0].Buses[1].ColorId);
            Assert.AreEqual(-1, back.Columns[0].Buses[1].ConnectedId); // default preserved
            Assert.AreEqual(0, back.Columns[1].Buses.Count);
        }
    }
}
```

- [ ] **Step 2: Run the test**

`assets-refresh`, then `tests-run` (class `BusQueueSerializationTests`). Expected: PASS (1 test). If it does not even compile, the test asmdef refs from Task 1 are wrong — fix Task 1 first.

- [ ] **Step 3: Commit (include `.meta`)**

```bash
git add Assets/BusBuddies/Tests/Editor/BusQueueSerializationTests.cs
git status --porcelain Assets/BusBuddies/Tests/Editor   # verify the new .cs AND its .meta are staged
git commit -m "test(busbuddies): guard BusQueueData<->TopSection JSON round-trip (1b)"
```

---

## Task 3: Cell definitions (`BBPixelCellDefinition` + `BBEmptyCellDefinition`)

**Files:**
- Create: `Assets/BusBuddies/Editor/Cells/BBPixelCellDefinition.cs`
- Create: `Assets/BusBuddies/Editor/Cells/BBEmptyCellDefinition.cs`
- Test: `Assets/BusBuddies/Tests/Editor/BBCellDefinitionTests.cs`

**Interfaces:**
- Consumes: `Hoppa.LevelEditor.Core.Editor.CellTypeDefinition` (abstract), `ColorPaletteAsset`, `ColorSwatchDrawer` (`MeasureHeight`/`Draw`/`Size`), `BBPixelCell`/`BBEmptyCell` (1a).
- Produces: `Hoppa.BusBuddies.Editor.BBPixelCellDefinition` (`CreateDefault()` → `BBPixelCell`; palette-swatch brush), `Hoppa.BusBuddies.Editor.BBEmptyCellDefinition` (`CreateDefault()` → `BBEmptyCell`; checker render). Both carry `[CreateAssetMenu]`.

> **Testability honesty:** `DrawCell`/`DrawInspector` are IMGUI and are exercised **manually** (Task 8 checklist), not unit-tested. The unit-testable contract is `CreateDefault()` returning the correct concrete cell type — that is what binds painted cells to the right serializer entry.

- [ ] **Step 1: Write the failing test**

`Assets/BusBuddies/Tests/Editor/BBCellDefinitionTests.cs`:

```csharp
using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.BusBuddies;
using Hoppa.BusBuddies.Editor;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BBCellDefinitionTests
    {
        [Test]
        public void PixelDefinition_CreatesPixelCell()
        {
            var def = ScriptableObject.CreateInstance<BBPixelCellDefinition>();
            var cell = def.CreateDefault();
            Assert.IsInstanceOf<BBPixelCell>(cell);
            Assert.AreEqual("bb.pixel", cell.CellTypeId);
            Assert.IsInstanceOf<IColoredCell>(cell);
        }

        [Test]
        public void EmptyDefinition_CreatesEmptyCell()
        {
            var def = ScriptableObject.CreateInstance<BBEmptyCellDefinition>();
            var cell = def.CreateDefault();
            Assert.IsInstanceOf<BBEmptyCell>(cell);
            Assert.AreEqual("bb.empty", cell.CellTypeId);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

`assets-refresh`, then `tests-run` (class `BBCellDefinitionTests`). Expected: FAIL — `BBPixelCellDefinition`/`BBEmptyCellDefinition` do not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

`Assets/BusBuddies/Editor/Cells/BBPixelCellDefinition.cs` (mirrors `YAKWoolCellDefinition` verbatim, retyped to `BBPixelCell`):

```csharp
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Palette-swatch brush for the coloured Pixel Block. The grid canvas calls
    // DrawCell to render each painted pixel and DrawInspector for the brush picker.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Cells/Pixel")]
    public sealed class BBPixelCellDefinition : CellTypeDefinition
    {
        [SerializeField] private ColorPaletteAsset _palette;

        private static readonly Color UnknownColor = new Color(0.4f, 0.4f, 0.4f);

        // Larger swatches so the brush picker is easy to click during configuration.
        private const float BrushSwatchSize = 32f;
        // Approximate inner width of the BRUSH panel (narrowest consumer).
        private const float BrushInnerWidth = 185f;

        private Vector2 _scroll;

        public override float InspectorPreferredHeight
        {
            get
            {
                if (_palette == null) return 80f;
                return ColorSwatchDrawer.MeasureHeight(_palette, BrushInnerWidth, size: BrushSwatchSize) + 6f;
            }
        }

        public override ICellData CreateDefault() => new BBPixelCell();

        public override void DrawCell(Rect rect, ICellData data)
        {
            if (data is not BBPixelCell pixel) return;
            var color = UnknownColor;
            if (_palette != null) _palette.TryGetColor(pixel.ColorId, out color);
            EditorGUI.DrawRect(rect, color);
        }

        public override void DrawInspector(Rect rect, ref ICellData data)
        {
            if (data is not BBPixelCell pixel) return;

            float contentH = ColorSwatchDrawer.MeasureHeight(_palette, rect.width, size: BrushSwatchSize);

            // Fits — draw inline (popup case).
            if (contentH <= rect.height + 0.5f)
            {
                pixel.ColorId = ColorSwatchDrawer.Draw(rect, _palette, pixel.ColorId, size: BrushSwatchSize);
                return;
            }

            // Doesn't fit — scroll (brush panel case). Reserve 14px for the scrollbar.
            float innerW   = rect.width - 14f;
            float scrollH  = ColorSwatchDrawer.MeasureHeight(_palette, innerW, size: BrushSwatchSize);
            var   viewRect = new Rect(0f, 0f, innerW, scrollH);
            _scroll = GUI.BeginScrollView(rect, _scroll, viewRect);
            pixel.ColorId = ColorSwatchDrawer.Draw(viewRect, _palette, pixel.ColorId, size: BrushSwatchSize);
            GUI.EndScrollView();
        }
    }
}
```

`Assets/BusBuddies/Editor/Cells/BBEmptyCellDefinition.cs` (mirrors `YAKEmptyCellDefinition` verbatim, retyped to `BBEmptyCell`):

```csharp
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using UnityEditor;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // "No block" cell. Subtle checker so designers see which cells are unpainted.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Cells/Empty")]
    public sealed class BBEmptyCellDefinition : CellTypeDefinition
    {
        private static readonly Color DarkA = new Color(0.16f, 0.17f, 0.20f);
        private static readonly Color DarkB = new Color(0.13f, 0.14f, 0.17f);

        public override ICellData CreateDefault() => new BBEmptyCell();

        public override void DrawCell(Rect rect, ICellData data)
        {
            EditorGUI.DrawRect(rect, DarkA);
            float halfW = rect.width  * 0.5f;
            float halfH = rect.height * 0.5f;
            EditorGUI.DrawRect(new Rect(rect.x,         rect.y,         halfW, halfH), DarkB);
            EditorGUI.DrawRect(new Rect(rect.x + halfW, rect.y + halfH, halfW, halfH), DarkB);
        }

        public override void DrawInspector(Rect rect, ref ICellData data) { }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

`assets-refresh`, then `tests-run` (class `BBCellDefinitionTests`). Expected: PASS (2 tests).

- [ ] **Step 5: Commit (include `.meta` + new folder meta)**

```bash
git add Assets/BusBuddies/Editor/Cells Assets/BusBuddies/Tests/Editor/BBCellDefinitionTests.cs
git status --porcelain Assets/BusBuddies/Editor/Cells   # verify both .cs, their .meta, and the Cells folder .meta are staged
git commit -m "feat(busbuddies): BBPixelCellDefinition + BBEmptyCellDefinition (1b)"
```

---

## Task 4: `BusBuddiesPalette.asset`

**Files:**
- Create: `Assets/BusBuddies/Data/Config/Palette/BusBuddiesPalette.asset`

**Interfaces:**
- Consumes: core `ColorPaletteAsset` (script GUID `232eb0793e72a1c48876d9d723e20bf2`).
- Produces: a palette asset with a distinct bus-colour set, consumed by the pixel cell-def brush and the profile.

> **Testability honesty:** A data-only `.asset` cannot be meaningfully unit-tested headlessly (no `AssetDatabase` path inside EditMode tests without coupling). Verification is the import (Step 2) + the manual checklist in Task 8. This is authored as authoritative YAML because it binds only the **existing** core `ColorPaletteAsset` script — no new-script GUID chicken-and-egg.

**Bus-colour list (LEAD-CONFIRMABLE — default chosen here).** Eight maximally-separable colours suited to a "buses + passengers" theme; mirrors `YAKPalette.asset`'s `_entries` structure (`Id`/`DisplayName`/`Color`/`SwatchIcon`). Confirm or amend the set/count before mass-generation:

| Id | DisplayName | RGB |
|---|---|---|
| `red` | Red | 0.90, 0.30, 0.30 |
| `blue` | Blue | 0.20, 0.55, 0.95 |
| `green` | Green | 0.30, 0.78, 0.40 |
| `yellow` | Yellow | 0.98, 0.85, 0.30 |
| `orange` | Orange | 0.98, 0.60, 0.25 |
| `purple` | Purple | 0.65, 0.40, 0.90 |
| `pink` | Pink | 0.98, 0.55, 0.78 |
| `cyan` | Cyan | 0.35, 0.85, 0.92 |

- [ ] **Step 1: Write the palette asset**

`Assets/BusBuddies/Data/Config/Palette/BusBuddiesPalette.asset`:

```yaml
%YAML 1.1
%TAG !u! tag:unity3d.com,2011:
--- !u!114 &11400000
MonoBehaviour:
  m_ObjectHideFlags: 0
  m_CorrespondingSourceObject: {fileID: 0}
  m_PrefabInstance: {fileID: 0}
  m_PrefabAsset: {fileID: 0}
  m_GameObject: {fileID: 0}
  m_Enabled: 1
  m_EditorHideFlags: 0
  m_Script: {fileID: 11500000, guid: 232eb0793e72a1c48876d9d723e20bf2, type: 3}
  m_Name: BusBuddiesPalette
  m_EditorClassIdentifier: Hoppa.LevelEditor.Core.Editor::Hoppa.LevelEditor.Core.Editor.ColorPaletteAsset
  _entries:
  - Id: red
    DisplayName: Red
    Color: {r: 0.9, g: 0.3, b: 0.3, a: 1}
    SwatchIcon: {fileID: 0}
  - Id: blue
    DisplayName: Blue
    Color: {r: 0.2, g: 0.55, b: 0.95, a: 1}
    SwatchIcon: {fileID: 0}
  - Id: green
    DisplayName: Green
    Color: {r: 0.3, g: 0.78, b: 0.4, a: 1}
    SwatchIcon: {fileID: 0}
  - Id: yellow
    DisplayName: Yellow
    Color: {r: 0.98, g: 0.85, b: 0.3, a: 1}
    SwatchIcon: {fileID: 0}
  - Id: orange
    DisplayName: Orange
    Color: {r: 0.98, g: 0.6, b: 0.25, a: 1}
    SwatchIcon: {fileID: 0}
  - Id: purple
    DisplayName: Purple
    Color: {r: 0.65, g: 0.4, b: 0.9, a: 1}
    SwatchIcon: {fileID: 0}
  - Id: pink
    DisplayName: Pink
    Color: {r: 0.98, g: 0.55, b: 0.78, a: 1}
    SwatchIcon: {fileID: 0}
  - Id: cyan
    DisplayName: Cyan
    Color: {r: 0.35, g: 0.85, b: 0.92, a: 1}
    SwatchIcon: {fileID: 0}
```

- [ ] **Step 2: Import and verify**

`assets-refresh`. Then verify with `mcp__ai-game-developer__assets-get-data` on `Assets/BusBuddies/Data/Config/Palette/BusBuddiesPalette.asset` that `_entries` has 8 rows with the IDs above and the `m_Script` GUID resolved (no "script missing"). If the asset shows as a broken MonoBehaviour, the GUID is wrong — re-check it equals the core `ColorPaletteAsset` script GUID.

- [ ] **Step 3: Commit (include `.meta` + new folder metas)**

```bash
git add Assets/BusBuddies/Data
git status --porcelain Assets/BusBuddies/Data   # verify the .asset, its .meta, and every new folder .meta (Data, Config, Palette) are staged
git commit -m "feat(busbuddies): BusBuddiesPalette color set (1b)"
```

---

## Task 5: `BBColorBalanceRule`

**Files:**
- Create: `Assets/BusBuddies/Editor/Validation/BBColorBalanceRule.cs`
- Test: `Assets/BusBuddies/Tests/Editor/BBColorBalanceRuleTests.cs`

**Interfaces:**
- Consumes: `ValidationRuleBase` (`Id`, `Configure`, `Scope`, `Evaluate`), `ValidationContext`/`ValidationEntry`/`ValidationSeverity`/`ValidationScope`, `BBPixelCell`, `BusQueueData`.
- Produces: `Hoppa.BusBuddies.Editor.BBColorBalanceRule : ValidationRuleBase` — one entry per colour: `Info` when grid block count == total bus capacity, `Error` otherwise. `[CreateAssetMenu]`.

- [ ] **Step 1: Write the failing test**

`Assets/BusBuddies/Tests/Editor/BBColorBalanceRuleTests.cs`:

```csharp
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.BusBuddies;
using Hoppa.BusBuddies.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BBColorBalanceRuleTests
    {
        private static LevelDocument MakeDoc(GridData<ICellData> grid, BusQueueData queue) => new LevelDocument
        {
            SchemaVersion = "busbuddies",
            Grid = grid,
            TopSection = JObject.FromObject(queue),
        };

        private static List<ValidationEntry> Evaluate(LevelDocument doc)
        {
            var rule = ScriptableObject.CreateInstance<BBColorBalanceRule>();
            rule.Configure("busbuddies.color_balance");
            return rule.Evaluate(new ValidationContext(doc, null)).ToList();
        }

        [Test]
        public void BalancedColor_IsInfo()
        {
            // 2 A blocks, bus A capacity 2 -> balanced.
            var grid = new GridData<ICellData>(2, 1);
            grid.Set(0, 0, new BBPixelCell { ColorId = "A" });
            grid.Set(1, 0, new BBPixelCell { ColorId = "A" });
            var q = new BusQueueData();
            var c0 = new BusColumn(); c0.Buses.Add(new BusEntry { ColorId = "A", Capacity = 2 });
            q.Columns.Add(c0);

            var entries = Evaluate(MakeDoc(grid, q));
            var a = entries.Single(e => e.Message.StartsWith("A:"));
            Assert.AreEqual(ValidationSeverity.Info, a.Severity);
        }

        [Test]
        public void UnbalancedColor_IsError()
        {
            // 3 A blocks, bus A capacity 2 -> error.
            var grid = new GridData<ICellData>(3, 1);
            grid.Set(0, 0, new BBPixelCell { ColorId = "A" });
            grid.Set(1, 0, new BBPixelCell { ColorId = "A" });
            grid.Set(2, 0, new BBPixelCell { ColorId = "A" });
            var q = new BusQueueData();
            var c0 = new BusColumn(); c0.Buses.Add(new BusEntry { ColorId = "A", Capacity = 2 });
            q.Columns.Add(c0);

            var entries = Evaluate(MakeDoc(grid, q));
            var a = entries.Single(e => e.Message.StartsWith("A:"));
            Assert.AreEqual(ValidationSeverity.Error, a.Severity);
        }

        [Test]
        public void Scope_IsColor()
        {
            var rule = ScriptableObject.CreateInstance<BBColorBalanceRule>();
            Assert.AreEqual(ValidationScope.Color, rule.Scope);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

`assets-refresh`, then `tests-run` (class `BBColorBalanceRuleTests`). Expected: FAIL — `BBColorBalanceRule` does not exist.

- [ ] **Step 3: Write minimal implementation**

`Assets/BusBuddies/Editor/Validation/BBColorBalanceRule.cs` (mirrors `YAKColorBalanceRule`, reading `BBPixelCell` + `BusQueueData`):

```csharp
using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Compares Pixel Block count per color (grid) against total bus capacity per
    // color (top section). One entry per color: Info when balanced, Error when not.
    // Hidden buses are included — Hidden is a runtime reveal flag, not a balance flag.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Validation/Color Balance")]
    public sealed class BBColorBalanceRule : ValidationRuleBase
    {
        public override ValidationScope Scope => ValidationScope.Color;

        public override IEnumerable<ValidationEntry> Evaluate(ValidationContext ctx)
        {
            var blocks   = new Dictionary<string, int>();
            var capacity = new Dictionary<string, int>();

            if (ctx.Grid?.Cells != null)
            {
                foreach (var cell in ctx.Grid.Cells)
                    if (cell is BBPixelCell pixel && !string.IsNullOrEmpty(pixel.ColorId))
                        Add(blocks, pixel.ColorId, 1);
            }

            if (ctx.Document?.TopSection != null)
            {
                BusQueueData top = null;
                try   { top = ctx.Document.TopSection.ToObject<BusQueueData>(); }
                catch { top = null; }

                if (top?.Columns != null)
                    foreach (var col in top.Columns)
                        if (col?.Buses != null)
                            foreach (var bus in col.Buses)
                                if (!string.IsNullOrEmpty(bus.ColorId))
                                    Add(capacity, bus.ColorId, bus.Capacity);
            }

            var colors = new HashSet<string>(blocks.Keys);
            colors.UnionWith(capacity.Keys);

            foreach (var colorId in colors)
            {
                blocks.TryGetValue(colorId, out var n);
                capacity.TryGetValue(colorId, out var cap);

                bool   balanced = n == cap;
                string mark     = balanced ? "✓" : "✗";
                var    severity = balanced ? ValidationSeverity.Info : ValidationSeverity.Error;

                Color? swatch = ctx.Palette != null && ctx.Palette.TryGetColor(colorId, out var c)
                                  ? c : (Color?)null;

                yield return new ValidationEntry(Id, severity,
                    $"{colorId}: {n} blocks / {cap} capacity  {mark}",
                    swatch: swatch);
            }
        }

        private static void Add(Dictionary<string, int> d, string key, int amount)
        {
            if (string.IsNullOrEmpty(key)) return;
            d.TryGetValue(key, out var v);
            d[key] = v + amount;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

`assets-refresh`, then `tests-run` (class `BBColorBalanceRuleTests`). Expected: PASS (3 tests).

- [ ] **Step 5: Commit (include `.meta` + new folder meta)**

```bash
git add Assets/BusBuddies/Editor/Validation Assets/BusBuddies/Tests/Editor/BBColorBalanceRuleTests.cs
git status --porcelain Assets/BusBuddies/Editor/Validation   # verify .cs + .meta + Validation folder .meta staged
git commit -m "feat(busbuddies): BBColorBalanceRule color-balance validation (1b)"
```

---

## Task 6: `BusBuddiesAnalyzerConfig` + `BusBuddiesAnalyzer`

**Files:**
- Create: `Assets/BusBuddies/Editor/Analysis/BusBuddiesAnalyzerConfig.cs`
- Create: `Assets/BusBuddies/Editor/Analysis/BusBuddiesAnalyzer.cs`
- Test: `Assets/BusBuddies/Tests/Editor/BusBuddiesAnalyzerTests.cs`

**Interfaces:**
- Consumes: `LevelAnalyzerAsset.Analyze(LevelDocument, GameProfile, AnalysisRequest)`; `LevelAnalysisResult`; `AnalysisStatus`; `AnalysisRequest` (`ConveyorCapacityOverride`, `RolloutCount`, `NodeBudget`, `TimeoutMs`, `Seed`); 1a `BusLevelModel.Build` / `IsColorBalanced` / public readonly fields, `BusSolver(maxNodes,timeoutMs).Solve` / `Outcome` / `Result`, `BusAveragePlayer.Estimate` / `Config` / `Result` / `ApsCap`; `BusQueueData` (via `doc.TopSection.ToObject<BusQueueData>()`).
- Produces:
  - `Hoppa.BusBuddies.Editor.BusBuddiesAnalyzerConfig : ScriptableObject` — `int DefaultActiveSlots=5; int SmallGridThreshold=64; long NodeBudget=200000; long TimeoutMs=5000; float Epsilon=0.1f; int Lookahead=1; int Runs=400; int RngSeed=12345; bool ApsCalibrated=false; float[] ApsBandThresholds={2,4,6,8}; int BandFor(float aps);`
  - `Hoppa.BusBuddies.Editor.BusBuddiesAnalyzer : LevelAnalyzerAsset` with `[SerializeField] BusBuddiesAnalyzerConfig _config;` implementing `Analyze`.

**Analyze algorithm (mirrors `YAKLevelAnalyzer`, retargeted to the bus sim):**
1. Null doc/grid → `Faulted`.
2. Resolve active slots: `doc.GameData["conveyorCount"]` → else `req.ConveyorCapacityOverride` → else `cfg.DefaultActiveSlots`; clamp `>=1`.
3. Parse `BusQueueData` from `doc.TopSection`; `BusLevelModel.Build`.
4. `TotalBuses == 0` → `Unknown` ("level has no buses to analyze").
5. `!IsColorBalanced()` → `Unsolvable` with a per-colour reason (computed locally from the model's public arrays).
6. If `W*H <= SmallGridThreshold`: run `BusSolver` → `Solvable`(+`WinPath`,`WinPathCount=1`) / `Unsolvable` / `BudgetExceeded→TimedOut`. Else leave `Status = Unknown` (rollout decides).
7. Unless already `Unsolvable`: run `BusAveragePlayer.Estimate` → `WinRate`, `ApsEstimate=est.Aps` (already capped at `ApsCap`), `RolloutsRun`, `ApsCalibrated=cfg.ApsCalibrated`, `Band=cfg.BandFor(aps)`. **Rollout rescue:** if `Status ∈ {TimedOut, Unknown}` and `WinRate > 0` → `Solvable` (clear `FailureReason`).

- [ ] **Step 1: Write the failing test**

`Assets/BusBuddies/Tests/Editor/BusBuddiesAnalyzerTests.cs`:

```csharp
using System.Reflection;
using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.BusBuddies;
using Hoppa.BusBuddies.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BusBuddiesAnalyzerTests
    {
        private static void SetField(Object obj, string field, Object value)
            => obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);

        private static BusBuddiesAnalyzer MakeAnalyzer(int runs = 60)
        {
            var cfg = ScriptableObject.CreateInstance<BusBuddiesAnalyzerConfig>();
            cfg.Runs = runs;
            var ana = ScriptableObject.CreateInstance<BusBuddiesAnalyzer>();
            SetField(ana, "_config", cfg);
            return ana;
        }

        private static LevelDocument Doc(GridData<ICellData> grid, BusQueueData queue, int slots) => new LevelDocument
        {
            SchemaVersion = "busbuddies",
            LevelId = "test",
            Grid = grid,
            TopSection = queue != null ? JObject.FromObject(queue) : null,
            GameData = new JObject { ["conveyorCount"] = slots },
        };

        private static (GridData<ICellData> grid, BusQueueData q) TwoCellAB()
        {
            var grid = new GridData<ICellData>(2, 1);
            grid.Set(0, 0, new BBPixelCell { ColorId = "A" });
            grid.Set(1, 0, new BBPixelCell { ColorId = "B" });
            var q = new BusQueueData();
            var c0 = new BusColumn(); c0.Buses.Add(new BusEntry { ColorId = "A", Capacity = 1 });
            var c1 = new BusColumn(); c1.Buses.Add(new BusEntry { ColorId = "B", Capacity = 1 });
            q.Columns.Add(c0); q.Columns.Add(c1);
            return (grid, q);
        }

        [Test]
        public void BalancedSmallGrid_IsSolvable_WithApsAndBand()
        {
            var (grid, q) = TwoCellAB();
            var r = MakeAnalyzer().Analyze(Doc(grid, q, 2), null, new AnalysisRequest { Seed = 99 });

            Assert.AreEqual(AnalysisStatus.Solvable, r.Status);
            Assert.IsTrue(r.Solvable);
            Assert.IsNotNull(r.WinPath);              // small grid -> exact solver path
            Assert.Greater(r.ApsEstimate, 0f);
            Assert.GreaterOrEqual(r.Band, 1);
            Assert.Greater(r.RolloutsRun, 0);
        }

        [Test]
        public void UnbalancedGrid_IsProvenUnsolvable()
        {
            // 3 A blocks, bus A capacity 2.
            var grid = new GridData<ICellData>(3, 1);
            grid.Set(0, 0, new BBPixelCell { ColorId = "A" });
            grid.Set(1, 0, new BBPixelCell { ColorId = "A" });
            grid.Set(2, 0, new BBPixelCell { ColorId = "A" });
            var q = new BusQueueData();
            var c0 = new BusColumn(); c0.Buses.Add(new BusEntry { ColorId = "A", Capacity = 2 });
            q.Columns.Add(c0);

            var r = MakeAnalyzer().Analyze(Doc(grid, q, 1), null, new AnalysisRequest());
            Assert.AreEqual(AnalysisStatus.Unsolvable, r.Status);
            Assert.IsFalse(r.Solvable);
            Assert.IsNotNull(r.FailureReason);
        }

        [Test]
        public void NoBuses_IsUnknown()
        {
            var grid = new GridData<ICellData>(1, 1);
            grid.Set(0, 0, new BBPixelCell { ColorId = "A" });
            var r = MakeAnalyzer().Analyze(Doc(grid, new BusQueueData(), 1), null, new AnalysisRequest());
            Assert.AreEqual(AnalysisStatus.Unknown, r.Status);
            Assert.IsFalse(r.Solvable);
        }

        [Test]
        public void Aps_IsDeterministic_ForFixedSeed()
        {
            var (g1, q1) = TwoCellAB();
            var (g2, q2) = TwoCellAB();
            var a = MakeAnalyzer().Analyze(Doc(g1, q1, 2), null, new AnalysisRequest { Seed = 7 });
            var b = MakeAnalyzer().Analyze(Doc(g2, q2, 2), null, new AnalysisRequest { Seed = 7 });
            Assert.AreEqual(a.ApsEstimate, b.ApsEstimate);
            Assert.AreEqual(a.WinRate, b.WinRate);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

`assets-refresh`, then `tests-run` (class `BusBuddiesAnalyzerTests`). Expected: FAIL — `BusBuddiesAnalyzer`/`BusBuddiesAnalyzerConfig` do not exist.

- [ ] **Step 3: Write minimal implementation**

`Assets/BusBuddies/Editor/Analysis/BusBuddiesAnalyzerConfig.cs`:

```csharp
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Designer-tunable knobs for BusBuddiesAnalyzer. Its own asset, referenced from
    // the analyzer asset (GameProfile stays game-agnostic) — mirrors YAKAnalyzerConfig.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Analysis/Bus Buddies Analyzer Config")]
    public sealed class BusBuddiesAnalyzerConfig : ScriptableObject
    {
        [Header("Active Bus Row")]
        [Tooltip("Active-row slot count used when neither the request nor GameData[\"conveyorCount\"] supplies one. Surfaced to designers as 'Active Bus Slots'.")]
        [Min(1)] public int DefaultActiveSlots = 5;

        [Header("Exact solver (small grids only)")]
        [Tooltip("Run the exact BusSolver only when W*H <= this. Larger grids rely on Monte-Carlo rollout for solvability (rollout rescue).")]
        [Min(1)] public int SmallGridThreshold = 64;
        [Tooltip("Max search nodes the win-path solver may expand before giving up (a hit reports TimedOut, never Unsolvable).")]
        public long NodeBudget = 200_000;
        [Tooltip("Wall-clock budget (ms) for the win-path solver.")]
        public long TimeoutMs = 5_000;

        [Header("Average-player APS estimator")]
        [Tooltip("Carelessness e: probability of a random (non-greedy) pull per turn.")]
        [Range(0f, 1f)] public float Epsilon = 0.1f;
        [Tooltip("Greedy planning depth in plies. 1 = react to the current board only.")]
        [Min(1)] public int Lookahead = 1;
        [Tooltip("Monte-Carlo playout count per analysis. Higher = steadier win-rate, slower.")]
        [Min(1)] public int Runs = 400;
        [Tooltip("Base RNG seed for reproducible playouts (overridden by AnalysisRequest.Seed when non-zero).")]
        public int RngSeed = 12345;

        [Tooltip("FALSE until e/lookahead have been fitted to real player APS data. While false, results are flagged 'uncalibrated' so APS is never presented as ground truth.")]
        public bool ApsCalibrated = false;

        [Header("Difficulty bands")]
        [Tooltip("Ascending APS boundaries. Band = number of boundaries the measured APS meets or exceeds, plus 1. Default {2,4,6,8} -> bands 1..5.")]
        public float[] ApsBandThresholds = { 2f, 4f, 6f, 8f };

        private void OnEnable()
        {
            if (ApsBandThresholds == null || ApsBandThresholds.Length == 0)
                ApsBandThresholds = new[] { 2f, 4f, 6f, 8f };
        }

        public int BandFor(float aps)
        {
            int band = 1;
            if (ApsBandThresholds != null)
                foreach (var t in ApsBandThresholds)
                    if (aps >= t) band++;
            return band;
        }
    }
}
```

`Assets/BusBuddies/Editor/Analysis/BusBuddiesAnalyzer.cs`:

```csharp
using System.Diagnostics;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.BusBuddies.Sim;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Bus Buddies difficulty scorer. Wraps the engine-agnostic 1a simulator stack
    // (BusLevelModel + BusSolver + BusAveragePlayer) and maps it onto the generic
    // ILevelAnalyzer contract. Reports honest, distinct outcomes via
    // LevelAnalysisResult.Status (Solvable / Unsolvable / TimedOut / Faulted /
    // Unknown) and a MEASURED APS (~ 1 / average-player win-rate), flagged
    // uncalibrated until the estimator policy is fitted to real player data.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Analysis/Bus Buddies Analyzer")]
    public sealed class BusBuddiesAnalyzer : LevelAnalyzerAsset
    {
        [SerializeField] private BusBuddiesAnalyzerConfig _config;

        public override LevelAnalysisResult Analyze(LevelDocument doc, GameProfile profile, AnalysisRequest req)
        {
            var sw = Stopwatch.StartNew();
            var result = new LevelAnalysisResult();
            var cfg = _config != null ? _config : ScriptableObject.CreateInstance<BusBuddiesAnalyzerConfig>();

            try
            {
                if (doc == null || doc.Grid == null)
                    return Fault(result, sw, "document or grid is null");

                // Resolve Active Bus Row slots. The level's GameData wins (authored
                // per level), then the request override, then the config default.
                // Reuses YAK's "conveyorCount" key (repurposed) so cloned tooling works.
                int slots;
                var cc = doc.GameData?["conveyorCount"];
                if (cc != null && cc.Type != JTokenType.Null)
                    slots = (int)cc;
                else if (req?.ConveyorCapacityOverride is int ov)
                    slots = ov;
                else
                    slots = cfg.DefaultActiveSlots;
                if (slots < 1) slots = 1;

                var queue = doc.TopSection?.ToObject<BusQueueData>();
                var model = BusLevelModel.Build(doc.Grid, queue, slots);

                // No buses authored yet -> can't answer solvability. Honest Unknown.
                if (model.TotalBuses == 0)
                {
                    result.Status = AnalysisStatus.Unknown;
                    result.Solvable = false;
                    result.FailureReason = "level has no buses to analyze";
                    return Done(result, sw);
                }

                // Cheap structural pre-check: per-color blocks must equal per-color
                // bus capacity, or the level is provably unsolvable.
                if (!model.IsColorBalanced())
                {
                    result.Status = AnalysisStatus.Unsolvable;
                    result.Solvable = false;
                    result.FailureReason = BalanceReason(model);
                    return Done(result, sw);
                }

                // Exact win-path solver — small grids only (no gravity => full 2D
                // state, so it does not scale; large grids fall through to rollout).
                if (model.W * model.H <= cfg.SmallGridThreshold)
                {
                    long nodeBudget = (req != null && req.NodeBudget > 0) ? req.NodeBudget : cfg.NodeBudget;
                    long timeout    = (req != null && req.TimeoutMs  > 0) ? req.TimeoutMs  : cfg.TimeoutMs;
                    var solver = new BusSolver(nodeBudget, timeout);
                    var solve = solver.Solve(model);
                    result.StatesExplored = solve.Nodes;

                    switch (solve.Outcome)
                    {
                        case BusSolver.Outcome.Solvable:
                            result.Status = AnalysisStatus.Solvable;
                            result.Solvable = true;
                            result.WinPathCount = 1; // first-solution search
                            result.WinPath = solve.WinPath;
                            break;
                        case BusSolver.Outcome.Unsolvable:
                            result.Status = AnalysisStatus.Unsolvable;
                            result.Solvable = false;
                            result.FailureReason = "no winning pull order exists";
                            break;
                        default: // BudgetExceeded
                            result.Status = AnalysisStatus.TimedOut;
                            result.Solvable = false;
                            result.CountWasCapped = true;
                            result.FailureReason = $"search budget hit ({solve.Nodes} nodes) — solvability unknown";
                            break;
                    }
                }
                // else: Status stays Unknown; rollout below decides.

                // Measured APS unless the solver already proved Unsolvable.
                if (result.Status != AnalysisStatus.Unsolvable)
                {
                    int runs = (req != null && req.RolloutCount > 0) ? req.RolloutCount : cfg.Runs;
                    int seed = (req != null && req.Seed != 0) ? req.Seed : cfg.RngSeed;
                    var player = new BusAveragePlayer();
                    var est = player.Estimate(model, new BusAveragePlayer.Config
                    {
                        Epsilon = cfg.Epsilon, Lookahead = cfg.Lookahead, Runs = runs, Seed = seed,
                    });
                    result.WinRate = est.WinRate;
                    result.RolloutsRun = est.Runs;
                    result.ApsEstimate = est.Aps; // already capped at BusAveragePlayer.ApsCap
                    result.ApsCalibrated = cfg.ApsCalibrated;
                    result.Band = cfg.BandFor(est.Aps);

                    // Rollout rescue: on large grids the exact solver is skipped
                    // (Status == Unknown) or hits budget (TimedOut). A winning playout
                    // still PROVES solvability, so upgrade to Solvable when any won.
                    // WinPath stays null (no canonical path found) for those cases.
                    if ((result.Status == AnalysisStatus.TimedOut || result.Status == AnalysisStatus.Unknown)
                        && result.WinRate > 0.0)
                    {
                        result.Status = AnalysisStatus.Solvable;
                        result.Solvable = true;
                        result.FailureReason = null;
                    }
                }

                return Done(result, sw);
            }
            catch (System.Exception ex)
            {
                return Fault(result, sw, "analyzer faulted: " + ex.Message);
            }
        }

        // Per-color balance reason. BusLevelModel.IsColorBalanced returns only a
        // bool, so re-derive the offending color here from the model's public
        // readonly arrays (same invariant, surfaced as a clear message).
        private static string BalanceReason(BusLevelModel m)
        {
            var blocks = new int[m.NumColors];
            for (int i = 0; i < m.Grid.Length; i++)
            {
                int c = m.Grid[i];
                if (c >= 0 && c < m.NumColors) blocks[c]++;
            }
            var cap = new int[m.NumColors];
            for (int col = 0; col < m.Columns; col++)
                for (int k = 0; k < m.BusColor[col].Length; k++)
                {
                    int c = m.BusColor[col][k];
                    if (c >= 0 && c < m.NumColors) cap[c] += m.BusCap[col][k];
                }
            for (int k = 0; k < m.NumColors; k++)
                if (blocks[k] != cap[k])
                {
                    string name = (m.ColorNames != null && k < m.ColorNames.Length) ? m.ColorNames[k] : k.ToString();
                    return $"color '{name}' unbalanced: {blocks[k]} blocks vs {cap[k]} bus capacity";
                }
            return "color balance mismatch";
        }

        private static LevelAnalysisResult Fault(LevelAnalysisResult r, Stopwatch sw, string reason)
        {
            r.Status = AnalysisStatus.Faulted;
            r.Solvable = false;
            r.FailureReason = reason;
            return Done(r, sw);
        }

        private static LevelAnalysisResult Done(LevelAnalysisResult r, Stopwatch sw)
        {
            sw.Stop();
            r.ElapsedMs = sw.ElapsedMilliseconds;
            return r;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

`assets-refresh`, then `tests-run` (class `BusBuddiesAnalyzerTests`). Expected: PASS (4 tests). Then run the **whole** assembly once to confirm no regressions: `tests-run` `{ "testMode": "EditMode", "testAssembly": "Hoppa.BusBuddies.Editor.Tests" }`.

- [ ] **Step 5: Commit (include `.meta` + new folder meta)**

```bash
git add Assets/BusBuddies/Editor/Analysis Assets/BusBuddies/Tests/Editor/BusBuddiesAnalyzerTests.cs
git status --porcelain Assets/BusBuddies/Editor/Analysis   # verify both .cs, their .meta, and the Analysis folder .meta staged
git commit -m "feat(busbuddies): BusBuddiesAnalyzer + config over the 1a sim (1b)"
```

---

## Task 7: Layer-2 config assets (cell defs, rule, analyzer config + analyzer)

**Files (created via `CreateAssetMenu`, not hand-written YAML — guarantees correct new-script GUID binding):**
- `Assets/BusBuddies/Data/Config/CellDefs/BBEmptyCellDef.asset`
- `Assets/BusBuddies/Data/Config/CellDefs/BBPixelCellDef.asset`
- `Assets/BusBuddies/Data/Config/Rules/BBColorBalanceRule.asset`
- `Assets/BusBuddies/Data/Config/Analysis/BusBuddiesAnalyzerConfig.asset`
- `Assets/BusBuddies/Data/Config/Analysis/BusBuddiesAnalyzer.asset`

**Interfaces:**
- Consumes: the Task-3/5/6 scripts (their `CreateAssetMenu` entries) and `BusBuddiesPalette.asset` (Task 4).
- Produces: the asset instances the profile (Task 8) references.

> **Testability honesty:** these are data assets — there is no unit test; verification is import + `assets-get-data` field checks + the Task 8 manual run. Because each references a **newly created** script (whose `.meta` GUID is generated on import), create them through the editor's Create menu / MCP so Unity resolves the GUID — do not hand-author cross-referencing YAML.

- [ ] **Step 1: Create the cell-def assets and set fields**

Create via **Assets ▸ Create ▸ Hoppa/BusBuddies/Cells/Empty** and **…/Cells/Pixel** (or `mcp__ai-game-developer__script-execute` calling `ScriptableObject.CreateInstance` + `AssetDatabase.CreateAsset`). Place at the paths above. Then set fields (Inspector or `mcp__ai-game-developer__object-modify` / `assets-modify`):

- `BBEmptyCellDef.asset`: `_typeId = "bb.empty"`, `_displayName = "Empty"`, `_paletteGroup = "BusBuddies"`.
- `BBPixelCellDef.asset`: `_typeId = "bb.pixel"`, `_displayName = "Pixel"`, `_paletteGroup = "BusBuddies"`, `_palette = BusBuddiesPalette.asset`.

**Critical:** `_typeId` must equal the cell-data `CellTypeId` (`bb.empty` / `bb.pixel`) so the `CellTypeRegistry` binds saved JSON back to the right type.

- [ ] **Step 2: Create the rule + analyzer config + analyzer assets and set fields**

- **Assets ▸ Create ▸ Hoppa/BusBuddies/Validation/Color Balance** → `Rules/BBColorBalanceRule.asset`; set `_id = "busbuddies.color_balance"`.
- **Assets ▸ Create ▸ Hoppa/BusBuddies/Analysis/Bus Buddies Analyzer Config** → `Analysis/BusBuddiesAnalyzerConfig.asset`; leave defaults (`DefaultActiveSlots=5`, `SmallGridThreshold=64`, bands `{2,4,6,8}`).
- **Assets ▸ Create ▸ Hoppa/BusBuddies/Analysis/Bus Buddies Analyzer** → `Analysis/BusBuddiesAnalyzer.asset`; set `_config = BusBuddiesAnalyzerConfig.asset`.

- [ ] **Step 3: Verify the assets imported clean**

`assets-refresh`, then `assets-get-data` each asset: confirm `m_Script` resolved (no missing-script), `_typeId`/`_id`/`_config`/`_palette` set as above. If any shows missing-script, the `CreateAssetMenu` path or script compile failed — fix before continuing.

- [ ] **Step 4: Commit (include every `.meta`)**

```bash
git add Assets/BusBuddies/Data
git status --porcelain Assets/BusBuddies/Data   # verify all 5 .asset, their .meta, and any new folder .meta (CellDefs, Rules, Analysis) are staged
git commit -m "feat(busbuddies): cell-def + rule + analyzer config assets (1b)"
```

---

## Task 8: `BusBuddiesProfile.asset` (wire available slots) + manual verification

**Files:**
- `Assets/BusBuddies/Data/Config/BusBuddiesProfile.asset`

**Interfaces:**
- Consumes: every asset from Tasks 4 + 7.
- Produces: a `GameProfile` instance that the shared `LevelEditorWindow` loads to drive the Bus Buddies editor (paint / validate / analyze).

> **Testability honesty:** `GameProfile` is **sealed** and references multiple newly-created assets by GUID — it is built via the Create menu + Inspector (or MCP), not hand-authored YAML, then verified by a **manual checklist** (no headless unit test). The analyzer logic the profile exposes is already proven by Task 6.

**Slots to wire NOW (1b):**

| Slot | Value |
|---|---|
| `_schemaId` | `busbuddies` |
| `_colorPalette` | `BusBuddiesPalette.asset` |
| `_gridWidth` / `_gridHeight` | `30` / `30` |
| `_cellTypes` | `[ BBEmptyCellDef, BBPixelCellDef ]` — **empty FIRST** (it is the erase/fill type) |
| `_rules` | `[ BBColorBalanceRule ]` |
| `_levelAnalyzer` | `BusBuddiesAnalyzer.asset` |
| `_spoolsBelowGrid` | `true` (grid on top, bus queue below — matches CWS flip + reference art) |

**Slots left EMPTY (filled in 1b-ii / 1c — note in the asset/PR):** `_exporters`, `_imageToGrid`, `_levelGenerator`, `_generatorConfig`, `_orderPanel`, `_canvasOverlay`, `_topSectionScript`. `_bottomSectionScript` ← `BusBuddiesQueuePanel` and `_levelCompleter` ← `BusBuddiesAutofiller` are wired in **1b-ii**.

- [ ] **Step 1: Create + wire the profile**

**Assets ▸ Create ▸ Hoppa/Level Editor/Game Profile** → `Assets/BusBuddies/Data/Config/BusBuddiesProfile.asset`. Set the slots in the table above (Inspector drag-drop or MCP `object-modify`). Leave the deferred slots empty.

- [ ] **Step 2: Manual verification checklist**

`assets-refresh`, ensure no compile errors (`console-get-logs`), then open the level editor on this profile and confirm:

1. **Profile loads** — the `LevelEditorWindow` opens with a 30×30 grid, grid anchored top with the (empty) bottom-section region below (`_spoolsBelowGrid` honoured).
2. **Palette shows** the 8 bus colours; the **Pixel** brush paints coloured blocks and the **Empty** brush clears them; painted pixels render in their palette colour and empties show the checker.
3. **Validation panel** lists `BBColorBalanceRule` output — paint an unbalanced colour and confirm an **Error** row; balanced shows **Info**. (Bus capacities can't be authored yet — the queue panel is 1b-ii — so capacity reads 0 and every painted colour validly shows as unbalanced until 1b-ii; that is expected.)
4. **Analysis side panel** appears (the profile has a `LevelAnalyzer`); the **Analyze** button is present. (A full hand-authored *Solvable + APS + Band* read requires authoring buses, which needs the **1b-ii** queue panel; the analyzer math itself is already green in Task 6.)

Record the checklist outcome in the PR description.

- [ ] **Step 3: Commit (include `.meta`)**

```bash
git add Assets/BusBuddies/Data/Config/BusBuddiesProfile.asset Assets/BusBuddies/Data/Config/BusBuddiesProfile.asset.meta
git status --porcelain Assets/BusBuddies/Data   # verify the profile .asset + .meta staged
git commit -m "feat(busbuddies): BusBuddiesProfile wiring palette/cells/rule/analyzer (1b)"
```

---

## Follow-on plan — 1b-ii: Autofiller + Queue Editor Panel

A separate plan (`2026-06-29-bus-buddies-1b-ii-autofill-queue.md`) closes the **author** half:

- **`BusBuddiesAutofiller : LevelCompleterAsset` + `BusBuddiesAutofillConfig`** — clone `YAKSpoolAutofiller`'s structure: inventory per-colour blocks from the grid via `IColoredCell`; per attempt pick a column count in `ColumnRange` (1–5), `Partition` each colour's total into bus capacities in `[Min,Max]`≈`Avg` **summing exactly** (solvable by construction), assign buses across columns; emit a `BusQueueData` `TopSection`; gate each candidate on `profile.LevelAnalyzer.Analyze` (keep `Solvable`, accept first within `ApsTolerance`, else closest-solvable best-effort). Hidden/Connected carried as data only (`Hidden` flag / `ConnectedId` pairing); modeling deferred. **Fully TDD-able** (mirror `YakAutofillTests` + `YakAutofillTestFixtures`: reflect-inject configs, build a multi-colour grid, assert `LevelCompletionResult.Succeeded`/`Analysis.Solvable` and exact per-colour capacity balance). Then wire `_levelCompleter` on the profile.
- **`BusBuddiesQueuePanel : TopSectionPanel`** — clone `YAKSpoolSectionPanel`: 1–5 bus columns, per-bus colour swatch (right-click palette picker) + capacity int field + Hidden toggle + Connected pairing affordance + reorder/move/add/delete; read/write `session.Document.TopSection` as `BusQueueData` via `JObject.FromObject`/`ToObject`. **Manual/IMGUI — no unit tests;** ship with a build recipe + a manual checklist (author buses → Validation balances → Analyze yields Solvable + APS + Band, closing the spec §12 1b success criterion). Then wire `_bottomSectionScript` on the profile.

Splitting here keeps the autofiller's TDD cycle and the panel's manual cycle each coherent, and lets the analyze path land and be reviewed independently.

---

## Self-check (spec coverage)

- **Editor asmdef** → Task 1. **Cell definitions** (pixel + empty) → Task 3. **Palette** → Task 4 (colour list flagged lead-confirmable). **Balance rule** → Task 5. **Analyzer + config** (conveyorCount resolution, queue parse, Build, balance precheck → Unsolvable, small-grid solver → WinPath, estimator → WinRate/APS, rollout-rescue, Band) → Task 6. **Config assets** → Task 7. **Profile asset** (schemaId busbuddies, 30×30, `_spoolsBelowGrid=true`, available slots; deferred slots noted) → Task 8. **Serialization round-trip** → Task 2.
- **Deferred to 1b-ii (explicit):** `BusBuddiesAutofiller` + `BusBuddiesAutofillConfig`, `BusBuddiesQueuePanel`, and the profile's `_levelCompleter` + `_bottomSectionScript` wiring.
- **Deferred to 1c (out of 1b entirely):** `BusBuddiesImageToGrid`, exporter + colour source, generator + config, batch/curve harness.
- All analyzer/solver/estimator/cell signatures match the **shipped 1a source** read verbatim (`BusLevelModel.Build`, `IsColorBalanced`, public readonly fields; `BusSolver.Outcome/Result/Solve`; `BusAveragePlayer.Config/Result/Estimate/ApsCap`). No placeholders; type/method names consistent across tasks.
