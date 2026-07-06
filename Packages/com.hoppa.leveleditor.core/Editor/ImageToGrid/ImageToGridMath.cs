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
