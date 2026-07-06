# ImageToGrid Color Control Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Give the image→grid converters two color controls — dominant-color downscale (default on) and an optional per-image color remap — so subjects like a lime frog stop coming out yellow.

**Architecture:** Extract the duplicated pure pixel math from `YAKImageToGrid` and `BusBuddiesImageToGrid` into a new `ImageToGridMath` static class in the core Editor package (both already reference it). Add the two new features there once, then have both converters call it. A shared custom inspector renders the remap UI (ColorField source with Unity's built-in eyedropper, palette-id dropdown target, reach slider).

**Tech Stack:** Unity Editor C# (2022.3 / U6 compatible), Newtonsoft.Json, Unity Test Framework (EditMode). Tests run via the Unity MCP `tests-run` tool against the open editor-core project.

## Global Constraints

- Write all `.cs` files with Write/Edit tools — never PowerShell (approval bottleneck).
- Include Unity `.meta` sidecars for every new file — `git add` the containing folder so metas ship.
- No absolute/machine-specific paths anywhere.
- Tests run through the Unity MCP (`mcp__ai-game-developer__tests-run`, EditMode). Adding a `.cs` to an existing assembly triggers a domain reload; the MCP may 500 for tens of seconds while recompiling — retry, don't panic.
- The redmean distance metric is unchanged. Segmentation, outline, and background-neutral behavior are unchanged.
- New default: `Sampling = Dominant` on both converters. Existing tests that assert area-average output are converted into explicit `AreaAverage` regression guards (set the field on the test converter) rather than re-guessed — see Tasks 4 and 5.
- New shared types (`PaletteColor`, `SampleMode`, `ColorRemap`) and `ImageToGridMath` are **public** in `Hoppa.LevelEditor.Core.Editor` (cross-assembly use).
- No asmdef edits are needed: all new files land in assemblies that glob their folders (`Hoppa.LevelEditor.Core.Editor`, its Tests asmdef, `Hoppa.YAK.Editor`, `Hoppa.BusBuddies.Editor`).

**Key paths**
- Core Editor code: `Packages/com.hoppa.leveleditor.core/Editor/ImageToGrid/`
- Core Editor tests (asmdef `Hoppa.LevelEditor.Core.Editor.Tests`): `Packages/com.hoppa.leveleditor.core/Tests/Editor/`
- YAK converter: `Assets/YAK/Editor/ImageToGrid/YAKImageToGrid.cs`; tests: `Assets/YAK/Tests/Editor/YakImageToGridTests.cs`
- BB converter: `Assets/BusBuddies/Editor/ImageToGrid/BusBuddiesImageToGrid.cs`; tests: `Assets/BusBuddies/Tests/Editor/BusBuddiesImageToGridTests.cs`
- Panel: `Packages/com.hoppa.leveleditor.core/Editor/ImageToGrid/ImageToGridModePanel.cs`

---

### Task 1: `ImageToGridMath` — shared types + moved pure math (area-average parity)

Create the shared math class holding the helpers currently duplicated in both converters, plus the new shared types. No converter is changed yet, so behavior is unchanged and the project still compiles.

**Files:**
- Create: `Packages/com.hoppa.leveleditor.core/Editor/ImageToGrid/ImageToGridMath.cs`
- Test: `Packages/com.hoppa.leveleditor.core/Tests/Editor/ImageToGridMathTests.cs`

**Interfaces:**
- Produces (public, namespace `Hoppa.LevelEditor.Core.Editor`):
  - `struct PaletteColor { public string Id; public Color C; }`
  - `enum SampleMode { AreaAverage, Dominant }`
  - `[Serializable] sealed class ColorRemap { public Color Source; public string TargetColorId; [Range(0,1)] public float Reach = 0.15f; }`
  - `static class ImageToGridMath` with:
    - `List<PaletteColor> BuildPalette(GameProfile profile)`
    - `double RedmeanDist(Color a, Color b)`
    - `int NearestIndex(Color c, IReadOnlyList<PaletteColor> palette)`
    - `string NearestId(Color c, IReadOnlyList<PaletteColor> palette)`
    - `float Luminance(Color c)`
    - `bool[] ComputeOutlineMask(bool[] isBg, int W, int H)`
    - `float Fraction(bool[] mask)`
    - `Color[] Downscale(Color[] src, int sw, int sh, int W, int H, SampleMode mode, IReadOnlyList<PaletteColor> palette)` (AreaAverage branch only in this task; Dominant added in Task 2)
    - `void MergeToCap(string[] ids, IReadOnlyList<PaletteColor> palette, int cap, bool[] isEmpty, string protectedId)`
    - `bool[] BorderRing(Color[] avg, int W, int H, IReadOnlyList<PaletteColor> palette)`
    - `bool[] BySaturation(Color[] avg, int W, int H)`

- [ ] **Step 1: Write the failing test**

Create `Packages/com.hoppa.leveleditor.core/Tests/Editor/ImageToGridMathTests.cs`:

```csharp
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using Hoppa.LevelEditor.Core.Editor;

namespace Hoppa.LevelEditor.Core.EditorTests
{
    public class ImageToGridMathTests
    {
        static List<PaletteColor> Palette() => new List<PaletteColor>
        {
            new PaletteColor { Id = "Red",    C = Color.red },
            new PaletteColor { Id = "Green",  C = Color.green },
            new PaletteColor { Id = "Blue",   C = Color.blue },
            new PaletteColor { Id = "Yellow", C = new Color(1f, 1f, 0f) },
        };

        [Test]
        public void RedmeanDist_IdenticalColors_IsZero()
        {
            Assert.AreEqual(0.0, ImageToGridMath.RedmeanDist(Color.red, Color.red), 1e-6);
        }

        [Test]
        public void RedmeanDist_IsSymmetric()
        {
            var a = new Color(0.2f, 0.6f, 0.1f);
            var b = new Color(0.9f, 0.8f, 0.0f);
            Assert.AreEqual(ImageToGridMath.RedmeanDist(a, b), ImageToGridMath.RedmeanDist(b, a), 1e-6);
        }

        [Test]
        public void NearestId_PicksClosestPaletteColor()
        {
            // Pure green pixel → "Green".
            Assert.AreEqual("Green", ImageToGridMath.NearestId(new Color(0.05f, 0.9f, 0.05f), Palette()));
        }

        [Test]
        public void Downscale_AreaAverage_AveragesRegion()
        {
            // 2x2 source: two white, two black → single cell = mid-grey.
            var src = new[] { Color.white, Color.black, Color.black, Color.white };
            var outp = ImageToGridMath.Downscale(src, 2, 2, 1, 1, SampleMode.AreaAverage, Palette());
            Assert.AreEqual(1, outp.Length);
            Assert.AreEqual(0.5f, outp[0].r, 1e-4);
            Assert.AreEqual(0.5f, outp[0].g, 1e-4);
        }

        [Test]
        public void ComputeOutlineMask_MarksBgAdjacentToSubject()
        {
            // 3x1: bg, subject, bg  → both bg cells border the subject.
            var isBg = new[] { true, false, true };
            var mask = ImageToGridMath.ComputeOutlineMask(isBg, 3, 1);
            Assert.IsTrue(mask[0]);
            Assert.IsFalse(mask[1]); // subject cells are never outline
            Assert.IsTrue(mask[2]);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run (Unity MCP `tests-run`): EditMode, testMode `EditMode`, filter assembly `Hoppa.LevelEditor.Core.Editor.Tests`, class `ImageToGridMathTests`.
Expected: FAIL — `ImageToGridMath` / `PaletteColor` do not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

Create `Packages/com.hoppa.leveleditor.core/Editor/ImageToGrid/ImageToGridMath.cs`. Move the pure helpers **verbatim** from `Assets/YAK/Editor/ImageToGrid/YAKImageToGrid.cs` (the canonical copy), changing the private `struct PaletteColor` to the shared public one and making every method `public static`. Full file:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Shared pure image→grid math, extracted from the per-game converters so the
    // logic (and the new color-control features) live in one place. No asset state.
    public struct PaletteColor { public string Id; public Color C; }

    public enum SampleMode { AreaAverage, Dominant }

    [Serializable]
    public sealed class ColorRemap
    {
        [Tooltip("Source color to remap — set by hand or via the ColorField eyedropper.")]
        public Color Source = Color.white;

        [Tooltip("Palette ColorId this source color is forced to.")]
        public string TargetColorId = "";

        [Tooltip("0 = exact color only; higher widens the range of nearby colors caught.")]
        [Range(0f, 1f)] public float Reach = 0.15f;
    }

    public static class ImageToGridMath
    {
        // Reference distance (black↔white) used to normalize redmean into ~[0,1].
        private static readonly double RefDist = RedmeanDist(Color.black, Color.white);

        public static List<PaletteColor> BuildPalette(GameProfile profile)
        {
            var list = new List<PaletteColor>();
            var pal = profile.ColorPalette;
            if (pal == null) return list;
            foreach (var id in pal.ColorIds)
                if (pal.TryGetColor(id, out var c))
                    list.Add(new PaletteColor { Id = id, C = c });
            return list;
        }

        public static double RedmeanDist(Color a, Color b)
        {
            double r1 = a.r * 255.0, g1 = a.g * 255.0, b1 = a.b * 255.0;
            double r2 = b.r * 255.0, g2 = b.g * 255.0, b2 = b.b * 255.0;
            double rmean = (r1 + r2) * 0.5;
            double dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
            return (2 + rmean / 256.0) * dr * dr + 4.0 * dg * dg + (2 + (255.0 - rmean) / 256.0) * db * db;
        }

        public static int NearestIndex(Color c, IReadOnlyList<PaletteColor> palette)
        {
            int best = 0; double bestD = double.PositiveInfinity;
            for (int i = 0; i < palette.Count; i++)
            {
                double d = RedmeanDist(c, palette[i].C);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        public static string NearestId(Color c, IReadOnlyList<PaletteColor> palette)
            => palette[NearestIndex(c, palette)].Id;

        public static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        public static bool[] ComputeOutlineMask(bool[] isBg, int W, int H)
        {
            var mask = new bool[W * H];
            for (int i = 0; i < mask.Length; i++)
            {
                if (!isBg[i]) continue;
                int x = i % W, y = i / W;
                if ((x > 0     && !isBg[i - 1]) ||
                    (x < W - 1 && !isBg[i + 1]) ||
                    (y > 0     && !isBg[i - W]) ||
                    (y < H - 1 && !isBg[i + W]))
                    mask[i] = true;
            }
            return mask;
        }

        public static float Fraction(bool[] mask)
        {
            int c = 0; foreach (var b in mask) if (b) c++;
            return mask.Length == 0 ? 0f : (float)c / mask.Length;
        }

        private static Color ColorOf(string id, IReadOnlyList<PaletteColor> palette)
        {
            foreach (var p in palette) if (string.Equals(p.Id, id, StringComparison.Ordinal)) return p.C;
            return Color.gray;
        }

        public static Color[] Downscale(Color[] src, int sw, int sh, int W, int H,
                                        SampleMode mode, IReadOnlyList<PaletteColor> palette)
        {
            var outp = new Color[W * H];
            for (int gy = 0; gy < H; gy++)
            for (int gx = 0; gx < W; gx++)
            {
                int x0 = (int)((long)gx * sw / W);
                int x1 = Mathf.Max(x0 + 1, (int)((long)(gx + 1) * sw / W));
                int y0 = (int)((long)gy * sh / H);
                int y1 = Mathf.Max(y0 + 1, (int)((long)(gy + 1) * sh / H));
                x1 = Mathf.Min(x1, sw); y1 = Mathf.Min(y1, sh);

                // AreaAverage (also the fallback for Dominant on an empty region).
                float r = 0, g = 0, b = 0, a = 0; int n = 0;
                for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    var c = src[y * sw + x];
                    r += c.r; g += c.g; b += c.b; a += c.a; n++;
                }
                if (n == 0) n = 1;
                outp[gy * W + gx] = new Color(r / n, g / n, b / n, a / n);
            }
            return outp;
        }

        public static void MergeToCap(string[] ids, IReadOnlyList<PaletteColor> palette, int cap,
                                      bool[] isEmpty = null, string protectedId = null)
        {
            cap = Mathf.Max(1, cap);
            while (true)
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                for (int i = 0; i < ids.Length; i++)
                {
                    if (isEmpty != null && isEmpty[i]) continue;
                    counts.TryGetValue(ids[i], out var n);
                    counts[ids[i]] = n + 1;
                }
                if (counts.Count <= cap) break;

                string least = null; int leastN = int.MaxValue;
                foreach (var kv in counts)
                {
                    if (protectedId != null && string.Equals(kv.Key, protectedId, StringComparison.Ordinal)) continue;
                    if (kv.Value < leastN) { leastN = kv.Value; least = kv.Key; }
                }
                if (least == null) break;

                Color leastColor = ColorOf(least, palette);
                string target = null; double bestD = double.PositiveInfinity;
                foreach (var kv in counts)
                {
                    if (string.Equals(kv.Key, least, StringComparison.Ordinal)) continue;
                    double d = RedmeanDist(leastColor, ColorOf(kv.Key, palette));
                    if (d < bestD) { bestD = d; target = kv.Key; }
                }
                if (target == null) break;

                for (int i = 0; i < ids.Length; i++)
                    if ((isEmpty == null || !isEmpty[i]) &&
                        string.Equals(ids[i], least, StringComparison.Ordinal))
                        ids[i] = target;
            }
        }

        public static bool[] BorderRing(Color[] avg, int W, int H, IReadOnlyList<PaletteColor> palette)
        {
            var ids = new int[W * H];
            for (int i = 0; i < ids.Length; i++) ids[i] = NearestIndex(avg[i], palette);

            var border = new Dictionary<int, int>();
            void Tally(int idx) { border.TryGetValue(ids[idx], out var n); border[ids[idx]] = n + 1; }
            for (int x = 0; x < W; x++) { Tally(x); Tally((H - 1) * W + x); }
            for (int y = 0; y < H; y++) { Tally(y * W); Tally(y * W + (W - 1)); }

            int dom = -1, best = -1;
            foreach (var kv in border) if (kv.Value > best) { best = kv.Value; dom = kv.Key; }

            var bg = new bool[W * H];
            if (dom < 0) return bg;
            var queue = new Queue<int>();
            void Seed(int idx) { if (ids[idx] == dom && !bg[idx]) { bg[idx] = true; queue.Enqueue(idx); } }
            for (int x = 0; x < W; x++) { Seed(x); Seed((H - 1) * W + x); }
            for (int y = 0; y < H; y++) { Seed(y * W); Seed(y * W + (W - 1)); }
            while (queue.Count > 0)
            {
                int idx = queue.Dequeue();
                int cx = idx % W, cy = idx / W;
                void Visit(int nx, int ny)
                {
                    if (nx < 0 || ny < 0 || nx >= W || ny >= H) return;
                    int nn = ny * W + nx;
                    if (!bg[nn] && ids[nn] == dom) { bg[nn] = true; queue.Enqueue(nn); }
                }
                Visit(cx + 1, cy); Visit(cx - 1, cy); Visit(cx, cy + 1); Visit(cx, cy - 1);
            }
            return bg;
        }

        public static bool[] BySaturation(Color[] avg, int W, int H)
        {
            int n = W * H;
            var sat = new float[n];
            var sorted = new float[n];
            for (int i = 0; i < n; i++)
            {
                Color.RGBToHSV(avg[i], out _, out var s, out _);
                sat[i] = s; sorted[i] = s;
            }
            Array.Sort(sorted);
            float median = sorted[n / 2];
            var bg = new bool[n];
            for (int i = 0; i < n; i++) bg[i] = sat[i] < median;
            return bg;
        }
    }
}
```

- [ ] **Step 4: Run test to verify it passes**

Run (Unity MCP `tests-run`): EditMode, class `ImageToGridMathTests`.
Expected: PASS (5 tests).

- [ ] **Step 5: Commit**

```bash
git add "Packages/com.hoppa.leveleditor.core/Editor/ImageToGrid/" "Packages/com.hoppa.leveleditor.core/Tests/Editor/"
git commit -m "feat(imagetogrid): shared ImageToGridMath with moved pure helpers"
```

---

### Task 2: Dominant-color downscale

Add the Dominant branch to `Downscale`: bin source pixels by nearest palette color, take the majority bin, return the mean of that bin's pixels.

**Files:**
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/ImageToGrid/ImageToGridMath.cs`
- Test: `Packages/com.hoppa.leveleditor.core/Tests/Editor/ImageToGridMathTests.cs`

**Interfaces:**
- Consumes: `Downscale(..., SampleMode mode, IReadOnlyList<PaletteColor> palette)`, `NearestIndex`.
- Produces: `Downscale` with a working `SampleMode.Dominant` branch.

- [ ] **Step 1: Write the failing test**

Append to `ImageToGridMathTests.cs`:

```csharp
        [Test]
        public void Downscale_Dominant_PicksMajorityColorIgnoringMinority()
        {
            // One cell over a 5x1 region: 3 green pixels, 2 red pixels → green wins,
            // and the returned color is the mean of the GREEN pixels (not muddied by red).
            var g = new Color(0f, 0.8f, 0f);
            var r = new Color(0.9f, 0f, 0f);
            var src = new[] { g, g, g, r, r };
            var outp = ImageToGridMath.Downscale(src, 5, 1, 1, 1, SampleMode.Dominant, Palette());
            Assert.AreEqual("Green", ImageToGridMath.NearestId(outp[0], Palette()));
            Assert.AreEqual(0f, outp[0].r, 1e-4, "red minority must not bleed in");
            Assert.AreEqual(0.8f, outp[0].g, 1e-4);
        }

        [Test]
        public void Downscale_Dominant_TieBreaksToLowestPaletteIndex()
        {
            // 1 red + 1 green, palette order Red(0) before Green(1) → tie → Red.
            var src = new[] { new Color(0.9f, 0f, 0f), new Color(0f, 0.8f, 0f) };
            var outp = ImageToGridMath.Downscale(src, 2, 1, 1, 1, SampleMode.Dominant, Palette());
            Assert.AreEqual("Red", ImageToGridMath.NearestId(outp[0], Palette()));
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run (Unity MCP `tests-run`): class `ImageToGridMathTests`.
Expected: FAIL — Dominant currently falls through to the area-average path (red bleeds in; `outp[0].r` ≈ 0.36, not 0).

- [ ] **Step 3: Write minimal implementation**

In `ImageToGridMath.Downscale`, replace the single AreaAverage body with a mode switch. After computing `x0,x1,y0,y1`, use:

```csharp
                if (mode == SampleMode.Dominant && palette != null && palette.Count > 0)
                {
                    // Bin each source pixel by nearest palette color; keep per-bin sums.
                    int k = palette.Count;
                    var binCount = new int[k];
                    var binR = new float[k]; var binG = new float[k]; var binB = new float[k]; var binA = new float[k];
                    int total = 0;
                    for (int y = y0; y < y1; y++)
                    for (int x = x0; x < x1; x++)
                    {
                        var c = src[y * sw + x];
                        int bi = NearestIndex(c, palette);
                        binCount[bi]++; binR[bi] += c.r; binG[bi] += c.g; binB[bi] += c.b; binA[bi] += c.a;
                        total++;
                    }
                    if (total > 0)
                    {
                        int maj = 0;
                        for (int i = 1; i < k; i++) if (binCount[i] > binCount[maj]) maj = i; // ties keep lower index
                        int m = binCount[maj];
                        outp[gy * W + gx] = new Color(binR[maj] / m, binG[maj] / m, binB[maj] / m, binA[maj] / m);
                        continue;
                    }
                    // total == 0 → fall through to the average path below (empty region).
                }

                // AreaAverage (also the fallback for Dominant on an empty region).
                float r = 0, g = 0, b = 0, a = 0; int n = 0;
                for (int y = y0; y < y1; y++)
                for (int x = x0; x < x1; x++)
                {
                    var c = src[y * sw + x];
                    r += c.r; g += c.g; b += c.b; a += c.a; n++;
                }
                if (n == 0) n = 1;
                outp[gy * W + gx] = new Color(r / n, g / n, b / n, a / n);
```

(The `continue` skips the average path when a Dominant bin was chosen. The initial `maj` scan starting at index 0 and using strict `>` guarantees the lowest-index tie-break.)

- [ ] **Step 4: Run test to verify it passes**

Run (Unity MCP `tests-run`): class `ImageToGridMathTests`.
Expected: PASS (7 tests — 5 prior + 2 new).

- [ ] **Step 5: Commit**

```bash
git add "Packages/com.hoppa.leveleditor.core/Editor/ImageToGrid/ImageToGridMath.cs" "Packages/com.hoppa.leveleditor.core/Tests/Editor/ImageToGridMathTests.cs"
git commit -m "feat(imagetogrid): dominant-color downscale sampling"
```

---

### Task 3: `ResolveRemap` + normalized distance

Add the remap resolver: given a cell color and a list of `ColorRemap`, return the forced palette id or null.

**Files:**
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/ImageToGrid/ImageToGridMath.cs`
- Test: `Packages/com.hoppa.leveleditor.core/Tests/Editor/ImageToGridMathTests.cs`

**Interfaces:**
- Consumes: `RedmeanDist`, `RefDist`, `ColorRemap`, `PaletteColor`.
- Produces:
  - `float NormalizedDistance(Color a, Color b)` — 0 for identical, ~1 for black↔white.
  - `string ResolveRemap(Color cellColor, IReadOnlyList<ColorRemap> remaps, IReadOnlyList<PaletteColor> palette)` — forced id or null.

- [ ] **Step 1: Write the failing test**

Append to `ImageToGridMathTests.cs`:

```csharp
        static bool InPalette(string id, List<PaletteColor> p)
        { foreach (var e in p) if (e.Id == id) return true; return false; }

        [Test]
        public void NormalizedDistance_IsZeroForSame_AndOneForBlackWhite()
        {
            Assert.AreEqual(0f, ImageToGridMath.NormalizedDistance(Color.green, Color.green), 1e-4);
            Assert.AreEqual(1f, ImageToGridMath.NormalizedDistance(Color.black, Color.white), 1e-3);
        }

        [Test]
        public void ResolveRemap_InReach_ReturnsTarget()
        {
            var remaps = new List<ColorRemap> {
                new ColorRemap { Source = new Color(0.7f, 0.85f, 0.15f), TargetColorId = "Green", Reach = 0.3f },
            };
            // A lime-ish cell close to the source → forced to Green.
            var id = ImageToGridMath.ResolveRemap(new Color(0.72f, 0.83f, 0.18f), remaps, Palette());
            Assert.AreEqual("Green", id);
        }

        [Test]
        public void ResolveRemap_OutOfReach_ReturnsNull()
        {
            var remaps = new List<ColorRemap> {
                new ColorRemap { Source = new Color(0.7f, 0.85f, 0.15f), TargetColorId = "Green", Reach = 0.02f },
            };
            Assert.IsNull(ImageToGridMath.ResolveRemap(Color.blue, remaps, Palette()));
        }

        [Test]
        public void ResolveRemap_ClosestSourceWins()
        {
            var remaps = new List<ColorRemap> {
                new ColorRemap { Source = Color.red,   TargetColorId = "Red",   Reach = 1f },
                new ColorRemap { Source = Color.green, TargetColorId = "Green", Reach = 1f },
            };
            Assert.AreEqual("Green", ImageToGridMath.ResolveRemap(new Color(0.1f, 0.8f, 0.1f), remaps, Palette()));
        }

        [Test]
        public void ResolveRemap_TargetNotInPalette_IsIgnored()
        {
            var remaps = new List<ColorRemap> {
                new ColorRemap { Source = Color.green, TargetColorId = "Magenta", Reach = 1f },
            };
            Assert.IsNull(ImageToGridMath.ResolveRemap(Color.green, remaps, Palette()));
        }

        [Test]
        public void ResolveRemap_NullOrEmpty_ReturnsNull()
        {
            Assert.IsNull(ImageToGridMath.ResolveRemap(Color.green, null, Palette()));
            Assert.IsNull(ImageToGridMath.ResolveRemap(Color.green, new List<ColorRemap>(), Palette()));
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run (Unity MCP `tests-run`): class `ImageToGridMathTests`.
Expected: FAIL — `NormalizedDistance` / `ResolveRemap` do not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

Add to `ImageToGridMath` (below `RedmeanDist`):

```csharp
        // Redmean distance normalized so identical = 0 and black↔white ≈ 1.
        public static float NormalizedDistance(Color a, Color b)
            => (float)(Math.Sqrt(RedmeanDist(a, b)) / Math.Sqrt(RefDist));

        // Returns the TargetColorId of the closest in-reach remap whose target exists
        // in the palette, or null if none apply. Ties resolve to the earliest entry.
        public static string ResolveRemap(Color cellColor, IReadOnlyList<ColorRemap> remaps,
                                          IReadOnlyList<PaletteColor> palette)
        {
            if (remaps == null || remaps.Count == 0) return null;
            string best = null; float bestDist = float.PositiveInfinity;
            for (int i = 0; i < remaps.Count; i++)
            {
                var rm = remaps[i];
                if (rm == null || string.IsNullOrEmpty(rm.TargetColorId)) continue;
                bool inPalette = false;
                for (int p = 0; p < palette.Count; p++)
                    if (string.Equals(palette[p].Id, rm.TargetColorId, StringComparison.Ordinal)) { inPalette = true; break; }
                if (!inPalette) continue;

                float d = NormalizedDistance(cellColor, rm.Source);
                if (d <= rm.Reach && d < bestDist) { bestDist = d; best = rm.TargetColorId; }
            }
            return best;
        }
```

- [ ] **Step 4: Run test to verify it passes**

Run (Unity MCP `tests-run`): class `ImageToGridMathTests`.
Expected: PASS (13 tests).

- [ ] **Step 5: Commit**

```bash
git add "Packages/com.hoppa.leveleditor.core/Editor/ImageToGrid/ImageToGridMath.cs" "Packages/com.hoppa.leveleditor.core/Tests/Editor/ImageToGridMathTests.cs"
git commit -m "feat(imagetogrid): color remap resolver + normalized distance"
```

---

### Task 4: Wire YAKImageToGrid to shared math + new fields

Refactor `YAKImageToGrid` to delegate to `ImageToGridMath`, add `Sampling` + `ColorRemaps`, and apply both in `Convert`.

**Files:**
- Modify: `Assets/YAK/Editor/ImageToGrid/YAKImageToGrid.cs`
- Test: `Assets/YAK/Tests/Editor/YakImageToGridTests.cs`

**Interfaces:**
- Consumes: `ImageToGridMath.*`, `SampleMode`, `ColorRemap`, `PaletteColor`.
- Produces: `YAKImageToGrid.Sampling` (SampleMode, default Dominant), `YAKImageToGrid.ColorRemaps` (List<ColorRemap>).

- [ ] **Step 1: Write the failing test**

First, open `Assets/YAK/Tests/Editor/YakImageToGridTests.cs`. For every existing test that asserts specific output colors, add `converter.Sampling = SampleMode.AreaAverage;` before `Convert` so those remain area-average regression guards (their expectations are unchanged). Then append two new tests (adjust the converter-construction helper name to match the file's existing pattern — reuse whatever factory/`ScriptableObject.CreateInstance<YAKImageToGrid>()` the file already uses, and the file's existing test profile/palette builder in `TestProfiles.cs`):

```csharp
        [Test]
        public void Convert_RemapsLimeToGreen_SubjectBecomesGreen()
        {
            // A solid lime block on a contrasting border; remap lime→Green.
            var converter = ScriptableObject.CreateInstance<YAKImageToGrid>();
            converter.Sampling = SampleMode.Dominant;
            converter.ColorRemaps = new System.Collections.Generic.List<ColorRemap> {
                new ColorRemap { Source = new Color(0.72f, 0.85f, 0.15f), TargetColorId = "Green", Reach = 0.35f },
            };
            var profile = TestProfiles.MakeYakProfile();          // existing helper; palette must contain "Green"
            var tex = MakeSolidBlockTexture(new Color(0.72f, 0.85f, 0.15f), profile); // lime subject
            var doc = converter.Convert(tex, profile);
            Assert.IsTrue(HasColor(doc, "Green"), "remapped subject should contain Green");
            Assert.IsFalse(HasColor(doc, "Yellow"), "lime should no longer land on Yellow");
            Object.DestroyImmediate(converter);
        }

        [Test]
        public void Convert_DominantDefault_IsCleanerThanAverage_OnTwoToneSubject()
        {
            // Sanity: default Sampling is Dominant (field default), not AreaAverage.
            var converter = ScriptableObject.CreateInstance<YAKImageToGrid>();
            Assert.AreEqual(SampleMode.Dominant, converter.Sampling);
            Object.DestroyImmediate(converter);
        }
```

Add these small helpers to the test class if the file doesn't already have equivalents:

```csharp
        static bool HasColor(LevelDocument doc, string colorId)
        {
            foreach (var cell in doc.Grid.Cells)
                if (cell is IColoredCell c && c.ColorId == colorId) return true;
            return false;
        }

        static Texture2D MakeSolidBlockTexture(Color subject, GameProfile profile)
        {
            // Small texture: subject-filled center, distinct border so segmentation finds a subject.
            int w = 16, h = 16;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = Color.black; // border/background
            for (int y = 3; y < h - 3; y++)
            for (int x = 3; x < w - 3; x++) px[y * w + x] = subject;
            tex.SetPixels(px); tex.Apply();
            return tex;
        }
```

- [ ] **Step 2: Run test to verify it fails**

Run (Unity MCP `tests-run`): assembly `Hoppa.YAK.Editor.Tests`, class `YakImageToGridTests`.
Expected: FAIL — `converter.Sampling` / `converter.ColorRemaps` don't exist (compile error).

- [ ] **Step 3: Write minimal implementation**

In `YAKImageToGrid.cs`:

1. Add the two fields (near the existing `[Header("Colors")]` block):

```csharp
        [Header("Sampling")]
        [Tooltip("Dominant (default): each square takes its majority color — truer, less muddy. AreaAverage: legacy blur.")]
        public SampleMode Sampling = SampleMode.Dominant;

        [Header("Color Remap")]
        [Tooltip("Optional: force chosen source colors to specific palette colors (e.g. a lime frog → Green). Empty = automatic nearest-color matching.")]
        public System.Collections.Generic.List<ColorRemap> ColorRemaps = new System.Collections.Generic.List<ColorRemap>();
```

2. In `Convert`, replace the local math calls with the shared ones and thread the two features in. Specifically:
   - `var palette = ImageToGridMath.BuildPalette(profile);` (returns `List<PaletteColor>` — the shared type).
   - `var avg = ImageToGridMath.Downscale(src, sw, sh, W, H, Sampling, palette);`
   - Segmentation calls become `Segment(avg, W, H, palette)` (keep the local `Segment` wrapper, but have it call `ImageToGridMath.BorderRing` / `ImageToGridMath.BySaturation` / `ImageToGridMath.Fraction`).
   - Quantize step (was `ids[i] = NearestId(avg[i], palette);`) becomes:
     ```csharp
     for (int i = 0; i < ids.Length; i++)
         ids[i] = ImageToGridMath.ResolveRemap(avg[i], ColorRemaps, palette)
                  ?? ImageToGridMath.NearestId(avg[i], palette);
     ```
   - `isOutline = OutlineSubject ? ImageToGridMath.ComputeOutlineMask(isBg, W, H) : null;`
   - Background-neutral selection: keep `ChooseBackgroundNeutral`/`MostFrequent`/`ColorOf`/`Luminance` local helpers OR call `ImageToGridMath.Luminance`; `MostFrequent` and `ChooseBackgroundNeutral` are YAK-specific and stay local (they may call `ImageToGridMath.Luminance` / a local `TryColorOf`).
   - `ImageToGridMath.MergeToCap(ids, palette, ColorCap, isEmpty: null, protectedId: resolvedOutlineId);` (YAK FillNeutral has no empties; pass `null`).
   - `ResolveOutlineId` stays local (uses `ImageToGridMath.Luminance`).

3. Delete the now-unused private copies: `struct PaletteColor`, `BuildPalette`, `ReadablePixels` (keep — still local, it's IO), `Downscale`, `NearestId`, `NearestIndex`, `RedmeanDist`, `MergeToCap`, `ComputeOutlineMask` (was public static — remove; nothing external references it), `BorderRing`, `BySaturation`, `Fraction`, `Luminance`. Keep `ReadablePixels` (texture IO), the local `Segment` wrapper, `ChooseBackgroundNeutral`, `MostFrequent`, `ColorOf`/`TryColorOf`, `ResolveOutlineId`. Update the local `Segment` to call the shared `BorderRing`/`BySaturation`/`Fraction` and to take `List<PaletteColor>`.

> The precise final shape: `Convert` orchestrates; all pure color math routes through `ImageToGridMath`; only texture IO (`ReadablePixels`), the YAK background/outline policy, and emit stay local.

- [ ] **Step 4: Run tests to verify they pass**

Run (Unity MCP `tests-run`): assembly `Hoppa.YAK.Editor.Tests` (whole assembly, to catch the re-baselined existing tests).
Expected: PASS — existing tests (now pinned to `AreaAverage`) still green; two new tests green.

- [ ] **Step 5: Commit**

```bash
git add "Assets/YAK/Editor/ImageToGrid/YAKImageToGrid.cs" "Assets/YAK/Tests/Editor/YakImageToGridTests.cs"
git commit -m "feat(yak): image converter uses shared math + dominant sampling + color remap"
```

---

### Task 5: Wire BusBuddiesImageToGrid to shared math + new fields

Same refactor for BB. BB always emits empty background (no neutral fill) and defaults outline on.

**Files:**
- Modify: `Assets/BusBuddies/Editor/ImageToGrid/BusBuddiesImageToGrid.cs`
- Test: `Assets/BusBuddies/Tests/Editor/BusBuddiesImageToGridTests.cs`

**Interfaces:**
- Consumes: `ImageToGridMath.*`, `SampleMode`, `ColorRemap`, `PaletteColor`.
- Produces: `BusBuddiesImageToGrid.Sampling` (default Dominant), `BusBuddiesImageToGrid.ColorRemaps`.

- [ ] **Step 1: Write the failing test**

In `Assets/BusBuddies/Tests/Editor/BusBuddiesImageToGridTests.cs`, add `converter.Sampling = SampleMode.AreaAverage;` before `Convert` in existing color-asserting tests (keep them as area-average guards). Append:

```csharp
        [Test]
        public void Convert_RemapsLimeToGreen_SubjectBecomesGreen()
        {
            var converter = ScriptableObject.CreateInstance<BusBuddiesImageToGrid>();
            converter.Sampling = SampleMode.Dominant;
            converter.OutlineSubject = false; // isolate the subject fill for the assertion
            converter.ColorRemaps = new System.Collections.Generic.List<ColorRemap> {
                new ColorRemap { Source = new Color(0.72f, 0.85f, 0.15f), TargetColorId = "green", Reach = 0.35f },
            };
            var profile = MakeBbProfile();            // reuse the file's existing BB test-profile helper
            var tex = MakeSolidBlockTexture(new Color(0.72f, 0.85f, 0.15f));
            var doc = converter.Convert(tex, profile);
            Assert.IsTrue(HasPixelColor(doc, "green"));
            Assert.IsFalse(HasPixelColor(doc, "yellow"));
            Object.DestroyImmediate(converter);
        }

        [Test]
        public void Convert_DefaultSampling_IsDominant()
        {
            var converter = ScriptableObject.CreateInstance<BusBuddiesImageToGrid>();
            Assert.AreEqual(SampleMode.Dominant, converter.Sampling);
            Object.DestroyImmediate(converter);
        }
```

Add helpers if not already present (BB pixel cells expose `ColorId`; use the BB palette's lowercase ids like `"green"`/`"yellow"`):

```csharp
        static bool HasPixelColor(LevelDocument doc, string colorId)
        {
            foreach (var cell in doc.Grid.Cells)
                if (cell is IColoredCell c && c.ColorId == colorId) return true;
            return false;
        }

        static Texture2D MakeSolidBlockTexture(Color subject)
        {
            int w = 16, h = 16;
            var tex = new Texture2D(w, h, TextureFormat.RGBA32, false);
            var px = new Color[w * h];
            for (int i = 0; i < px.Length; i++) px[i] = Color.black;
            for (int y = 3; y < h - 3; y++)
            for (int x = 3; x < w - 3; x++) px[y * w + x] = subject;
            tex.SetPixels(px); tex.Apply();
            return tex;
        }
```

> Confirm the BB palette ids: the test's remap `TargetColorId` and assertions must use the exact ids in `BusBuddiesPalette.asset` (lowercase `red/blue/green/yellow/orange/purple/pink/cyan`). If the file's existing profile helper is named differently than `MakeBbProfile`, use that name.

- [ ] **Step 2: Run test to verify it fails**

Run (Unity MCP `tests-run`): assembly `Hoppa.BusBuddies.Editor.Tests`, class `BusBuddiesImageToGridTests`.
Expected: FAIL — `Sampling` / `ColorRemaps` don't exist (compile error).

- [ ] **Step 3: Write minimal implementation**

In `BusBuddiesImageToGrid.cs`, apply the same changes as Task 4 Step 3, with BB specifics:
- Add the `Sampling` (default Dominant) and `ColorRemaps` fields (same code as Task 4).
- `Convert`: `Downscale(..., Sampling, palette)`; quantize via `ResolveRemap ?? NearestId`; `ComputeOutlineMask`/`MergeToCap`/`BorderRing`/`BySaturation`/`Fraction` from `ImageToGridMath`. BB's `MergeToCap` call passes the real `isEmpty` mask and `resolvedOutlineId` (unchanged from today).
- Delete BB's private copies of the moved helpers (`struct PaletteColor`, `BuildPalette`, `Downscale`, `NearestId/Index`, `RedmeanDist`, `MergeToCap`, `ComputeOutlineMask`, `BorderRing`, `BySaturation`, `Fraction`, `Luminance`, `ColorOf`). Keep `ReadablePixels`, the local `Segment` wrapper (routed to shared helpers), and `ResolveOutlineId` (uses `ImageToGridMath.Luminance`).

- [ ] **Step 4: Run tests to verify they pass**

Run (Unity MCP `tests-run`): assembly `Hoppa.BusBuddies.Editor.Tests`.
Expected: PASS — existing (now AreaAverage-pinned) tests green; two new tests green.

- [ ] **Step 5: Commit**

```bash
git add "Assets/BusBuddies/Editor/ImageToGrid/BusBuddiesImageToGrid.cs" "Assets/BusBuddies/Tests/Editor/BusBuddiesImageToGridTests.cs"
git commit -m "feat(busbuddies): image converter uses shared math + dominant sampling + color remap"
```

---

### Task 6: Shared remap inspector + panel palette handshake

Add the custom inspector that renders the remap list with a ColorField source (built-in eyedropper), a palette-id dropdown target, and a reach slider; wire the panel to publish the active palette and to size the embedded inspector area dynamically.

**Files:**
- Create: `Packages/com.hoppa.leveleditor.core/Editor/ImageToGrid/ImageToGridAssetEditor.cs`
- Modify: `Packages/com.hoppa.leveleditor.core/Editor/ImageToGrid/ImageToGridModePanel.cs`
- Test: `Packages/com.hoppa.leveleditor.core/Tests/Editor/ImageToGridAssetEditorTests.cs`

**Interfaces:**
- Consumes: `ColorPaletteAsset.ColorIds`, `GameProfile.ColorPalette`, `ImageToGridAsset`.
- Produces: `ImageToGridAssetEditor` (`[CustomEditor(typeof(ImageToGridAsset), true)]`) with `public static ColorPaletteAsset ActivePalette`.

- [ ] **Step 1: Write the failing test**

Create `Packages/com.hoppa.leveleditor.core/Tests/Editor/ImageToGridAssetEditorTests.cs`. UI drawing can't be asserted headlessly, but the editor-resolution and dropdown-source can:

```csharp
using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Hoppa.LevelEditor.Core.Editor;

namespace Hoppa.LevelEditor.Core.EditorTests
{
    public class ImageToGridAssetEditorTests
    {
        // A minimal ImageToGridAsset subclass so CreateEditor has a concrete target.
        private sealed class DummyConverter : ImageToGridAsset
        {
            public override Hoppa.LevelEditor.Core.LevelDocument Convert(Texture2D s, GameProfile p) => null;
        }

        [Test]
        public void CreateEditor_ForConverter_ResolvesSharedCustomEditor()
        {
            var conv = ScriptableObject.CreateInstance<DummyConverter>();
            var ed = UnityEditor.Editor.CreateEditor(conv);
            Assert.IsInstanceOf<ImageToGridAssetEditor>(ed);
            Object.DestroyImmediate(ed);
            Object.DestroyImmediate(conv);
        }

        [Test]
        public void PaletteIds_FromActivePalette_AreUsableForDropdown()
        {
            // ActivePalette is the dropdown's source of truth; null → editor falls back to text.
            ImageToGridAssetEditor.ActivePalette = null;
            Assert.IsNull(ImageToGridAssetEditor.ActivePalette);
        }
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

Run (Unity MCP `tests-run`): assembly `Hoppa.LevelEditor.Core.Editor.Tests`, class `ImageToGridAssetEditorTests`.
Expected: FAIL — `ImageToGridAssetEditor` does not exist (compile error).

- [ ] **Step 3: Write minimal implementation**

Create `ImageToGridAssetEditor.cs`:

```csharp
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Shared inspector for every ImageToGridAsset subclass. Renders the default
    // fields, then a friendly ColorRemaps editor: a ColorField source (Unity's
    // built-in swatch + screen eyedropper), a palette-id dropdown target, and a
    // reach slider. The active palette is published by ImageToGridModePanel.
    [CustomEditor(typeof(ImageToGridAsset), editorForChildClasses: true)]
    public sealed class ImageToGridAssetEditor : UnityEditor.Editor
    {
        // Set by the Image panel each frame before drawing; null when inspected elsewhere.
        public static ColorPaletteAsset ActivePalette;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            // Everything except the remap list via the default drawer.
            DrawPropertiesExcluding(serializedObject, "m_Script", "ColorRemaps");

            var remaps = serializedObject.FindProperty("ColorRemaps");
            if (remaps != null)
                DrawRemaps(remaps);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawRemaps(SerializedProperty remaps)
        {
            EditorGUILayout.Space();
            EditorGUILayout.LabelField("Color Remap", EditorStyles.boldLabel);

            string[] ids = ActivePalette?.ColorIds?.ToArray();

            for (int i = 0; i < remaps.arraySize; i++)
            {
                var entry   = remaps.GetArrayElementAtIndex(i);
                var source  = entry.FindPropertyRelative("Source");
                var target  = entry.FindPropertyRelative("TargetColorId");
                var reach   = entry.FindPropertyRelative("Reach");

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.PropertyField(source, new GUIContent("Source"));
                        if (GUILayout.Button("✕", GUILayout.Width(22)))
                        {
                            remaps.DeleteArrayElementAtIndex(i);
                            break;
                        }
                    }

                    if (ids != null && ids.Length > 0)
                    {
                        int cur = Mathf.Max(0, System.Array.IndexOf(ids, target.stringValue));
                        int next = EditorGUILayout.Popup("Target", cur, ids);
                        target.stringValue = ids[next];
                    }
                    else
                    {
                        EditorGUILayout.PropertyField(target, new GUIContent("Target Color Id"));
                    }

                    EditorGUILayout.Slider(reach, 0f, 1f, new GUIContent("Reach"));
                }
            }

            if (GUILayout.Button("+ Add remap"))
                remaps.InsertArrayElementAtIndex(remaps.arraySize);
        }
    }
}
```

Then in `ImageToGridModePanel.cs`, publish the palette right before drawing the embedded inspector, and grow the area to fit. In `DrawParams`, replace the Advanced block:

```csharp
            if (_showAdvanced)
            {
                EnsureConfigEditor(profile.ImageToGrid);
                ImageToGridAssetEditor.ActivePalette = profile.ColorPalette; // publish for the remap dropdown
                var advRect = new Rect(Pad, y, w, 420f);                     // was 320f — room for remaps
                GUILayout.BeginArea(advRect);
                if (_configEditor != null) _configEditor.OnInspectorGUI();
                GUILayout.EndArea();
                y += advRect.height + 4f;
            }
```

And widen the scroll content so the taller inspector + buttons stay reachable — change the scroll content height in `DrawParams` from `720f` to `900f`:

```csharp
            var scrollContent = new Rect(0f, 0f, rect.width - 16f, 900f);
```

- [ ] **Step 4: Run test to verify it passes**

Run (Unity MCP `tests-run`): assembly `Hoppa.LevelEditor.Core.Editor.Tests`, class `ImageToGridAssetEditorTests`.
Expected: PASS (2 tests).

- [ ] **Step 5: Commit**

```bash
git add "Packages/com.hoppa.leveleditor.core/Editor/ImageToGrid/" "Packages/com.hoppa.leveleditor.core/Tests/Editor/ImageToGridAssetEditorTests.cs"
git commit -m "feat(imagetogrid): shared remap inspector + panel palette handshake"
```

---

### Task 7: Full-suite regression + manual eyeball handoff

Confirm nothing regressed and hand the visual check to the lead/verifier.

**Files:** none (verification only).

- [ ] **Step 1: Run the full EditMode suite**

Run (Unity MCP `tests-run`): EditMode, no filter.
Expected: PASS — all prior tests plus the new ones (baseline before this work was 291; expect 291 + the new math/converter/editor tests, 0 failures).

- [ ] **Step 2: Confirm metas + no stray files**

```bash
git status --porcelain
```
Expected: clean (all new `.cs` committed with their `.meta`). If any `.meta` is untracked, `git add` the folder and amend the relevant commit.

- [ ] **Step 3: Manual eyeball (lead/verifier, in-editor)**

Not automatable — the eyedropper + dropdown are IMGUI. In the open editor: `Window ▸ Level Editor` → pick the YAK (or BusBuddies) profile → `🖼 Image` → select the frog image → in Advanced, add a remap, eyedrop the frog's green into Source, set Target = Green, Convert. Confirm: (a) with Dominant default the squares look truer even before remapping; (b) the remapped subject is now green, not yellow.

- [ ] **Step 4: No commit** — verification task.

---

## Notes for the executor

- **Domain reloads:** every task adds a `.cs` to an existing assembly → a Unity recompile. The MCP `tests-run` refreshes assets and defers across the reload; if a call 500s mid-reload, retry.
- **Existing-test re-baselining (Tasks 4–5):** do NOT guess new Dominant expected values for old tests. Pin the old tests to `SampleMode.AreaAverage` (their expectations already match that path) and prove the new behavior with the new tests. This keeps the area-average path guarded and the Dominant/remap paths covered.
- **Palette ids differ per game:** YAK uses PascalCase (`Green`, `Yellow`); BusBuddies uses lowercase (`green`, `yellow`). Use the right casing in each game's tests and remaps.
- **If a test file's helper names differ** from what's referenced here (profile factory, converter construction), use the file's existing helpers — do not invent parallel ones.
