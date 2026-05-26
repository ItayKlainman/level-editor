# YarnTwist Spool Auto-fill + Win-path Analysis — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Designer paints the grid manually; editor auto-completes the spool top section and reports exact win-path count. Two new generic Layer 1 contracts (`ILevelAnalyzer`, `ILevelCompleter`) plus YarnTwist v1 implementations.

**Architecture:** Layer 1 in `Packages/com.hoppa.leveleditor.core/Editor/Analysis/` defines generic contracts + an IMGUI side panel. Layer 2 in `Assets/YarnTwist/Editor/Analysis/` implements a DFS win-path simulator + a reroll-loop spool fill targeted at a Difficulty band. Game-specific tunings (`YarnTwistSpoolAutofillConfig`) live on the Layer 2 completer asset — `GameProfile` only gains generic typed fields (`_levelAnalyzer`, `_levelCompleter`) plus an `_extensions` list for future per-profile data.

**Tech Stack:** Unity 2022.3, C# (.NET Framework 4.x), Newtonsoft.Json for `JObject` serialisation, IMGUI for editor UI, NUnit for tests, MCP tool `mcp__ai-game-developer__tests-run` for running edit-mode tests, `mcp__ai-game-developer__assets-refresh` for triggering Unity recompiles, `mcp__ai-game-developer__assets-modify` for editing existing `.asset` YAML.

**Source spec:** `docs/superpowers/specs/2026-05-26-yarntwist-spool-autofill-design.md`.

---

## Conventions for every task

- **Use `Write`/`Edit` tools** for all `.cs` and `.json` files. Never use PowerShell `Out-File` / `Set-Content` (causes manual-approval pause per `feedback_write_files_directly`).
- **After writing any new `.cs` file**, call `mcp__ai-game-developer__assets-refresh` so Unity generates the `.meta` file. **Both** `.cs` and `.meta` must be committed — Unity silently excludes `.cs` files with missing `.meta` from compilation in package consumers (see prior `MultiSelectPanel.cs.meta` incident, SESSION_NOTES.md §"Package hosting").
- **Run tests** via `mcp__ai-game-developer__tests-run` with `testMode: "EditMode"` and a class filter. The MCP refreshes assets and defers across domain reloads if scripts changed — no manual recompile needed.
- **Commit each task** as a single commit, immediately after the test step passes. Use semantic prefixes consistent with this repo's history (e.g. `feat(analysis):`, `feat(yarntwist):`, `test(yarntwist):`).
- **Layer 2 sync note:** the YarnTwist Layer 2 files in this repo also live in the YarnTwist game project at `E:/Projects/Hoppa/YarnTwist/Assets/_YAT/Scripts/Editor/...`. Do **not** sync during the plan execution — keep changes here only. The sync to the game repo is a manual operator step after the plan completes (and after the matching Layer 1 UPM tag is published).

---

## File structure (created in this plan)

### Layer 1 — new files under `Packages/com.hoppa.leveleditor.core/Editor/Analysis/`

| File | Responsibility |
|---|---|
| `AnalysisRequest.cs` | DTO: `Mode`, `WinPathCap`, `TimeoutMs`, `ConveyorCapacityOverride?` |
| `LevelAnalysisResult.cs` | DTO: `Solvable`, `WinPathCount`, `CountWasCapped`, `StatesExplored`, `ElapsedMs`, `FailureReason`, `ToString()` |
| `ILevelAnalyzer.cs` | Interface — `Analyze(doc, profile, req) → LevelAnalysisResult` |
| `LevelAnalyzerAsset.cs` | Abstract `ScriptableObject : ILevelAnalyzer` base |
| `CompletionRequest.cs` | DTO: `Difficulty`, `TargetAPS?`, `Seed`, `ConveyorCapacityOverride?` |
| `LevelCompletionResult.cs` | DTO: `TopSection (JObject)`, `Analysis`, `Succeeded`, `CandidatesTried`, `SeedUsed`, `ElapsedMs`, `CandidatePathCountHistogram`, `FailureReason` |
| `ILevelCompleter.cs` | Interface — `Complete(doc, profile, req) → LevelCompletionResult` |
| `LevelCompleterAsset.cs` | Abstract `ScriptableObject : ILevelCompleter` base |
| `AutofillPanel.cs` | IMGUI side-panel (right column); calls into the two assets from the profile |

### Layer 1 — modified

| File | Modification |
|---|---|
| `Packages/com.hoppa.leveleditor.core/Editor/Infrastructure/GameProfile.cs` | Add `_levelAnalyzer`, `_levelCompleter`, `_extensions` fields + public accessors + `GetExtension<T>()` |
| `Packages/com.hoppa.leveleditor.core/Editor/Window/LevelEditorWindow.cs` | Slot `AutofillPanel` into the right column (under Summary) when `profile.LevelAnalyzer != null` |

### Layer 2 — new files under `Assets/YarnTwist/Editor/Analysis/`

| File | Responsibility |
|---|---|
| `YarnTwistLevelAnalyzer.cs` | DFS win-path simulator (Box / ArrowBox / Tunnel rules); subclass of `LevelAnalyzerAsset` |
| `YarnTwistSpoolAutofiller.cs` | Reroll-loop spool fill targeted at Difficulty band; subclass of `LevelCompleterAsset` |
| `YarnTwistSpoolAutofillConfig.cs` | `ScriptableObject` config — Difficulty curves, caps, timeouts |

### Layer 2 — new assets

| Asset | Notes |
|---|---|
| `Assets/YarnTwist/Data/Config/YarnTwistLevelAnalyzer.asset` | Instance of `YarnTwistLevelAnalyzer` |
| `Assets/YarnTwist/Data/Config/YarnTwistSpoolAutofiller.asset` | Instance of `YarnTwistSpoolAutofiller`; references `YarnTwistSpoolAutofillConfig.asset` |
| `Assets/YarnTwist/Data/Config/YarnTwistSpoolAutofillConfig.asset` | Instance of `YarnTwistSpoolAutofillConfig` |

### Layer 2 — modified

| File | Modification |
|---|---|
| `Assets/YarnTwist/Data/Config/YarnTwistProfile.asset` | Wire `_levelAnalyzer` and `_levelCompleter` references |

### Tests — new

| File | Asmdef |
|---|---|
| `Packages/com.hoppa.leveleditor.core/Tests/Editor/GameProfileExtensionsTests.cs` | `Hoppa.LevelEditor.Core.Editor.Tests` |
| `Assets/YarnTwist/Tests/Editor/YarnTwistLevelAnalyzerTests.cs` | `Hoppa.YarnTwist.Editor.Tests` |
| `Assets/YarnTwist/Tests/Editor/YarnTwistSpoolAutofillerTests.cs` | `Hoppa.YarnTwist.Editor.Tests` |

---

## Task 1: Layer 1 — Request / Result DTOs

**Files:**
- Create: `Packages/com.hoppa.leveleditor.core/Editor/Analysis/AnalysisRequest.cs`
- Create: `Packages/com.hoppa.leveleditor.core/Editor/Analysis/LevelAnalysisResult.cs`
- Create: `Packages/com.hoppa.leveleditor.core/Editor/Analysis/CompletionRequest.cs`
- Create: `Packages/com.hoppa.leveleditor.core/Editor/Analysis/LevelCompletionResult.cs`

No test in this task — these are pure data classes. They are exercised by every subsequent test.

- [ ] **Step 1: Create `AnalysisRequest.cs`**

```csharp
using System;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Inputs to ILevelAnalyzer.Analyze. Created by the AutofillPanel for standalone
    // analyses and by ILevelCompleter implementations during their reroll loops.
    public sealed class AnalysisRequest
    {
        public AnalysisMode Mode = AnalysisMode.Count;

        // Stop counting once this many distinct winning sequences have been found.
        // The result is reported as the cap value with CountWasCapped = true.
        public int WinPathCap = 10_000;

        // Soft per-call timeout. Analyzers should check elapsed time at every
        // recursion entry and abort with whatever count they have so far.
        public long TimeoutMs = 5_000;

        // Game-specific override for the conveyor / belt / queue capacity.
        // Null = analyzer uses its own default.
        public int? ConveyorCapacityOverride;
    }

    public enum AnalysisMode
    {
        // Short-circuit at the first winning leaf; WinPathCount = 0 or 1.
        Solvable,

        // Exhaustive count of winning sequences up to WinPathCap.
        Count,
    }
}
```

- [ ] **Step 2: Create `LevelAnalysisResult.cs`**

```csharp
using System.Text;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Output of ILevelAnalyzer.Analyze. Game-agnostic: every analyzer reports
    // the same shape so the AutofillPanel can render results uniformly.
    public sealed class LevelAnalysisResult
    {
        public bool   Solvable;
        public long   WinPathCount;
        public bool   CountWasCapped;
        public long   StatesExplored;
        public long   ElapsedMs;
        public string FailureReason;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append("Win paths: ");
            sb.Append(CountWasCapped ? "≥" + WinPathCount : WinPathCount.ToString());
            sb.Append(" · ");
            sb.Append(Solvable ? "solvable" : "unsolvable");
            sb.Append(" · ");
            sb.Append(StatesExplored).Append(" states · ").Append(ElapsedMs).Append(" ms");
            if (!string.IsNullOrEmpty(FailureReason))
                sb.Append(" · ").Append(FailureReason);
            return sb.ToString();
        }
    }
}
```

- [ ] **Step 3: Create `CompletionRequest.cs`**

```csharp
namespace Hoppa.LevelEditor.Core.Editor
{
    // Inputs to ILevelCompleter.Complete. The Difficulty knob is a 1..10 hint
    // that the Layer 2 completer maps to game-specific behaviour via its own
    // config asset. Seed = 0 means "pick a random seed".
    public sealed class CompletionRequest
    {
        public int    Difficulty = 5;
        public float? TargetAPS;
        public int    Seed;
        public int?   ConveyorCapacityOverride;
    }
}
```

- [ ] **Step 4: Create `LevelCompletionResult.cs`**

```csharp
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json.Linq;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Output of ILevelCompleter.Complete. TopSection is the JSON blob the
    // caller can drop straight into LevelDocument.TopSection. Analysis is the
    // win-path analysis of the chosen fill, so the panel never has to re-run
    // the analyzer to display results.
    public sealed class LevelCompletionResult
    {
        public JObject               TopSection;
        public LevelAnalysisResult   Analysis;
        public bool                  Succeeded;
        public int                   CandidatesTried;
        public int                   SeedUsed;
        public long                  ElapsedMs;
        public Dictionary<int, int>  CandidatePathCountHistogram = new Dictionary<int, int>();
        public string                FailureReason;

        public override string ToString()
        {
            var sb = new StringBuilder();
            sb.Append(Succeeded ? "Hit band" : "Best-effort");
            sb.Append(" · ").Append(CandidatesTried).Append(" candidate(s) · seed ").Append(SeedUsed);
            sb.Append(" · ").Append(ElapsedMs).Append(" ms");
            if (Analysis != null)
                sb.Append(" · win paths: ").Append(Analysis.CountWasCapped ? "≥" + Analysis.WinPathCount : Analysis.WinPathCount.ToString());
            if (!string.IsNullOrEmpty(FailureReason))
                sb.Append(" · ").Append(FailureReason);
            return sb.ToString();
        }
    }
}
```

- [ ] **Step 5: Refresh AssetDatabase so Unity generates `.meta` files**

Call `mcp__ai-game-developer__assets-refresh` (no args). Wait for `requestId` completion. Expected: no compile errors.

- [ ] **Step 6: Verify all four `.meta` files now exist**

Use `Glob` for pattern `Packages/com.hoppa.leveleditor.core/Editor/Analysis/*.meta`. Expected: 4 results.

- [ ] **Step 7: Commit**

```bash
git add Packages/com.hoppa.leveleditor.core/Editor/Analysis/
git commit -m "feat(analysis): add Layer 1 analyze + complete DTOs"
```

---

## Task 2: Layer 1 — Interfaces and abstract `ScriptableObject` bases

**Files:**
- Create: `Packages/com.hoppa.leveleditor.core/Editor/Analysis/ILevelAnalyzer.cs`
- Create: `Packages/com.hoppa.leveleditor.core/Editor/Analysis/LevelAnalyzerAsset.cs`
- Create: `Packages/com.hoppa.leveleditor.core/Editor/Analysis/ILevelCompleter.cs`
- Create: `Packages/com.hoppa.leveleditor.core/Editor/Analysis/LevelCompleterAsset.cs`

- [ ] **Step 1: Create `ILevelAnalyzer.cs`**

```csharp
namespace Hoppa.LevelEditor.Core.Editor
{
    // Generic contract: "given a complete level, is it playable and how many
    // distinct winning sequences exist?" Layer 1 does NOT define what
    // 'playable' means — the Layer 2 analyzer subclass knows the game rules.
    public interface ILevelAnalyzer
    {
        LevelAnalysisResult Analyze(LevelDocument doc, GameProfile profile, AnalysisRequest req);
    }
}
```

- [ ] **Step 2: Create `LevelAnalyzerAsset.cs`**

```csharp
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Base type the inspector type-filters on. GameProfile._levelAnalyzer is
    // typed as LevelAnalyzerAsset so designers can only drop in concrete
    // analyzer assets (not arbitrary ScriptableObjects).
    public abstract class LevelAnalyzerAsset : ScriptableObject, ILevelAnalyzer
    {
        public abstract LevelAnalysisResult Analyze(LevelDocument doc, GameProfile profile, AnalysisRequest req);
    }
}
```

- [ ] **Step 3: Create `ILevelCompleter.cs`**

```csharp
namespace Hoppa.LevelEditor.Core.Editor
{
    // Generic contract: "given a partial level + knobs, fill in what's missing."
    // Layer 2 decides what 'missing' means (spools, enemies, item placements…).
    // For YarnTwist v1 this fills the TopSection from a hand-painted grid.
    public interface ILevelCompleter
    {
        LevelCompletionResult Complete(LevelDocument doc, GameProfile profile, CompletionRequest req);
    }
}
```

- [ ] **Step 4: Create `LevelCompleterAsset.cs`**

```csharp
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public abstract class LevelCompleterAsset : ScriptableObject, ILevelCompleter
    {
        public abstract LevelCompletionResult Complete(LevelDocument doc, GameProfile profile, CompletionRequest req);
    }
}
```

- [ ] **Step 5: Refresh AssetDatabase**

Call `mcp__ai-game-developer__assets-refresh`. Expected: no compile errors.

- [ ] **Step 6: Commit**

```bash
git add Packages/com.hoppa.leveleditor.core/Editor/Analysis/
git commit -m "feat(analysis): add ILevelAnalyzer + ILevelCompleter contracts"
```

---

## Task 3: Layer 1 — Extend `GameProfile` (analyzer / completer / extensions)

**Files:**
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Infrastructure/GameProfile.cs`
- Create: `Packages/com.hoppa.leveleditor.core/Tests/Editor/GameProfileExtensionsTests.cs`

- [ ] **Step 1: Write the failing test**

Create `Packages/com.hoppa.leveleditor.core/Tests/Editor/GameProfileExtensionsTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor.Tests
{
    public class GameProfileExtensionsTests
    {
        private sealed class FakeExtensionA : ScriptableObject { }
        private sealed class FakeExtensionB : ScriptableObject { }

        [Test]
        public void GetExtension_ReturnsNull_WhenListEmpty()
        {
            var profile = ScriptableObject.CreateInstance<GameProfile>();
            Assert.IsNull(profile.GetExtension<FakeExtensionA>());
            Object.DestroyImmediate(profile);
        }

        [Test]
        public void GetExtension_ReturnsMatchingType_WhenPresent()
        {
            var profile = ScriptableObject.CreateInstance<GameProfile>();
            var a = ScriptableObject.CreateInstance<FakeExtensionA>();
            var b = ScriptableObject.CreateInstance<FakeExtensionB>();
            // Inject via reflection — keeps GameProfile.cs free of test-only setters.
            var field = typeof(GameProfile).GetField("_extensions",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(field, "GameProfile is missing the _extensions field.");
            field.SetValue(profile, new System.Collections.Generic.List<ScriptableObject> { a, b });

            Assert.AreSame(a, profile.GetExtension<FakeExtensionA>());
            Assert.AreSame(b, profile.GetExtension<FakeExtensionB>());

            Object.DestroyImmediate(a);
            Object.DestroyImmediate(b);
            Object.DestroyImmediate(profile);
        }
    }
}
```

- [ ] **Step 2: Refresh AssetDatabase and run the test — expect compile error**

Call `mcp__ai-game-developer__assets-refresh`, then `mcp__ai-game-developer__tests-run` with:

```json
{"testMode": "EditMode", "filter": {"testClass": "Hoppa.LevelEditor.Core.Editor.Tests.GameProfileExtensionsTests"}}
```

Expected: compile error referencing the missing `_extensions` field / `GetExtension<T>()` method.

- [ ] **Step 3: Modify `GameProfile.cs` — add fields and accessors**

Edit `Packages/com.hoppa.leveleditor.core/Editor/Infrastructure/GameProfile.cs`. Add three serialized fields after the existing `_generatorConfig` (around line 46), and add the matching public accessors after the existing `GeneratorConfig` getter (around line 57).

Insert after `[SerializeField] private ScriptableObject _generatorConfig;`:

```csharp

        [Tooltip("Optional: assign a LevelAnalyzerAsset subclass to enable the Spool Analysis side panel and report win-path counts for the current document.\nExample: YarnTwistLevelAnalyzer")]
        [SerializeField] private LevelAnalyzerAsset _levelAnalyzer;

        [Tooltip("Optional: assign a LevelCompleterAsset subclass to enable the 'Auto-fill' button in the Spool Analysis panel. Generates parts of the level (e.g. the top section) from the hand-painted grid + a Difficulty knob.\nExample: YarnTwistSpoolAutofiller")]
        [SerializeField] private LevelCompleterAsset _levelCompleter;

        [Tooltip("Generic per-profile extension data. Used by Layer 2 implementations that need profile-scoped configuration outside the typed fields above. Look up by type via GetExtension<T>().")]
        [SerializeField] private List<ScriptableObject> _extensions = new List<ScriptableObject>();
```

Insert after `public ScriptableObject GeneratorConfig => _generatorConfig;`:

```csharp
        public LevelAnalyzerAsset  LevelAnalyzer  => _levelAnalyzer;
        public LevelCompleterAsset LevelCompleter => _levelCompleter;

        // Generic typed-lookup over the _extensions list. Returns the first
        // ScriptableObject that is assignable to T, or null. Use for Layer 2
        // configuration data that should be attached to the profile itself
        // (not to a Layer 2 asset).
        public T GetExtension<T>() where T : ScriptableObject
        {
            foreach (var e in _extensions)
                if (e is T t) return t;
            return null;
        }
```

- [ ] **Step 4: Refresh and re-run the test — expect PASS**

Call `mcp__ai-game-developer__assets-refresh`, then `mcp__ai-game-developer__tests-run` with the same filter as Step 2. Expected: both tests pass.

- [ ] **Step 5: Commit**

```bash
git add Packages/com.hoppa.leveleditor.core/Editor/Infrastructure/GameProfile.cs \
        Packages/com.hoppa.leveleditor.core/Tests/Editor/GameProfileExtensionsTests.cs
git commit -m "feat(profile): add LevelAnalyzer + LevelCompleter + extensions slots"
```

---

## Task 4: YarnTwist analyzer — basic boxes (fixtures 1, 2, 3)

**Files:**
- Create: `Assets/YarnTwist/Editor/Analysis/YarnTwistLevelAnalyzer.cs`
- Create: `Assets/YarnTwist/Tests/Editor/YarnTwistLevelAnalyzerTests.cs`

This task lands the analyzer with support for boxes only (no arrow boxes, no tunnels). All three fixtures exercise the basic DFS + conveyor-capacity dead-end detection.

- [ ] **Step 1: Write the failing tests**

Create `Assets/YarnTwist/Tests/Editor/YarnTwistLevelAnalyzerTests.cs`:

```csharp
using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YarnTwist;
using Hoppa.YarnTwist.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor.Tests
{
    public class YarnTwistLevelAnalyzerTests
    {
        private const string ProfilePath = "Assets/YarnTwist/Data/Config/YarnTwistProfile.asset";
        private GameProfile _profile;
        private YarnTwistLevelAnalyzer _analyzer;

        [SetUp]
        public void SetUp()
        {
            _profile = AssetDatabase.LoadAssetAtPath<GameProfile>(ProfilePath);
            Assert.IsNotNull(_profile);
            // Until Task 12 wires the asset onto the profile, instantiate the
            // analyzer directly. After Task 12, _profile.LevelAnalyzer would
            // also work — but tests that don't depend on profile-wiring stay
            // robust by constructing the analyzer themselves.
            _analyzer = ScriptableObject.CreateInstance<YarnTwistLevelAnalyzer>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_analyzer != null) Object.DestroyImmediate(_analyzer);
        }

        // ── Fixture 1: 1 pink box, 3 pink spools → 1 path ───────────────

        [Test]
        public void Analyze_SingleBoxWithMatchedSpools_ReportsExactlyOnePath()
        {
            var doc = MakeDoc(width: 1, height: 1,
                cells: (0, 0, new YarnBoxCell { ColorId = "pink" }),
                topSection: SpoolColumns(("pink","pink","pink"), (), (), ()));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Solvable, r.FailureReason);
            Assert.AreEqual(1L, r.WinPathCount);
            Assert.IsFalse(r.CountWasCapped);
        }

        // ── Fixture 2: color mismatch → 0 paths ─────────────────────────

        [Test]
        public void Analyze_ColorMismatch_ReportsUnsolvable()
        {
            var doc = MakeDoc(width: 1, height: 1,
                cells: (0, 0, new YarnBoxCell { ColorId = "pink" }),
                topSection: SpoolColumns(("blue","blue","blue"), (), (), ()));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.IsFalse(r.Solvable);
            Assert.AreEqual(0L, r.WinPathCount);
            Assert.IsNotNull(r.FailureReason);
        }

        // ── Fixture 3: conveyor overflow inevitable → 0 paths ───────────

        [Test]
        public void Analyze_OverflowInevitable_ReportsUnsolvable()
        {
            // 5 pink boxes = 45 balls. 3 pink spools = 9 ball capacity.
            // Even with conveyor cap=24, only 9 balls can ever be consumed.
            var doc = MakeDoc(width: 5, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnBoxCell{ColorId="pink"}),
                    (1,0,new YarnBoxCell{ColorId="pink"}),
                    (2,0,new YarnBoxCell{ColorId="pink"}),
                    (3,0,new YarnBoxCell{ColorId="pink"}),
                    (4,0,new YarnBoxCell{ColorId="pink"}),
                },
                topSection: SpoolColumns(("pink","pink","pink"), (), (), ()));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.IsFalse(r.Solvable);
            Assert.AreEqual(0L, r.WinPathCount);
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static LevelDocument MakeDoc(int width, int height, params (int x, int y, ICellData cell)[] cells) =>
            MakeDoc(width, height, cells, SpoolColumns());

        private static LevelDocument MakeDoc(int width, int height, IEnumerable<(int x, int y, ICellData cell)> cells, JObject topSection)
        {
            var grid = new GridData<ICellData>(width, height);
            for (int i = 0; i < grid.Cells.Length; i++)
                grid.Cells[i] = new YarnEmptyCell();
            foreach (var (x, y, cell) in cells)
                grid.Set(x, y, cell);

            return new LevelDocument
            {
                SchemaVersion = "yarn-twist.v1",
                LevelId       = "test",
                Grid          = grid,
                TopSection    = topSection,
            };
        }

        private static JObject SpoolColumns(params (string a, string b, string c)[] columns)
        {
            var data = new YarnTopSectionData();
            for (int i = 0; i < 4; i++)
                data.Columns.Add(new YarnSpoolColumn());
            for (int i = 0; i < columns.Length && i < 4; i++)
            {
                foreach (var c in new[] { columns[i].a, columns[i].b, columns[i].c })
                    if (!string.IsNullOrEmpty(c))
                        data.Columns[i].Spools.Add(new YarnSpoolData { ColorId = c });
            }
            return JObject.FromObject(data);
        }
    }
}
```

- [ ] **Step 2: Create the analyzer skeleton (compile-only, no behaviour)**

Create `Assets/YarnTwist/Editor/Analysis/YarnTwistLevelAnalyzer.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    // Win-path DFS simulator for YarnTwist. Counts the number of distinct
    // tap-orderings of grid items (boxes / arrowboxes / tunnel queue entries)
    // that empty the grid and clear every spool without overflowing the
    // conveyor.
    //
    // State is hashed for memoization. The belt is modelled as a multiset of
    // colors (order on the belt is irrelevant for win-counting under the
    // continuous-circulation match rule).
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Analysis/Yarn Twist Level Analyzer")]
    public sealed class YarnTwistLevelAnalyzer : LevelAnalyzerAsset
    {
        private const int Columns        = 4;
        private const int BallsPerSpool  = 3;
        private const int BallsPerItem   = 9;
        private const int DefaultCapacity = 24;

        public override LevelAnalysisResult Analyze(LevelDocument doc, GameProfile profile, AnalysisRequest req)
        {
            var sw = Stopwatch.StartNew();
            var result = new LevelAnalysisResult();

            if (doc == null || doc.Grid == null)
            {
                result.FailureReason = "document or grid is null";
                sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            }

            int capacity = req?.ConveyorCapacityOverride ?? DefaultCapacity;
            int cap      = req?.WinPathCap     ?? 10_000;
            long timeout = req?.TimeoutMs      ?? 5_000;
            var  mode    = req?.Mode           ?? AnalysisMode.Count;

            // Materialise grid into a flat tappable-item list + parse top section.
            var items = BuildItems(doc.Grid);
            var columns = ParseTopSection(doc.TopSection);

            // Fast invariant check: every color present in items has at least one
            // spool of that color somewhere in the columns.
            if (!ColorCoverageOk(items, columns, out var missing))
            {
                result.FailureReason = $"color '{missing}' has no matching spools";
                sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            }

            // Run DFS.
            var ctx = new SearchContext(items, columns, capacity, cap, timeout, mode, sw);
            ctx.Run();

            result.Solvable       = ctx.PathCount > 0;
            result.WinPathCount   = Math.Min(ctx.PathCount, cap);
            result.CountWasCapped = ctx.PathCount >= cap || ctx.TimedOut;
            result.StatesExplored = ctx.StatesExplored;
            sw.Stop();
            result.ElapsedMs = sw.ElapsedMilliseconds;
            if (!result.Solvable && string.IsNullOrEmpty(result.FailureReason))
                result.FailureReason = "no winning sequence exists";
            return result;
        }

        // ── Parsing ─────────────────────────────────────────────────────

        private struct Item
        {
            public ItemKind Kind;
            public string   ColorId;     // for Box / ArrowBox / single-queue-entry
            public int      PrereqIndex; // for ArrowBox: index into items[] of the box that must be tapped first; -1 if none
            public List<string> Queue;   // for Tunnel: queue of colors (in order). For Box/ArrowBox: null.
        }

        private enum ItemKind { Box, ArrowBox, Tunnel }

        private static List<Item> BuildItems(GridData<ICellData> grid)
        {
            // In Task 4 we only support boxes. Arrow boxes and tunnels land in
            // Tasks 5 and 6.
            var items = new List<Item>();
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < grid.Width;  x++)
            {
                var cell = grid.Get(x, y);
                if (cell is YarnBoxCell b)
                    items.Add(new Item { Kind = ItemKind.Box, ColorId = b.ColorId, PrereqIndex = -1 });
            }
            return items;
        }

        // ── Top section ─────────────────────────────────────────────────

        private struct Column { public List<string> SpoolColors; }

        private static Column[] ParseTopSection(JObject topJson)
        {
            var cols = new Column[Columns];
            for (int i = 0; i < Columns; i++) cols[i] = new Column { SpoolColors = new List<string>() };
            if (topJson == null) return cols;
            var data = topJson.ToObject<YarnTopSectionData>();
            if (data?.Columns == null) return cols;
            for (int i = 0; i < Math.Min(data.Columns.Count, Columns); i++)
                foreach (var s in data.Columns[i].Spools)
                    cols[i].SpoolColors.Add(s.ColorId);
            return cols;
        }

        private static bool ColorCoverageOk(List<Item> items, Column[] cols, out string missing)
        {
            missing = null;
            var spoolColors = new HashSet<string>();
            foreach (var c in cols) foreach (var s in c.SpoolColors) spoolColors.Add(s);
            foreach (var it in items)
                if (!spoolColors.Contains(it.ColorId))
                {
                    missing = it.ColorId;
                    return false;
                }
            return true;
        }

        // ── DFS ─────────────────────────────────────────────────────────

        private sealed class SearchContext
        {
            private readonly List<Item> _items;
            private readonly Column[]   _cols;
            private readonly int        _capacity;
            private readonly int        _cap;
            private readonly long       _timeoutMs;
            private readonly AnalysisMode _mode;
            private readonly Stopwatch  _sw;

            // Mutable state.
            private readonly bool[] _tapped;          // per-item
            private readonly int[]  _spoolHead;       // [Columns]
            private readonly int[]  _spoolFill;       // [Columns]
            private readonly Dictionary<string, int> _bag = new Dictionary<string, int>();

            public long PathCount;
            public long StatesExplored;
            public bool TimedOut;

            public SearchContext(List<Item> items, Column[] cols, int capacity, int cap, long timeoutMs, AnalysisMode mode, Stopwatch sw)
            {
                _items = items; _cols = cols;
                _capacity = capacity; _cap = cap; _timeoutMs = timeoutMs;
                _mode = mode; _sw = sw;
                _tapped    = new bool[items.Count];
                _spoolHead = new int[Columns];
                _spoolFill = new int[Columns];
            }

            public void Run() => Dfs();

            private void Dfs()
            {
                if (TimedOut) return;
                if (_sw.ElapsedMilliseconds > _timeoutMs) { TimedOut = true; return; }
                StatesExplored++;

                int bagSum = 0;
                foreach (var v in _bag.Values) bagSum += v;
                if (bagSum > _capacity) return;

                bool allTapped = true;
                for (int i = 0; i < _items.Count; i++) if (!_tapped[i]) { allTapped = false; break; }
                bool allSpoolsCleared = true;
                for (int k = 0; k < Columns; k++)
                    if (_spoolHead[k] < _cols[k].SpoolColors.Count) { allSpoolsCleared = false; break; }

                if (allTapped && allSpoolsCleared && bagSum == 0)
                {
                    PathCount++;
                    return;
                }
                if (allTapped) return; // no producer left but state isn't winning

                for (int i = 0; i < _items.Count; i++)
                {
                    if (_tapped[i]) continue;
                    if (!IsTappable(i)) continue;
                    if (PathCount >= _cap) return;
                    if (_mode == AnalysisMode.Solvable && PathCount >= 1) return;

                    // Snapshot state.
                    var savedBag = new Dictionary<string, int>(_bag);
                    var savedHead = (int[])_spoolHead.Clone();
                    var savedFill = (int[])_spoolFill.Clone();

                    ApplyTap(i);
                    _tapped[i] = true;

                    Dfs();

                    // Restore.
                    _tapped[i] = false;
                    _bag.Clear(); foreach (var kv in savedBag) _bag[kv.Key] = kv.Value;
                    Array.Copy(savedHead, _spoolHead, Columns);
                    Array.Copy(savedFill, _spoolFill, Columns);

                    if (TimedOut) return;
                    if (PathCount >= _cap) return;
                    if (_mode == AnalysisMode.Solvable && PathCount >= 1) return;
                }
            }

            private bool IsTappable(int i)
            {
                // Task 4: only boxes. Arrow / tunnel land in Tasks 5–6.
                return _items[i].Kind == ItemKind.Box;
            }

            private void ApplyTap(int i)
            {
                var it = _items[i];
                AddToBag(it.ColorId, BallsPerItem);
                ResolveMatches();
            }

            private void AddToBag(string color, int count)
            {
                _bag.TryGetValue(color, out var n);
                _bag[color] = n + count;
            }

            private void ResolveMatches()
            {
                bool changed = true;
                while (changed)
                {
                    changed = false;
                    for (int k = 0; k < Columns; k++)
                    {
                        while (_spoolHead[k] < _cols[k].SpoolColors.Count)
                        {
                            var color = _cols[k].SpoolColors[_spoolHead[k]];
                            if (!_bag.TryGetValue(color, out var have) || have <= 0) break;
                            int need = BallsPerSpool - _spoolFill[k];
                            int take = Math.Min(need, have);
                            _spoolFill[k] += take;
                            _bag[color] = have - take;
                            changed = true;
                            if (_spoolFill[k] == BallsPerSpool)
                            {
                                _spoolHead[k]++;
                                _spoolFill[k] = 0;
                            }
                            else break;
                        }
                    }
                }
            }
        }
    }
}
```

- [ ] **Step 3: Run the tests — expect fixtures 1, 2, 3 to PASS**

Call `mcp__ai-game-developer__assets-refresh`, then `mcp__ai-game-developer__tests-run`:

```json
{"testMode": "EditMode", "filter": {"testClass": "Hoppa.YarnTwist.Editor.Tests.YarnTwistLevelAnalyzerTests"}}
```

Expected: 3 / 3 pass.

- [ ] **Step 4: Commit**

```bash
git add Assets/YarnTwist/Editor/Analysis/YarnTwistLevelAnalyzer.cs \
        Assets/YarnTwist/Tests/Editor/YarnTwistLevelAnalyzerTests.cs
git commit -m "feat(yarntwist/analysis): win-path DFS for basic-box grids"
```

---

## Task 5: YarnTwist analyzer — arrow box gating (fixture 4)

**Files:**
- Modify: `Assets/YarnTwist/Editor/Analysis/YarnTwistLevelAnalyzer.cs`
- Modify: `Assets/YarnTwist/Tests/Editor/YarnTwistLevelAnalyzerTests.cs`

- [ ] **Step 1: Append the failing test to `YarnTwistLevelAnalyzerTests.cs`**

Add before the `// ── Helpers ──` comment line:

```csharp
        // ── Fixture 4: arrow box A points to box B → exactly 1 path ─────

        [Test]
        public void Analyze_ArrowBoxGating_OnlyOnePathWhenChained()
        {
            // Layout (1 row, 2 cells): [ArrowBox(pink, Direction=Right)] [Box(pink)]
            // Arrow at (0,0) points to box at (1,0). Arrow only becomes
            // tappable once the box at (1,0) is tapped.
            // Total balls: 9 + 9 = 18 pink. Need 6 pink spools = 2 columns × 3.
            var doc = MakeDoc(width: 2, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnArrowBoxCell{ColorId="pink", Direction=YarnDirection.Right}),
                    (1,0,new YarnBoxCell{ColorId="pink"}),
                },
                topSection: SpoolColumns(
                    ("pink","pink","pink"),
                    ("pink","pink","pink"),
                    (),
                    ()));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Solvable, r.FailureReason);
            Assert.AreEqual(1L, r.WinPathCount);  // must be B then A — A first is not tappable
        }
```

- [ ] **Step 2: Run the test — expect FAIL**

```json
{"testMode": "EditMode", "filter": {"testMethod": "Hoppa.YarnTwist.Editor.Tests.YarnTwistLevelAnalyzerTests.Analyze_ArrowBoxGating_OnlyOnePathWhenChained"}}
```

Expected: FAIL because `BuildItems` only handles `YarnBoxCell`, so the arrow box is invisible to the search and the level reports `Solvable=false` (only the box exists; tapping it doesn't satisfy "all items consumed" because the arrow box's balls still need to flow). Actually, since the arrow is invisible to the search, the test reports `WinPathCount=0`. Either way, FAIL.

- [ ] **Step 3: Modify `YarnTwistLevelAnalyzer.cs` — handle arrow boxes**

Replace the `BuildItems` method body:

```csharp
        private static List<Item> BuildItems(GridData<ICellData> grid)
        {
            // Pass 1: collect (x, y) → tappable index for boxes and arrow boxes.
            var items = new List<Item>();
            var idxAt = new Dictionary<long, int>(); // y * width + x → items index

            int W = grid.Width;
            long Key(int x, int y) => (long)y * W + x;

            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < W;          x++)
            {
                var cell = grid.Get(x, y);
                if (cell is YarnBoxCell b)
                {
                    idxAt[Key(x, y)] = items.Count;
                    items.Add(new Item { Kind = ItemKind.Box, ColorId = b.ColorId, PrereqIndex = -1 });
                }
                else if (cell is YarnArrowBoxCell ab)
                {
                    idxAt[Key(x, y)] = items.Count;
                    items.Add(new Item { Kind = ItemKind.ArrowBox, ColorId = ab.ColorId, PrereqIndex = -1 });
                }
            }

            // Pass 2: resolve arrow-box prerequisites by direction → neighbor item.
            int k = 0;
            for (int y = 0; y < grid.Height; y++)
            for (int x = 0; x < W;          x++)
            {
                var cell = grid.Get(x, y);
                if (cell is YarnArrowBoxCell ab)
                {
                    var (nx, ny) = NeighborOf(x, y, ab.Direction);
                    if (grid.InBounds(nx, ny) && idxAt.TryGetValue(Key(nx, ny), out var nIdx))
                    {
                        var it = items[k];
                        it.PrereqIndex = nIdx;
                        items[k] = it;
                    }
                    // else: arrow's neighbor isn't a tappable item — arrow is
                    // permanently unreachable; PrereqIndex stays -1 and the
                    // arrow will never become tappable. The level is broken;
                    // YarnArrowBoxTargetRule should already flag this.
                    k++;
                }
                else if (cell is YarnBoxCell) k++;
            }

            return items;
        }

        private static (int x, int y) NeighborOf(int x, int y, YarnDirection d) => d switch
        {
            YarnDirection.Up    => (x, y + 1),
            YarnDirection.Down  => (x, y - 1),
            YarnDirection.Left  => (x - 1, y),
            YarnDirection.Right => (x + 1, y),
            _                   => (x, y),
        };
```

Replace `IsTappable`:

```csharp
            private bool IsTappable(int i)
            {
                var it = _items[i];
                switch (it.Kind)
                {
                    case ItemKind.Box: return true;
                    case ItemKind.ArrowBox:
                        // Locked until the prerequisite has been tapped. An
                        // arrow box with no valid prerequisite (PrereqIndex
                        // = -1) is permanently locked.
                        return it.PrereqIndex >= 0 && _tapped[it.PrereqIndex];
                    case ItemKind.Tunnel: return false; // Task 6
                    default: return false;
                }
            }
```

- [ ] **Step 4: Re-run the test — expect PASS**

```json
{"testMode": "EditMode", "filter": {"testClass": "Hoppa.YarnTwist.Editor.Tests.YarnTwistLevelAnalyzerTests"}}
```

Expected: 4 / 4 pass (fixtures 1–4).

- [ ] **Step 5: Commit**

```bash
git add Assets/YarnTwist/Editor/Analysis/YarnTwistLevelAnalyzer.cs \
        Assets/YarnTwist/Tests/Editor/YarnTwistLevelAnalyzerTests.cs
git commit -m "feat(yarntwist/analysis): arrow-box gating (chained prerequisites)"
```

---

## Task 6: YarnTwist analyzer — tunnels (fixture 5)

**Files:**
- Modify: `Assets/YarnTwist/Editor/Analysis/YarnTwistLevelAnalyzer.cs`
- Modify: `Assets/YarnTwist/Tests/Editor/YarnTwistLevelAnalyzerTests.cs`

Tunnels are modelled as N sequential "tappable phases" — one per queue entry. Each phase taps emit 9 balls of the queue's current color.

- [ ] **Step 1: Append the failing test**

Add before `// ── Helpers ──`:

```csharp
        // ── Fixture 5: tunnel queue=[pink, blue], matched spools → 1 path ──

        [Test]
        public void Analyze_TunnelQueue_DequeuesInOrder()
        {
            // 1 tunnel with queue [pink, blue] = 9 pink + 9 blue balls.
            // 3 pink spools + 3 blue spools, in two columns.
            var tunnel = new YarnTunnelCell {
                OutputDirection = YarnDirection.Up,
                Queue = new List<string> { "pink", "blue" },
            };
            var doc = MakeDoc(width: 1, height: 2,
                cells: new (int, int, ICellData)[] {
                    (0,0,tunnel),
                    (0,1,new YarnEmptyCell()),
                },
                topSection: SpoolColumns(
                    ("pink","pink","pink"),
                    ("blue","blue","blue"),
                    (),
                    ()));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Solvable, r.FailureReason);
            Assert.AreEqual(1L, r.WinPathCount); // tunnel tapped twice in queue order
        }
```

- [ ] **Step 2: Run the test — expect FAIL**

Same filter as Task 5. Expected: FAIL (tunnel currently invisible to `BuildItems`).

- [ ] **Step 3: Add tunnel support to the analyzer**

Modify `Item` struct to track `TunnelTapsRemaining` and the queue. Replace the struct (top of the class file):

```csharp
        private struct Item
        {
            public ItemKind  Kind;
            public string    ColorId;
            public int       PrereqIndex;
            public List<string> Queue;     // for Tunnel
            public int       QueueIndex;   // for Tunnel: next dequeue position (0-based)
        }
```

Extend `BuildItems` — after the `YarnArrowBoxCell` branch in Pass 1, add a tunnel branch:

```csharp
                else if (cell is YarnTunnelCell t && t.Queue != null && t.Queue.Count > 0)
                {
                    idxAt[Key(x, y)] = items.Count;
                    items.Add(new Item {
                        Kind = ItemKind.Tunnel,
                        ColorId = null,
                        PrereqIndex = -1,
                        Queue = new List<string>(t.Queue),
                        QueueIndex = 0,
                    });
                }
```

…and increment `k` in Pass 2 when a tunnel is encountered (so the index lines up with Pass 1):

```csharp
                else if (cell is YarnTunnelCell) k++;
```

Replace `ColorCoverageOk` to account for tunnel queue colors:

```csharp
        private static bool ColorCoverageOk(List<Item> items, Column[] cols, out string missing)
        {
            missing = null;
            var spoolColors = new HashSet<string>();
            foreach (var c in cols) foreach (var s in c.SpoolColors) spoolColors.Add(s);
            foreach (var it in items)
            {
                if (it.Kind == ItemKind.Tunnel)
                {
                    foreach (var q in it.Queue)
                        if (!spoolColors.Contains(q)) { missing = q; return false; }
                }
                else
                {
                    if (!spoolColors.Contains(it.ColorId)) { missing = it.ColorId; return false; }
                }
            }
            return true;
        }
```

Replace `IsTappable` so tunnels are tappable while they still have queue entries:

```csharp
            private bool IsTappable(int i)
            {
                var it = _items[i];
                switch (it.Kind)
                {
                    case ItemKind.Box:      return !_tapped[i];
                    case ItemKind.ArrowBox: return !_tapped[i] && it.PrereqIndex >= 0 && _tapped[it.PrereqIndex];
                    case ItemKind.Tunnel:   return it.QueueIndex < it.Queue.Count;
                    default: return false;
                }
            }
```

Note: tunnels reuse `_tapped[i]` is **not** meaningful any more (they can be tapped repeatedly). The `Dfs` loop must skip the `if (_tapped[i]) continue;` shortcut for tunnels. Update that loop:

```csharp
                for (int i = 0; i < _items.Count; i++)
                {
                    if (!IsTappable(i)) continue;
                    if (PathCount >= _cap) return;
                    if (_mode == AnalysisMode.Solvable && PathCount >= 1) return;

                    // Snapshot
                    var savedBag = new Dictionary<string, int>(_bag);
                    var savedHead = (int[])_spoolHead.Clone();
                    var savedFill = (int[])_spoolFill.Clone();
                    bool savedTapped = _tapped[i];
                    int savedQueueIdx = _items[i].QueueIndex;

                    ApplyTap(i);

                    Dfs();

                    // Restore
                    _bag.Clear(); foreach (var kv in savedBag) _bag[kv.Key] = kv.Value;
                    Array.Copy(savedHead, _spoolHead, Columns);
                    Array.Copy(savedFill, _spoolFill, Columns);
                    _tapped[i] = savedTapped;
                    var restored = _items[i]; restored.QueueIndex = savedQueueIdx; _items[i] = restored;

                    if (TimedOut) return;
                    if (PathCount >= _cap) return;
                    if (_mode == AnalysisMode.Solvable && PathCount >= 1) return;
                }
```

Replace `ApplyTap` to dispatch by kind:

```csharp
            private void ApplyTap(int i)
            {
                var it = _items[i];
                if (it.Kind == ItemKind.Tunnel)
                {
                    string color = it.Queue[it.QueueIndex];
                    var bumped = it; bumped.QueueIndex = it.QueueIndex + 1;
                    _items[i] = bumped;
                    AddToBag(color, BallsPerItem);
                }
                else
                {
                    _tapped[i] = true;
                    AddToBag(it.ColorId, BallsPerItem);
                }
                ResolveMatches();
            }
```

Replace `Dfs`'s `allTapped` check so it accounts for tunnel queues:

```csharp
                bool allConsumed = true;
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i].Kind == ItemKind.Tunnel)
                    {
                        if (_items[i].QueueIndex < _items[i].Queue.Count) { allConsumed = false; break; }
                    }
                    else if (!_tapped[i]) { allConsumed = false; break; }
                }
                bool allSpoolsCleared = true;
                for (int k = 0; k < Columns; k++)
                    if (_spoolHead[k] < _cols[k].SpoolColors.Count) { allSpoolsCleared = false; break; }

                if (allConsumed && allSpoolsCleared && bagSum == 0) { PathCount++; return; }
                if (allConsumed) return;
```

`SearchContext._items` must become a writable field (`List<Item>`) rather than reading the analyzer's parameter — verify it already is (it was passed in as `List<Item>` and stored in `_items` in Task 4's skeleton).

- [ ] **Step 4: Re-run tests — expect PASS**

```json
{"testMode": "EditMode", "filter": {"testClass": "Hoppa.YarnTwist.Editor.Tests.YarnTwistLevelAnalyzerTests"}}
```

Expected: 5 / 5 pass.

- [ ] **Step 5: Commit**

```bash
git add Assets/YarnTwist/Editor/Analysis/YarnTwistLevelAnalyzer.cs \
        Assets/YarnTwist/Tests/Editor/YarnTwistLevelAnalyzerTests.cs
git commit -m "feat(yarntwist/analysis): tunnel queues (per-tap dequeue)"
```

---

## Task 7: YarnTwist analyzer — branching, cap, determinism, multi-column matches (fixtures 6, 7, 8, 9)

**Files:**
- Modify: `Assets/YarnTwist/Editor/Analysis/YarnTwistLevelAnalyzer.cs`
- Modify: `Assets/YarnTwist/Tests/Editor/YarnTwistLevelAnalyzerTests.cs`

This task adds memoization + tightens the cap / timeout semantics so the new tests pass.

- [ ] **Step 1: Append the four failing tests**

Add before `// ── Helpers ──`:

```csharp
        // ── Fixture 6: 2 independent matched boxes → exactly 2 paths ────

        [Test]
        public void Analyze_TwoIndependentBoxes_ReportsTwoPaths()
        {
            var doc = MakeDoc(width: 2, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnBoxCell{ColorId="pink"}),
                    (1,0,new YarnBoxCell{ColorId="blue"}),
                },
                topSection: SpoolColumns(
                    ("pink","pink","pink"),
                    ("blue","blue","blue"),
                    (),
                    ()));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Solvable);
            Assert.AreEqual(2L, r.WinPathCount);
        }

        // ── Fixture 7: many independent matched boxes → hits cap ────────

        [Test]
        public void Analyze_HighBranching_HitsCap()
        {
            // 6 boxes of 6 distinct colors: 6! = 720 orderings. Cap at 100.
            string[] colors = { "pink", "blue", "teal", "green", "yellow", "purple" };
            var cells = new List<(int, int, ICellData)>();
            for (int i = 0; i < colors.Length; i++)
                cells.Add((i, 0, new YarnBoxCell { ColorId = colors[i] }));

            var data = new YarnTopSectionData();
            for (int i = 0; i < 4; i++) data.Columns.Add(new YarnSpoolColumn());
            // 18 spools distributed across the 4 columns.
            int spoolIdx = 0;
            foreach (var c in colors)
                for (int s = 0; s < 3; s++)
                {
                    data.Columns[spoolIdx % 4].Spools.Add(new YarnSpoolData { ColorId = c });
                    spoolIdx++;
                }

            var doc = MakeDoc(width: colors.Length, height: 1, cells: cells, topSection: JObject.FromObject(data));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, WinPathCap = 100, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Solvable);
            Assert.AreEqual(100L, r.WinPathCount);
            Assert.IsTrue(r.CountWasCapped);
        }

        // ── Fixture 8: determinism ──────────────────────────────────────

        [Test]
        public void Analyze_SameFixture_ReturnsIdenticalCount()
        {
            var doc1 = MakeDoc(width: 2, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnBoxCell{ColorId="pink"}),
                    (1,0,new YarnBoxCell{ColorId="blue"}),
                },
                topSection: SpoolColumns(("pink","pink","pink"),("blue","blue","blue"),(),()));
            var doc2 = MakeDoc(width: 2, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnBoxCell{ColorId="pink"}),
                    (1,0,new YarnBoxCell{ColorId="blue"}),
                },
                topSection: SpoolColumns(("pink","pink","pink"),("blue","blue","blue"),(),()));

            var r1 = _analyzer.Analyze(doc1, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            var r2 = _analyzer.Analyze(doc2, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.AreEqual(r1.WinPathCount, r2.WinPathCount);
            Assert.AreEqual(r1.Solvable, r2.Solvable);
        }

        // ── Fixture 9: two columns same active color → greedy multi-clear ─

        [Test]
        public void Analyze_TwoColumnsSameActiveColor_OneTapMultiClears()
        {
            // 1 box of pink (9 balls). 2 columns × 3 pink spools = 18 ball capacity
            // — so 9 balls fully clear column 0 (3 spools), nothing left for column 1.
            // After tap: column 0 advanced past all 3, column 1 still has 3 active.
            // Spools cleared: 3 / 6. Therefore Solvable=false (only 1 box, 6 spools).
            // Same logic with 2 pink boxes (18 balls) = both columns clear exactly.
            var doc = MakeDoc(width: 2, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnBoxCell{ColorId="pink"}),
                    (1,0,new YarnBoxCell{ColorId="pink"}),
                },
                topSection: SpoolColumns(
                    ("pink","pink","pink"),
                    ("pink","pink","pink"),
                    (), ()));
            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Solvable);
            Assert.AreEqual(2L, r.WinPathCount); // tap order between the two boxes
        }
```

- [ ] **Step 2: Run the tests — expect mixed pass/fail**

```json
{"testMode": "EditMode", "filter": {"testClass": "Hoppa.YarnTwist.Editor.Tests.YarnTwistLevelAnalyzerTests"}}
```

Expected: fixtures 6, 8, 9 already pass on the existing implementation; fixture 7 may pass or may be slow without memoization. Continue regardless to add the polishing changes.

- [ ] **Step 3: Add memoization + state hashing to `SearchContext`**

In `YarnTwistLevelAnalyzer.cs`, replace the `Dfs()` method with a memoized version that returns the count from the current state:

```csharp
            private readonly Dictionary<ulong, long> _memo = new Dictionary<ulong, long>();

            public void Run() => PathCount = Math.Min(DfsMemo(), long.MaxValue);

            private long DfsMemo()
            {
                if (TimedOut) return 0;
                if (_sw.ElapsedMilliseconds > _timeoutMs) { TimedOut = true; return 0; }
                StatesExplored++;

                int bagSum = 0;
                foreach (var v in _bag.Values) bagSum += v;
                if (bagSum > _capacity) return 0;

                bool allConsumed = true;
                for (int i = 0; i < _items.Count; i++)
                {
                    if (_items[i].Kind == ItemKind.Tunnel)
                    {
                        if (_items[i].QueueIndex < _items[i].Queue.Count) { allConsumed = false; break; }
                    }
                    else if (!_tapped[i]) { allConsumed = false; break; }
                }
                bool allSpoolsCleared = true;
                for (int k = 0; k < Columns; k++)
                    if (_spoolHead[k] < _cols[k].SpoolColors.Count) { allSpoolsCleared = false; break; }

                if (allConsumed && allSpoolsCleared && bagSum == 0) return 1;
                if (allConsumed) return 0;

                ulong stateKey = HashState();
                if (_memo.TryGetValue(stateKey, out var cached)) return cached;

                long total = 0;

                for (int i = 0; i < _items.Count; i++)
                {
                    if (!IsTappable(i)) continue;
                    if (total >= _cap) break;
                    if (_mode == AnalysisMode.Solvable && total >= 1) break;

                    var savedBag = new Dictionary<string, int>(_bag);
                    var savedHead = (int[])_spoolHead.Clone();
                    var savedFill = (int[])_spoolFill.Clone();
                    bool savedTapped = _tapped[i];
                    int savedQueueIdx = _items[i].QueueIndex;

                    ApplyTap(i);
                    long sub = DfsMemo();
                    total = Math.Min(_cap, total + sub);

                    _bag.Clear(); foreach (var kv in savedBag) _bag[kv.Key] = kv.Value;
                    Array.Copy(savedHead, _spoolHead, Columns);
                    Array.Copy(savedFill, _spoolFill, Columns);
                    _tapped[i] = savedTapped;
                    var restored = _items[i]; restored.QueueIndex = savedQueueIdx; _items[i] = restored;

                    if (TimedOut) break;
                }

                _memo[stateKey] = total;
                return total;
            }

            // Compact 64-bit state hash. NOT a perfect hash — collisions
            // possible but rare. For exact counts we'd need to detect
            // collisions; since the worst case is over-counting up to the
            // cap (which is itself an upper bound), the impact is acceptable
            // and tests assert exact small numbers that don't collide.
            private ulong HashState()
            {
                ulong h = 1469598103934665603UL; // FNV offset
                const ulong P = 1099511628211UL;

                // tapped bitmask + tunnel queue positions
                for (int i = 0; i < _items.Count; i++)
                {
                    int v = _items[i].Kind == ItemKind.Tunnel
                        ? _items[i].QueueIndex
                        : (_tapped[i] ? 1 : 0);
                    h = (h ^ (ulong)v) * P;
                }
                for (int k = 0; k < Columns; k++)
                {
                    h = (h ^ (ulong)_spoolHead[k]) * P;
                    h = (h ^ (ulong)_spoolFill[k]) * P;
                }
                // bag — iterate in sorted color order for determinism
                var keys = new List<string>(_bag.Keys);
                keys.Sort(StringComparer.Ordinal);
                foreach (var key in keys)
                {
                    h = (h ^ (ulong)key.GetHashCode()) * P;
                    h = (h ^ (ulong)_bag[key]) * P;
                }
                return h;
            }
```

- [ ] **Step 4: Run all analyzer tests — expect 9 / 9 PASS**

```json
{"testMode": "EditMode", "filter": {"testClass": "Hoppa.YarnTwist.Editor.Tests.YarnTwistLevelAnalyzerTests"}}
```

- [ ] **Step 5: Commit**

```bash
git add Assets/YarnTwist/Editor/Analysis/YarnTwistLevelAnalyzer.cs \
        Assets/YarnTwist/Tests/Editor/YarnTwistLevelAnalyzerTests.cs
git commit -m "feat(yarntwist/analysis): memoize DFS + branching/cap/determinism tests"
```

---

## Task 8: YarnTwist — `YarnTwistSpoolAutofillConfig` SO

**Files:**
- Create: `Assets/YarnTwist/Editor/Analysis/YarnTwistSpoolAutofillConfig.cs`

No tests in this task — the config SO is pure data, exercised by the autofiller tests in Tasks 9–10.

- [ ] **Step 1: Create the config class**

```csharp
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    // Designer-tunable knobs for YarnTwistSpoolAutofiller. Lives as an asset
    // referenced from YarnTwistSpoolAutofiller.asset (not from GameProfile —
    // per the golden rule that GameProfile stays game-agnostic).
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Analysis/Yarn Twist Spool Auto-fill Config")]
    public sealed class YarnTwistSpoolAutofillConfig : ScriptableObject
    {
        [Tooltip("Target number of distinct win paths per Difficulty. Lower at high Difficulty = harder. Evaluated at the integer Difficulty.")]
        public AnimationCurve WinPathTargetByDifficulty =
            new AnimationCurve(new Keyframe(1f, 2000f), new Keyframe(10f, 5f));

        [Tooltip("Tolerance around the target band, expressed as a fraction. 0.5 = accept any count within ±50% of target.")]
        [Range(0f, 1f)] public float WinPathTolerance = 0.5f;

        [Tooltip("Percentage of generated spools marked hidden, per Difficulty. Hidden flags are placed by the autofiller but ignored by the analyzer's path counting.")]
        public AnimationCurve HiddenSpoolRatio =
            new AnimationCurve(new Keyframe(1f, 0f), new Keyframe(10f, 40f));

        [Tooltip("Maximum number of candidate spool arrangements to try before giving up and returning the best-so-far.")]
        public int MaxRerollAttempts = 100;

        [Tooltip("Analyzer's WinPathCap when called from the autofill loop.")]
        public int WinPathCap = 10_000;

        [Tooltip("Analyzer's TimeoutMs per autofill candidate.")]
        public int PerCandidateTimeoutMs = 500;

        [Tooltip("Hard upper bound on the autofill outer loop's total wall-clock time (ms).")]
        public int TotalTimeoutMs = 30_000;

        [Tooltip("Conveyor capacity used when the CompletionRequest doesn't override it.")]
        public int DefaultConveyorCapacity = 24;

        private void OnEnable()
        {
            // Defensive: Unity sometimes deserialises an AnimationCurve as
            // empty when the SO YAML doesn't include keyframes. Re-seed if so.
            if (WinPathTargetByDifficulty == null || WinPathTargetByDifficulty.length == 0)
                WinPathTargetByDifficulty = new AnimationCurve(new Keyframe(1f, 2000f), new Keyframe(10f, 5f));
            if (HiddenSpoolRatio == null || HiddenSpoolRatio.length == 0)
                HiddenSpoolRatio = new AnimationCurve(new Keyframe(1f, 0f), new Keyframe(10f, 40f));
        }
    }
}
```

- [ ] **Step 2: Refresh AssetDatabase**

Call `mcp__ai-game-developer__assets-refresh`. Expected: no compile errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/YarnTwist/Editor/Analysis/YarnTwistSpoolAutofillConfig.cs
git commit -m "feat(yarntwist/analysis): add spool auto-fill config SO"
```

---

## Task 9: YarnTwist autofiller — happy path (determinism, color balance, empty grid)

**Files:**
- Create: `Assets/YarnTwist/Editor/Analysis/YarnTwistSpoolAutofiller.cs`
- Create: `Assets/YarnTwist/Tests/Editor/YarnTwistSpoolAutofillerTests.cs`

- [ ] **Step 1: Write the failing tests**

Create `Assets/YarnTwist/Tests/Editor/YarnTwistSpoolAutofillerTests.cs`:

```csharp
using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YarnTwist;
using Hoppa.YarnTwist.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor.Tests
{
    public class YarnTwistSpoolAutofillerTests
    {
        private const string ProfilePath = "Assets/YarnTwist/Data/Config/YarnTwistProfile.asset";
        private GameProfile _profile;
        private YarnTwistLevelAnalyzer _analyzer;
        private YarnTwistSpoolAutofiller _autofiller;
        private YarnTwistSpoolAutofillConfig _config;

        [SetUp]
        public void SetUp()
        {
            _profile = AssetDatabase.LoadAssetAtPath<GameProfile>(ProfilePath);
            Assert.IsNotNull(_profile);

            _analyzer   = ScriptableObject.CreateInstance<YarnTwistLevelAnalyzer>();
            _autofiller = ScriptableObject.CreateInstance<YarnTwistSpoolAutofiller>();
            _config     = ScriptableObject.CreateInstance<YarnTwistSpoolAutofillConfig>();
            _config.WinPathTargetByDifficulty = new AnimationCurve(new Keyframe(1f, 100f), new Keyframe(10f, 1f));
            _config.WinPathTolerance = 0.9f; // wide band so determinism/coverage tests don't churn
            _config.MaxRerollAttempts = 10;
            _config.PerCandidateTimeoutMs = 200;
            _config.TotalTimeoutMs = 5_000;

            // Inject the config + analyzer via reflection (test-only).
            var cfgField = typeof(YarnTwistSpoolAutofiller).GetField("_config",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(cfgField, "YarnTwistSpoolAutofiller is missing the _config field");
            cfgField.SetValue(_autofiller, _config);

            var anaField = typeof(YarnTwistSpoolAutofiller).GetField("_analyzerOverride",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
            Assert.IsNotNull(anaField, "YarnTwistSpoolAutofiller is missing the _analyzerOverride field");
            anaField.SetValue(_autofiller, _analyzer);
        }

        [TearDown]
        public void TearDown()
        {
            if (_analyzer   != null) Object.DestroyImmediate(_analyzer);
            if (_autofiller != null) Object.DestroyImmediate(_autofiller);
            if (_config     != null) Object.DestroyImmediate(_config);
        }

        // ── Fixture 1: determinism — same seed → identical top section ─

        [Test]
        public void Complete_SameSeed_ProducesIdenticalTopSection()
        {
            var doc = MakeBoxGrid(("pink",4),("blue",4));
            var req = new CompletionRequest { Difficulty = 5, Seed = 12345, ConveyorCapacityOverride = 24 };
            var r1 = _autofiller.Complete(doc, _profile, req);
            var r2 = _autofiller.Complete(doc, _profile, req);
            Assert.IsNotNull(r1.TopSection);
            Assert.IsNotNull(r2.TopSection);
            Assert.AreEqual(r1.TopSection.ToString(Newtonsoft.Json.Formatting.None),
                            r2.TopSection.ToString(Newtonsoft.Json.Formatting.None));
        }

        // ── Fixture 2: output top section satisfies color balance ─────

        [Test]
        public void Complete_AnyResult_HasExactColorBalance()
        {
            var doc = MakeBoxGrid(("pink",5),("blue",3),("green",2));
            var r = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 5, Seed = 7777, ConveyorCapacityOverride = 24 });
            Assert.IsNotNull(r.TopSection);

            var counts = new Dictionary<string, int>();
            var data = r.TopSection.ToObject<YarnTopSectionData>();
            foreach (var col in data.Columns)
                foreach (var s in col.Spools)
                {
                    counts.TryGetValue(s.ColorId, out var n);
                    counts[s.ColorId] = n + 1;
                }
            Assert.AreEqual(5 * 3, counts["pink"]);
            Assert.AreEqual(3 * 3, counts["blue"]);
            Assert.AreEqual(2 * 3, counts["green"]);
        }

        // ── Fixture 7: empty grid trivially succeeds ───────────────────

        [Test]
        public void Complete_EmptyGrid_SucceedsWithEmptyTopSection()
        {
            var doc = MakeBoxGrid(); // no boxes
            var r = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 5, Seed = 1, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Succeeded);
            var data = r.TopSection.ToObject<YarnTopSectionData>();
            int totalSpools = 0;
            foreach (var col in data.Columns) totalSpools += col.Spools.Count;
            Assert.AreEqual(0, totalSpools);
        }

        // ── Helpers ────────────────────────────────────────────────────

        // Produces a 1-row grid with `count` boxes of each requested color, in left-to-right order.
        private static LevelDocument MakeBoxGrid(params (string color, int count)[] perColor)
        {
            int total = 0;
            foreach (var p in perColor) total += p.count;
            int width = System.Math.Max(1, total);
            var grid = new GridData<ICellData>(width, 1);
            for (int i = 0; i < grid.Cells.Length; i++) grid.Cells[i] = new YarnEmptyCell();

            int x = 0;
            foreach (var (color, n) in perColor)
                for (int i = 0; i < n; i++) { grid.Set(x, 0, new YarnBoxCell { ColorId = color }); x++; }

            return new LevelDocument
            {
                SchemaVersion = "yarn-twist.v1",
                LevelId       = "test",
                Grid          = grid,
                TopSection    = new JObject(),
            };
        }
    }
}
```

- [ ] **Step 2: Create the autofiller skeleton — runs reroll loop, applies hidden marks, no advanced logic yet**

Create `Assets/YarnTwist/Editor/Analysis/YarnTwistSpoolAutofiller.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    // Auto-fills the top section of a partially-painted YarnTwist level so
    // that color balance is satisfied by construction and the resulting
    // puzzle's win-path count lands in a Difficulty-targeted band.
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Analysis/Yarn Twist Spool Auto-fill")]
    public sealed class YarnTwistSpoolAutofiller : LevelCompleterAsset
    {
        private const int Columns       = 4;
        private const int BallsPerSpool = 3;
        private const int BallsPerItem  = 9;

        [Tooltip("Configuration asset with Difficulty curves, caps and timeouts.")]
        [SerializeField] private YarnTwistSpoolAutofillConfig _config;

        // Tests inject an analyzer instance via reflection; production code reads
        // _profile.LevelAnalyzer. Both code paths land here.
        [NonSerialized] private LevelAnalyzerAsset _analyzerOverride;

        public override LevelCompletionResult Complete(LevelDocument doc, GameProfile profile, CompletionRequest req)
        {
            var sw = Stopwatch.StartNew();
            var result = new LevelCompletionResult();

            if (_config == null)
            {
                result.FailureReason = "no YarnTwistSpoolAutofillConfig assigned";
                sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            }

            var analyzer = _analyzerOverride ?? profile?.LevelAnalyzer;
            if (analyzer == null)
            {
                result.FailureReason = "no analyzer wired on profile";
                sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            }

            int difficulty = Mathf.Clamp(req?.Difficulty ?? 5, 1, 10);
            int rootSeed = (req != null && req.Seed != 0)
                ? req.Seed
                : new System.Random().Next(1, int.MaxValue);
            result.SeedUsed = rootSeed;

            int capacity = req?.ConveyorCapacityOverride ?? _config.DefaultConveyorCapacity;

            // ── Inventory: per-color item count from grid ────────────────
            var perColor = new Dictionary<string, int>();
            if (doc?.Grid != null)
            {
                foreach (var cell in doc.Grid.Cells)
                {
                    switch (cell)
                    {
                        case YarnBoxCell      b: Bump(perColor, b.ColorId, 1); break;
                        case YarnArrowBoxCell a: Bump(perColor, a.ColorId, 1); break;
                        case YarnTunnelCell   t:
                            if (t.Queue != null)
                                foreach (var q in t.Queue) Bump(perColor, q, 1);
                            break;
                    }
                }
            }

            // Empty grid → empty top section, trivially succeeds.
            if (perColor.Count == 0)
            {
                var emptyTop = new YarnTopSectionData();
                for (int i = 0; i < Columns; i++) emptyTop.Columns.Add(new YarnSpoolColumn());
                result.TopSection = JObject.FromObject(emptyTop);
                result.Succeeded = true;
                result.CandidatesTried = 0;
                sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                return result;
            }

            // ── Flat spool list: 3 spools per item-color ────────────────
            var flatColors = new List<string>();
            foreach (var kv in perColor)
                for (int i = 0; i < kv.Value * BallsPerItem / BallsPerSpool; i++)
                    flatColors.Add(kv.Key);

            // ── Reroll loop ─────────────────────────────────────────────
            var rng = new System.Random(rootSeed);
            float hiddenPct = Mathf.Clamp01(_config.HiddenSpoolRatio.Evaluate(difficulty) / 100f);
            int hiddenN = Mathf.RoundToInt(flatColors.Count * hiddenPct);
            float target = _config.WinPathTargetByDifficulty.Evaluate(difficulty);
            float bandLow = target * (1f - _config.WinPathTolerance);
            float bandHigh = target * (1f + _config.WinPathTolerance);

            JObject bestTop = null;
            LevelAnalysisResult bestAnalysis = null;
            double bestDist = double.PositiveInfinity;

            for (int attempt = 0; attempt < _config.MaxRerollAttempts; attempt++)
            {
                if (sw.ElapsedMilliseconds > _config.TotalTimeoutMs) break;

                var working = new List<string>(flatColors);
                Shuffle(working, rng);
                var hiddenMask = new bool[working.Count];
                var idxs = Enumerable.Range(0, working.Count).ToList();
                Shuffle(idxs, rng);
                for (int i = 0; i < Math.Min(hiddenN, idxs.Count); i++) hiddenMask[idxs[i]] = true;

                var data = new YarnTopSectionData();
                for (int i = 0; i < Columns; i++) data.Columns.Add(new YarnSpoolColumn());
                for (int i = 0; i < working.Count; i++)
                    data.Columns[i % Columns].Spools.Add(new YarnSpoolData
                    {
                        ColorId = working[i],
                        Hidden  = hiddenMask[i],
                    });

                var topJson = JObject.FromObject(data);
                var candDoc = ShallowCopyWithTop(doc, topJson);

                var analysis = analyzer.Analyze(candDoc, profile, new AnalysisRequest
                {
                    Mode = AnalysisMode.Count,
                    WinPathCap = _config.WinPathCap,
                    TimeoutMs  = _config.PerCandidateTimeoutMs,
                    ConveyorCapacityOverride = capacity,
                });

                BumpHist(result.CandidatePathCountHistogram, analysis.WinPathCount);
                result.CandidatesTried++;

                if (analysis.Solvable && analysis.WinPathCount >= bandLow && analysis.WinPathCount <= bandHigh)
                {
                    result.TopSection = topJson;
                    result.Analysis = analysis;
                    result.Succeeded = true;
                    sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
                    return result;
                }

                double dist = Math.Abs(analysis.WinPathCount - target);
                if (dist < bestDist)
                {
                    bestDist = dist;
                    bestTop = topJson;
                    bestAnalysis = analysis;
                }
            }

            // Out of attempts — return best-so-far with Succeeded=false.
            result.TopSection = bestTop;
            result.Analysis   = bestAnalysis;
            result.Succeeded  = false;
            if (string.IsNullOrEmpty(result.FailureReason))
                result.FailureReason = "no candidate landed in Difficulty band; best-effort returned";
            sw.Stop(); result.ElapsedMs = sw.ElapsedMilliseconds;
            return result;
        }

        // ── Helpers ────────────────────────────────────────────────────

        private static void Bump(Dictionary<string, int> d, string key, int amount)
        {
            if (string.IsNullOrEmpty(key)) return;
            d.TryGetValue(key, out var v);
            d[key] = v + amount;
        }

        private static void Shuffle<T>(IList<T> list, System.Random rng)
        {
            for (int i = list.Count - 1; i > 0; i--)
            {
                int j   = rng.Next(0, i + 1);
                var tmp = list[i]; list[i] = list[j]; list[j] = tmp;
            }
        }

        private static void BumpHist(Dictionary<int, int> hist, long count)
        {
            // Log-base-2 bucketed histogram (bucket=0 means count=0).
            int bucket = count <= 0 ? 0 : (int)Math.Log(count, 2) + 1;
            hist.TryGetValue(bucket, out var n);
            hist[bucket] = n + 1;
        }

        private static LevelDocument ShallowCopyWithTop(LevelDocument src, JObject top)
        {
            return new LevelDocument
            {
                SchemaVersion = src.SchemaVersion,
                LevelId       = src.LevelId,
                DisplayName   = src.DisplayName,
                Metadata      = src.Metadata,
                Grid          = src.Grid,
                TopSection    = top,
                GameData      = src.GameData,
            };
        }
    }
}
```

- [ ] **Step 3: Run tests — expect 3 / 3 PASS**

```json
{"testMode": "EditMode", "filter": {"testClass": "Hoppa.YarnTwist.Editor.Tests.YarnTwistSpoolAutofillerTests"}}
```

- [ ] **Step 4: Commit**

```bash
git add Assets/YarnTwist/Editor/Analysis/YarnTwistSpoolAutofiller.cs \
        Assets/YarnTwist/Tests/Editor/YarnTwistSpoolAutofillerTests.cs
git commit -m "feat(yarntwist/analysis): spool auto-fill base loop"
```

---

## Task 10: YarnTwist autofiller — Difficulty correlation, hidden ratio, capacity override, impossible target

**Files:**
- Modify: `Assets/YarnTwist/Tests/Editor/YarnTwistSpoolAutofillerTests.cs`

These tests don't require new implementation — they validate that the existing loop responds correctly to its knobs.

- [ ] **Step 1: Append the four tests**

Add before `// ── Helpers ──`:

```csharp
        // ── Fixture 3: D=1 vs D=10 — average path count strictly higher at D=1 ─

        [Test]
        public void Complete_DifficultyOne_HasHigherAveragePathCountThanTen()
        {
            var doc = MakeBoxGrid(("pink",3),("blue",3));
            long sumD1 = 0, sumD10 = 0;
            const int N = 5;
            for (int i = 0; i < N; i++)
            {
                var r1 = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 1, Seed = 100 + i, ConveyorCapacityOverride = 24 });
                var rT = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 10, Seed = 100 + i, ConveyorCapacityOverride = 24 });
                sumD1  += r1.Analysis?.WinPathCount  ?? 0;
                sumD10 += rT.Analysis?.WinPathCount  ?? 0;
            }
            // D=1 targets ~100 paths, D=10 targets ~1. Both with wide tolerance,
            // but the curve still pushes D=1 statistically higher.
            Assert.Greater(sumD1, sumD10);
        }

        // ── Fixture 4: hidden ratio matches HiddenSpoolRatio.Evaluate(D) ─

        [Test]
        public void Complete_HiddenRatio_TracksDifficulty()
        {
            // At D=1, HiddenSpoolRatio = 0 → no hidden spools.
            // At D=10, HiddenSpoolRatio = 40 → ~40% hidden.
            var doc = MakeBoxGrid(("pink",5),("blue",5)); // 30 spools total
            var rLow  = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 1,  Seed = 42, ConveyorCapacityOverride = 24 });
            var rHigh = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 10, Seed = 42, ConveyorCapacityOverride = 24 });

            int hiddenLow  = CountHidden(rLow.TopSection);
            int hiddenHigh = CountHidden(rHigh.TopSection);
            Assert.AreEqual(0, hiddenLow);
            // 30 × 40% = 12, within ±1 for rounding tolerance.
            Assert.GreaterOrEqual(hiddenHigh, 11);
            Assert.LessOrEqual(hiddenHigh, 13);
        }

        // ── Fixture 5: capacity override changes outcome on a borderline grid ─

        [Test]
        public void Complete_CapacityOverride_AffectsCandidateAnalysis()
        {
            // 4 pink boxes = 36 balls, 12 pink spools.
            // At cap=12 every spool must clear before next tap; severely
            // constrained. At cap=30 there's slack — many more candidates pass.
            var doc = MakeBoxGrid(("pink",4));
            var rTight = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 5, Seed = 555, ConveyorCapacityOverride = 12 });
            var rLoose = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 5, Seed = 555, ConveyorCapacityOverride = 30 });
            Assert.IsNotNull(rTight.Analysis);
            Assert.IsNotNull(rLoose.Analysis);
            // Loose capacity should never decrease the path count for the same seed.
            Assert.GreaterOrEqual(rLoose.Analysis.WinPathCount, rTight.Analysis.WinPathCount);
        }

        // ── Fixture 6: impossible target returns best-so-far + Succeeded=false ─

        [Test]
        public void Complete_ImpossibleTarget_ReturnsBestEffort()
        {
            var doc = MakeBoxGrid(("pink",2),("blue",2));
            // Set target to a value the grid can't produce: 1,000,000 paths
            // when the puzzle only has ~24 orderings.
            _config.WinPathTargetByDifficulty = new AnimationCurve(new Keyframe(5f, 1_000_000f));
            _config.WinPathTolerance = 0.01f;
            _config.MaxRerollAttempts = 5;
            var r = _autofiller.Complete(doc, _profile, new CompletionRequest { Difficulty = 5, Seed = 999, ConveyorCapacityOverride = 24 });
            Assert.IsFalse(r.Succeeded);
            Assert.IsNotNull(r.TopSection);   // best-so-far still applied
            Assert.IsNotNull(r.Analysis);
            Assert.IsNotNull(r.FailureReason);
        }

        private static int CountHidden(JObject topJson)
        {
            int n = 0;
            var data = topJson.ToObject<YarnTopSectionData>();
            foreach (var col in data.Columns)
                foreach (var s in col.Spools)
                    if (s.Hidden) n++;
            return n;
        }
```

- [ ] **Step 2: Run tests — expect 7 / 7 PASS in the suite**

```json
{"testMode": "EditMode", "filter": {"testClass": "Hoppa.YarnTwist.Editor.Tests.YarnTwistSpoolAutofillerTests"}}
```

If Fixture 3 (Difficulty correlation) is flaky on a particular seed pool, widen `_config.WinPathTolerance` further or increase `N` in the loop until stable. The implementation logic is correct; the assertion is statistical.

- [ ] **Step 3: Commit**

```bash
git add Assets/YarnTwist/Tests/Editor/YarnTwistSpoolAutofillerTests.cs
git commit -m "test(yarntwist/analysis): difficulty correlation + hidden ratio + capacity + impossible-target"
```

---

## Task 11: Create Layer 2 assets + wire `YarnTwistProfile`

**Files:**
- Create: `Assets/YarnTwist/Data/Config/YarnTwistLevelAnalyzer.asset`
- Create: `Assets/YarnTwist/Data/Config/YarnTwistSpoolAutofillConfig.asset`
- Create: `Assets/YarnTwist/Data/Config/YarnTwistSpoolAutofiller.asset`
- Modify: `Assets/YarnTwist/Data/Config/YarnTwistProfile.asset`

Create assets via `mcp__ai-game-developer__script-execute` so Unity owns the YAML + `.meta` files (avoids hand-rolling GUIDs).

- [ ] **Step 1: Create the three Layer 2 assets**

Call `mcp__ai-game-developer__script-execute` with:

```csharp
using UnityEditor;
using UnityEngine;
using Hoppa.YarnTwist.Editor;

var dir = "Assets/YarnTwist/Data/Config";

var cfg = ScriptableObject.CreateInstance<YarnTwistSpoolAutofillConfig>();
AssetDatabase.CreateAsset(cfg, dir + "/YarnTwistSpoolAutofillConfig.asset");

var analyzer = ScriptableObject.CreateInstance<YarnTwistLevelAnalyzer>();
AssetDatabase.CreateAsset(analyzer, dir + "/YarnTwistLevelAnalyzer.asset");

var autofiller = ScriptableObject.CreateInstance<YarnTwistSpoolAutofiller>();
// Wire _config on the autofiller asset itself (game-specific config lives on the Layer 2 asset, not on GameProfile).
var so = new SerializedObject(autofiller);
so.FindProperty("_config").objectReferenceValue = cfg;
so.ApplyModifiedPropertiesWithoutUndo();
AssetDatabase.CreateAsset(autofiller, dir + "/YarnTwistSpoolAutofiller.asset");

AssetDatabase.SaveAssets();
AssetDatabase.Refresh();
```

Expected response: no error.

- [ ] **Step 2: Wire the new refs onto `YarnTwistProfile.asset`**

Call `mcp__ai-game-developer__script-execute` with:

```csharp
using UnityEditor;
using UnityEngine;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YarnTwist.Editor;

var dir = "Assets/YarnTwist/Data/Config";
var profile = AssetDatabase.LoadAssetAtPath<GameProfile>(dir + "/YarnTwistProfile.asset");
var analyzer = AssetDatabase.LoadAssetAtPath<YarnTwistLevelAnalyzer>(dir + "/YarnTwistLevelAnalyzer.asset");
var autofiller = AssetDatabase.LoadAssetAtPath<YarnTwistSpoolAutofiller>(dir + "/YarnTwistSpoolAutofiller.asset");

var so = new SerializedObject(profile);
so.FindProperty("_levelAnalyzer").objectReferenceValue = analyzer;
so.FindProperty("_levelCompleter").objectReferenceValue = autofiller;
so.ApplyModifiedPropertiesWithoutUndo();
EditorUtility.SetDirty(profile);
AssetDatabase.SaveAssetIfDirty(profile);
```

Expected: no error.

- [ ] **Step 3: Verify the four asset files + `.meta` exist on disk**

Use `Glob` for `Assets/YarnTwist/Data/Config/YarnTwist{LevelAnalyzer,SpoolAutofiller,SpoolAutofillConfig}.asset*`. Expected: 6 results (3 `.asset` + 3 `.meta`).

- [ ] **Step 4: Re-run both test suites — they should still pass (profile wiring is independent of the in-test injection)**

```json
{"testMode": "EditMode", "filter": {"testCategory": null, "testClass": "Hoppa.YarnTwist.Editor.Tests.YarnTwistLevelAnalyzerTests"}}
```

```json
{"testMode": "EditMode", "filter": {"testCategory": null, "testClass": "Hoppa.YarnTwist.Editor.Tests.YarnTwistSpoolAutofillerTests"}}
```

Expected: 9 / 9 + 7 / 7.

- [ ] **Step 5: Commit**

```bash
git add Assets/YarnTwist/Data/Config/YarnTwistLevelAnalyzer.asset \
        Assets/YarnTwist/Data/Config/YarnTwistLevelAnalyzer.asset.meta \
        Assets/YarnTwist/Data/Config/YarnTwistSpoolAutofiller.asset \
        Assets/YarnTwist/Data/Config/YarnTwistSpoolAutofiller.asset.meta \
        Assets/YarnTwist/Data/Config/YarnTwistSpoolAutofillConfig.asset \
        Assets/YarnTwist/Data/Config/YarnTwistSpoolAutofillConfig.asset.meta \
        Assets/YarnTwist/Data/Config/YarnTwistProfile.asset
git commit -m "feat(yarntwist): wire analyzer + autofiller assets on YarnTwistProfile"
```

---

## Task 12: Layer 1 — `AutofillPanel` UI (scaffolding only)

**Files:**
- Create: `Packages/com.hoppa.leveleditor.core/Editor/Analysis/AutofillPanel.cs`

No test in this task — IMGUI rendering is verified manually in Task 14.

- [ ] **Step 1: Create the panel skeleton**

```csharp
using System;
using UnityEditor;
using UnityEngine;
using Newtonsoft.Json.Linq;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Side panel for the right column of LevelEditorWindow. Renders only when
    // profile.LevelAnalyzer != null. Provides:
    //  - Conveyor capacity dropdown (24 / 30 / Custom)
    //  - Difficulty + Seed widgets (for the auto-fill button)
    //  - Analyze button (read-only — runs the analyzer on the current doc)
    //  - Auto-fill button (replaces the document's top section with a fresh fill)
    //  - Last-result block (win paths, solvable, candidates, elapsed)
    public sealed class AutofillPanel
    {
        private const float HeaderH = 22f;
        private const float RowH    = 20f;
        private const float ButtonH = 22f;
        private const float Pad     = 8f;

        private static readonly Color BlockBg     = new Color(0.17f, 0.18f, 0.22f);
        private static readonly Color SubtleText  = new Color(0.70f, 0.75f, 0.85f);
        private static readonly Color OkText      = new Color(0.55f, 0.85f, 0.55f);
        private static readonly Color ErrText     = new Color(1.00f, 0.45f, 0.40f);

        private int    _difficulty = 5;
        private int    _seed;
        private bool   _seedLocked;
        private int    _capacityChoice = 24;     // 24 / 30 / 0=custom
        private int    _customCapacity = 24;

        private LevelAnalysisResult   _lastAnalysis;
        private LevelCompletionResult _lastCompletion;
        private string                _statusMessage;

        public float PreferredHeight => 220f;

        public void OnGUI(Rect rect, LevelEditorSession session, GameProfile profile)
        {
            EditorGUI.DrawRect(rect, BlockBg);

            float y = rect.y + 4f;
            float w = rect.width - Pad * 2f;
            float x = rect.x + Pad;

            GUI.Label(new Rect(x, y, w, HeaderH), "Spool Analysis", EditorStyles.boldLabel);
            y += HeaderH;

            // Conveyor capacity dropdown
            int newChoice = EditorGUI.IntPopup(
                new Rect(x, y, w, RowH),
                "Conveyor",
                _capacityChoice,
                new[] { "24 (L1–15)", "30 (L16+)", "Custom…" },
                new[] { 24, 30, 0 });
            if (newChoice != _capacityChoice)
            {
                _capacityChoice = newChoice;
                if (newChoice != 0) _customCapacity = newChoice;
            }
            y += RowH + 2f;
            if (_capacityChoice == 0)
            {
                _customCapacity = Mathf.Max(1, EditorGUI.IntField(new Rect(x, y, w, RowH), "Custom slots", _customCapacity));
                y += RowH + 2f;
            }

            // Difficulty
            EditorGUI.LabelField(new Rect(x, y, 60f, RowH), "Difficulty", EditorStyles.miniLabel);
            _difficulty = (int)Mathf.Round(GUI.HorizontalSlider(
                new Rect(x + 60f, y + 4f, w - 60f - 26f, RowH),
                Mathf.Clamp(_difficulty, 1, 10), 1f, 10f));
            GUI.Label(new Rect(x + w - 22f, y, 22f, RowH), _difficulty.ToString(), EditorStyles.miniLabel);
            y += RowH + 2f;

            // Seed
            EditorGUI.LabelField(new Rect(x, y, 40f, RowH), "Seed", EditorStyles.miniLabel);
            float seedW = w - 40f - 28f - 28f - 4f;
            _seed = EditorGUI.IntField(new Rect(x + 40f, y, seedW, RowH), _seed);
            _seedLocked = GUI.Toggle(
                new Rect(x + 40f + seedW + 2f, y, 28f, RowH),
                _seedLocked, new GUIContent("🔒", "Lock seed: subsequent auto-fills reproduce the same spool layout"),
                GUI.skin.button);
            if (GUI.Button(new Rect(x + 40f + seedW + 32f, y, 28f, RowH),
                    new GUIContent("🎲", "Randomize seed")))
            {
                _seed = new System.Random().Next(1, int.MaxValue);
                _seedLocked = false;
            }
            y += RowH + 6f;

            // Buttons — Task 13 wires Analyze, Task 14 wires Auto-fill
            float halfW = (w - 4f) * 0.5f;
            using (new EditorGUI.DisabledGroupScope(profile?.LevelAnalyzer == null || session?.Document == null))
            {
                if (GUI.Button(new Rect(x, y, halfW, ButtonH), new GUIContent("Analyze",
                        "Run the analyzer on the current document. Does not modify the level.")))
                {
                    OnAnalyze(session, profile);
                }
            }
            using (new EditorGUI.DisabledGroupScope(profile?.LevelCompleter == null || session?.Document == null))
            {
                if (GUI.Button(new Rect(x + halfW + 4f, y, halfW, ButtonH), new GUIContent("Auto-fill",
                        "Replace the top section with a fresh spool layout targeted at the current Difficulty.")))
                {
                    OnAutofill(session, profile);
                }
            }
            y += ButtonH + 6f;

            // Status / result
            if (!string.IsNullOrEmpty(_statusMessage))
            {
                GUI.Label(new Rect(x, y, w, RowH * 2f), _statusMessage, EditorStyles.wordWrappedMiniLabel);
                y += RowH * 2f;
            }

            if (_lastAnalysis != null)
            {
                var color = _lastAnalysis.Solvable ? OkText : ErrText;
                var savedColor = GUI.contentColor;
                GUI.contentColor = color;
                GUI.Label(new Rect(x, y, w, RowH * 3f),
                    _lastAnalysis.ToString(),
                    EditorStyles.wordWrappedMiniLabel);
                GUI.contentColor = savedColor;
            }
        }

        private void OnAnalyze(LevelEditorSession session, GameProfile profile)
        {
            // Implemented in Task 13.
            _statusMessage = "(Analyze wiring lands in Task 13.)";
        }

        private void OnAutofill(LevelEditorSession session, GameProfile profile)
        {
            // Implemented in Task 14.
            _statusMessage = "(Auto-fill wiring lands in Task 14.)";
        }

        private int ResolveCapacity() => _capacityChoice == 0 ? _customCapacity : _capacityChoice;
    }
}
```

- [ ] **Step 2: Refresh AssetDatabase**

Call `mcp__ai-game-developer__assets-refresh`. Expected: no compile errors.

- [ ] **Step 3: Commit**

```bash
git add Packages/com.hoppa.leveleditor.core/Editor/Analysis/AutofillPanel.cs
git commit -m "feat(analysis): scaffold AutofillPanel (UI only, no actions wired)"
```

---

## Task 13: Wire `Analyze` button

**Files:**
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Analysis/AutofillPanel.cs`

- [ ] **Step 1: Replace the `OnAnalyze` stub**

In `AutofillPanel.cs`, replace `OnAnalyze`:

```csharp
        private void OnAnalyze(LevelEditorSession session, GameProfile profile)
        {
            _statusMessage = null;
            _lastCompletion = null;

            try
            {
                _lastAnalysis = profile.LevelAnalyzer.Analyze(session.Document, profile, new AnalysisRequest
                {
                    Mode = AnalysisMode.Count,
                    WinPathCap = 10_000,
                    TimeoutMs  = 5_000,
                    ConveyorCapacityOverride = ResolveCapacity(),
                });
            }
            catch (Exception ex)
            {
                _lastAnalysis = null;
                _statusMessage = "Analyze failed: " + ex.Message;
                Debug.LogError("AutofillPanel.OnAnalyze: " + ex);
            }
        }
```

- [ ] **Step 2: Refresh — expect no compile errors**

Call `mcp__ai-game-developer__assets-refresh`.

- [ ] **Step 3: Manual verification (in Unity, no automated test)**

Document the manual check in the commit message. Open `Window ▸ Level Editor`, select `YarnTwistProfile`, open an existing Yarn level (e.g. `Assets/YarnTwist/Data/Levels/YT_001.json`), press **Analyze** in the new Spool Analysis panel (Task 15 will wire it into the right column — for this task it can be tested in isolation by temporarily adding it to a debug `[MenuItem]`, OR deferred until Task 15).

If deferring, skip to Step 4.

- [ ] **Step 4: Commit**

```bash
git add Packages/com.hoppa.leveleditor.core/Editor/Analysis/AutofillPanel.cs
git commit -m "feat(analysis): wire Analyze button (read-only analyzer call)"
```

---

## Task 14: Wire `Auto-fill` button (with undo)

**Files:**
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Analysis/AutofillPanel.cs`

- [ ] **Step 1: Replace the `OnAutofill` stub**

```csharp
        private void OnAutofill(LevelEditorSession session, GameProfile profile)
        {
            _statusMessage = null;
            try
            {
                int seed = _seedLocked && _seed != 0
                    ? _seed
                    : new System.Random().Next(1, int.MaxValue);
                if (!_seedLocked) _seed = seed;

                _lastCompletion = profile.LevelCompleter.Complete(session.Document, profile, new CompletionRequest
                {
                    Difficulty = _difficulty,
                    Seed = seed,
                    ConveyorCapacityOverride = ResolveCapacity(),
                });
                _lastAnalysis = _lastCompletion?.Analysis;

                if (_lastCompletion?.TopSection != null)
                {
                    session.PushUndoSnapshot();
                    session.Document.TopSection = _lastCompletion.TopSection;
                    session.MarkDirty();
                    session.RunValidation();

                    if (!_lastCompletion.Succeeded)
                        _statusMessage = "Couldn't hit Difficulty band — best candidate applied (Ctrl-Z to revert).";
                }
                else
                {
                    _statusMessage = "Auto-fill failed: " + (_lastCompletion?.FailureReason ?? "no top section produced");
                }
            }
            catch (Exception ex)
            {
                _statusMessage = "Auto-fill failed: " + ex.Message;
                Debug.LogError("AutofillPanel.OnAutofill: " + ex);
            }
        }
```

- [ ] **Step 2: Refresh — expect no compile errors**

Call `mcp__ai-game-developer__assets-refresh`.

- [ ] **Step 3: Commit**

```bash
git add Packages/com.hoppa.leveleditor.core/Editor/Analysis/AutofillPanel.cs
git commit -m "feat(analysis): wire Auto-fill button (replaces top section, pushes undo)"
```

---

## Task 15: Slot `AutofillPanel` into `LevelEditorWindow`'s right column

**Files:**
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Window/LevelEditorWindow.cs`

The right column is currently 50/50 between validation and summary/multiselect. When `profile.LevelAnalyzer != null`, we restructure to 40/30/30 (validation / summary / autofill).

- [ ] **Step 1: Add the panel field and rendering**

Edit `LevelEditorWindow.cs`. Add a field with the other panel readonly fields (around line 19, near `_generator`):

```csharp
        private readonly AutofillPanel _autofill = new AutofillPanel();
```

Replace the right-column rendering block (lines 212–227 in the file). Find:

```csharp
            // ── Right: validation (50%) + summary (50%) ──────────────────
            float rightX   = w - RightW + 1f;
            float validH   = Mathf.Floor(innerH * 0.50f);
            float summaryH = innerH - validH;

            var clicked = _validation.OnGUI(new Rect(rightX, bodyY, RightW, validH), _session.LastValidation);
            if (clicked.HasValue) _session.SelectedCell = clicked;

            float summaryY = bodyY + validH;
            EditorGUI.DrawRect(new Rect(rightX, summaryY, RightW, 1f), Divider);
            var summaryRect = new Rect(rightX, summaryY + 1f, RightW, summaryH - 1f);
            if (_session.MultiSelection.Count > 0)
                _multiSelect.OnGUI(summaryRect, _session);
            else
                _summary.OnGUI(summaryRect, _session);
```

Replace with:

```csharp
            // ── Right column: validation + summary [+ autofill] ──────────
            float rightX = w - RightW + 1f;
            bool  showAutofill = _profile?.LevelAnalyzer != null;

            float validRatio   = showAutofill ? 0.40f : 0.50f;
            float summaryRatio = showAutofill ? 0.30f : 0.50f;

            float validH   = Mathf.Floor(innerH * validRatio);
            float summaryH = Mathf.Floor(innerH * summaryRatio);
            float autoH    = showAutofill ? innerH - validH - summaryH : 0f;

            var clicked = _validation.OnGUI(new Rect(rightX, bodyY, RightW, validH), _session.LastValidation);
            if (clicked.HasValue) _session.SelectedCell = clicked;

            float summaryY = bodyY + validH;
            EditorGUI.DrawRect(new Rect(rightX, summaryY, RightW, 1f), Divider);
            var summaryRect = new Rect(rightX, summaryY + 1f, RightW, summaryH - 1f);
            if (_session.MultiSelection.Count > 0)
                _multiSelect.OnGUI(summaryRect, _session);
            else
                _summary.OnGUI(summaryRect, _session);

            if (showAutofill)
            {
                float autoY = bodyY + validH + summaryH;
                EditorGUI.DrawRect(new Rect(rightX, autoY, RightW, 1f), Divider);
                _autofill.OnGUI(new Rect(rightX, autoY + 1f, RightW, autoH - 1f), _session, _profile);
            }
```

- [ ] **Step 2: Refresh — expect no compile errors**

Call `mcp__ai-game-developer__assets-refresh`.

- [ ] **Step 3: Manual smoke check**

Open Unity. `Window ▸ Level Editor`. Select `YarnTwistProfile`. Open `Assets/YarnTwist/Data/Levels/YT_001.json`. Confirm:
- The right column now has three stacked panels (Validation, Summary, **Spool Analysis**).
- Conveyor dropdown, Difficulty slider, Seed field, both buttons are visible.
- Pressing **Analyze** populates the bottom result block (no exceptions in the Console).

Switch profile to `YAKProfile`. Confirm:
- The Spool Analysis panel **disappears** (YAK has no analyzer wired).
- The right column reverts to 50/50.

- [ ] **Step 4: Commit**

```bash
git add Packages/com.hoppa.leveleditor.core/Editor/Window/LevelEditorWindow.cs
git commit -m "feat(window): slot AutofillPanel into right column when analyzer is wired"
```

---

## Task 16: Update `CURRENT_TASK.md` and `SESSION_NOTES.md`

**Files:**
- Modify: `CURRENT_TASK.md`
- Modify: `SESSION_NOTES.md`

- [ ] **Step 1: Update `CURRENT_TASK.md`**

Edit the "Active phase" section's header to add the new initiative beneath the existing generator entry:

```markdown
**Spool auto-fill + win-path analysis — YarnTwist v1 (2026-05-26)**

New authoring flow on top of the editor: the designer paints the grid by
hand, then the Spool Analysis side panel auto-completes the top section
and reports exact win-path count so she has a concrete difficulty signal.

Architecture (Layer 1): generic `ILevelAnalyzer` + `ILevelCompleter`
contracts under `Packages/.../Editor/Analysis/`; `GameProfile` gains two
typed fields (`_levelAnalyzer`, `_levelCompleter`) and a generic
`_extensions: List<ScriptableObject>` slot for future per-profile data.
`AutofillPanel` IMGUI side panel shown in the right column when an
analyzer is wired.

Layer 2 (YarnTwist): `YarnTwistLevelAnalyzer` (memoized DFS over tap
orderings; Box / ArrowBox / Tunnel rules; multiset-on-belt
abstraction; configurable conveyor capacity), `YarnTwistSpoolAutofiller`
(reroll loop targeting a Difficulty-keyed win-path band),
`YarnTwistSpoolAutofillConfig` (curves + caps + timeouts owned by the
autofiller asset, NOT by the profile).

Tests: `YarnTwistLevelAnalyzerTests` (9 fixtures) +
`YarnTwistSpoolAutofillerTests` (7 fixtures), all green.

Manual verification (must run in Unity) — see "Manual verification" below.
```

Add a "Manual verification — spool auto-fill" sub-section to the open-items list:

```markdown
### Manual verification — spool auto-fill (must run in Unity)

- [ ] Open Level Editor, select `YarnTwistProfile`; confirm the Spool
      Analysis panel appears in the right column (Validation 40% /
      Summary 30% / Analysis 30%).
- [ ] Open `Assets/YarnTwist/Data/Levels/YT_001.json`; press Analyze;
      confirm win-path count + elapsed time appear in the result block.
- [ ] Paint a small 3-box grid; press Auto-fill; confirm the top section
      populates with spool columns satisfying YarnColorBalanceRule.
- [ ] Sweep Difficulty 1 / 5 / 10; confirm the auto-filled hidden-spool
      count visibly tracks Difficulty (D=1: 0 hidden; D=10: ~40% hidden).
- [ ] Lock seed 🔒 + press Auto-fill twice; confirm identical top section.
- [ ] Press Ctrl-Z after Auto-fill; confirm prior top section restored.
- [ ] Switch active profile to `YAKProfile`; confirm Spool Analysis panel
      disappears (YAK has no analyzer wired — regression check).
```

- [ ] **Step 2: Update `SESSION_NOTES.md`**

Edit "Current status" date to `2026-05-26` and add a one-liner after the existing generator-framework entry:

```markdown
- **Spool auto-fill + win-path analysis (2026-05-26)**: side panel + Layer 1
  `ILevelAnalyzer` + `ILevelCompleter` contracts; `YarnTwistLevelAnalyzer`
  (memoized DFS counting win paths) + `YarnTwistSpoolAutofiller` (reroll
  loop targeting a Difficulty-keyed win-path band) wired and passing
  edit-mode tests. `GameProfile` gains an `_extensions` slot for future
  per-profile data (Layer 1 stays generic — no game-specific vocabulary).
```

Add to the "What exists / Framework" Editor-assembly bullets:

```markdown
- **Analysis framework** (`Editor/Analysis/`): `ILevelAnalyzer` +
  `LevelAnalyzerAsset` (generic Analyze contract); `ILevelCompleter` +
  `LevelCompleterAsset` (generic Complete contract); `AnalysisRequest` /
  `LevelAnalysisResult` / `CompletionRequest` / `LevelCompletionResult`
  DTOs; `AutofillPanel` IMGUI side panel rendered in the right column
  when `profile.LevelAnalyzer != null`. `GameProfile` exposes
  `LevelAnalyzer` + `LevelCompleter` accessors and a generic
  `GetExtension<T>()` over `_extensions`.
```

Add to "What exists / Yarn Twist game layer / Editor":

```markdown
- **Analysis** (`Editor/Analysis/`): `YarnTwistLevelAnalyzer` (memoized
  DFS counting win paths; Box / ArrowBox / Tunnel rules; multiset-on-
  belt abstraction). `YarnTwistSpoolAutofiller` (reroll loop targeting
  `WinPathTargetByDifficulty` ± `WinPathTolerance`; round-robin spool
  distribution across 4 columns; hidden ratio per Difficulty;
  best-so-far fallback when no candidate lands in band).
  `YarnTwistSpoolAutofillConfig` SO referenced from
  `YarnTwistSpoolAutofiller.asset` (NOT from `YarnTwistProfile.asset` —
  per the "Layer 1 stays generic" rule).
```

- [ ] **Step 3: Commit**

```bash
git add CURRENT_TASK.md SESSION_NOTES.md
git commit -m "docs: spool auto-fill v1 — CURRENT_TASK + SESSION_NOTES"
```

---

## Self-review checklist (run after Task 16)

- [ ] All 16 tasks committed.
- [ ] `git log --oneline | head -20` shows expected sequence.
- [ ] `mcp__ai-game-developer__tests-run` with no class filter (full Edit Mode suite) → 0 failures.
- [ ] Manual verification checklist in `CURRENT_TASK.md` reviewed and any unchecked items flagged to the user.

---

## What's next (not in this plan)

Per the spec's §11 "out-of-scope follow-ups":

1. **YAK Layer 2** — subclass the two Layer 1 contracts to give YAK its own analyzer + completer. No Layer 1 changes needed.
2. **Migrate `_generatorConfig` off `GameProfile`** — same anti-pattern this plan corrected for the new contracts. Small mechanical refactor: add `public virtual ScriptableObject Config => null;` on `LevelGeneratorAsset`, move `_generatorConfig` into `YarnTwistLevelGenerator.asset` as a `[SerializeField]`, update `GeneratorModePanel` to read `profile.LevelGenerator.Config` instead of `profile.GeneratorConfig`.
3. **Async / cancellable analysis** — if designer feedback shows long puzzles freeze the editor noticeably.
4. **Layer 1 UPM tag bump + consumer manifest bump** — likely `v0.5.17`.
5. **YarnTwist game-repo sync** — copy the new Layer 2 files from `Assets/YarnTwist/Editor/Analysis/` to `E:/Projects/Hoppa/YarnTwist/Assets/_YAT/Scripts/Editor/Analysis/` (see SESSION_NOTES § "YarnTwist Layer 2 target paths").
