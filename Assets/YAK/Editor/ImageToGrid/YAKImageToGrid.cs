using System;
using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.YAK.Editor
{
    // Converts a source image into a YAK level grid: a clean, blocky,
    // palette-quantized 30×30 of wool tiles with NO empty cells (background is a
    // real neutral color). Pipeline:
    //   downscale (area average) → segment subject/background → quantize subject
    //   to the profile palette (perceptual) → fill background with the most
    //   contrasting neutral → merge down to the color cap → emit LevelDocument.
    //
    // Colors are produced as palette string ColorIds (PascalCase YAK enum names);
    // int/enum mapping stays in YAKLevelExporter / YAKStaticManagerColorSource.
    [CreateAssetMenu(menuName = "Hoppa/YAK/Image To Grid")]
    public sealed class YAKImageToGrid : ImageToGridAsset
    {
        public enum SegmentationMode { BorderRing, Alpha, MostSaturated }

        [Header("Colors")]
        [Tooltip("Maximum number of distinct colors in the output (background neutral counts toward this).")]
        [Min(2)] public int ColorCap = 6;

        [Tooltip("Candidate background neutrals, most-preferred first. The one with the greatest luminance distance from the subject's dominant color is chosen.")]
        public string[] BackgroundNeutrals = { "Grey", "GreyLight", "GreyDark", "DarkGrey", "White", "Black" };

        [Header("Segmentation")]
        [Tooltip("How subject is separated from background.")]
        public SegmentationMode Segmentation = SegmentationMode.BorderRing;

        [Tooltip("Default conveyor (belt slot) count written to the new level's GameData.")]
        [Min(1)] public int DefaultConveyorCount = 5;

        public override LevelDocument Convert(Texture2D source, GameProfile profile)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            int W = Mathf.Max(1, profile.GridWidth);
            int H = Mathf.Max(1, profile.GridHeight);

            // Palette as (id, Color) pairs from the profile's color source.
            var palette = BuildPalette(profile);
            if (palette.Count == 0) throw new InvalidOperationException("Profile palette is empty.");

            // 1) Read + downscale (area average). avg[y*W+x], y=0 = bottom row.
            var (src, sw, sh) = ReadablePixels(source);
            var avg = Downscale(src, sw, sh, W, H);

            // 2) Segment subject vs background.
            bool[] isBg = Segment(avg, W, H, palette);

            // 3) Quantize: every cell to nearest palette color id.
            var ids = new string[W * H];
            for (int i = 0; i < ids.Length; i++) ids[i] = NearestId(avg[i], palette);

            // 4) Background fill: most contrasting neutral vs subject's dominant.
            string subjectDominant = MostFrequent(ids, isBg, wantBackground: false) ?? ids[0];
            string bgId = ChooseBackgroundNeutral(subjectDominant, palette);
            if (bgId != null)
                for (int i = 0; i < ids.Length; i++) if (isBg[i]) ids[i] = bgId;

            // 5) Merge down to the color cap (least-used → nearest neighbour).
            MergeToCap(ids, palette, ColorCap);

            // 6) Emit LevelDocument — all wool, zero empties.
            var grid = new GridData<ICellData>(W, H);
            for (int i = 0; i < ids.Length; i++)
                grid.Cells[i] = new YAKWoolCell { ColorId = ids[i] };

            string nowIso = DateTime.UtcNow.ToString("o");
            return new LevelDocument
            {
                SchemaVersion = profile.SchemaId + ".v1",
                LevelId       = "image_" + DateTime.UtcNow.ToString("yyMMdd_HHmmss"),
                DisplayName   = "Image · " + source.name,
                Metadata      = new LevelMetadata { Author = Environment.UserName, CreatedAt = nowIso, ModifiedAt = nowIso },
                Grid          = grid,
                GameData      = new JObject { ["conveyorCount"] = Mathf.Max(1, DefaultConveyorCount) },
            };
        }

        // ── Palette ───────────────────────────────────────────────────────────

        private struct PaletteColor { public string Id; public Color C; }

        private static List<PaletteColor> BuildPalette(GameProfile profile)
        {
            var list = new List<PaletteColor>();
            var pal = profile.ColorPalette;
            if (pal == null) return list;
            foreach (var id in pal.ColorIds)
                if (pal.TryGetColor(id, out var c))
                    list.Add(new PaletteColor { Id = id, C = c });
            return list;
        }

        // ── Pixel access + downscale ────────────────────────────────────────────

        // Returns a readable RGBA copy regardless of the source's import settings
        // (blit through a temporary RenderTexture). Pixels are row-major, y=0 = bottom.
        private static (Color[] px, int w, int h) ReadablePixels(Texture2D tex)
        {
            var rt = RenderTexture.GetTemporary(tex.width, tex.height, 0, RenderTextureFormat.ARGB32);
            var prev = RenderTexture.active;
            Graphics.Blit(tex, rt);
            RenderTexture.active = rt;
            var readable = new Texture2D(tex.width, tex.height, TextureFormat.RGBA32, false);
            readable.ReadPixels(new Rect(0, 0, tex.width, tex.height), 0, 0);
            readable.Apply();
            RenderTexture.active = prev;
            RenderTexture.ReleaseTemporary(rt);
            var px = readable.GetPixels();
            DestroyImmediate(readable);
            return (px, tex.width, tex.height);
        }

        private static Color[] Downscale(Color[] src, int sw, int sh, int W, int H)
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

        // ── Segmentation ────────────────────────────────────────────────────────

        private bool[] Segment(Color[] avg, int W, int H, List<PaletteColor> palette)
        {
            switch (Segmentation)
            {
                case SegmentationMode.Alpha:
                {
                    var bg = new bool[W * H];
                    bool any = false;
                    for (int i = 0; i < bg.Length; i++) { if (avg[i].a < 0.5f) { bg[i] = true; any = true; } }
                    if (any) return bg;
                    return BorderRing(avg, W, H, palette); // no alpha → fall back
                }
                case SegmentationMode.MostSaturated:
                    return BySaturation(avg, W, H);
                default:
                {
                    var bg = BorderRing(avg, W, H, palette);
                    float frac = Fraction(bg);
                    if (frac < 0.05f || frac > 0.95f)
                    {
                        Debug.Log($"[YAKImageToGrid] BorderRing segmentation ambiguous (bg {frac:P0}); falling back to MostSaturated.");
                        return BySaturation(avg, W, H);
                    }
                    return bg;
                }
            }
        }

        // Background = the region of the border's dominant quantized color that is
        // connected (4-way) to the image edge. Interior cells of that color stay subject.
        private static bool[] BorderRing(Color[] avg, int W, int H, List<PaletteColor> palette)
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
                    int n = ny * W + nx;
                    if (!bg[n] && ids[n] == dom) { bg[n] = true; queue.Enqueue(n); }
                }
                Visit(cx + 1, cy); Visit(cx - 1, cy); Visit(cx, cy + 1); Visit(cx, cy - 1);
            }
            return bg;
        }

        private static bool[] BySaturation(Color[] avg, int W, int H)
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
            for (int i = 0; i < n; i++) bg[i] = sat[i] < median; // low saturation = background
            return bg;
        }

        private static float Fraction(bool[] mask)
        {
            int c = 0; foreach (var b in mask) if (b) c++;
            return mask.Length == 0 ? 0f : (float)c / mask.Length;
        }

        // ── Quantization (perceptual redmean) ───────────────────────────────────

        private static string NearestId(Color c, List<PaletteColor> palette)
            => palette[NearestIndex(c, palette)].Id;

        private static int NearestIndex(Color c, List<PaletteColor> palette)
        {
            int best = 0; double bestD = double.PositiveInfinity;
            for (int i = 0; i < palette.Count; i++)
            {
                double d = RedmeanDist(c, palette[i].C);
                if (d < bestD) { bestD = d; best = i; }
            }
            return best;
        }

        // Cheap perceptual RGB distance (redmean). Works in 0..255.
        private static double RedmeanDist(Color a, Color b)
        {
            double r1 = a.r * 255.0, g1 = a.g * 255.0, b1 = a.b * 255.0;
            double r2 = b.r * 255.0, g2 = b.g * 255.0, b2 = b.b * 255.0;
            double rmean = (r1 + r2) * 0.5;
            double dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
            return (2 + rmean / 256.0) * dr * dr + 4.0 * dg * dg + (2 + (255.0 - rmean) / 256.0) * db * db;
        }

        // ── Background neutral selection ────────────────────────────────────────

        private string ChooseBackgroundNeutral(string subjectDominantId, List<PaletteColor> palette)
        {
            if (BackgroundNeutrals == null) return null;
            Color subjectColor = ColorOf(subjectDominantId, palette);
            float subjectLum = Luminance(subjectColor);

            string best = null; float bestDist = -1f;
            foreach (var id in BackgroundNeutrals)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (string.Equals(id, subjectDominantId, StringComparison.Ordinal)) continue; // exclude subject's own id
                if (!TryColorOf(id, palette, out var c)) continue;                            // must exist in palette
                float dist = Mathf.Abs(Luminance(c) - subjectLum);
                if (dist > bestDist) { bestDist = dist; best = id; }
            }
            return best;
        }

        private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private static Color ColorOf(string id, List<PaletteColor> palette)
            => TryColorOf(id, palette, out var c) ? c : Color.gray;

        private static bool TryColorOf(string id, List<PaletteColor> palette, out Color c)
        {
            foreach (var p in palette) if (string.Equals(p.Id, id, StringComparison.Ordinal)) { c = p.C; return true; }
            c = Color.gray; return false;
        }

        // ── Color-cap merge ─────────────────────────────────────────────────────

        private static void MergeToCap(string[] ids, List<PaletteColor> palette, int cap)
        {
            cap = Mathf.Max(1, cap);
            while (true)
            {
                var counts = new Dictionary<string, int>(StringComparer.Ordinal);
                foreach (var id in ids) { counts.TryGetValue(id, out var n); counts[id] = n + 1; }
                if (counts.Count <= cap) break;

                // Least-used color is merged into its nearest perceptual neighbour
                // among the OTHER present colors.
                string least = null; int leastN = int.MaxValue;
                foreach (var kv in counts) if (kv.Value < leastN) { leastN = kv.Value; least = kv.Key; }

                Color leastColor = ColorOf(least, palette);
                string target = null; double bestD = double.PositiveInfinity;
                foreach (var kv in counts)
                {
                    if (string.Equals(kv.Key, least, StringComparison.Ordinal)) continue;
                    double d = RedmeanDist(leastColor, ColorOf(kv.Key, palette));
                    if (d < bestD) { bestD = d; target = kv.Key; }
                }
                if (target == null) break; // only one color left

                for (int i = 0; i < ids.Length; i++)
                    if (string.Equals(ids[i], least, StringComparison.Ordinal)) ids[i] = target;
            }
        }

        // ── Misc ────────────────────────────────────────────────────────────────

        private static string MostFrequent(string[] ids, bool[] mask, bool wantBackground)
        {
            var counts = new Dictionary<string, int>(StringComparer.Ordinal);
            for (int i = 0; i < ids.Length; i++)
            {
                if (mask[i] != wantBackground) continue;
                counts.TryGetValue(ids[i], out var n); counts[ids[i]] = n + 1;
            }
            string best = null; int bestN = -1;
            foreach (var kv in counts) if (kv.Value > bestN) { bestN = kv.Value; best = kv.Key; }
            return best;
        }
    }
}
