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
        [Range(0f, 1f)] public float Reach = 0.2f;
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

        // Redmean distance normalized so identical = 0 and black↔white ≈ 1.
        public static float NormalizedDistance(Color a, Color b)
            => (float)(Math.Sqrt(RedmeanDist(a, b)) / Math.Sqrt(RefDist));

        // Returns the TargetColorId of the closest in-reach remap whose target exists
        // in the palette, or null if none apply. Ties resolve to the earliest entry.
        public static string ResolveRemap(Color cellColor, IReadOnlyList<ColorRemap> remaps,
                                          IReadOnlyList<PaletteColor> palette)
        {
            if (remaps == null || remaps.Count == 0) return null;
            if (palette == null) return null;
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

        // Cleans speckle out of a subject/background mask (4-connectivity):
        //   • subject islands (connected !isBg regions) smaller than minRegion → background,
        //   • interior background holes (isBg regions not touching the border) smaller than
        //     minRegion → subject (fills pinholes; large holes like a donut center survive).
        // Removes the stray edge pixels left when an anti-aliased source background doesn't
        // exactly match the flood's dominant color. minRegion <= 1 is a no-op. Mutates isBg.
        public static void Despeckle(bool[] isBg, int W, int H, int minRegion)
        {
            if (isBg == null || minRegion <= 1) return;
            int n = W * H;
            var visited = new bool[n];
            var comp = new List<int>();
            var stack = new Stack<int>();

            // Pass 1: small subject islands → background.
            for (int start = 0; start < n; start++)
            {
                if (isBg[start] || visited[start]) continue;
                FloodComponent(isBg, visited, stack, comp, start, W, H, wantBg: false, out _);
                if (comp.Count < minRegion)
                    foreach (var idx in comp) isBg[idx] = true;
            }

            // Pass 2: small interior background holes → subject.
            for (int i = 0; i < n; i++) visited[i] = false;
            for (int start = 0; start < n; start++)
            {
                if (!isBg[start] || visited[start]) continue;
                FloodComponent(isBg, visited, stack, comp, start, W, H, wantBg: true, out bool touchesBorder);
                if (!touchesBorder && comp.Count < minRegion)
                    foreach (var idx in comp) isBg[idx] = false;
            }
        }

        // Grows the background inward through the subject's halo ring — the anti-aliased blend
        // where the subject edge fades into the sticker's background/glow. Any subject cell
        // adjacent to background (or the grid border) whose DOWNSCALED color is within `maxDist`
        // (normalized redmean, 0..1) of the mean background color becomes background, repeated up
        // to maxLayers times. Catches a halo of ANY color (white, grey, pale-cyan, the source's
        // own bg tint) because the halo always resembles the background — while leaving the
        // contrasting subject fill intact. The background reference color is measured once, from
        // the initial background. No-op if maxLayers <= 0, avg is null, or there are no background
        // cells. Mutates isBg.
        public static void AbsorbBackgroundHalo(bool[] isBg, Color[] avg, int W, int H,
                                                float maxDist, int maxLayers)
        {
            if (isBg == null || avg == null || maxLayers <= 0) return;
            double sr = 0, sg = 0, sb = 0; int bn = 0;
            for (int i = 0; i < isBg.Length; i++)
                if (isBg[i]) { sr += avg[i].r; sg += avg[i].g; sb += avg[i].b; bn++; }
            if (bn == 0) return;
            Color bgColor = new Color((float)(sr / bn), (float)(sg / bn), (float)(sb / bn));

            for (int it = 0; it < maxLayers; it++)
            {
                var toBg = new List<int>();
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    if (isBg[i]) continue;
                    bool adjBg = x == 0 || y == 0 || x == W - 1 || y == H - 1
                                 || isBg[i - 1] || isBg[i + 1] || isBg[i - W] || isBg[i + W];
                    if (!adjBg) continue;
                    if (NormalizedDistance(avg[i], bgColor) <= maxDist) toBg.Add(i);
                }
                if (toBg.Count == 0) break;
                foreach (var i in toBg) isBg[i] = true;
            }
        }

        // Peels `iterations` layers off the subject: any subject cell touching the grid
        // border or orthogonally adjacent to a background cell becomes background, repeated.
        // Strips the thin halo ring and boundary speckle left by a glowing/anti-aliased
        // source background (which segmentation leaves fused to the subject). iterations <= 0
        // is a no-op. Mutates isBg.
        public static void ErodeSubject(bool[] isBg, int W, int H, int iterations)
        {
            if (isBg == null || iterations <= 0) return;
            for (int it = 0; it < iterations; it++)
            {
                var toBg = new List<int>();
                for (int y = 0; y < H; y++)
                for (int x = 0; x < W; x++)
                {
                    int i = y * W + x;
                    if (isBg[i]) continue;
                    bool edge = x == 0 || y == 0 || x == W - 1 || y == H - 1
                                || isBg[i - 1] || isBg[i + 1] || isBg[i - W] || isBg[i + W];
                    if (edge) toBg.Add(i);
                }
                foreach (var i in toBg) isBg[i] = true;
            }
        }

        // Reduces the subject to its single largest 4-connected component: every subject
        // cell that isn't part of the biggest blob becomes background. Ideal for single-
        // subject stickers, where stray arcs/dots left by a glowing or anti-aliased source
        // background survive segmentation as small separate blobs. No-op with 0 or 1 subject
        // component. Mutates isBg.
        public static void KeepLargestSubjectComponent(bool[] isBg, int W, int H)
        {
            if (isBg == null) return;
            int n = W * H;
            var visited = new bool[n];
            var comp = new List<int>();
            var stack = new Stack<int>();
            var components = new List<List<int>>();
            List<int> largest = null; int largestSize = 0;
            for (int start = 0; start < n; start++)
            {
                if (isBg[start] || visited[start]) continue;
                FloodComponent(isBg, visited, stack, comp, start, W, H, wantBg: false, out _);
                var snapshot = new List<int>(comp);
                components.Add(snapshot);
                if (snapshot.Count > largestSize) { largestSize = snapshot.Count; largest = snapshot; }
            }
            if (largest == null) return;
            foreach (var c in components)
                if (!ReferenceEquals(c, largest))
                    foreach (var idx in c) isBg[idx] = true;
        }

        // Iterative flood of the connected component containing `start` whose isBg value
        // equals `wantBg`. Fills `comp` with its cell indices and reports whether it touches
        // the grid border. Marks every visited cell in `visited`.
        private static void FloodComponent(bool[] isBg, bool[] visited, Stack<int> stack,
                                           List<int> comp, int start, int W, int H,
                                           bool wantBg, out bool touchesBorder)
        {
            comp.Clear(); stack.Clear();
            touchesBorder = false;
            visited[start] = true; stack.Push(start);
            while (stack.Count > 0)
            {
                int idx = stack.Pop();
                comp.Add(idx);
                int x = idx % W, y = idx / W;
                if (x == 0 || y == 0 || x == W - 1 || y == H - 1) touchesBorder = true;
                if (x > 0)     TryPush(isBg, visited, stack, idx - 1, wantBg);
                if (x < W - 1) TryPush(isBg, visited, stack, idx + 1, wantBg);
                if (y > 0)     TryPush(isBg, visited, stack, idx - W, wantBg);
                if (y < H - 1) TryPush(isBg, visited, stack, idx + W, wantBg);
            }
        }

        private static void TryPush(bool[] isBg, bool[] visited, Stack<int> stack, int nn, bool wantBg)
        {
            if (!visited[nn] && isBg[nn] == wantBg) { visited[nn] = true; stack.Push(nn); }
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
