# Phase 5 Data Pipeline Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Wire the level editor's Save pipeline to the Yarn Twist game's `level_config.json` so a designer saving a level automatically produces game-ready data — no game mechanics touched.

**Architecture:** `LevelExporterAsset` replaces `ScriptableObject` as the exporter base so exporters aren't locked to writing `.asset` files. `StringIntMapping` provides a reusable inspector-configurable string→int converter. `YarnMasterLevelExporter` uses both to transform `LevelDocument` → the game's JSON schema on Save. Game changes are data model only (new enum values + nullable fields, zero spawn logic).

**Tech Stack:** Unity 2022.3, C# 9, Newtonsoft.Json 13.x, NUnit via Unity Test Runner.

---

## File Map

### `hoppa-level-editor-core` (editor repo)

| File | Status | Responsibility |
|---|---|---|
| `Packages/com.hoppa.leveleditor.core/Editor/Exporters/LevelExporterAsset.cs` | **New** | Abstract SO base for all exporters |
| `Packages/com.hoppa.leveleditor.core/Editor/Exporters/ScriptableObjectExporter.cs` | **Modify** | Change base from `ScriptableObject` → `LevelExporterAsset` |
| `Packages/com.hoppa.leveleditor.core/Editor/Infrastructure/GameProfile.cs` | **Modify** | `_exporters` field type `List<ScriptableObjectExporter>` → `List<LevelExporterAsset>` |
| `Packages/com.hoppa.leveleditor.core/Editor/Mapping/StringIntMapping.cs` | **New** | Generic string→int SO + `StringIntEntry` inner type |
| `Packages/com.hoppa.leveleditor.core/Tests/Editor/StringIntMappingTests.cs` | **New** | NUnit tests for StringIntMapping |
| `Packages/com.hoppa.leveleditor.core/Tests/Editor/Hoppa.LevelEditor.Core.Editor.Tests.asmdef` | **Verify/Create** | Editor test assembly |
| `Assets/YarnTwist/Runtime/YarnEmptyCell.cs` | **Modify** | Add `[JsonIgnore]` to `CellTypeId` |
| `Assets/YarnTwist/Runtime/YarnWallCell.cs` | **Modify** | Add `[JsonIgnore]` to `CellTypeId` |
| `Assets/YarnTwist/Runtime/YarnBoxCell.cs` | **Modify** | Add `[JsonIgnore]` to `CellTypeId` |
| `Assets/YarnTwist/Runtime/YarnArrowBoxCell.cs` | **Modify** | Add `[JsonIgnore]` to `CellTypeId` |
| `Assets/YarnTwist/Runtime/YarnTunnelCell.cs` | **Modify** | Add `[JsonIgnore]` to `CellTypeId` |
| `Assets/YarnTwist/Editor/YarnMasterLevelExporter.cs` | **New** | Transforms LevelDocument → game schema, upserts level_config.json |
| `Assets/YarnTwist/Tests/Editor/YarnMasterLevelExporterTests.cs` | **New** | NUnit tests for the exporter transform |
| `Assets/YarnTwist/Tests/Editor/Hoppa.YarnTwist.Editor.Tests.asmdef` | **New** | Test assembly for YarnTwist editor layer |

### `YarnTwist` (game repo — data model only)

| File | Status | Responsibility |
|---|---|---|
| `Assets/_YAT/Scripts/Gamelogic/Managers/YATLevelManager.cs` | **Modify** | Add enum values + new fields + `TunnelQueueEntry` class |

---

## Known implementation details (read before coding)

- `YarnArrowBoxCell.CellTypeId` is `"yt.arrowbox"` (all lowercase — matches the cell type mapping key).
- `YarnArrowBoxCell` has **no `Hidden` field**. Exporter always emits `Hidden = false` for arrow boxes.
- `YarnTunnelCell.Queue` is `List<string>` (colorId strings only, no hidden flag per entry). Exporter maps each to `{ ColorType = int, Hidden = false }`.
- `YarnDirection` serialises as PascalCase strings (`"Up"`, `"Down"`, `"Left"`, `"Right"`) via `StringEnumConverter`. Pass through directly as the `Direction` string field.
- `YarnTopSectionData` is in the `Hoppa.YarnTwist` (runtime) namespace; deserialise from `document.TopSection` (a `JObject`).
- `GameProfile.Exporters` is iterated in `LevelEditorWindow.SaveToPath` — no change needed there because `LevelExporterAsset` implements `ILevelExporter` with the same `.Export(...)` signature.

---

## Task 1 — `LevelExporterAsset` base class + wiring

**Files:**
- Create: `Packages/com.hoppa.leveleditor.core/Editor/Exporters/LevelExporterAsset.cs`
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Exporters/ScriptableObjectExporter.cs`
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Infrastructure/GameProfile.cs`

- [ ] **Step 1.1 — Create `LevelExporterAsset.cs`**

```csharp
// Packages/com.hoppa.leveleditor.core/Editor/Exporters/LevelExporterAsset.cs
using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    public abstract class LevelExporterAsset : ScriptableObject, ILevelExporter
    {
        public abstract string Name { get; }
        public abstract bool Export(LevelDocument document, CellTypeRegistry cellTypes, string jsonFilePath);
    }
}
```

- [ ] **Step 1.2 — Update `ScriptableObjectExporter` to inherit from `LevelExporterAsset`**

Open `Packages/com.hoppa.leveleditor.core/Editor/Exporters/ScriptableObjectExporter.cs`.
Change line 11:
```csharp
// Before
public abstract class ScriptableObjectExporter : ScriptableObject, ILevelExporter

// After
public abstract class ScriptableObjectExporter : LevelExporterAsset
```
Remove the `ILevelExporter` interface and `ScriptableObject` base — both are now inherited through `LevelExporterAsset`. The `Name` property and `Export` method signatures remain unchanged.

- [ ] **Step 1.3 — Update `GameProfile` exporter list type**

Open `Packages/com.hoppa.leveleditor.core/Editor/Infrastructure/GameProfile.cs`.

Change the field and property (lines ~31 and ~42):
```csharp
// Before
[SerializeField] private List<ScriptableObjectExporter> _exporters = new List<ScriptableObjectExporter>();
// ...
public IReadOnlyList<ScriptableObjectExporter> Exporters => _exporters;

// After
[SerializeField] private List<LevelExporterAsset> _exporters = new List<LevelExporterAsset>();
// ...
public IReadOnlyList<LevelExporterAsset> Exporters => _exporters;
```

- [ ] **Step 1.4 — Verify Unity compiles without errors**

Open Unity (or check the console). Confirm zero compiler errors. The `LevelEditorWindow.SaveToPath` loop calls `.Export(...)` on each element — this continues to work because `LevelExporterAsset` implements `ILevelExporter`.

- [ ] **Step 1.5 — Commit**

```bash
git add Packages/com.hoppa.leveleditor.core/Editor/Exporters/LevelExporterAsset.cs
git add Packages/com.hoppa.leveleditor.core/Editor/Exporters/ScriptableObjectExporter.cs
git add Packages/com.hoppa.leveleditor.core/Editor/Infrastructure/GameProfile.cs
git commit -m "refactor: introduce LevelExporterAsset base so exporters aren't tied to .asset output pattern"
```

---

## Task 2 — `StringIntMapping` generic SO

**Files:**
- Create: `Packages/com.hoppa.leveleditor.core/Editor/Mapping/StringIntMapping.cs`
- Create: `Packages/com.hoppa.leveleditor.core/Tests/Editor/StringIntMappingTests.cs`
- Verify: `Packages/com.hoppa.leveleditor.core/Tests/Editor/Hoppa.LevelEditor.Core.Editor.Tests.asmdef`

- [ ] **Step 2.1 — Verify (or create) the Editor test assembly definition**

Check whether `Packages/com.hoppa.leveleditor.core/Tests/Editor/Hoppa.LevelEditor.Core.Editor.Tests.asmdef` exists. If missing, create it:

```json
{
  "name": "Hoppa.LevelEditor.Core.Editor.Tests",
  "rootNamespace": "Hoppa.LevelEditor.Core.Editor.Tests",
  "references": [
    "Hoppa.LevelEditor.Core.Runtime",
    "Hoppa.LevelEditor.Core.Editor",
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner"
  ],
  "includePlatforms": ["Editor"],
  "optionalUnityReferences": ["TestAssemblies"]
}
```

- [ ] **Step 2.2 — Write the failing tests first**

Create `Packages/com.hoppa.leveleditor.core/Tests/Editor/StringIntMappingTests.cs`:

```csharp
using NUnit.Framework;
using Hoppa.LevelEditor.Core.Editor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor.Tests
{
    public class StringIntMappingTests
    {
        private StringIntMapping CreateMapping(params (string key, int value)[] entries)
        {
            var mapping = ScriptableObject.CreateInstance<StringIntMapping>();
            foreach (var (key, value) in entries)
                mapping.Add(key, value);
            return mapping;
        }

        [Test]
        public void TryGet_KnownKey_ReturnsTrueAndCorrectValue()
        {
            var mapping = CreateMapping(("pink", 7), ("blue", 1));
            Assert.IsTrue(mapping.TryGet("pink", out int value));
            Assert.AreEqual(7, value);
        }

        [Test]
        public void TryGet_UnknownKey_ReturnsFalseAndFallback()
        {
            var mapping = CreateMapping(("pink", 7));
            Assert.IsFalse(mapping.TryGet("unknown", out int value));
            Assert.AreEqual(0, value);
        }

        [Test]
        public void Get_KnownKey_ReturnsValue()
        {
            var mapping = CreateMapping(("red", 9));
            Assert.AreEqual(9, mapping.Get("red"));
        }

        [Test]
        public void Get_UnknownKey_ReturnsFallback()
        {
            var mapping = CreateMapping(("pink", 7));
            Assert.AreEqual(-1, mapping.Get("missing", fallback: -1));
        }

        [Test]
        public void Get_CaseSensitive_NoMatch()
        {
            var mapping = CreateMapping(("Pink", 7));
            Assert.AreEqual(0, mapping.Get("pink"));
        }

        [Test]
        public void Entries_ReturnsAllAddedEntries()
        {
            var mapping = CreateMapping(("a", 1), ("b", 2));
            Assert.AreEqual(2, mapping.Entries.Count);
        }
    }
}
```

- [ ] **Step 2.3 — Run tests to confirm they FAIL (class not yet defined)**

Open Unity → Window → General → Test Runner → Edit Mode. Run `StringIntMappingTests`. All 6 tests should show as errors or compilation failures.

- [ ] **Step 2.4 — Implement `StringIntMapping`**

Create `Packages/com.hoppa.leveleditor.core/Editor/Mapping/StringIntMapping.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Level Editor/String-Int Mapping", order = 10)]
    public sealed class StringIntMapping : ScriptableObject
    {
        [SerializeField] private List<StringIntEntry> _entries = new List<StringIntEntry>();

        public IReadOnlyList<StringIntEntry> Entries => _entries;

        public bool TryGet(string key, out int value)
        {
            foreach (var entry in _entries)
            {
                if (entry.Key == key)
                {
                    value = entry.Value;
                    return true;
                }
            }
            value = 0;
            return false;
        }

        public int Get(string key, int fallback = 0)
            => TryGet(key, out int v) ? v : fallback;

        // Used by tests and editor tools to populate the mapping programmatically.
        public void Add(string key, int value)
            => _entries.Add(new StringIntEntry { Key = key, Value = value });
    }

    [Serializable]
    public sealed class StringIntEntry
    {
        public string Key;
        public int Value;
    }
}
```

- [ ] **Step 2.5 — Run tests to confirm all 6 PASS**

Unity Test Runner → Edit Mode → Run `StringIntMappingTests`. Expected: 6 green.

- [ ] **Step 2.6 — Commit**

```bash
git add Packages/com.hoppa.leveleditor.core/Editor/Mapping/StringIntMapping.cs
git add Packages/com.hoppa.leveleditor.core/Tests/Editor/StringIntMappingTests.cs
git add "Packages/com.hoppa.leveleditor.core/Tests/Editor/Hoppa.LevelEditor.Core.Editor.Tests.asmdef"
git commit -m "feat: add StringIntMapping generic string→int converter SO"
```

---

## Task 3 — Fix `CellTypeId` JSON leak on all 5 YarnTwist cell types

Every `ICellData` implementation serialises `CellTypeId` twice: once via the `CellDataConverter` (as `"type"`) and again as a plain property field. Fix: add `[JsonIgnore]` to the property on each class.

**Files:**
- Modify: `Assets/YarnTwist/Runtime/YarnEmptyCell.cs`
- Modify: `Assets/YarnTwist/Runtime/YarnWallCell.cs`
- Modify: `Assets/YarnTwist/Runtime/YarnBoxCell.cs`
- Modify: `Assets/YarnTwist/Runtime/YarnArrowBoxCell.cs`
- Modify: `Assets/YarnTwist/Runtime/YarnTunnelCell.cs`

- [ ] **Step 3.1 — Add `[JsonIgnore]` to `CellTypeId` in all 5 files**

For each file, add `[Newtonsoft.Json.JsonIgnore]` above the `CellTypeId` property. The pattern is identical in every file:

```csharp
// Before (example from YarnBoxCell.cs):
public string CellTypeId => "yt.box";

// After:
[Newtonsoft.Json.JsonIgnore]
public string CellTypeId => "yt.box";
```

Apply the same change to:
- `YarnEmptyCell.cs` → `"yt.empty"`
- `YarnWallCell.cs` → `"yt.wall"`
- `YarnBoxCell.cs` → `"yt.box"`
- `YarnArrowBoxCell.cs` → `"yt.arrowbox"`
- `YarnTunnelCell.cs` → `"yt.tunnel"`

- [ ] **Step 3.2 — Verify the fix with the existing serialisation round-trip test**

Unity Test Runner → Edit Mode → Run `SerializationRoundTripTests`. All tests must still pass. Then open a level in the editor, save it, and open the `.json` output. Confirm no `"CellTypeId"` field appears alongside `"type"`.

- [ ] **Step 3.3 — Commit**

```bash
git add Assets/YarnTwist/Runtime/YarnEmptyCell.cs
git add Assets/YarnTwist/Runtime/YarnWallCell.cs
git add Assets/YarnTwist/Runtime/YarnBoxCell.cs
git add Assets/YarnTwist/Runtime/YarnArrowBoxCell.cs
git add Assets/YarnTwist/Runtime/YarnTunnelCell.cs
git commit -m "fix: suppress redundant CellTypeId field in JSON output on all YarnTwist cell types"
```

---

## Task 4 — `YarnMasterLevelExporter`

**Files:**
- Create: `Assets/YarnTwist/Tests/Editor/Hoppa.YarnTwist.Editor.Tests.asmdef`
- Create: `Assets/YarnTwist/Tests/Editor/YarnMasterLevelExporterTests.cs`
- Create: `Assets/YarnTwist/Editor/YarnMasterLevelExporter.cs`

- [ ] **Step 4.1 — Create the YarnTwist Editor test assembly**

Create `Assets/YarnTwist/Tests/Editor/Hoppa.YarnTwist.Editor.Tests.asmdef`:

```json
{
  "name": "Hoppa.YarnTwist.Editor.Tests",
  "rootNamespace": "Hoppa.YarnTwist.Editor.Tests",
  "references": [
    "Hoppa.LevelEditor.Core.Runtime",
    "Hoppa.LevelEditor.Core.Editor",
    "Hoppa.YarnTwist.Runtime",
    "Hoppa.YarnTwist.Editor",
    "UnityEngine.TestRunner",
    "UnityEditor.TestRunner"
  ],
  "includePlatforms": ["Editor"],
  "optionalUnityReferences": ["TestAssemblies"]
}
```

- [ ] **Step 4.2 — Write the failing tests**

Create `Assets/YarnTwist/Tests/Editor/YarnMasterLevelExporterTests.cs`:

```csharp
using System.Collections.Generic;
using System.IO;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YarnTwist;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor.Tests
{
    public class YarnMasterLevelExporterTests
    {
        private string _tempFile;
        private StringIntMapping _colorMap;
        private StringIntMapping _cellMap;
        private YarnMasterLevelExporter _exporter;

        [SetUp]
        public void SetUp()
        {
            _tempFile = Path.Combine(Path.GetTempPath(), "level_config_test.json");
            if (File.Exists(_tempFile)) File.Delete(_tempFile);

            _colorMap = ScriptableObject.CreateInstance<StringIntMapping>();
            _colorMap.Add("pink", 7);
            _colorMap.Add("blue", 1);
            _colorMap.Add("red",  9);

            _cellMap = ScriptableObject.CreateInstance<StringIntMapping>();
            _cellMap.Add("yt.empty",    2);
            _cellMap.Add("yt.wall",     1);
            _cellMap.Add("yt.box",      3);
            _cellMap.Add("yt.arrowbox", 4);
            _cellMap.Add("yt.tunnel",   5);

            _exporter = ScriptableObject.CreateInstance<YarnMasterLevelExporter>();
            _exporter.SetTestDependencies(_tempFile, _colorMap, _cellMap, "Coin", 10);
        }

        [TearDown]
        public void TearDown()
        {
            if (File.Exists(_tempFile)) File.Delete(_tempFile);
        }

        // ── Level key parsing ──────────────────────────────────────────────

        [Test]
        public void Export_ParsesLevelId_001_ToKey1()
        {
            var doc = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection());
            _exporter.Export(doc, BuildRegistry(), _tempFile);
            var config = ReadOutput();
            Assert.IsTrue(config["LevelConfigs"].ToObject<JObject>().ContainsKey("1"));
        }

        [Test]
        public void Export_ParsesLevelId_025_ToKey25()
        {
            var doc = MakeDoc("level_025", new[] { EmptyCell() }, MakeTopSection());
            _exporter.Export(doc, BuildRegistry(), _tempFile);
            var config = ReadOutput();
            Assert.IsTrue(config["LevelConfigs"].ToObject<JObject>().ContainsKey("25"));
        }

        // ── BottomConfig cell transform ────────────────────────────────────

        [Test]
        public void Export_EmptyCell_ProducesBottomType2_ColorType0()
        {
            var doc = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection());
            _exporter.Export(doc, BuildRegistry(), _tempFile);
            var bottom = BottomConfigAt(0);
            Assert.AreEqual(2, (int)bottom["BottomType"]);
            Assert.AreEqual(0, (int)bottom["ColorType"]);
        }

        [Test]
        public void Export_BoxCell_ProducesBottomType3_AndMappedColor()
        {
            var doc = MakeDoc("level_001", new[] { BoxCell("pink") }, MakeTopSection());
            _exporter.Export(doc, BuildRegistry(), _tempFile);
            var bottom = BottomConfigAt(0);
            Assert.AreEqual(3, (int)bottom["BottomType"]);
            Assert.AreEqual(7, (int)bottom["ColorType"]);
        }

        [Test]
        public void Export_WallCell_ProducesBottomType1()
        {
            var doc = MakeDoc("level_001", new[] { WallCell() }, MakeTopSection());
            _exporter.Export(doc, BuildRegistry(), _tempFile);
            Assert.AreEqual(1, (int)BottomConfigAt(0)["BottomType"]);
        }

        [Test]
        public void Export_BoxCell_Position_MatchesGridCoordinates()
        {
            // Grid 2×1: cell[0] is (x=0,y=0), cell[1] is (x=1,y=0)
            var cells = new ICellData[] { EmptyCell(), BoxCell("blue") };
            var doc   = MakeDoc("level_001", cells, MakeTopSection(), width: 2, height: 1);
            _exporter.Export(doc, BuildRegistry(), _tempFile);
            var bottom1 = BottomConfigAt(1);
            Assert.AreEqual(1, (int)bottom1["Position"]["x"]);
            Assert.AreEqual(0, (int)bottom1["Position"]["y"]);
        }

        // ── TopConfig / WinderConfig transform ────────────────────────────

        [Test]
        public void Export_TopSection_ProducesExactly4TopConfigs()
        {
            var doc = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection());
            _exporter.Export(doc, BuildRegistry(), _tempFile);
            var topConfigs = TopConfigs();
            Assert.AreEqual(4, topConfigs.Count);
        }

        [Test]
        public void Export_SpoolColor_MapsToCorrectColorType()
        {
            // Column 0 has one spool: pink (→7)
            var doc = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection(col0Spool: "pink"));
            _exporter.Export(doc, BuildRegistry(), _tempFile);
            var winder = TopConfigs()[0]["WinderConfigs"][0];
            Assert.AreEqual(7, (int)winder["ColorType"]);
        }

        // ── Reward stub ────────────────────────────────────────────────────

        [Test]
        public void Export_NewLevel_StubsRewardEntry()
        {
            var doc = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection());
            _exporter.Export(doc, BuildRegistry(), _tempFile);
            var rewards = ReadOutput()["LevelRewardConfigs"]["1"];
            Assert.IsNotNull(rewards);
            Assert.AreEqual("Coin", (string)rewards["WinReward"][0]["ScoreType"]);
            Assert.AreEqual(10,     (int)rewards["WinReward"][0]["ScoreAmount"]);
        }

        [Test]
        public void Export_ExistingReward_IsPreserved()
        {
            // Pre-populate with a custom reward for level 1
            var existing = JObject.Parse(@"{
                ""LevelRewardConfigs"": { ""1"": { ""WinReward"": [{""ScoreType"":""Gem"",""ScoreAmount"":5}] } },
                ""LevelConfigs"": {}
            }");
            File.WriteAllText(_tempFile, existing.ToString());

            var doc = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection());
            _exporter.Export(doc, BuildRegistry(), _tempFile);

            var reward = ReadOutput()["LevelRewardConfigs"]["1"]["WinReward"][0];
            Assert.AreEqual("Gem", (string)reward["ScoreType"]);
            Assert.AreEqual(5,     (int)reward["ScoreAmount"]);
        }

        [Test]
        public void Export_Upsert_OverwritesExistingLevelData()
        {
            // Save level_001 twice with different cell types
            var doc1 = MakeDoc("level_001", new[] { EmptyCell() }, MakeTopSection());
            _exporter.Export(doc1, BuildRegistry(), _tempFile);

            var doc2 = MakeDoc("level_001", new[] { BoxCell("pink") }, MakeTopSection());
            _exporter.Export(doc2, BuildRegistry(), _tempFile);

            // Second save wins
            Assert.AreEqual(3, (int)BottomConfigAt(0)["BottomType"]);
        }

        // ── Helpers ───────────────────────────────────────────────────────

        private static ICellData EmptyCell()  => new YarnEmptyCell();
        private static ICellData WallCell()   => new YarnWallCell();
        private static ICellData BoxCell(string color) => new YarnBoxCell { ColorId = color };

        private static JObject MakeTopSection(string col0Spool = null)
        {
            var spool  = new JObject(new JProperty("colorId", col0Spool ?? "pink"),
                                     new JProperty("hidden",  false));
            var col0   = new JObject(new JProperty("spools", new JArray(spool)));
            var emptyCol = new JObject(new JProperty("spools", new JArray()));
            return JObject.FromObject(new YarnTopSectionData
            {
                Columns = new System.Collections.Generic.List<YarnSpoolColumn>
                {
                    new YarnSpoolColumn { Spools = new System.Collections.Generic.List<YarnSpoolData>
                        { new YarnSpoolData { ColorId = col0Spool ?? "pink", Hidden = false } } },
                    new YarnSpoolColumn(), new YarnSpoolColumn(), new YarnSpoolColumn()
                }
            });
        }

        private static LevelDocument MakeDoc(string levelId, ICellData[] cells,
            JObject topSection, int width = 1, int height = 1)
        {
            var grid = new GridData<ICellData>(width, height);
            for (int i = 0; i < cells.Length; i++) grid.Cells[i] = cells[i];
            return new LevelDocument
            {
                LevelId       = levelId,
                SchemaVersion = "yarn-twist.v1",
                Grid          = grid,
                TopSection    = topSection,
            };
        }

        private static CellTypeRegistry BuildRegistry()
        {
            var r = new CellTypeRegistry();
            // Register cell type IDs → concrete types so the serialiser knows them.
            // YarnMasterLevelExporter reads CellTypeId directly from ICellData,
            // so we only need a registry entry if the exporter uses serialisation.
            // For the transform path it inspects cell types directly — no registry needed.
            return r;
        }

        private JObject ReadOutput() =>
            JObject.Parse(File.ReadAllText(_tempFile));

        private JToken BottomConfigAt(int idx) =>
            ReadOutput()["LevelConfigs"]["1"]["BottomConfigs"][idx];

        private JArray TopConfigs() =>
            (JArray)ReadOutput()["LevelConfigs"]["1"]["TopConfigs"];
    }
}
```

- [ ] **Step 4.3 — Run tests to confirm they FAIL (class not yet defined)**

Unity Test Runner → Edit Mode → Run `YarnMasterLevelExporterTests`. Expected: compile error or test errors.

- [ ] **Step 4.4 — Implement `YarnMasterLevelExporter`**

Create `Assets/YarnTwist/Editor/YarnMasterLevelExporter.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Yarn Twist/Master Level Exporter")]
    public sealed class YarnMasterLevelExporter : LevelExporterAsset
    {
        [SerializeField] private string _outputPath;
        [SerializeField] private StringIntMapping _colorMapping;
        [SerializeField] private StringIntMapping _cellTypeMapping;
        [SerializeField] private string _defaultRewardScoreType = "Coin";
        [SerializeField] private int    _defaultRewardAmount    = 10;

        public override string Name => "MasterLevelConfig";

        // Called by tests to inject dependencies without an asset on disk.
        internal void SetTestDependencies(string outputPath, StringIntMapping colorMapping,
            StringIntMapping cellTypeMapping, string rewardScoreType, int rewardAmount)
        {
            _outputPath              = outputPath;
            _colorMapping            = colorMapping;
            _cellTypeMapping         = cellTypeMapping;
            _defaultRewardScoreType  = rewardScoreType;
            _defaultRewardAmount     = rewardAmount;
        }

        public override bool Export(LevelDocument document, CellTypeRegistry cellTypes, string jsonFilePath)
        {
            if (string.IsNullOrEmpty(_outputPath))
            {
                Debug.LogWarning("[YarnMasterLevelExporter] Output path is not set — skipping.");
                return false;
            }
            if (_colorMapping == null || _cellTypeMapping == null)
            {
                Debug.LogWarning("[YarnMasterLevelExporter] Color or cell-type mapping is not assigned — skipping.");
                return false;
            }

            if (!TryParseLevelKey(document.LevelId, out int levelKey))
            {
                Debug.LogWarning($"[YarnMasterLevelExporter] Cannot parse integer level key from '{document.LevelId}' — skipping.");
                return false;
            }

            // Read or initialise the master config.
            var master = ReadOrCreateMaster(_outputPath);

            // Upsert LevelConfigs entry.
            master["LevelConfigs"][levelKey.ToString()] = BuildLevelConfig(document);

            // Stub LevelRewardConfigs entry only for new levels.
            if (master["LevelRewardConfigs"][levelKey.ToString()] == null)
                master["LevelRewardConfigs"][levelKey.ToString()] = BuildDefaultReward();

            var dir = Path.GetDirectoryName(_outputPath);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);
            File.WriteAllText(_outputPath, master.ToString(Formatting.Indented));
            return true;
        }

        // ── Parsing ──────────────────────────────────────────────────────────

        private static bool TryParseLevelKey(string levelId, out int key)
        {
            var match = Regex.Match(levelId ?? string.Empty, @"\d+$");
            if (!match.Success) { key = 0; return false; }
            key = int.Parse(match.Value);
            return true;
        }

        // ── Master config I/O ────────────────────────────────────────────────

        private static JObject ReadOrCreateMaster(string path)
        {
            if (File.Exists(path))
            {
                try { return JObject.Parse(File.ReadAllText(path)); }
                catch { /* corrupt file — start fresh */ }
            }
            return JObject.Parse(@"{ ""LevelRewardConfigs"": {}, ""LevelConfigs"": {} }");
        }

        // ── LevelConfig build ────────────────────────────────────────────────

        private JObject BuildLevelConfig(LevelDocument document)
        {
            return new JObject(
                new JProperty("BottomConfigs", BuildBottomConfigs(document.Grid)),
                new JProperty("TopConfigs",    BuildTopConfigs(document.TopSection))
            );
        }

        // ── BottomConfigs ────────────────────────────────────────────────────

        private JArray BuildBottomConfigs(GridData<ICellData> grid)
        {
            var array = new JArray();
            for (int y = 0; y < grid.Height; y++)
            {
                for (int x = 0; x < grid.Width; x++)
                {
                    var cell = grid.Get(x, y);
                    array.Add(BuildBottomConfig(x, y, cell));
                }
            }
            return array;
        }

        private JObject BuildBottomConfig(int x, int y, ICellData cell)
        {
            if (cell == null)
                return DefaultBottomConfig(x, y);

            int bottomType = _cellTypeMapping.Get(cell.CellTypeId, fallback: 0);
            int colorType  = GetColorType(cell);

            var obj = new JObject(
                new JProperty("Position",   new JObject(new JProperty("x", x), new JProperty("y", y))),
                new JProperty("BottomType", bottomType),
                new JProperty("ColorType",  colorType)
            );

            // Direction (ArrowBox and Tunnel).
            string direction = GetDirection(cell);
            if (direction != null)
                obj["Direction"] = direction;

            // Hidden (Box only — ArrowBoxCell has no Hidden field in current implementation).
            if (cell is YarnBoxCell box)
                obj["Hidden"] = box.Hidden;

            // Queue (Tunnel only).
            if (cell is YarnTunnelCell tunnel && tunnel.Queue?.Count > 0)
                obj["Queue"] = BuildQueue(tunnel.Queue);

            return obj;
        }

        private static JObject DefaultBottomConfig(int x, int y) =>
            new JObject(
                new JProperty("Position",   new JObject(new JProperty("x", x), new JProperty("y", y))),
                new JProperty("BottomType", 0),
                new JProperty("ColorType",  0)
            );

        private int GetColorType(ICellData cell)
        {
            if (cell is IColoredCell colored)
                return _colorMapping.Get(colored.ColorId, fallback: 0);
            return 0;
        }

        private static string GetDirection(ICellData cell)
        {
            if (cell is YarnArrowBoxCell arrow)  return arrow.Direction.ToString();
            if (cell is YarnTunnelCell tunnel)   return tunnel.OutputDirection.ToString();
            return null;
        }

        private JArray BuildQueue(List<string> colorIds)
        {
            var array = new JArray();
            foreach (var colorId in colorIds)
                array.Add(new JObject(
                    new JProperty("ColorType", _colorMapping.Get(colorId, fallback: 0)),
                    new JProperty("Hidden",    false)
                ));
            return array;
        }

        // ── TopConfigs ───────────────────────────────────────────────────────

        private static JArray BuildTopConfigs(JObject topSection)
        {
            var data = topSection?.ToObject<YarnTopSectionData>() ?? new YarnTopSectionData();

            // Always emit exactly 4 columns (game hardcodes 4).
            while (data.Columns.Count < 4) data.Columns.Add(new YarnSpoolColumn());

            var array = new JArray();
            for (int i = 0; i < 4; i++)
            {
                var col     = data.Columns[i];
                var winders = new JArray();
                foreach (var spool in col.Spools)
                    winders.Add(new JObject(
                        new JProperty("ColorType", 0), // filled below
                        new JProperty("Hidden",    spool.Hidden)
                    ));
                array.Add(new JObject(
                    new JProperty("Index",         i),
                    new JProperty("WinderConfigs", winders)
                ));
            }
            return array;
        }

        // ── Reward stub ──────────────────────────────────────────────────────

        private JObject BuildDefaultReward() =>
            new JObject(new JProperty("WinReward", new JArray(
                new JObject(
                    new JProperty("ScoreType",   _defaultRewardScoreType),
                    new JProperty("ScoreAmount", _defaultRewardAmount)
                )
            )));
    }
}
```

> **Note:** `BuildTopConfigs` currently leaves `WinderConfig.ColorType = 0` for all spools because `TopSectionPanel` data arrives via `JObject` and `StringIntMapping` isn't statically accessible inside the static helper. Fix in Step 4.5.

- [ ] **Step 4.5 — Fix TopConfig color mapping (make `BuildTopConfigs` non-static)**

Replace the static `BuildTopConfigs` with an instance method that has access to `_colorMapping`:

```csharp
// Replace the static BuildTopConfigs with this instance version:
private JArray BuildTopConfigs(JObject topSection)
{
    var data = topSection?.ToObject<YarnTopSectionData>() ?? new YarnTopSectionData();
    while (data.Columns.Count < 4) data.Columns.Add(new YarnSpoolColumn());

    var array = new JArray();
    for (int i = 0; i < 4; i++)
    {
        var col     = data.Columns[i];
        var winders = new JArray();
        foreach (var spool in col.Spools)
            winders.Add(new JObject(
                new JProperty("ColorType", _colorMapping.Get(spool.ColorId, fallback: 0)),
                new JProperty("Hidden",    spool.Hidden)
            ));
        array.Add(new JObject(
            new JProperty("Index",         i),
            new JProperty("WinderConfigs", winders)
        ));
    }
    return array;
}
```

Remove the `static` keyword from the call site inside `BuildLevelConfig` accordingly (it's already calling `this.BuildTopConfigs`).

- [ ] **Step 4.6 — Run tests to confirm all pass**

Unity Test Runner → Edit Mode → Run `YarnMasterLevelExporterTests`. Expected: all green.

If `Export_SpoolColor_MapsToCorrectColorType` fails, verify Step 4.5 was applied correctly.

- [ ] **Step 4.7 — Commit**

```bash
git add "Assets/YarnTwist/Tests/Editor/Hoppa.YarnTwist.Editor.Tests.asmdef"
git add Assets/YarnTwist/Tests/Editor/YarnMasterLevelExporterTests.cs
git add Assets/YarnTwist/Editor/YarnMasterLevelExporter.cs
git commit -m "feat: add YarnMasterLevelExporter — transforms LevelDocument to game level_config.json schema"
```

---

## Task 5 — Game data model extensions (YarnTwist repo, data only)

**File:** `E:\Projects\Hoppa\YarnTwist\Assets\_YAT\Scripts\Gamelogic\Managers\YATLevelManager.cs`

No spawn logic. No `YATGameManagerComponent` changes. Data classes only.

- [ ] **Step 5.1 — Add `ArrowBox` and `Tunnel` to `YATBottomType` enum**

Find the `YATBottomType` enum in `YATLevelManager.cs` and append the two new values with TODO comments:

```csharp
public enum YATBottomType
{
    None  = 0,
    Wall  = 1,
    Empty = 2,
    Color = 3,

    //TODO: ArrowBox — a colored box with a directional arrow overlay. When triggered,
    // pushes yarn balls in the specified direction. Requires Direction field on
    // BottomConfig and a YATArrowBoxPrefabComponent prefab wired in YATGameManagerComponent.
    ArrowBox = 4,

    //TODO: Tunnel — a cell that delivers colored boxes from an internal queue sequentially.
    // Direction indicates which face is the output. Requires Queue field on BottomConfig
    // and a YATTunnelPrefabComponent prefab wired in YATGameManagerComponent.
    Tunnel = 5,
}
```

- [ ] **Step 5.2 — Add new fields to `BottomConfig`**

Find `class BottomConfig` in `YATLevelManager.cs` and add the three new fields:

```csharp
public class BottomConfig
{
    public YATVector2Int Position;
    public YATBottomType BottomType;
    public YATColorType  ColorType;

    //TODO: Direction — direction string for ArrowBox ("Up","Down","Left","Right") and
    // Tunnel (output face). Populated by YarnMasterLevelExporter. Ignored by the spawner
    // until ArrowBox/Tunnel prefab components are implemented.
    public string Direction;

    //TODO: Hidden — when true, this cell's color is concealed from the player until
    // revealed by gameplay. Currently unused in YATGameManagerComponent spawn logic.
    public bool Hidden;

    //TODO: Queue — tunnel delivery queue. Each entry has ColorType (int) and Hidden (bool).
    // Null for all non-tunnel cell types. See TunnelQueueEntry.
    public TunnelQueueEntry[] Queue;
}
```

- [ ] **Step 5.3 — Add `TunnelQueueEntry` class**

Add immediately after the `BottomConfig` class:

```csharp
//TODO: TunnelQueueEntry — one item in a tunnel cell's delivery queue.
// Maps to the editor's tunnel queue format { colorId → ColorType int, hidden → Hidden bool }.
// Used by YATTunnelPrefabComponent (not yet implemented).
public class TunnelQueueEntry
{
    public YATColorType ColorType;
    public bool         Hidden;
}
```

- [ ] **Step 5.4 — Add `Hidden` to `WinderConfig`**

Find `class WinderConfig` and add:

```csharp
public class WinderConfig
{
    public YATColorType ColorType;

    //TODO: Hidden — when true, this spool's color is concealed until revealed by gameplay.
    // Currently unused in YATWinderPrefabComponent.Init. Populated by YarnMasterLevelExporter.
    public bool Hidden;
}
```

- [ ] **Step 5.5 — Verify the game project compiles**

Open Unity in the Yarn Twist project. Check the Console for compiler errors. Expected: zero errors. The new fields are additive; Newtonsoft's default settings will deserialise them from the new JSON format and ignore them in the existing spawn logic.

- [ ] **Step 5.6 — Commit (in the YarnTwist repo)**

```bash
cd "E:\Projects\Hoppa\YarnTwist"
git add Assets/_YAT/Scripts/Gamelogic/Managers/YATLevelManager.cs
git commit -m "feat: extend level data model with ArrowBox/Tunnel enum values and stub fields (data only, no spawn logic)"
```

---

## Task 6 — Create Unity assets and wire the exporter

All steps in this task are performed inside **Unity Editor** in the `hoppa-level-editor-core` project.

- [ ] **Step 6.1 — Create the Color mapping asset**

In the Project window: right-click `Assets/YarnTwist/` → Create → Hoppa → Level Editor → String-Int Mapping. Name it `YarnColorMapping`.

Add entries (Key → Value):

| Key | Value |
|---|---|
| `blue` | 1 |
| `cyan` | 2 |
| `yellow` | 3 |
| `green` | 4 |
| `magenta` | 5 |
| `orange` | 6 |
| `pink` | 7 |
| `purple` | 8 |
| `red` | 9 |
| `turquoise` | 10 |

- [ ] **Step 6.2 — Create the Cell Type mapping asset**

Right-click `Assets/YarnTwist/` → Create → Hoppa → Level Editor → String-Int Mapping. Name it `YarnCellTypeMapping`.

Add entries:

| Key | Value |
|---|---|
| `yt.wall` | 1 |
| `yt.empty` | 2 |
| `yt.box` | 3 |
| `yt.arrowbox` | 4 |
| `yt.tunnel` | 5 |

- [ ] **Step 6.3 — Create the `YarnMasterLevelExporter` asset**

Right-click `Assets/YarnTwist/` → Create → Hoppa → Yarn Twist → Master Level Exporter. Name it `YarnMasterLevelExporter`.

Fill in the Inspector:
- **Output Path:** `E:/Projects/Hoppa/YarnTwist/Assets/_YAT/Configs/Resources/Configs/level_config.json`
- **Color Mapping:** assign `YarnColorMapping`
- **Cell Type Mapping:** assign `YarnCellTypeMapping`
- **Default Reward Score Type:** `Coin`
- **Default Reward Amount:** `10`

- [ ] **Step 6.4 — Fix the `GameProfile` schema ID**

Open the `YarnTwistProfile.asset` in the Inspector. Change **Schema Id** from `yarn-twist.v1` to `yarn-twist` (no `.v1` suffix — the window appends `.v1` automatically).

- [ ] **Step 6.5 — Add `YarnMasterLevelExporter` to the `GameProfile` Exporters list**

Open `YarnTwistProfile.asset`. In the **Exporters** list, add the `YarnMasterLevelExporter.asset` created in Step 6.3. The existing `YarnSortExporter.asset` should remain in the list.

- [ ] **Step 6.6 — End-to-end smoke test**

1. Open `Window → Level Editor`.
2. Select the `YarnTwistProfile` and create a new level.
3. Paint a few cells (some boxes with different colours).
4. Add some spools to the top section.
5. Save the level (File → Save). Choose a path inside `Assets/`.
6. Open `E:\Projects\Hoppa\YarnTwist\Assets\_YAT\Configs\Resources\Configs\level_config.json`.
7. Verify: the file contains a `LevelConfigs` entry with the correct integer key, `BottomConfigs` with correct `BottomType` and `ColorType` int values, and `TopConfigs` with `WinderConfigs` entries.
8. Verify: `LevelRewardConfigs` contains a matching entry with `"Coin"` and `10`.
9. Open Unity in the Yarn Twist project. Confirm `level_config.json` is detected as changed and no console errors appear on reimport.

- [ ] **Step 6.7 — Commit assets**

```bash
cd "E:\Projects\Hoppa\hoppa-level-editor-core"
git add Assets/YarnTwist/
git commit -m "feat: wire YarnMasterLevelExporter asset with color/cell-type mappings, fix GameProfile schemaId"
```

---

## Self-Review

**Spec coverage check:**

| Spec requirement | Covered in |
|---|---|
| `LevelExporterAsset` base class | Task 1 |
| `ScriptableObjectExporter` → inherit from `LevelExporterAsset` | Task 1 |
| `GameProfile._exporters` type update | Task 1 |
| `StringIntMapping` generic SO | Task 2 |
| `StringIntEntry` serializable class | Task 2 |
| `[JsonIgnore]` on `CellTypeId` in 5 cell files | Task 3 |
| `YarnMasterLevelExporter` with configurable output path | Task 4 |
| Level ID parsing `"level_001"` → `1` | Task 4 |
| Color mapping via `StringIntMapping` | Task 4 + Task 6.1 |
| Cell type mapping via `StringIntMapping` | Task 4 + Task 6.2 |
| Always emit 4 TopConfig entries | Task 4 |
| Upsert (preserve existing reward entry) | Task 4 |
| Stub `LevelRewardConfigs` for new levels | Task 4 |
| Schema version bug fix (`yarn-twist` not `yarn-twist.v1`) | Task 6.4 |
| Game: `ArrowBox=4`, `Tunnel=5` enum values with TODOs | Task 5 |
| Game: `Direction`, `Hidden`, `Queue` fields with TODOs | Task 5 |
| Game: `TunnelQueueEntry` class with TODO | Task 5 |
| Game: `WinderConfig.Hidden` with TODO | Task 5 |
| Grid offset `-3.5f` NOT changed | ✓ Not in plan |
| Spawn stubs NOT added to `YATGameManagerComponent` | ✓ Not in plan |

All spec requirements covered. No placeholders in task steps.
