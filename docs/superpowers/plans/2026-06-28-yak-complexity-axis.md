# YAK Complexity Axis Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a Complexity 1–10 difficulty dial that controls the click-pattern variety of generated YAK levels, constructed pattern-first and gated on the *measured* complexity of average-player playouts (mirroring how APS is measured-and-gated).

**Architecture:** A new pure helper (`YakClickPattern`) both (a) scores the click-pattern complexity of a tap sequence (1–10) and (b) builds a target tap pattern for a desired complexity honoring the boss's rules R25–R31. The autofiller assigns spools to columns by walking that pattern instead of round-robin. The average-player simulator records the tap sequence of winning runs and reports their mean complexity; the autofiller gates candidates on solvable AND APS-in-band AND complexity-in-band.

**Tech Stack:** Unity 6 (Editor), C#, Newtonsoft.Json, NUnit (EditMode tests). Engine-agnostic sim code is plain C# under `Assets/YAK/Runtime/Sim/`.

## Global Constraints

- Write all `.cs` files directly with Write/Edit (never via PowerShell) — copied from project memory `feedback_write_files_directly`.
- No absolute paths in committed assets/configs — project-relative only.
- All **185** existing EditMode tests must stay green; APS-only callers (no complexity requested) must behave byte-identically to today.
- Sim code (`Assets/YAK/Runtime/Sim/`) is plain C# — no `UnityEditor` references there. `Mathf`/`UnityEngine` math is allowed (the sim already uses `System.Random`; prefer `System.Math`/manual clamp to keep it dependency-light, but `UnityEngine.Mathf` is acceptable as the existing analyzer/config code uses it).
- **R27 ambiguity (resolved):** the boss's `maxRepeat` formula and his written table disagree at C3 and C8. We implement his **table** (C1–2⇒2, C3–5⇒3, C6–8⇒4, C9–10⇒5) as the source of truth, documented in a code comment. Flag to the lead for boss confirmation.
- Complexity is **measured-but-uncalibrated**, same honesty stance as APS — never present it as ground truth.

---

### Task 1: `YakClickPattern.MaxRepeat` + `Score` (pure metric)

The scoring half of the helper: map a tap sequence to a 1–10 complexity, and expose the R27 max-run table. No level/grid knowledge — pure list math.

**Files:**
- Create: `Assets/YAK/Runtime/Sim/YakClickPattern.cs`
- Test: `Assets/YAK/Tests/Editor/YakClickPatternTests.cs`

**Interfaces:**
- Consumes: nothing (leaf).
- Produces:
  - `public static int YakClickPattern.MaxRepeat(int complexity)` → max consecutive same-column run for a complexity (1–10), per the boss's table.
  - `public static float YakClickPattern.Score(System.Collections.Generic.IReadOnlyList<int> taps, int numColumns)` → complexity in [1,10]. Pure round-robin / trivial → ~1; long-run + jumpy → ~10.

- [ ] **Step 1: Write the failing tests**

Create `Assets/YAK/Tests/Editor/YakClickPatternTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using Hoppa.YAK.Sim;

namespace Hoppa.YAK.Tests
{
    public class YakClickPatternTests
    {
        [TestCase(1, 2)] [TestCase(2, 2)]
        [TestCase(3, 3)] [TestCase(4, 3)] [TestCase(5, 3)]
        [TestCase(6, 4)] [TestCase(7, 4)] [TestCase(8, 4)]
        [TestCase(9, 5)] [TestCase(10, 5)]
        public void MaxRepeat_MatchesBossTable(int complexity, int expected)
            => Assert.AreEqual(expected, YakClickPattern.MaxRepeat(complexity));

        [Test]
        public void MaxRepeat_ClampsOutOfRange()
        {
            Assert.AreEqual(2, YakClickPattern.MaxRepeat(0));
            Assert.AreEqual(5, YakClickPattern.MaxRepeat(99));
        }

        [Test]
        public void Score_PureRoundRobin_IsMinimal()
        {
            var taps = new List<int> { 0, 1, 2, 0, 1, 2, 0, 1, 2 };
            float s = YakClickPattern.Score(taps, 3);
            Assert.LessOrEqual(s, 2.0f, "pure round-robin must read as minimal complexity");
        }

        [Test]
        public void Score_LongRunsAndJumps_IsHigh()
        {
            // long same-column runs + non-successor jumps
            var taps = new List<int> { 0, 0, 0, 2, 2, 2, 0, 0, 2, 2 };
            float s = YakClickPattern.Score(taps, 3);
            Assert.GreaterOrEqual(s, 6.0f, "long runs + jumps must read as high complexity");
        }

        [Test]
        public void Score_AlwaysInRange_AndDegenerateInputs()
        {
            Assert.AreEqual(1f, YakClickPattern.Score(new List<int>(), 3));
            Assert.AreEqual(1f, YakClickPattern.Score(new List<int> { 0 }, 3));
            Assert.AreEqual(1f, YakClickPattern.Score(new List<int> { 0, 0, 0 }, 1)); // 1 column → no choice
            float s = YakClickPattern.Score(new List<int> { 0, 2, 1, 2, 0 }, 3);
            Assert.That(s, Is.InRange(1f, 10f));
        }

        [Test]
        public void Score_MonotoneOnConstructedRamp()
        {
            // round-robin < mild-deviation < runs+jumps
            var rr   = new List<int> { 0, 1, 2, 0, 1, 2 };
            var mild = new List<int> { 0, 2, 1, 0, 2, 1 };           // non-successor, runs of 1
            var hard = new List<int> { 0, 0, 2, 2, 1, 1 };           // runs of 2 + jumps
            float a = YakClickPattern.Score(rr, 3);
            float b = YakClickPattern.Score(mild, 3);
            float c = YakClickPattern.Score(hard, 3);
            Assert.Less(a, c, "round-robin must score below runs+jumps");
            Assert.LessOrEqual(a, b, "round-robin must not exceed mild-deviation");
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail**

Run EditMode tests filtered to `YakClickPatternTests` (Unity Test Runner, or `tests-run` MCP with class filter `YakClickPatternTests`).
Expected: FAIL — `YakClickPattern` does not exist (compile error / missing type).

- [ ] **Step 3: Implement `MaxRepeat` + `Score`**

Create `Assets/YAK/Runtime/Sim/YakClickPattern.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace Hoppa.YAK.Sim
{
    // Click-pattern complexity helper (engine-agnostic, plain C#).
    //
    // Two responsibilities:
    //   • Score(taps, K)  — measure how "complex" a column-tap sequence is, 1..10.
    //   • Build(K, n, C)  — construct a target tap pattern for a desired complexity
    //                       (see Task 2), honoring the boss's rules R25–R31.
    //
    // Complexity is a property of the click SEQUENCE: pure round-robin (1,2,3,1,2,3)
    // is the trivial minimum; long same-column runs plus unpredictable jumps are the
    // maximum. Two blended signals: deviation-from-round-robin and max-run-length.
    public static class YakClickPattern
    {
        // R27: max consecutive taps on the same column for a complexity level.
        // NOTE: the boss's written TABLE (used here) and his formula
        // `max(2, min(5, 1 + C/2))` disagree at C3 and C8; we follow the table.
        public static int MaxRepeat(int complexity)
        {
            int c = complexity < 1 ? 1 : (complexity > 10 ? 10 : complexity);
            if (c <= 2) return 2;
            if (c <= 5) return 3;
            if (c <= 8) return 4;
            return 5;
        }

        // Complexity of a tap sequence in [1,10]. Degenerate inputs → 1.
        public static float Score(IReadOnlyList<int> taps, int numColumns)
        {
            if (taps == null || taps.Count <= 1 || numColumns <= 1) return 1f;
            int n = taps.Count;

            // (1) Max consecutive same-column run, normalized: run 1 → 0, run ≥5 → 1.
            int maxRun = 1, run = 1;
            for (int i = 1; i < n; i++)
            {
                run = taps[i] == taps[i - 1] ? run + 1 : 1;
                if (run > maxRun) maxRun = run;
            }
            float runScore = Clamp01((maxRun - 1) / 4f);

            // (2) Deviation from the pure round-robin successor (prev+1)%K:
            // 0 = exact round-robin, 1 = never the successor.
            int deviations = 0;
            for (int i = 1; i < n; i++)
                if (taps[i] != (taps[i - 1] + 1) % numColumns) deviations++;
            float rrScore = (float)deviations / (n - 1);

            float c01 = 0.6f * rrScore + 0.4f * runScore;
            float mapped = 1f + 9f * c01;
            return mapped < 1f ? 1f : (mapped > 10f ? 10f : mapped);
        }

        private static float Clamp01(float v) => v < 0f ? 0f : (v > 1f ? 1f : v);
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run the `YakClickPatternTests` class.
Expected: PASS (all cases). If `Score_MonotoneOnConstructedRamp` or the high/low thresholds are borderline, adjust the blend weights (`0.6/0.4`) — the **ordering** assertions are the contract; exact magnitudes are tunable.

- [ ] **Step 5: Commit**

```bash
git add Assets/YAK/Runtime/Sim/YakClickPattern.cs Assets/YAK/Tests/Editor/YakClickPatternTests.cs
git commit -m "feat(yak): click-pattern complexity metric + R27 max-repeat table"
```

---

### Task 2: `YakClickPattern.Build` (pattern-first constructor)

The construction half: given columns/spool-count/complexity, build a tap pattern with the right variety. Pattern-first (R26), per-column quota (R28), complexity-scaled selection (R28–R31), never pure round-robin (R25).

**Files:**
- Modify: `Assets/YAK/Runtime/Sim/YakClickPattern.cs` (add `Build`)
- Test: `Assets/YAK/Tests/Editor/YakClickPatternTests.cs` (add cases)

**Interfaces:**
- Consumes: `YakClickPattern.MaxRepeat`, `YakClickPattern.Score` (Task 1).
- Produces: `public static int[] YakClickPattern.Build(int numColumns, int numSpools, int complexity, System.Random rng)` → tap pattern of length `numSpools`; entry `k` = the column the k-th spool is assigned to.

- [ ] **Step 1: Write the failing tests**

Append to `YakClickPatternTests.cs` (inside the class):

```csharp
        private static int[] ColumnCounts(int[] pattern, int k)
        {
            var counts = new int[k];
            foreach (int c in pattern) counts[c]++;
            return counts;
        }

        [Test]
        public void Build_HasExactLength_AndBalancedQuota()
        {
            var rng = new System.Random(123);
            int[] p = YakClickPattern.Build(numColumns: 3, numSpools: 10, complexity: 5, rng);
            Assert.AreEqual(10, p.Length);
            var counts = ColumnCounts(p, 3);
            foreach (int c in counts) Assert.That(c, Is.InRange(2, 4)); // 10/3 ≈ 3 ± 1
            Assert.AreEqual(10, counts[0] + counts[1] + counts[2]);
        }

        [Test]
        public void Build_NeverPureRoundRobin_EvenAtComplexity1()
        {
            for (int seed = 1; seed <= 20; seed++)
            {
                var rng = new System.Random(seed);
                int[] p = YakClickPattern.Build(3, 9, 1, rng);
                bool isRr = true;
                for (int i = 1; i < p.Length; i++)
                    if (p[i] != (p[i - 1] + 1) % 3) { isRr = false; break; }
                Assert.IsFalse(isRr, $"seed {seed}: pattern must never be pure round-robin (R25)");
            }
        }

        [Test]
        public void Build_ComplexityRaisesMeasuredScore()
        {
            // Averaged over seeds, higher complexity → higher measured Score.
            float lowSum = 0f, highSum = 0f; int trials = 25;
            for (int seed = 1; seed <= trials; seed++)
            {
                lowSum  += YakClickPattern.Score(YakClickPattern.Build(4, 20, 1,  new System.Random(seed)), 4);
                highSum += YakClickPattern.Score(YakClickPattern.Build(4, 20, 10, new System.Random(seed)), 4);
            }
            Assert.Less(lowSum / trials, highSum / trials,
                "mean measured complexity at C=10 must exceed C=1");
        }

        [Test]
        public void Build_DegenerateInputs()
        {
            Assert.AreEqual(0, YakClickPattern.Build(3, 0, 5, new System.Random(1)).Length);
            int[] oneCol = YakClickPattern.Build(1, 5, 9, new System.Random(1));
            Assert.AreEqual(5, oneCol.Length);
            foreach (int c in oneCol) Assert.AreEqual(0, c); // only column available
        }
```

- [ ] **Step 2: Run tests to verify they fail**

Run `YakClickPatternTests`.
Expected: FAIL — `Build` not defined.

- [ ] **Step 3: Implement `Build`**

Add to `YakClickPattern` (in `YakClickPattern.cs`):

```csharp
        // Build a tap pattern of length `numSpools` over `numColumns`, with variety
        // scaled by complexity (1..10). Each column gets ≈ numSpools/numColumns taps
        // (±1, R28). Low C → near round-robin with short runs; high C → long runs
        // (up to MaxRepeat) and non-successor jumps (R28–R31). Never the pure
        // round-robin cycle (R25).
        public static int[] Build(int numColumns, int numSpools, int complexity, Random rng)
        {
            int k = numColumns < 1 ? 1 : numColumns;
            int n = numSpools < 0 ? 0 : numSpools;
            var result = new int[n];
            if (n == 0) return result;
            if (k == 1) return result; // all zeros — only column

            rng ??= new Random();
            int maxRepeat = MaxRepeat(complexity);
            float c01 = Clamp01((complexity - 1) / 9f);

            // Per-column quota: n/k each, first (n % k) columns get one extra (R28).
            var quota = new int[k];
            for (int c = 0; c < k; c++) quota[c] = n / k;
            for (int r = 0; r < n % k; r++) quota[r]++;

            int prev = -1, prevRun = 0;
            var weights = new double[k];
            for (int i = 0; i < n; i++)
            {
                double total = 0;
                for (int c = 0; c < k; c++)
                {
                    weights[c] = 0;
                    if (quota[c] <= 0) continue;
                    double w = quota[c]; // weighted-random by remaining quota (R28/R29)
                    if (c == prev)
                    {
                        if (prevRun >= maxRepeat) continue;       // soft run cap
                        w *= Lerp(0.15, 2.5, c01);                // high C favors runs
                    }
                    else if (c == (prev + 1) % k)
                    {
                        w *= Lerp(1.6, 0.35, c01);                // high C avoids the obvious successor
                    }
                    else
                    {
                        w *= Lerp(0.5, 1.3, c01);                 // high C favors distant jumps
                    }
                    weights[c] = w;
                    total += w;
                }

                int pick;
                if (total <= 0)
                {
                    // Only the capped `prev` has quota left → forced (quota exhaustion).
                    pick = prev;
                }
                else
                {
                    double roll = rng.NextDouble() * total;
                    pick = k - 1;
                    for (int c = 0; c < k; c++) { roll -= weights[c]; if (roll <= 0) { pick = c; break; } }
                }

                result[i] = pick;
                quota[pick]--;
                prevRun = pick == prev ? prevRun + 1 : 1;
                prev = pick;
            }

            // R25 guard: if construction still landed on the exact round-robin cycle,
            // swap two non-adjacent positions of different columns to break it.
            if (IsPureRoundRobin(result, k))
                PerturbSwap(result, rng);

            return result;
        }

        private static bool IsPureRoundRobin(int[] p, int k)
        {
            if (p.Length < 2) return false;
            for (int i = 1; i < p.Length; i++)
                if (p[i] != (p[i - 1] + 1) % k) return false;
            return true;
        }

        private static void PerturbSwap(int[] p, Random rng)
        {
            // Find any two positions with different columns at least 2 apart and swap.
            for (int a = 0; a < p.Length; a++)
                for (int b = a + 2; b < p.Length; b++)
                    if (p[a] != p[b]) { (p[a], p[b]) = (p[b], p[a]); return; }
        }

        private static double Lerp(double a, double b, double t) => a + (b - a) * t;
```

- [ ] **Step 4: Run tests to verify they pass**

Run `YakClickPatternTests`. Expected: PASS. If `Build_ComplexityRaisesMeasuredScore` is flaky/too tight, raise `trials` or widen the Lerp spread (e.g. run-bias `2.5`→`3.0`); the mean-ordering is the contract.

- [ ] **Step 5: Commit**

```bash
git add Assets/YAK/Runtime/Sim/YakClickPattern.cs Assets/YAK/Tests/Editor/YakClickPatternTests.cs
git commit -m "feat(yak): pattern-first tap-pattern builder (R25–R31)"
```

---

### Task 3: Average-player records & scores winning-run complexity

Extend the Monte-Carlo player to optionally capture each winning run's tap sequence, score it via `YakClickPattern.Score`, and report the mean over winning runs. Off by default → APS-only callers unaffected.

**Files:**
- Modify: `Assets/YAK/Runtime/Sim/YakAveragePlayer.cs`
- Test: `Assets/YAK/Tests/Editor/YakAveragePlayerComplexityTests.cs` (new)

**Interfaces:**
- Consumes: `YakClickPattern.Score` (Task 1); existing `YakLevelModel`, `YakSimState`.
- Produces (additions to `YakAveragePlayer`):
  - `Config.MeasureComplexity` (bool) — when true, winning runs record taps and are scored.
  - `Result.ComplexityEstimate` (float) — mean `Score` over winning runs (0 when none won / not measured).
  - `Result.ComplexitySamples` (int) — number of winning runs scored.

- [ ] **Step 1: Write the failing test**

Create `Assets/YAK/Tests/Editor/YakAveragePlayerComplexityTests.cs`:

```csharp
using NUnit.Framework;
using Hoppa.YAK.Sim;

namespace Hoppa.YAK.Tests
{
    public class YakAveragePlayerComplexityTests
    {
        // Trivially-solvable single-color level: one column, wool clears in order.
        private static YakLevelModel TrivialModel()
        {
            // 2x1 grid, single color 0; one spool column with one spool capacity 2.
            var grid = new int[][] { new[] { 0 }, new[] { 0 } }; // 2 columns, 1 tall each
            var spoolColors = new int[][] { new[] { 0 } };
            var spoolCaps   = new int[][] { new[] { 2 } };
            return YakLevelModel.FromArrays(grid, spoolColors, spoolCaps, conveyorSlots: 3, numColors: 1,
                colorNames: new[] { "c0" });
        }

        [Test]
        public void Estimate_WithoutMeasure_LeavesComplexityZero()
        {
            var m = TrivialModel();
            var r = new YakAveragePlayer().Estimate(m,
                new YakAveragePlayer.Config { Runs = 50, Seed = 7, MeasureComplexity = false });
            Assert.AreEqual(0f, r.ComplexityEstimate);
            Assert.AreEqual(0, r.ComplexitySamples);
        }

        [Test]
        public void Estimate_WithMeasure_ReportsInRangeComplexityOnWins()
        {
            var m = TrivialModel();
            var r = new YakAveragePlayer().Estimate(m,
                new YakAveragePlayer.Config { Runs = 50, Seed = 7, MeasureComplexity = true });
            Assert.Greater(r.ComplexitySamples, 0, "trivial level should win some runs");
            Assert.That(r.ComplexityEstimate, Is.InRange(1f, 10f));
        }
    }
}
```

> **Note:** this test calls a helper `YakLevelModel.FromArrays(...)`. If `YakLevelModel` has no array constructor, add a minimal internal test factory in this task (see Step 3a). Inspect `YakLevelModel.cs` first and reuse its existing build path if one fits; only add a factory if needed.

- [ ] **Step 2: Run test to verify it fails**

Run `YakAveragePlayerComplexityTests`.
Expected: FAIL — `Config.MeasureComplexity` / `Result.ComplexityEstimate` (and possibly `FromArrays`) don't exist.

- [ ] **Step 3a: (If needed) add a test factory to `YakLevelModel`**

Read `Assets/YAK/Runtime/Sim/YakLevelModel.cs`. If it lacks a direct array factory, add one mirroring its fields (only if no existing constructor fits the test):

```csharp
// Test/utility factory: build a model directly from column arrays.
public static YakLevelModel FromArrays(int[][] gridCols, int[][] spoolColors, int[][] spoolCaps,
                                       int conveyorSlots, int numColors, string[] colorNames)
{
    // Populate the same fields Build(...) sets. Match existing field names exactly.
}
```

Keep it consistent with the real `Build` (field names, `TotalWool`, `TotalSpools`, `Columns`, `Width`). If `Build` is easy to drive from arrays instead, prefer reusing it and delete this note.

- [ ] **Step 3b: Extend `YakAveragePlayer`**

In `Config` struct, add field:

```csharp
            public bool  MeasureComplexity; // when true, score winning-run click patterns
```

In `Result` struct, add fields:

```csharp
            public float ComplexityEstimate; // mean YakClickPattern.Score over winning runs (0 = none/not measured)
            public int   ComplexitySamples;  // winning runs scored
```

In `Estimate`, accumulate over winning runs. Change `Playout` to optionally collect the tap list and return it on a win. Concretely:

```csharp
            double complexitySum = 0; int complexitySamples = 0;
            var taps = cfg.MeasureComplexity ? new System.Collections.Generic.List<int>(model.TotalSpools) : null;

            int wins = 0;
            for (int r = 0; r < runs; r++)
            {
                var rng = new Random(unchecked(seed * 1000003 + r));
                taps?.Clear();
                if (Playout(model, eps, lookahead, rng, taps))
                {
                    wins++;
                    if (taps != null && taps.Count > 0)
                    {
                        complexitySum += YakClickPattern.Score(taps, model.Columns);
                        complexitySamples++;
                    }
                }
            }
```

Add the scored fields to the returned `Result`:

```csharp
                ComplexityEstimate = complexitySamples > 0 ? (float)(complexitySum / complexitySamples) : 0f,
                ComplexitySamples  = complexitySamples,
```

Change `Playout` signature to `private static bool Playout(YakLevelModel model, float eps, int lookahead, Random rng, System.Collections.Generic.List<int> taps)` and record each chosen column: after `s.ApplyMove(chosen);` add `taps?.Add(chosen);`. All existing `Playout(...)` callers pass `null`.

- [ ] **Step 4: Run tests to verify they pass**

Run `YakAveragePlayerComplexityTests` AND the existing `YakAnalyzerTests` (to confirm APS path unchanged).
Expected: PASS for both.

- [ ] **Step 5: Commit**

```bash
git add Assets/YAK/Runtime/Sim/YakAveragePlayer.cs Assets/YAK/Runtime/Sim/YakLevelModel.cs Assets/YAK/Tests/Editor/YakAveragePlayerComplexityTests.cs
git commit -m "feat(yak): average player measures winning-run click complexity (opt-in)"
```

---

### Task 4: Surface complexity through result + request + analyzer

Add the carrier fields to the generic Layer-1 result/request and copy the measured complexity in `YAKLevelAnalyzer` when requested.

**Files:**
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Analysis/LevelAnalysisResult.cs`
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Analysis/AnalysisRequest.cs`
- Modify: `Assets/YAK/Editor/Analysis/YAKLevelAnalyzer.cs`
- Test: `Assets/YAK/Tests/Editor/YakAnalyzerComplexityTests.cs` (new)

**Interfaces:**
- Consumes: `YakAveragePlayer.Config.MeasureComplexity`, `Result.ComplexityEstimate` (Task 3).
- Produces:
  - `LevelAnalysisResult.ComplexityEstimate` (float, 0 = not computed).
  - `AnalysisRequest.MeasureComplexity` (bool, default false).
  - `YAKLevelAnalyzer.Analyze` populates `result.ComplexityEstimate` when `req.MeasureComplexity` and a win is possible.

- [ ] **Step 1: Write the failing test**

Create `Assets/YAK/Tests/Editor/YakAnalyzerComplexityTests.cs`. Reuse the existing analyzer-test setup pattern (look at `YakAnalyzerTests.cs` for how it builds a `LevelDocument` + `GameProfile`):

```csharp
using NUnit.Framework;
using Hoppa.LevelEditor.Core.Editor;

namespace Hoppa.YAK.Tests
{
    public class YakAnalyzerComplexityTests
    {
        [Test]
        public void Analyze_WithoutFlag_LeavesComplexityZero()
        {
            var (doc, profile) = YakAnalyzerTestFixtures.SolvableSmallLevel();
            var analyzer = YakAnalyzerTestFixtures.Analyzer();
            var r = analyzer.Analyze(doc, profile, new AnalysisRequest { RolloutCount = 60, Seed = 5 });
            Assert.AreEqual(0f, r.ComplexityEstimate);
        }

        [Test]
        public void Analyze_WithFlag_PopulatesComplexityForSolvable()
        {
            var (doc, profile) = YakAnalyzerTestFixtures.SolvableSmallLevel();
            var analyzer = YakAnalyzerTestFixtures.Analyzer();
            var r = analyzer.Analyze(doc, profile,
                new AnalysisRequest { RolloutCount = 60, Seed = 5, MeasureComplexity = true });
            Assert.IsTrue(r.Solvable);
            Assert.That(r.ComplexityEstimate, Is.InRange(1f, 10f));
        }
    }
}
```

> If `YakAnalyzerTests.cs` builds its doc/profile inline rather than via a shared fixture, **extract the shared builder** into a small `YakAnalyzerTestFixtures` static (new file `Assets/YAK/Tests/Editor/YakAnalyzerTestFixtures.cs`) with `SolvableSmallLevel()` and `Analyzer()`, and have the new test use it. Do not duplicate the inline setup. If a fixture already exists, reuse it and drop this note.

- [ ] **Step 2: Run test to verify it fails**

Run `YakAnalyzerComplexityTests`.
Expected: FAIL — `AnalysisRequest.MeasureComplexity` / `LevelAnalysisResult.ComplexityEstimate` undefined.

- [ ] **Step 3: Add fields + wire the analyzer**

In `LevelAnalysisResult.cs`, after the APS block (`public bool ApsCalibrated;`):

```csharp
        // Measured click-pattern complexity (1..10) of average-player winning runs.
        // 0 = not computed (request did not set MeasureComplexity, or no win). Like
        // APS this is MEASURED-but-uncalibrated — do not present as ground truth.
        public float ComplexityEstimate;
```

In `LevelAnalysisResult.ToString()`, after the APS append block:

```csharp
            if (ComplexityEstimate > 0f)
                sb.Append(" · cplx ").Append(ComplexityEstimate.ToString("0.0"));
```

In `AnalysisRequest.cs`, add:

```csharp
        // When true the analyzer also measures the click-pattern complexity of the
        // average player's winning runs (LevelAnalysisResult.ComplexityEstimate).
        // Costs nothing extra for callers that leave it false.
        public bool MeasureComplexity = false;
```

In `YAKLevelAnalyzer.Analyze`, inside the measured-APS block, set the flag on the player config and copy the result. Change the `player.Estimate(...)` config to include `MeasureComplexity = req != null && req.MeasureComplexity,` and after `result.Band = cfg.BandFor(aps);` add:

```csharp
                    result.ComplexityEstimate = est.ComplexityEstimate;
```

- [ ] **Step 4: Run tests to verify they pass**

Run `YakAnalyzerComplexityTests` + `YakAnalyzerTests`.
Expected: PASS (no regression in existing analyzer tests).

- [ ] **Step 5: Commit**

```bash
git add Packages/com.hoppa.leveleditor.core/Editor/Analysis/LevelAnalysisResult.cs Packages/com.hoppa.leveleditor.core/Editor/Analysis/AnalysisRequest.cs Assets/YAK/Editor/Analysis/YAKLevelAnalyzer.cs Assets/YAK/Tests/Editor/YakAnalyzerComplexityTests.cs Assets/YAK/Tests/Editor/YakAnalyzerTestFixtures.cs
git commit -m "feat(core,yak): carry measured click complexity through analyzer result"
```

---

### Task 5: Autofiller — pattern-first assignment + complexity gate

Replace round-robin dealing with pattern-first assignment, add the complexity target/tolerance config + request field, and gate acceptance on complexity-in-band (combined best-effort with APS).

**Files:**
- Modify: `Assets/YAK/Editor/Analysis/YAKSpoolAutofillConfig.cs`
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Analysis/CompletionRequest.cs`
- Modify: `Assets/YAK/Editor/Analysis/YAKSpoolAutofiller.cs`
- Test: `Assets/YAK/Tests/Editor/YakSpoolAutofillerComplexityTests.cs` (new)

**Interfaces:**
- Consumes: `YakClickPattern.Build` (Task 2); `AnalysisRequest.MeasureComplexity` + `LevelAnalysisResult.ComplexityEstimate` (Task 4).
- Produces:
  - `YAKSpoolAutofillConfig.DefaultComplexity` (int 1–10, default 3), `YAKSpoolAutofillConfig.ComplexityTolerance` (float, default 1.5).
  - `CompletionRequest.TargetComplexity` (int?, null = use config default).
  - `BuildCandidate` assigns spools by walking `YakClickPattern.Build(...)`.
  - Acceptance gate requires complexity-in-band in addition to APS-in-band.

- [ ] **Step 1: Write the failing test**

Create `Assets/YAK/Tests/Editor/YakSpoolAutofillerComplexityTests.cs`. Reuse the existing autofiller test setup (see how the current YAK autofiller tests build a grid/profile — search `Assets/YAK/Tests/Editor/` for the autofiller test file and mirror its fixture; extract a shared helper if needed rather than duplicating):

```csharp
using NUnit.Framework;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YAK.Sim;

namespace Hoppa.YAK.Tests
{
    public class YakSpoolAutofillerComplexityTests
    {
        // Helper: count the max consecutive same-column run in an assignment by
        // reading the produced top-section column order back into a tap sequence.
        // (Uses the same Score helper to express "more complex".)

        [Test]
        public void Autofill_HigherComplexityTarget_RaisesMeasuredComplexity()
        {
            // Same grid; request low vs high complexity; measured complexity should trend up.
            var grid = YakAutofillTestFixtures.MultiColorGrid(seed: 11);

            float low  = YakAutofillTestFixtures.RunAndMeasureComplexity(grid, targetComplexity: 1, seed: 11);
            float high = YakAutofillTestFixtures.RunAndMeasureComplexity(grid, targetComplexity: 9, seed: 11);

            Assert.Greater(high, low,
                "a higher complexity target must yield higher measured click complexity");
        }

        [Test]
        public void Autofill_StillSolvable_AtAnyComplexity()
        {
            var grid = YakAutofillTestFixtures.MultiColorGrid(seed: 3);
            var result = YakAutofillTestFixtures.Run(grid, targetComplexity: 7, seed: 3);
            Assert.IsTrue(result.Analysis == null || result.Analysis.Solvable || !result.Succeeded,
                "accepted candidates must be solvable; a best-effort may be returned otherwise");
        }
    }
}
```

> Implement `YakAutofillTestFixtures` (new file `Assets/YAK/Tests/Editor/YakAutofillTestFixtures.cs`) with: `MultiColorGrid(seed)` → a `LevelDocument` with a multi-color wool grid sized so several spools result; `Run(grid, targetComplexity, seed)` → calls the autofiller's `Complete` with a `CompletionRequest { TargetComplexity = ..., Seed = ... }` against a YAK profile; `RunAndMeasureComplexity(...)` → runs then returns `result.Analysis.ComplexityEstimate` (re-analyzing with `MeasureComplexity = true` if the completer's own analysis didn't measure it). Mirror the existing autofiller tests' profile construction; do not invent a new profile path.

- [ ] **Step 2: Run test to verify it fails**

Run `YakSpoolAutofillerComplexityTests`.
Expected: FAIL — `CompletionRequest.TargetComplexity` / config fields undefined.

- [ ] **Step 3a: Add config fields**

In `YAKSpoolAutofillConfig.cs`, add a section before "Search budget":

```csharp
        [Header("Click-pattern complexity (1..10)")]
        [Tooltip("Target click-pattern complexity when the request supplies none. 1 = simple/near-round-robin, 10 = long runs + unpredictable jumps. MEASURED-but-uncalibrated, like APS.")]
        [Range(1, 10)] public int DefaultComplexity = 3;
        [Tooltip("Accept a candidate when |measured complexity − target| <= this (in 1..10 units).")]
        [Min(0f)] public float ComplexityTolerance = 1.5f;
```

- [ ] **Step 3b: Add the request field**

In `CompletionRequest.cs`, add after `TargetAPS`:

```csharp
        // Target click-pattern complexity (1..10). Null = the completer's config
        // default. Composes with TargetAPS — a candidate must satisfy both bands.
        public int? TargetComplexity;
```

- [ ] **Step 3c: Pattern-first assignment in `BuildCandidate`**

In `YAKSpoolAutofiller.cs`, change `BuildCandidate` to accept the target complexity and assign via the pattern. Replace the signature and the round-robin tail:

```csharp
        private static YAKTopSectionData BuildCandidate(
            Dictionary<string, int> perColor, int columns, int complexity,
            YAKSpoolAutofillConfig cfg, System.Random rng)
        {
            var spools = new List<YAKSpoolEntry>();
            foreach (var kv in perColor)
                foreach (var cap in Partition(kv.Value, cfg.MinCapacity, cfg.MaxCapacity, cfg.AvgCapacity, rng))
                    spools.Add(new YAKSpoolEntry { ColorId = kv.Key, Capacity = cap, Hidden = false });

            Shuffle(spools, rng); // color mixing across columns

            int hide = Mathf.Clamp(Mathf.RoundToInt(cfg.HiddenRatio * spools.Count), 0, spools.Count);
            for (int i = 0; i < hide; i++) spools[i].Hidden = true;

            var top = new YAKTopSectionData();
            for (int i = 0; i < columns; i++) top.Columns.Add(new YAKSpoolColumn());

            // Pattern-first: build the intended click pattern, then assign the k-th
            // spool to the column the pattern names (R26). Replaces round-robin i%cols.
            int[] pattern = Hoppa.YAK.Sim.YakClickPattern.Build(columns, spools.Count, complexity, rng);
            for (int i = 0; i < spools.Count; i++)
                top.Columns[pattern[i]].Spools.Add(spools[i]);
            return top;
        }
```

- [ ] **Step 3d: Resolve target + gate on complexity in `Complete`**

In `Complete`, after resolving `targetAps`, add:

```csharp
            int targetComplexity = (req != null && req.TargetComplexity.HasValue)
                ? Mathf.Clamp(req.TargetComplexity.Value, 1, 10) : cfg.DefaultComplexity;
```

Change the `BuildCandidate(...)` call to pass `targetComplexity`:

```csharp
                var top = BuildCandidate(perColor, columns, targetComplexity, cfg, ar);
```

Set `MeasureComplexity = true` on the `AnalysisRequest` inside the loop:

```csharp
                    MeasureComplexity = true,
```

Replace the accept/best-effort block with a combined APS+complexity gate. Replace the body from `double dist = Math.Abs(...)` through the `if (dist < bestDist) ...` line with:

```csharp
                double apsDist  = Math.Abs(analysis.ApsEstimate - targetAps);
                double cplxDist = Math.Abs(analysis.ComplexityEstimate - targetComplexity);
                bool apsOk  = apsDist  <= cfg.ApsTolerance;
                bool cplxOk = cplxDist <= cfg.ComplexityTolerance;
                if (apsOk && cplxOk)
                {
                    result.TopSection = JObject.FromObject(top);
                    result.Analysis = analysis;
                    result.Succeeded = true;
                    return Done(result, sw);
                }
                // Combined normalized distance for best-effort ranking.
                double combined = apsDist / Math.Max(1e-3, cfg.ApsTolerance)
                                + cplxDist / Math.Max(1e-3, cfg.ComplexityTolerance);
                if (combined < bestDist) { bestDist = combined; best = top; bestAnalysis = analysis; }
```

Update the best-effort `FailureReason` to mention both axes:

```csharp
                result.FailureReason =
                    $"closest solvable APS {bestAnalysis.ApsEstimate:0.0}/target {targetAps:0.0} (±{cfg.ApsTolerance:0.0}), " +
                    $"complexity {bestAnalysis.ComplexityEstimate:0.0}/target {targetComplexity} (±{cfg.ComplexityTolerance:0.0}) — target may be unreachable for this grid";
```

- [ ] **Step 4: Run tests to verify they pass**

Run `YakSpoolAutofillerComplexityTests` + the existing autofiller tests + `YakAnalyzerTests`.
Expected: PASS. If `Autofill_HigherComplexityTarget_RaisesMeasuredComplexity` is borderline (small grids have few spools → little room), enlarge `MultiColorGrid` so ≥ ~12 spools result, or average over a couple of seeds inside the fixture.

- [ ] **Step 5: Commit**

```bash
git add Assets/YAK/Editor/Analysis/YAKSpoolAutofillConfig.cs Packages/com.hoppa.leveleditor.core/Editor/Analysis/CompletionRequest.cs Assets/YAK/Editor/Analysis/YAKSpoolAutofiller.cs Assets/YAK/Tests/Editor/YakSpoolAutofillerComplexityTests.cs Assets/YAK/Tests/Editor/YakAutofillTestFixtures.cs
git commit -m "feat(yak): pattern-first spool assignment + complexity acceptance gate"
```

---

### Task 6: Wire Complexity into the curve — tier, profile builder, window, stats

Expose Complexity on `TierPreset`, push it into the transient autofiller config, draw it in the Difficulty Curve window, and write it into per-level stats.

**Files:**
- Modify: `Assets/YAK/Editor/Generator/YAKDifficultyCurveConfig.cs` (`TierPreset`)
- Modify: `Assets/YAK/Editor/Generator/YAKTierProfileBuilder.cs`
- Modify: `Assets/YAK/Editor/Generator/YAKDifficultyCurveWindow.cs`
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Batch/BatchStaging.cs` (`LevelStats`)
- Modify: `Assets/YAK/Editor/Generator/YAKCurveBatchHarness.cs`
- Test: `Assets/YAK/Tests/Editor/YakTierProfileBuilderTests.cs` (extend)

**Interfaces:**
- Consumes: `YAKSpoolAutofillConfig.DefaultComplexity` / `ComplexityTolerance` (Task 5); `LevelAnalysisResult.ComplexityEstimate` (Task 4).
- Produces: `TierPreset.Complexity` (int 1–10); `LevelStats.complexity` (float); window field; harness writes complexity into stats and analyzes with `MeasureComplexity = true`.

- [ ] **Step 1: Write the failing test**

Extend `Assets/YAK/Tests/Editor/YakTierProfileBuilderTests.cs` with a case asserting the tier's Complexity lands on the cloned autofiller config. Mirror how existing cases read the cloned `_config` (reflection) — copy that pattern:

```csharp
        [Test]
        public void Build_PushesComplexityOntoAutofillConfig()
        {
            var baseProfile = TestProfiles.YakProfile();
            var tier = new TierPreset { Name = "T", Complexity = 8 };
            var tp = YAKTierProfileBuilder.Build(baseProfile, tier);
            try
            {
                var af = tp.Profile.LevelCompleter as YAKSpoolAutofiller;
                Assert.IsNotNull(af);
                var cfg = typeof(YAKSpoolAutofiller)
                    .GetField("_config", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                    .GetValue(af) as YAKSpoolAutofillConfig;
                Assert.IsNotNull(cfg);
                Assert.AreEqual(8, cfg.DefaultComplexity);
            }
            finally { tp.Cleanup(); }
        }
```

> Use the same profile/fixture the existing `YakTierProfileBuilderTests` use (`TestProfiles.YakProfile()` or equivalent — check the file). Don't introduce a new profile source.

- [ ] **Step 2: Run test to verify it fails**

Run `YakTierProfileBuilderTests`.
Expected: FAIL — `TierPreset.Complexity` undefined.

- [ ] **Step 3a: Add `TierPreset.Complexity`**

In `YAKDifficultyCurveConfig.cs`, in `TierPreset` under the "Difficulty target" header:

```csharp
        [Range(1, 10)] public int Complexity = 3;
```

(`Clone()` is `MemberwiseClone` — covers the new field automatically.)

- [ ] **Step 3b: Push it in `YAKTierProfileBuilder`**

In `YAKTierProfileBuilder.Build`, inside the autofiller-clone block, after `cfg.HiddenRatio = ...`:

```csharp
                cfg.DefaultComplexity = Mathf.Clamp(tier.Complexity, 1, 10);
```

- [ ] **Step 3c: Draw it in the window**

In `YAKDifficultyCurveWindow.DrawPresets`, after the `p.ApsTolerance` line:

```csharp
                p.Complexity = EditorGUILayout.IntSlider(
                    new GUIContent("Complexity (1..10)", "Click-pattern variety target. Measured-but-uncalibrated, like APS."),
                    Mathf.Clamp(p.Complexity, 1, 10), 1, 10);
```

- [ ] **Step 3d: Add `LevelStats.complexity` + write it in the harness**

In `BatchStaging.cs`, add to `LevelStats`:

```csharp
        public float  complexity;  // measured click-pattern complexity (0 = not measured)
```

In `YAKCurveBatchHarness.RunCurve`, set `MeasureComplexity = true` on the analyzer request:

```csharp
                        var an = tp.Profile.LevelAnalyzer.Analyze(doc, tp.Profile,
                            new AnalysisRequest { RolloutCount = 120, Seed = seed, MeasureComplexity = true });
```

And in the `WriteStats(...) new LevelStats { ... }`, add:

```csharp
                        complexity = bestAn != null ? bestAn.ComplexityEstimate : 0f,
```

- [ ] **Step 4: Run tests to verify they pass**

Run `YakTierProfileBuilderTests` + `YakCurveBatchTests`.
Expected: PASS.

- [ ] **Step 5: Commit**

```bash
git add Assets/YAK/Editor/Generator/YAKDifficultyCurveConfig.cs Assets/YAK/Editor/Generator/YAKTierProfileBuilder.cs Assets/YAK/Editor/Generator/YAKDifficultyCurveWindow.cs Packages/com.hoppa.leveleditor.core/Editor/Batch/BatchStaging.cs Assets/YAK/Editor/Generator/YAKCurveBatchHarness.cs Assets/YAK/Tests/Editor/YakTierProfileBuilderTests.cs
git commit -m "feat(yak): expose Complexity on tier preset + curve window + batch stats"
```

---

### Task 7: Review-window display + full-suite green + docs

Show measured complexity in the batch review window, run the whole EditMode suite, generate any new `.meta` files, and tick the backlog.

**Files:**
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/Batch/BatchReviewWindow.cs` (display only)
- Modify: `docs/YAK-DIFFICULTY-PARAMETER-BACKLOG.md` (mark §1 done)
- Modify: `Packages/com.hoppa.leveleditor.core/CHANGELOG.md`

**Interfaces:**
- Consumes: `LevelStats.complexity` (Task 6).
- Produces: no new public API — display + docs.

- [ ] **Step 1: Display complexity in the review window**

Read `BatchReviewWindow.cs`, find where it renders a candidate's `Stats.aps` / `Stats.band`, and add the complexity next to it. Example (match the surrounding rendering style exactly):

```csharp
// where APS is shown, e.g.:
//   GUILayout.Label($"APS {c.Stats.aps:0.0}  band {c.Stats.band}");
// add:
if (c.Stats != null && c.Stats.complexity > 0f)
    GUILayout.Label($"cplx {c.Stats.complexity:0.0}");
```

If the window has no per-stat label row (e.g. it only shows id/aps), add a minimal label consistent with what's there. Display-only — no logic change.

- [ ] **Step 2: Refresh assets + generate metas**

Run the `assets-refresh` MCP tool (or AssetDatabase refresh) so Unity creates `.meta` files for the new scripts:
- `Assets/YAK/Runtime/Sim/YakClickPattern.cs`
- `Assets/YAK/Tests/Editor/YakClickPatternTests.cs`
- `Assets/YAK/Tests/Editor/YakAveragePlayerComplexityTests.cs`
- `Assets/YAK/Tests/Editor/YakAnalyzerComplexityTests.cs`
- `Assets/YAK/Tests/Editor/YakAnalyzerTestFixtures.cs` (if created)
- `Assets/YAK/Tests/Editor/YakSpoolAutofillerComplexityTests.cs`
- `Assets/YAK/Tests/Editor/YakAutofillTestFixtures.cs`

- [ ] **Step 3: Run the FULL EditMode suite**

Run all EditMode tests (`tests-run` MCP, mode EditMode, no filter).
Expected: PASS — at least **185** prior tests + the new complexity tests, all green. Fix any regression before proceeding.

- [ ] **Step 4: Update docs**

In `docs/YAK-DIFFICULTY-PARAMETER-BACKLOG.md`, mark §1 (Complexity axis) as done with a one-line pointer to the spec/plan dates and note the R27 table-vs-formula decision (pending boss confirmation). In `CHANGELOG.md`, add an entry under the working version for the Complexity axis.

- [ ] **Step 5: Commit**

```bash
git add Packages/com.hoppa.leveleditor.core/Editor/Batch/BatchReviewWindow.cs docs/YAK-DIFFICULTY-PARAMETER-BACKLOG.md Packages/com.hoppa.leveleditor.core/CHANGELOG.md Assets/YAK/**/*.meta
git commit -m "feat(yak): show measured complexity in batch review; docs + changelog"
```

---

## Self-Review

**Spec coverage:**
- §3.1 dial (Complexity on TierPreset, CompletionRequest.TargetComplexity, config default+tolerance, curve window, tier builder, harness) → Tasks 5 + 6. ✓
- §3.2 pattern builder (quota, maxRepeat R27, R25 guard, complexity-scaled selection) → Tasks 1 + 2. ✓
- §3.3 spool→column pattern-first assignment → Task 5 (Step 3c). ✓
- §3.4 complexity measurement (player records winning runs, `ClickPatternComplexity` metric, result/request fields, analyzer copy) → Tasks 1 (Score), 3, 4. ✓
- §3.5 gate (solvable + APS + complexity, combined best-effort) → Task 5 (Step 3d). ✓
- §3.6 reporting (stats + review window) → Tasks 6 + 7. ✓
- §5 testing (pure metric, builder, integration ramp, 185 green) → Tasks 1–7. ✓
- §6 out-of-scope (no §2/§3, uncalibrated honesty) → respected; calibration explicitly not attempted. ✓

**Placeholder scan:** Step 3a in Task 3 and the fixtures in Tasks 4/5 are conditional ("if no factory exists") rather than literal code, because they depend on existing test-helper shapes I must not blind-overwrite. Each gives the exact factory signature + the rule (reuse existing, extract shared, never duplicate). This is a deliberate "inspect-then-match" instruction, not an unfilled TODO.

**Type consistency:** `MeasureComplexity` (bool) consistent across Config/AnalysisRequest; `ComplexityEstimate` (float) consistent across `YakAveragePlayer.Result` → `LevelAnalysisResult` → `LevelStats.complexity`; `TargetComplexity` (int?) on CompletionRequest; `DefaultComplexity` (int) / `ComplexityTolerance` (float) on config; `YakClickPattern.Build`/`Score`/`MaxRepeat` signatures match every call site. ✓

**Open flag for the lead:** R27 table-vs-formula divergence — implemented the table; confirm with the boss.
