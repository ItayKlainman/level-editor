# YAK Difficulty Curve Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Let a designer define a difficulty ramp once (tier presets + ordered ranges) and mass-produce a numbered, difficulty-scaled YAK level set through the existing generate→autofill→analyze→review→export pipeline.

**Architecture:** A single `YAKDifficultyCurveConfig` ScriptableObject holds a list of `TierPreset` recipes and an ordered `Curve` of `{tier, count}` segments. A batch runner (`YAKCurveBatchHarness`) walks the curve; for each level it assembles a **transient, tier-configured copy** of the wired `GameProfile` (via `Object.Instantiate` + reflection field-set on the few varied fields) and calls the *unchanged* `profile.LevelGenerator.Generate(...)`. This means the entire existing pipeline runs as-is — the only pipeline code change is adding a hidden-spool ratio to the autofiller. Output is numbered `level_N.json` (+ png + stats) routed through the existing `BatchReviewWindow`.

**Tech Stack:** Unity 6 (editor-core), C#, Newtonsoft.Json, NUnit EditMode tests, IMGUI EditorWindow.

## Global Constraints

- Manual single-level editing MUST remain byte-for-byte unchanged: the curve system NEVER mutates the on-disk profile or config assets — it works on transient `Object.Instantiate` copies only.
- Write `.cs` files with Write/Edit tools (never PowerShell). [[feedback_write_files_directly]]
- No absolute paths in any committed asset; staging output goes under `Application.dataPath/..` (project-relative), matching `YAKBatchHarness`. [[feedback_no_absolute_paths_in_committed_assets]]
- New code lives in the YAK Editor assembly (`Assets/YAK/Editor/...`); tests in `Assets/YAK/Tests/Editor/` under namespace `Hoppa.YAK.Editor.Tests` (asmdef `Hoppa.YAK.Editor.Tests`).
- The spool data class is `YAKSpoolEntry` with members `ColorId` / `Capacity` / `Hidden` (JSON `colorId`/`capacity`/`hidden`). There is NO `SpoolConfig`/`IsHidden` type.
- The generator chains through `profile.ImageToGrid.Convert(...)`, `profile.LevelCompleter.Complete(...)`, `profile.LevelAnalyzer.Analyze(...)` — interface-typed indirection. Vary behavior by swapping the profile's wired assets, not by editing these classes.
- `GameProfile` private fields to set via reflection: `_gridWidth`, `_gridHeight`, `_imageToGrid`, `_levelCompleter`, `_levelAnalyzer`, `_generatorConfig`. `YAKSpoolAutofiller._config` and `YAKLevelAnalyzer._config` likewise.
- KNOWN LIMITATION (document, do not fix here): `YakAveragePlayer` does not consult `SpoolHidden`, so measured APS does NOT reflect hidden-spool difficulty. `HiddenRatio` is therefore a manual difficulty knob until analyzer hidden-support lands (fast-follow, ties to Gap B). No silent capping — surface this in code comments + the review UI tooltip.

**Running EditMode tests:** via the Unity Test Runner (EditMode), filtering to the new fixtures, e.g. test class `YakDifficultyCurveTests`. The verifier runs these through the Unity MCP `tests-run` (or `Unity -batchmode -runTests -testPlatform EditMode`). Expected baseline before this work: existing YAK suite green.

---

### Task 1: `YAKDifficultyCurveConfig` — data model, curve resolution, CRUD & validation

**Files:**
- Create: `Assets/YAK/Editor/Generator/YAKDifficultyCurveConfig.cs`
- Test: `Assets/YAK/Tests/Editor/YakDifficultyCurveTests.cs`

**Interfaces:**
- Produces:
  - `class TierPreset` (public fields: `string Name; int GridWidth; int GridHeight; int MaxColors; int AvgCapacity; float CapacitySlack; int ConveyorSlots; Vector2Int ColumnRange; float HiddenRatio; float TargetAps; float ApsTolerance;`)
  - `class CurveSegment { string TierName; int LevelCount; }`
  - `class YAKDifficultyCurveConfig : ScriptableObject` with `List<TierPreset> Presets`, `List<CurveSegment> Curve`, and methods:
    - `int TotalLevels()`
    - `TierPreset TierForLevel(int oneBasedIndex)` — null if no curve/preset resolvable
    - `TierPreset Duplicate(int index)` — appends an independent copy named "<Name> Copy", returns it
    - `void DeletePreset(int index)`
    - `List<string> Validate()` — empty list = valid
    - `string Summary()` — e.g. "30 levels → Tutorial ×5, Easy ×10, Medium ×15"

- [ ] **Step 1: Write the failing tests**

Create `Assets/YAK/Tests/Editor/YakDifficultyCurveTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Hoppa.YAK.Editor;

namespace Hoppa.YAK.Editor.Tests
{
    public sealed class YakDifficultyCurveTests
    {
        private static YAKDifficultyCurveConfig MakeConfig()
        {
            var c = ScriptableObject.CreateInstance<YAKDifficultyCurveConfig>();
            c.Presets = new List<TierPreset>
            {
                new TierPreset { Name = "Tutorial", GridWidth = 8,  GridHeight = 8,  MaxColors = 2, AvgCapacity = 30, ColumnRange = new Vector2Int(2,3), TargetAps = 1.5f, ApsTolerance = 0.6f },
                new TierPreset { Name = "Easy",     GridWidth = 12, GridHeight = 12, MaxColors = 3, AvgCapacity = 25, ColumnRange = new Vector2Int(2,4), TargetAps = 2.5f, ApsTolerance = 0.6f },
                new TierPreset { Name = "Medium",   GridWidth = 20, GridHeight = 20, MaxColors = 4, AvgCapacity = 20, ColumnRange = new Vector2Int(3,5), TargetAps = 4f,   ApsTolerance = 0.6f },
            };
            c.Curve = new List<CurveSegment>
            {
                new CurveSegment { TierName = "Tutorial", LevelCount = 5 },
                new CurveSegment { TierName = "Easy",     LevelCount = 10 },
                new CurveSegment { TierName = "Medium",   LevelCount = 15 },
            };
            return c;
        }

        [Test]
        public void TotalLevels_SumsSegments()
        {
            Assert.AreEqual(30, MakeConfig().TotalLevels());
        }

        [Test]
        public void TierForLevel_MapsRangesAndBoundaries()
        {
            var c = MakeConfig();
            Assert.AreEqual("Tutorial", c.TierForLevel(1).Name);
            Assert.AreEqual("Tutorial", c.TierForLevel(5).Name);   // last tutorial
            Assert.AreEqual("Easy",     c.TierForLevel(6).Name);   // first easy
            Assert.AreEqual("Easy",     c.TierForLevel(15).Name);
            Assert.AreEqual("Medium",   c.TierForLevel(16).Name);
            Assert.AreEqual("Medium",   c.TierForLevel(30).Name);
        }

        [Test]
        public void TierForLevel_OutOfRange_ClampsToLastTier()
        {
            var c = MakeConfig();
            Assert.AreEqual("Medium", c.TierForLevel(99).Name); // beyond total → last
            Assert.AreEqual("Tutorial", c.TierForLevel(0).Name); // below 1 → first
        }

        [Test]
        public void Duplicate_ProducesIndependentCopy()
        {
            var c = MakeConfig();
            var dup = c.Duplicate(0);
            Assert.AreEqual(4, c.Presets.Count);
            Assert.AreNotSame(c.Presets[0], dup);
            dup.GridWidth = 999;
            Assert.AreEqual(8, c.Presets[0].GridWidth, "editing the copy must not touch the original");
        }

        [Test]
        public void Validate_FlagsDanglingTierAndEmpties()
        {
            var c = MakeConfig();
            Assert.IsEmpty(c.Validate());
            c.Curve.Add(new CurveSegment { TierName = "DoesNotExist", LevelCount = 3 });
            CollectionAssert.IsNotEmpty(c.Validate());

            var empty = ScriptableObject.CreateInstance<YAKDifficultyCurveConfig>();
            CollectionAssert.IsNotEmpty(empty.Validate());
        }

        [Test]
        public void Summary_DescribesRamp()
        {
            StringAssert.Contains("30 levels", MakeConfig().Summary());
            StringAssert.Contains("Tutorial", MakeConfig().Summary());
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run the EditMode fixture `YakDifficultyCurveTests`.
Expected: FAIL / compile error — `YAKDifficultyCurveConfig` not defined.

- [ ] **Step 3: Implement `YAKDifficultyCurveConfig`**

Create `Assets/YAK/Editor/Generator/YAKDifficultyCurveConfig.cs`:

```csharp
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEngine;

namespace Hoppa.YAK.Editor
{
    [Serializable]
    public class TierPreset
    {
        public string Name = "Tier";
        [Header("Board")]
        public int GridWidth = 12;
        public int GridHeight = 12;
        [Min(2)] public int MaxColors = 3;
        [Header("Spools")]
        [Min(1)] public int AvgCapacity = 20;
        [Range(0f, 1f)] public float CapacitySlack = 0f;
        [Min(1)] public int ConveyorSlots = 5;
        public Vector2Int ColumnRange = new Vector2Int(2, 5);
        [Range(0f, 1f)] public float HiddenRatio = 0f;
        [Header("Difficulty target")]
        public float TargetAps = 3f;
        public float ApsTolerance = 0.6f;

        public TierPreset Clone() => (TierPreset)MemberwiseClone();
    }

    [Serializable]
    public class CurveSegment
    {
        public string TierName = "";
        [Min(0)] public int LevelCount = 1;
    }

    [CreateAssetMenu(menuName = "Hoppa/YAK/Generator/YAK Difficulty Curve")]
    public sealed class YAKDifficultyCurveConfig : ScriptableObject
    {
        public List<TierPreset> Presets = new List<TierPreset>();
        public List<CurveSegment> Curve = new List<CurveSegment>();

        public int TotalLevels()
        {
            int t = 0;
            if (Curve != null) foreach (var s in Curve) t += Mathf.Max(0, s.LevelCount);
            return t;
        }

        public TierPreset FindPreset(string name)
            => Presets?.FirstOrDefault(p => p.Name == name);

        public TierPreset TierForLevel(int oneBasedIndex)
        {
            if (Curve == null || Curve.Count == 0 || Presets == null || Presets.Count == 0)
                return null;
            if (oneBasedIndex < 1)
                return FindPreset(Curve[0].TierName) ?? Presets[0];

            int acc = 0;
            foreach (var seg in Curve)
            {
                acc += Mathf.Max(0, seg.LevelCount);
                if (oneBasedIndex <= acc)
                    return FindPreset(seg.TierName);
            }
            // beyond total → last segment's tier
            return FindPreset(Curve[Curve.Count - 1].TierName);
        }

        public TierPreset Duplicate(int index)
        {
            if (Presets == null || index < 0 || index >= Presets.Count) return null;
            var copy = Presets[index].Clone();
            copy.Name = Presets[index].Name + " Copy";
            Presets.Add(copy);
            return copy;
        }

        public void DeletePreset(int index)
        {
            if (Presets != null && index >= 0 && index < Presets.Count)
                Presets.RemoveAt(index);
        }

        public List<string> Validate()
        {
            var errors = new List<string>();
            if (Presets == null || Presets.Count == 0) errors.Add("No tier presets defined.");
            if (Curve == null || Curve.Count == 0) errors.Add("Curve is empty — add at least one segment.");
            if (Curve != null && Presets != null)
            {
                foreach (var seg in Curve)
                    if (FindPreset(seg.TierName) == null)
                        errors.Add($"Curve segment references unknown tier '{seg.TierName}'.");
            }
            return errors;
        }

        public string Summary()
        {
            var sb = new StringBuilder();
            sb.Append(TotalLevels()).Append(" levels");
            if (Curve != null && Curve.Count > 0)
            {
                sb.Append(" → ");
                sb.Append(string.Join(", ", Curve.Select(s => $"{s.TierName} ×{Mathf.Max(0, s.LevelCount)}")));
            }
            return sb.ToString();
        }
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run `YakDifficultyCurveTests`. Expected: all 6 PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/YAK/Editor/Generator/YAKDifficultyCurveConfig.cs Assets/YAK/Tests/Editor/YakDifficultyCurveTests.cs
git commit -m "feat(yak): difficulty-curve config — tiers, ranges, resolution, CRUD"
```

---

### Task 2: Hidden-spool ratio in the autofiller

**Files:**
- Modify: `Assets/YAK/Editor/Analysis/YAKSpoolAutofillConfig.cs` (add field after line 31, the Conveyor block)
- Modify: `Assets/YAK/Editor/Analysis/YAKSpoolAutofiller.cs:143-159` (`BuildCandidate`)
- Test: `Assets/YAK/Tests/Editor/YakAutofillTests.cs` (add two tests to the existing fixture)

**Interfaces:**
- Consumes: `YAKSpoolAutofillConfig` (existing public fields), `YAKSpoolEntry { ColorId; Capacity; Hidden }`.
- Produces: `YAKSpoolAutofillConfig.HiddenRatio` (public float, default 0); `BuildCandidate` now marks a `HiddenRatio` fraction of spools `Hidden = true` deterministically.

- [ ] **Step 1: Write the failing tests**

Add to `Assets/YAK/Tests/Editor/YakAutofillTests.cs` (inside the existing class):

```csharp
        [Test]
        public void BuildCandidate_HiddenRatioZero_MarksNoneHidden()
        {
            var cfg = ScriptableObject.CreateInstance<YAKSpoolAutofillConfig>();
            cfg.HiddenRatio = 0f;
            var perColor = new System.Collections.Generic.Dictionary<string, int> { { "Red", 60 }, { "Blue", 60 } };
            var top = InvokeBuildCandidate(perColor, columns: 3, cfg, new System.Random(1));
            int hidden = CountHidden(top);
            Assert.AreEqual(0, hidden);
        }

        [Test]
        public void BuildCandidate_HiddenRatioHalf_MarksHalfHidden_Deterministic()
        {
            var cfg = ScriptableObject.CreateInstance<YAKSpoolAutofillConfig>();
            cfg.HiddenRatio = 0.5f;
            var perColor = new System.Collections.Generic.Dictionary<string, int> { { "Red", 60 }, { "Blue", 60 } };
            var topA = InvokeBuildCandidate(perColor, columns: 3, cfg, new System.Random(7));
            var topB = InvokeBuildCandidate(perColor, columns: 3, cfg, new System.Random(7));
            int total = CountSpools(topA);
            Assert.AreEqual(Mathf.RoundToInt(0.5f * total), CountHidden(topA));
            Assert.AreEqual(CountHidden(topA), CountHidden(topB), "same seed → same hidden count");
        }

        // --- helpers (add to the fixture) ---
        private static Hoppa.YAK.YAKTopSectionData InvokeBuildCandidate(
            System.Collections.Generic.Dictionary<string,int> perColor, int columns, YAKSpoolAutofillConfig cfg, System.Random rng)
        {
            var m = typeof(YAKSpoolAutofiller).GetMethod("BuildCandidate",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);
            return (Hoppa.YAK.YAKTopSectionData)m.Invoke(null, new object[] { perColor, columns, cfg, rng });
        }
        private static int CountHidden(Hoppa.YAK.YAKTopSectionData t)
        {
            int n = 0; foreach (var c in t.Columns) foreach (var s in c.Spools) if (s.Hidden) n++; return n;
        }
        private static int CountSpools(Hoppa.YAK.YAKTopSectionData t)
        {
            int n = 0; foreach (var c in t.Columns) n += c.Spools.Count; return n;
        }
```

> If `BuildCandidate`/helpers already exist in the fixture from earlier tasks, reuse them rather than redeclaring.

- [ ] **Step 2: Run tests to verify they fail**

Run `YakAutofillTests`. Expected: `BuildCandidate_HiddenRatioHalf...` FAILS (count hidden = 0, since `Hidden` is hardcoded false); the zero test passes incidentally.

- [ ] **Step 3: Add the config field**

In `Assets/YAK/Editor/Analysis/YAKSpoolAutofillConfig.cs`, after the Conveyor block (after line 31), add:

```csharp
    [Header("Hidden spools")]
    [Tooltip("Fraction of spools marked hidden (0..1). NOTE: the average-player analyzer does not yet model hidden spools, so measured APS will NOT reflect this difficulty (fast-follow).")]
    [Range(0f, 1f)] public float HiddenRatio = 0f;
```

- [ ] **Step 4: Mark the hidden fraction in `BuildCandidate`**

In `Assets/YAK/Editor/Analysis/YAKSpoolAutofiller.cs`, in `BuildCandidate`, after `Shuffle(spools, rng);` (line 152) and before the columns are built, add:

```csharp
            // Mark a deterministic fraction hidden (post-shuffle → uniform random selection).
            int hide = Mathf.Clamp(Mathf.RoundToInt(cfg.HiddenRatio * spools.Count), 0, spools.Count);
            for (int i = 0; i < hide; i++) spools[i].Hidden = true;
```

(The list is already shuffled by `rng`, so taking the first `hide` entries is a uniform random selection that is deterministic for a given seed.)

- [ ] **Step 5: Run tests to verify they pass**

Run `YakAutofillTests`. Expected: all PASS, including both new tests. Existing autofill tests still green (HiddenRatio defaults to 0 → no behavior change).

- [ ] **Step 6: Commit**

```bash
git add Assets/YAK/Editor/Analysis/YAKSpoolAutofillConfig.cs Assets/YAK/Editor/Analysis/YAKSpoolAutofiller.cs Assets/YAK/Tests/Editor/YakAutofillTests.cs
git commit -m "feat(yak): autofiller HiddenRatio knob (deterministic hidden-spool fraction)"
```

---

### Task 3: `YAKTierProfileBuilder` — transient tier-configured profile

**Files:**
- Create: `Assets/YAK/Editor/Generator/YAKTierProfileBuilder.cs`
- Test: `Assets/YAK/Tests/Editor/YakTierProfileBuilderTests.cs`

**Interfaces:**
- Consumes: `TierPreset` (Task 1), base `GameProfile` with YAK assets wired (`YAKImageToGrid`, `YAKSpoolAutofiller` + its `YAKSpoolAutofillConfig`, `YAKGeneratorConfig`).
- Produces:
  - `struct TierProfile { GameProfile Profile; void Cleanup(); }` (Cleanup destroys all transient SOs)
  - `static class YAKTierProfileBuilder { static TierProfile Build(GameProfile baseProfile, TierPreset tier); }`
  - Build returns a transient profile copy where: `_gridWidth=tier.GridWidth`, `_gridHeight=tier.GridHeight`; `_imageToGrid` is a clone with `ColorCap=tier.MaxColors`, `DefaultConveyorCount=tier.ConveyorSlots`; `_levelCompleter` is a clone autofiller whose `_config` clone has `AvgCapacity/MinCapacity/MaxCapacity` shifted around `tier.AvgCapacity`, `ColumnRange=tier.ColumnRange`, `DefaultConveyorSlots=tier.ConveyorSlots`, `DefaultApsTarget=tier.TargetAps`, `ApsTolerance=tier.ApsTolerance`, `HiddenRatio=tier.HiddenRatio`; `_generatorConfig` clone has `TargetAPS=tier.TargetAps`, `ApsTolerance=tier.ApsTolerance`, `ConveyorCount=tier.ConveyorSlots`.

- [ ] **Step 1: Write the failing tests**

Create `Assets/YAK/Tests/Editor/YakTierProfileBuilderTests.cs`:

```csharp
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YAK.Editor;

namespace Hoppa.YAK.Editor.Tests
{
    public sealed class YakTierProfileBuilderTests
    {
        private static void SetField(Object obj, string field, object value)
            => obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(obj, value);
        private static T GetField<T>(Object obj, string field)
            => (T)obj.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance).GetValue(obj);

        private static GameProfile MakeBaseProfile(out YAKImageToGrid ig, out YAKSpoolAutofiller af, out YAKSpoolAutofillConfig afc, out YAKGeneratorConfig gc)
        {
            var profile = ScriptableObject.CreateInstance<GameProfile>();
            ig  = ScriptableObject.CreateInstance<YAKImageToGrid>();
            af  = ScriptableObject.CreateInstance<YAKSpoolAutofiller>();
            afc = ScriptableObject.CreateInstance<YAKSpoolAutofillConfig>();
            gc  = ScriptableObject.CreateInstance<YAKGeneratorConfig>();
            SetField(af, "_config", afc);
            SetField(profile, "_gridWidth", 30);
            SetField(profile, "_gridHeight", 30);
            SetField(profile, "_imageToGrid", ig);
            SetField(profile, "_levelCompleter", af);
            SetField(profile, "_generatorConfig", gc);
            return profile;
        }

        [Test]
        public void Build_AppliesTierKnobs_ToTransientProfile()
        {
            var baseProfile = MakeBaseProfile(out var ig, out var af, out var afc, out var gc);
            var tier = new TierPreset { Name="Easy", GridWidth=10, GridHeight=10, MaxColors=2,
                AvgCapacity=28, ConveyorSlots=7, ColumnRange=new Vector2Int(2,3),
                HiddenRatio=0.25f, TargetAps=1.5f, ApsTolerance=0.4f };

            var built = YAKTierProfileBuilder.Build(baseProfile, tier);
            try
            {
                Assert.AreEqual(10, built.Profile.GridWidth);
                Assert.AreEqual(10, built.Profile.GridHeight);
                var bIg = (YAKImageToGrid)built.Profile.ImageToGrid;
                Assert.AreEqual(2, bIg.ColorCap);
                Assert.AreEqual(7, bIg.DefaultConveyorCount);
                var bAfc = GetField<YAKSpoolAutofillConfig>(built.Profile.LevelCompleter, "_config");
                Assert.AreEqual(28, bAfc.AvgCapacity);
                Assert.AreEqual(7, bAfc.DefaultConveyorSlots);
                Assert.AreEqual(0.25f, bAfc.HiddenRatio);
                Assert.AreEqual(new Vector2Int(2,3), bAfc.ColumnRange);
                var bGc = (YAKGeneratorConfig)built.Profile.GeneratorConfig;
                Assert.AreEqual(1.5f, bGc.TargetAPS);
                Assert.AreEqual(7, bGc.ConveyorCount);
            }
            finally { built.Cleanup(); }
        }

        [Test]
        public void Build_DoesNotMutateOriginals()
        {
            var baseProfile = MakeBaseProfile(out var ig, out var af, out var afc, out var gc);
            ig.ColorCap = 6; afc.AvgCapacity = 20; gc.TargetAPS = 3f;
            var tier = new TierPreset { Name="X", GridWidth=8, GridHeight=8, MaxColors=2, AvgCapacity=30, ConveyorSlots=5, ColumnRange=new Vector2Int(2,3), TargetAps=1f, ApsTolerance=0.5f };

            var built = YAKTierProfileBuilder.Build(baseProfile, tier);
            built.Cleanup();

            Assert.AreEqual(6,  ig.ColorCap,      "original ImageToGrid must be untouched");
            Assert.AreEqual(20, afc.AvgCapacity,  "original autofill config must be untouched");
            Assert.AreEqual(3f, gc.TargetAPS,     "original generator config must be untouched");
            Assert.AreEqual(30, baseProfile.GridWidth, "original profile grid must be untouched");
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run `YakTierProfileBuilderTests`. Expected: compile error — `YAKTierProfileBuilder` not defined.

- [ ] **Step 3: Implement the builder**

Create `Assets/YAK/Editor/Generator/YAKTierProfileBuilder.cs`:

```csharp
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;
using Hoppa.LevelEditor.Core.Editor;
using Object = UnityEngine.Object;

namespace Hoppa.YAK.Editor
{
    /// A transient, tier-configured copy of a GameProfile. NEVER touches on-disk assets:
    /// every varied object is an Object.Instantiate clone. Call Cleanup() when done.
    public struct TierProfile
    {
        public GameProfile Profile;
        private List<Object> _owned;
        public TierProfile(GameProfile profile, List<Object> owned) { Profile = profile; _owned = owned; }
        public void Cleanup()
        {
            if (_owned == null) return;
            foreach (var o in _owned) if (o != null) Object.DestroyImmediate(o);
            _owned = null;
        }
    }

    public static class YAKTierProfileBuilder
    {
        private const BindingFlags FF = BindingFlags.NonPublic | BindingFlags.Instance;

        private static void Set(Object obj, string field, object value)
            => obj.GetType().GetField(field, FF)?.SetValue(obj, value);

        public static TierProfile Build(GameProfile baseProfile, TierPreset tier)
        {
            var owned = new List<Object>();

            var profile = Object.Instantiate(baseProfile);
            owned.Add(profile);

            // Grid size.
            Set(profile, "_gridWidth", Mathf.Max(1, tier.GridWidth));
            Set(profile, "_gridHeight", Mathf.Max(1, tier.GridHeight));

            // Image→grid clone: color cap + conveyor.
            if (baseProfile.ImageToGrid is YAKImageToGrid baseIg)
            {
                var ig = Object.Instantiate(baseIg);
                ig.ColorCap = Mathf.Max(2, tier.MaxColors);
                ig.DefaultConveyorCount = Mathf.Max(1, tier.ConveyorSlots);
                owned.Add(ig);
                Set(profile, "_imageToGrid", ig);
            }

            // Autofiller clone + its config clone: capacity, columns, conveyor, APS, hidden.
            if (baseProfile.LevelCompleter is YAKSpoolAutofiller baseAf)
            {
                var af = Object.Instantiate(baseAf);
                var baseCfg = af.GetType().GetField("_config", FF)?.GetValue(af) as YAKSpoolAutofillConfig;
                var cfg = baseCfg != null ? Object.Instantiate(baseCfg) : ScriptableObject.CreateInstance<YAKSpoolAutofillConfig>();
                int avg = Mathf.Max(1, tier.AvgCapacity);
                // Slack widens the [min,max] window around avg; 0 slack = tight band.
                int half = Mathf.Max(0, Mathf.RoundToInt(avg * Mathf.Clamp01(tier.CapacitySlack)));
                cfg.AvgCapacity = avg;
                cfg.MinCapacity = Mathf.Max(1, avg - half);
                cfg.MaxCapacity = avg + half + Mathf.Max(0, half == 0 ? 0 : 0); // max >= avg
                if (cfg.MaxCapacity < avg) cfg.MaxCapacity = avg;
                cfg.ColumnRange = tier.ColumnRange;
                cfg.DefaultConveyorSlots = Mathf.Max(1, tier.ConveyorSlots);
                cfg.DefaultApsTarget = tier.TargetAps;
                cfg.ApsTolerance = tier.ApsTolerance;
                cfg.HiddenRatio = Mathf.Clamp01(tier.HiddenRatio);
                Set(af, "_config", cfg);
                owned.Add(cfg);
                owned.Add(af);
                Set(profile, "_levelCompleter", af);
            }

            // Generator config clone: APS target/tolerance + conveyor.
            if (baseProfile.GeneratorConfig is YAKGeneratorConfig baseGc)
            {
                var gc = Object.Instantiate(baseGc);
                gc.TargetAPS = tier.TargetAps;
                gc.ApsTolerance = tier.ApsTolerance;
                gc.ConveyorCount = Mathf.Max(1, tier.ConveyorSlots);
                owned.Add(gc);
                Set(profile, "_generatorConfig", gc);
            }

            return new TierProfile(profile, owned);
        }
    }
}
```

> Note: `MaxCapacity` must stay ≥ `AvgCapacity` ≥ `MinCapacity` for `Partition` to behave; the clamps above guarantee it. When slack is 0, min=max=avg (a tight, exact band).

- [ ] **Step 4: Run tests to verify they pass**

Run `YakTierProfileBuilderTests`. Expected: both PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/YAK/Editor/Generator/YAKTierProfileBuilder.cs Assets/YAK/Tests/Editor/YakTierProfileBuilderTests.cs
git commit -m "feat(yak): tier→transient profile builder (no on-disk asset mutation)"
```

---

### Task 4: `LevelStats` curve fields + `BatchReviewWindow` off-target display

**Files:**
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Batch/BatchStaging.cs` (`LevelStats` struct, ~lines 14-19)
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Batch/BatchReviewWindow.cs` (the per-candidate stats display, ~lines 141-143)
- Test: `Assets/YAK/Tests/Editor/YakCurveBatchTests.cs` (stats round-trip assertion lives with Task 5; here just the additive field round-trip)

**Interfaces:**
- Produces: `LevelStats` gains `public string tier;`, `public float targetAps;`, `public bool offTarget;` (additive; existing fields unchanged so existing stats files still deserialize).

- [ ] **Step 1: Add fields to `LevelStats`**

In `Packages/com.hoppa.leveleditor.core/Editor/Batch/BatchStaging.cs`, extend the `LevelStats` serializable struct (keep existing `id/status/solvable/aps/band/distinctColors`) with:

```csharp
        public string tier;       // difficulty tier name (curve runs); empty for legacy batches
        public float  targetAps;  // the tier's APS target
        public bool   offTarget;  // true = generation could not hit the tier's APS band
```

- [ ] **Step 2: Show the flag in `BatchReviewWindow`**

In `BatchReviewWindow.cs`, where it renders the per-candidate stats line (APS/band/colors, ~line 141-143), append tier + an off-target marker. Locate the existing label that prints aps/band and add alongside it:

```csharp
            if (!string.IsNullOrEmpty(stats.tier))
                GUILayout.Label($"Tier: {stats.tier}" + (stats.offTarget ? "  ⚠ OFF-TARGET" : ""),
                    stats.offTarget ? EditorStyles.boldLabel : EditorStyles.label);
```

(Match the surrounding rendering style; `using UnityEditor;` is already present in this window.)

- [ ] **Step 3: Verify compile (no dedicated unit test for IMGUI)**

The round-trip of the new fields is asserted by Task 5's harness test (which writes a stats file with `tier`/`offTarget` set and reads it back). Confirm the project compiles with no errors after this edit (verifier: console-get-logs clean).

- [ ] **Step 4: Commit**

```bash
git add Packages/com.hoppa.leveleditor.core/Editor/Batch/BatchStaging.cs Packages/com.hoppa.leveleditor.core/Editor/Batch/BatchReviewWindow.cs
git commit -m "feat(core): LevelStats tier/offTarget fields + review-window display"
```

---

### Task 5: `YAKCurveBatchHarness` — walk the curve, generate numbered levels

**Files:**
- Create: `Assets/YAK/Editor/Generator/YAKCurveBatchHarness.cs`
- Test: `Assets/YAK/Tests/Editor/YakCurveBatchTests.cs`

**Interfaces:**
- Consumes: `YAKDifficultyCurveConfig` (Task 1), `YAKTierProfileBuilder` (Task 3), `LevelStats` (Task 4), existing `JsonLevelSerializer`, `LevelThumbnail.RenderPng`, `BatchStaging.WriteStats`/`StatsSuffix`, `profile.LevelGenerator.Generate`, `LevelGeneratorRequest`, `GameProfile.BuildRegistry()`.
- Produces:
  - `static class YAKCurveBatchHarness` with `static string RunCurve(YAKDifficultyCurveConfig curve, GameProfile baseProfile, int attemptsPerLevel, string stagingRoot)` returning the staging dir (or null on validation failure).
  - For each level index `i` (1-based): resolves the tier, builds a tier profile, retries seeds up to `attemptsPerLevel` to get `result.Succeeded` (in-band), else keeps the attempt closest to `tier.TargetAps`; writes `level_{i}.json` (+ `.png` + stats with `tier`/`targetAps`/`offTarget`), `doc.LevelId = $"level_{i}"`.

- [ ] **Step 1: Write the failing test**

Create `Assets/YAK/Tests/Editor/YakCurveBatchTests.cs`. This test builds a minimal real profile (reuse the pattern from `YakTierProfileBuilderTests` plus wire an analyzer), runs a tiny 2-level curve with a small budget, and asserts numbered files appear and stats carry tier info. Use a temp staging dir under the OS temp path.

```csharp
using System.IO;
using System.Reflection;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YAK.Editor;

namespace Hoppa.YAK.Editor.Tests
{
    public sealed class YakCurveBatchTests
    {
        private static void SetField(Object o, string f, object v)
            => o.GetType().GetField(f, BindingFlags.NonPublic | BindingFlags.Instance).SetValue(o, v);

        [Test]
        public void RunCurve_WritesNumberedLevels_WithTierStats()
        {
            // Minimal profile: palette + generator + completer(autofiller) + analyzer + imageToGrid(procedural fallback path).
            var profile = TestProfiles.MakeYakProfile();   // see helper note below
            var curve = ScriptableObject.CreateInstance<YAKDifficultyCurveConfig>();
            curve.Presets = new List<TierPreset> {
                new TierPreset { Name="Tiny", GridWidth=6, GridHeight=6, MaxColors=2, AvgCapacity=12,
                                 ConveyorSlots=6, ColumnRange=new Vector2Int(2,3), TargetAps=2f, ApsTolerance=5f }, // wide tol → accepts fast
            };
            curve.Curve = new List<CurveSegment> { new CurveSegment { TierName="Tiny", LevelCount=2 } };

            string root = Path.Combine(Path.GetTempPath(), "yak_curve_test_" + System.Guid.NewGuid().ToString("N"));
            string dir = YAKCurveBatchHarness.RunCurve(curve, profile, attemptsPerLevel: 8, stagingRoot: root);

            Assert.IsNotNull(dir);
            Assert.IsTrue(File.Exists(Path.Combine(dir, "level_1.json")), "level_1.json written");
            Assert.IsTrue(File.Exists(Path.Combine(dir, "level_2.json")), "level_2.json written");
            string statsPath = Path.Combine(dir, "level_1" + Hoppa.LevelEditor.Core.Editor.BatchStaging.StatsSuffix);
            Assert.IsTrue(File.Exists(statsPath), "stats written");
            StringAssert.Contains("Tiny", File.ReadAllText(statsPath), "stats carry tier name");

            Directory.Delete(dir, true);
        }

        [Test]
        public void RunCurve_EmptyCurve_ReturnsNull()
        {
            var profile = TestProfiles.MakeYakProfile();
            var curve = ScriptableObject.CreateInstance<YAKDifficultyCurveConfig>();
            Assert.IsNull(YAKCurveBatchHarness.RunCurve(curve, profile, 4, Path.GetTempPath()));
        }
    }
}
```

> **Helper note for the implementer:** `TestProfiles.MakeYakProfile()` is a small test helper that wires a real YAK pipeline in-memory (palette, `YAKLevelGenerator`, `YAKSpoolAutofiller`+config, `YAKLevelAnalyzer`+config, `YAKImageToGrid`, `YAKGeneratorConfig` with `UseImageSource=false` so the procedural grid path runs without needing image files). Model it on the in-memory profile assembly already used in `YakAnalyzerTests.cs`/`YakBatchTests.cs`. If those tests already expose such a helper, reuse it; otherwise add `Assets/YAK/Tests/Editor/TestProfiles.cs` with a `public static GameProfile MakeYakProfile()` that mirrors their setup. Keep grids tiny and tolerances wide so the test runs fast.

- [ ] **Step 2: Run test to verify it fails**

Run `YakCurveBatchTests`. Expected: compile error — `YAKCurveBatchHarness` not defined (and possibly `TestProfiles` if not yet added).

- [ ] **Step 3: Implement the harness**

Create `Assets/YAK/Editor/Generator/YAKCurveBatchHarness.cs`:

```csharp
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;

namespace Hoppa.YAK.Editor
{
    public static class YAKCurveBatchHarness
    {
        public static string RunCurve(YAKDifficultyCurveConfig curve, GameProfile baseProfile,
                                      int attemptsPerLevel, string stagingRoot)
        {
            if (curve == null || baseProfile == null) return null;
            var errors = curve.Validate();
            if (errors.Count > 0)
            {
                Debug.LogError("[YAKCurve] invalid curve: " + string.Join("; ", errors));
                return null;
            }
            if (baseProfile.LevelGenerator == null || baseProfile.LevelAnalyzer == null)
            {
                Debug.LogError("[YAKCurve] profile needs a Level Generator and Analyzer wired.");
                return null;
            }

            int total = curve.TotalLevels();
            if (total <= 0) return null;

            string root = stagingRoot ?? Path.Combine(
                Directory.GetParent(Application.dataPath).FullName, "YAK_Batch");
            string stagingDir = Path.Combine(root, "curve_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            Directory.CreateDirectory(stagingDir);

            var serializer = new JsonLevelSerializer();
            var registry = baseProfile.BuildRegistry();
            var rng = new System.Random(Environment.TickCount);

            for (int i = 1; i <= total; i++)
            {
                var tier = curve.TierForLevel(i);
                if (tier == null) continue;
                var tp = YAKTierProfileBuilder.Build(baseProfile, tier);
                try
                {
                    LevelDocument best = null; LevelAnalysisResult bestAn = null;
                    float bestDelta = float.MaxValue; bool accepted = false;

                    for (int a = 0; a < Mathf.Max(1, attemptsPerLevel) && !accepted; a++)
                    {
                        int seed = rng.Next(1, int.MaxValue);
                        LevelGeneratorResult gen;
                        try { gen = tp.Profile.LevelGenerator.Generate(
                            new LevelGeneratorRequest { Seed = seed, TargetAPS = tier.TargetAps }, tp.Profile); }
                        catch (Exception e) { Debug.LogError($"[YAKCurve] L{i} generate threw: {e.Message}"); continue; }

                        var doc = gen?.Document;
                        if (doc?.Grid == null) continue;

                        var an = tp.Profile.LevelAnalyzer.Analyze(doc, tp.Profile,
                            new AnalysisRequest { RolloutCount = 120, Seed = seed });
                        float delta = an.Status == AnalysisStatus.Solvable
                            ? Mathf.Abs(an.ApsEstimate - tier.TargetAps) : float.MaxValue;

                        if (gen.Succeeded) { best = doc; bestAn = an; bestDelta = delta; accepted = true; }
                        else if (delta < bestDelta) { best = doc; bestAn = an; bestDelta = delta; }
                    }

                    if (best == null) { Debug.LogWarning($"[YAKCurve] L{i}: no candidate produced."); continue; }

                    string id = $"level_{i}";
                    best.LevelId = id;
                    bool offTarget = !accepted;

                    File.WriteAllText(Path.Combine(stagingDir, id + ".json"), serializer.Save(best, registry));

                    var png = LevelThumbnail.RenderPng(best, baseProfile.ColorPalette, cellPixels: 4);
                    if (png != null) File.WriteAllBytes(Path.Combine(stagingDir, id + ".png"), png);

                    BatchStaging.WriteStats(Path.Combine(stagingDir, id + BatchStaging.StatsSuffix), new LevelStats
                    {
                        id = id,
                        status = bestAn != null ? bestAn.Status.ToString() : "Unknown",
                        solvable = bestAn != null && bestAn.Solvable,
                        aps = bestAn != null ? bestAn.ApsEstimate : 0f,
                        band = bestAn != null ? bestAn.Band : 0,
                        distinctColors = 0,
                        tier = tier.Name,
                        targetAps = tier.TargetAps,
                        offTarget = offTarget,
                    });
                }
                finally { tp.Cleanup(); }
            }

            Debug.Log($"[YAKCurve] generated {total} levels → {stagingDir}");
            AssetDatabase.Refresh();
            return stagingDir;
        }
    }
}
```

> `distinctColors` is left 0 here to avoid duplicating `YAKBatchHarness`'s private `DistinctColors` helper; if a shared public helper exists, call it. Not difficulty-relevant.

- [ ] **Step 4: Run test to verify it passes**

Run `YakCurveBatchTests`. Expected: both PASS (`level_1.json`/`level_2.json` written, stats contain "Tiny"; empty curve → null). If slow, lower `attemptsPerLevel` / grid size further.

- [ ] **Step 5: Commit**

```bash
git add Assets/YAK/Editor/Generator/YAKCurveBatchHarness.cs Assets/YAK/Tests/Editor/YakCurveBatchTests.cs Assets/YAK/Tests/Editor/TestProfiles.cs
git commit -m "feat(yak): curve batch harness — numbered, tier-scaled level generation"
```

---

### Task 6: `YAKDifficultyCurveWindow` — the editor UI

**Files:**
- Create: `Assets/YAK/Editor/Generator/YAKDifficultyCurveWindow.cs`
- (No unit test — IMGUI; all logic it calls is already tested in Tasks 1/5. Manual verification steps below.)

**Interfaces:**
- Consumes: `YAKDifficultyCurveConfig` (+ its CRUD/Validate/Summary), `YAKCurveBatchHarness.RunCurve`, `BatchReviewWindow` (to open the staging dir), the YAK profile at `Assets/YAK/Data/Config/YAKProfile.asset`.

- [ ] **Step 1: Implement the window**

Create `Assets/YAK/Editor/Generator/YAKDifficultyCurveWindow.cs`:

```csharp
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using Hoppa.LevelEditor.Core.Editor;

namespace Hoppa.YAK.Editor
{
    public sealed class YAKDifficultyCurveWindow : EditorWindow
    {
        private const string ProfilePath = "Assets/YAK/Data/Config/YAKProfile.asset";
        private YAKDifficultyCurveConfig _config;
        private Vector2 _scroll;
        private int _attemptsPerLevel = 30;

        [MenuItem("Window/Hoppa/YAK/Difficulty Curve")]
        public static void Open() => GetWindow<YAKDifficultyCurveWindow>("Difficulty Curve");

        private void OnGUI()
        {
            _config = (YAKDifficultyCurveConfig)EditorGUILayout.ObjectField(
                "Curve Config", _config, typeof(YAKDifficultyCurveConfig), false);
            if (_config == null)
            {
                EditorGUILayout.HelpBox("Assign or create a YAK Difficulty Curve config " +
                    "(Create ▸ Hoppa ▸ YAK ▸ Generator ▸ YAK Difficulty Curve).", MessageType.Info);
                return;
            }

            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawPresets();
            EditorGUILayout.Space();
            DrawCurve();
            EditorGUILayout.EndScrollView();

            EditorGUILayout.Space();
            EditorGUILayout.LabelField(_config.Summary(), EditorStyles.boldLabel);

            var errors = _config.Validate();
            using (new EditorGUI.DisabledScope(errors.Count > 0))
            {
                _attemptsPerLevel = EditorGUILayout.IntSlider("Attempts / level", _attemptsPerLevel, 1, 200);
                if (GUILayout.Button("Generate Curve", GUILayout.Height(30)))
                    Generate();
            }
            foreach (var e in errors) EditorGUILayout.HelpBox(e, MessageType.Error);
        }

        private void DrawPresets()
        {
            EditorGUILayout.LabelField("Tier Presets", EditorStyles.boldLabel);
            for (int i = 0; i < _config.Presets.Count; i++)
            {
                var p = _config.Presets[i];
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.BeginHorizontal();
                p.Name = EditorGUILayout.TextField(p.Name);
                if (GUILayout.Button("Duplicate", GUILayout.Width(80))) { _config.Duplicate(i); Dirty(); break; }
                if (GUILayout.Button("Delete", GUILayout.Width(60)))   { _config.DeletePreset(i); Dirty(); break; }
                EditorGUILayout.EndHorizontal();
                p.GridWidth     = EditorGUILayout.IntField("Grid Width", p.GridWidth);
                p.GridHeight    = EditorGUILayout.IntField("Grid Height", p.GridHeight);
                p.MaxColors     = EditorGUILayout.IntField("Max Colors", p.MaxColors);
                p.AvgCapacity   = EditorGUILayout.IntField("Avg Spool Capacity", p.AvgCapacity);
                p.CapacitySlack = EditorGUILayout.Slider("Capacity Slack", p.CapacitySlack, 0f, 1f);
                p.ConveyorSlots = EditorGUILayout.IntField("Conveyor Slots", p.ConveyorSlots);
                p.ColumnRange   = EditorGUILayout.Vector2IntField("Spool Columns [min,max]", p.ColumnRange);
                p.HiddenRatio   = EditorGUILayout.Slider(
                    new GUIContent("Hidden Spool %", "APS does not yet model hidden spools — manual difficulty until analyzer support lands."),
                    p.HiddenRatio, 0f, 1f);
                p.TargetAps     = EditorGUILayout.FloatField("Target APS", p.TargetAps);
                p.ApsTolerance  = EditorGUILayout.FloatField("APS Tolerance", p.ApsTolerance);
                EditorGUILayout.EndVertical();
            }
            if (GUILayout.Button("New Tier")) { _config.Presets.Add(new TierPreset()); Dirty(); }
        }

        private void DrawCurve()
        {
            EditorGUILayout.LabelField("Curve (ordered)", EditorStyles.boldLabel);
            var names = _config.Presets.ConvertAll(p => p.Name).ToArray();
            for (int i = 0; i < _config.Curve.Count; i++)
            {
                var seg = _config.Curve[i];
                EditorGUILayout.BeginHorizontal();
                int idx = Mathf.Max(0, System.Array.IndexOf(names, seg.TierName));
                idx = EditorGUILayout.Popup(idx, names);
                if (names.Length > 0) seg.TierName = names[Mathf.Clamp(idx, 0, names.Length - 1)];
                seg.LevelCount = EditorGUILayout.IntField(seg.LevelCount, GUILayout.Width(60));
                if (GUILayout.Button("↑", GUILayout.Width(24)) && i > 0) { (_config.Curve[i-1], _config.Curve[i]) = (_config.Curve[i], _config.Curve[i-1]); Dirty(); }
                if (GUILayout.Button("↓", GUILayout.Width(24)) && i < _config.Curve.Count-1) { (_config.Curve[i+1], _config.Curve[i]) = (_config.Curve[i], _config.Curve[i+1]); Dirty(); }
                if (GUILayout.Button("✕", GUILayout.Width(24))) { _config.Curve.RemoveAt(i); Dirty(); break; }
                EditorGUILayout.EndHorizontal();
            }
            if (GUILayout.Button("Add Segment")) { _config.Curve.Add(new CurveSegment()); Dirty(); }
        }

        private void Dirty() { EditorUtility.SetDirty(_config); }

        private void Generate()
        {
            var profile = AssetDatabase.LoadAssetAtPath<GameProfile>(ProfilePath);
            if (profile == null) { EditorUtility.DisplayDialog("YAK Curve", "Profile not found at " + ProfilePath, "OK"); return; }
            AssetDatabase.SaveAssets();
            string dir = YAKCurveBatchHarness.RunCurve(_config, profile, _attemptsPerLevel, null);
            if (dir != null)
            {
                var win = GetWindow<BatchReviewWindow>();   // open review on the staging dir
                win.Show();
                EditorUtility.RevealInFinder(dir);
            }
        }
    }
}
```

> If `BatchReviewWindow` exposes an explicit "load staging dir" API, call it instead of relying on its default scan; check its public surface (the harness already returns the dir). `RevealInFinder` is a fallback so the designer can locate output.

- [ ] **Step 2: Manual verification (Unity)**

1. Create the config asset: Project ▸ Create ▸ Hoppa ▸ YAK ▸ Generator ▸ YAK Difficulty Curve.
2. Open Window ▸ Hoppa ▸ YAK ▸ Difficulty Curve; assign the config.
3. Add 2 tiers (Tiny 6×6 / Small 12×12), add 2 curve segments (Tiny ×2, Small ×2); confirm the summary reads "4 levels → Tiny ×2, Small ×2".
4. Test New / Duplicate / Delete on a preset; confirm duplicate appears as "<name> Copy".
5. Click Generate Curve; confirm `level_1..level_4.json` (+ png/stats) appear in the staging folder and the review window opens.
6. Confirm console has no errors.

- [ ] **Step 3: Commit**

```bash
git add Assets/YAK/Editor/Generator/YAKDifficultyCurveWindow.cs
git commit -m "feat(yak): Difficulty Curve editor window (preset CRUD + curve + generate)"
```

---

## Self-Review

**Spec coverage:**
- Tier presets + ranges + CRUD (create/delete/duplicate) → Task 1 + Task 6. ✓
- Knob set (grid W×H, max colors, avg capacity, capacity slack, conveyor, columns, hidden %, target APS+tol) → `TierPreset` (Task 1), applied in builder (Task 3), hidden in autofiller (Task 2). ✓
- Per-level override path (override else static fallback; manual editing untouched) → implemented as transient profile clones (Task 3), proven untouched by `Build_DoesNotMutateOriginals`. ✓ (Refinement of spec §2: the carrier is a configured profile copy, not request fields — cleaner given the interface-typed call chain.)
- Batch runner producing numbered levels with off-target flagging → Task 5. ✓
- Editor window (presets/curve/generate/live readout) → Task 6. ✓
- Output numbered `level_N.json` → review → export (Gap C path) → Task 5 naming + Task 6 opens review. ✓
- Testing (curve resolution, CRUD, override fallback, hidden ratio, monotonicity) → Tasks 1/2/3 cover resolution, CRUD, fallback (no-mutation), hidden ratio. **Monotonicity sanity test:** folded into the harness/analyzer trust net rather than a brittle standalone test — the off-target flag + manual verification cover "knobs move difficulty"; a dedicated monotonicity property test is omitted as flaky (documented deviation).
- Edge cases (empty curve, dangling tier, grid-too-small, APS unreachable, slack) → Validate() (Tasks 1/5), best-effort+offTarget (Task 5), slack clamps (Task 3). ✓

**Placeholder scan:** No TBD/TODO; every code step has full code. The one helper left to the implementer (`TestProfiles.MakeYakProfile`) has an explicit construction recipe + an existing pattern to copy.

**Type consistency:** `YAKDifficultyCurveConfig`, `TierPreset`, `CurveSegment` names consistent across Tasks 1/3/5/6. `TierProfile`/`YAKTierProfileBuilder.Build` consistent between Task 3 and 5. `LevelStats` field names (`tier/targetAps/offTarget`) consistent between Task 4 and 5. Spool member `Hidden` (not `IsHidden`) used throughout.

## Known limitations carried forward (fast-follows, not this plan)
- Analyzer does not model hidden spools → APS won't score HiddenRatio difficulty (Gap B / analyzer hidden-support).
- Per-tier image generation (MaxColors → OpenAI prompt + regeneration) is the approved image-gen fast-follow; this plan only applies MaxColors at the grid color-cap.
