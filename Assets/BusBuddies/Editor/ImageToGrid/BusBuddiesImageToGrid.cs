using System;
using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.BusBuddies;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor
{
    // Converts a source image into a Bus Buddies level grid: a subject silhouette of
    // BBPixelCell tiles surrounded by BBEmptyCell background (no-gravity flood
    // accessibility needs real empties, not a neutral fill). Pipeline mirrors
    // YAKImageToGrid but with Bus-Buddies defaults baked in:
    //   downscale (area average) → segment subject/background
    //   → compute outline mask → quantize subject to the profile palette
    //   → outline ring becomes BBPixelCell(OutlineColorId) → non-outline background
    //     becomes BBEmptyCell → merge subject/outline colors down to the cap
    //   → emit LevelDocument, stamping GameData["conveyorCount"] = DefaultActiveSlots.
    //
    // Copy-mirror of YAKImageToGrid's helpers (per the Partition-copied precedent):
    // BB.Editor deliberately does NOT reference the YAK.Editor assembly, so the pure
    // image helpers (including ComputeOutlineMask) are duplicated here rather than
    // reused across the assembly boundary.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Image To Grid")]
    public sealed class BusBuddiesImageToGrid : ImageToGridAsset
    {
        public enum SegmentationMode { BorderRing, Alpha, MostSaturated }

        [Header("Colors")]
        [Tooltip("Maximum number of distinct pixel colors in the output (outline color counts toward this). Empty background cells do not count.")]
        [Min(2)] public int ColorCap = 6;

        [Tooltip("Candidate background neutrals — retained for parity with YAK, but unused in Bus Buddies (background is always emitted as empty cells).")]
        public string[] BackgroundNeutrals = { "Grey", "GreyLight", "GreyDark", "DarkGrey", "White", "Black" };

        [Header("Outline")]
        [Tooltip("Paint background cells orthogonally adjacent to the subject with OutlineColorId, producing a silhouette outline of pixel cells. On by default for Bus Buddies.")]
        public bool OutlineSubject = true;

        [Tooltip("Palette ColorId used for the outline. Falls back to the darkest palette color if absent.")]
        public string OutlineColorId = "Black";

        [Header("Segmentation")]
        [Tooltip("How subject is separated from background.")]
        public SegmentationMode Segmentation = SegmentationMode.BorderRing;

        [Header("Active Bus Row")]
        [Tooltip("Active-row slot count stamped into GameData[\"conveyorCount\"] on the new level.")]
        [Min(1)] public int DefaultActiveSlots = 5;

        public override LevelDocument Convert(Texture2D source, GameProfile profile)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            int W = Mathf.Max(1, profile.GridWidth);
            int H = Mathf.Max(1, profile.GridHeight);

            var palette = BuildPalette(profile);
            if (palette.Count == 0) throw new InvalidOperationException("Profile palette is empty.");

            // 1) Read + downscale (area average). avg[y*W+x], y=0 = bottom row.
            var (src, sw, sh) = ReadablePixels(source);
            var avg = Downscale(src, sw, sh, W, H);

            // 2) Segment subject vs background.
            bool[] isBg = Segment(avg, W, H, palette);

            // 3) Quantize every cell to its nearest palette color id.
            var ids = new string[W * H];
            for (int i = 0; i < ids.Length; i++) ids[i] = NearestId(avg[i], palette);

            // 3b) Outline mask BEFORE any bg mutation (bg cell adjacent to a subject cell).
            bool[] isOutline = OutlineSubject ? ComputeOutlineMask(isBg, W, H) : null;

            // 4) Apply outline color (a real pixel color, protected from the cap merge).
            string resolvedOutlineId = null;
            if (OutlineSubject && isOutline != null)
            {
                resolvedOutlineId = ResolveOutlineId(palette);
                if (resolvedOutlineId != null)
                    for (int i = 0; i < ids.Length; i++)
                        if (isOutline[i]) ids[i] = resolvedOutlineId;
            }

            // 5) Empty mask: non-outline background cells become BBEmptyCell.
            var isEmpty = new bool[W * H];
            for (int i = 0; i < isEmpty.Length; i++)
                isEmpty[i] = isBg[i] && (isOutline == null || !isOutline[i]);

            // 6) Merge subject/outline colors down to the cap (empties excluded from
            //    both the count and the merge; the outline id is protected).
            MergeToCap(ids, palette, ColorCap, isEmpty, resolvedOutlineId);

            // 7) Emit LevelDocument. Empty cell → BBEmptyCell; everything else → BBPixelCell.
            var grid = new GridData<ICellData>(W, H);
            for (int i = 0; i < ids.Length; i++)
                grid.Cells[i] = isEmpty[i]
                    ? (ICellData)new BBEmptyCell()
                    : new BBPixelCell { ColorId = ids[i] };

            string nowIso = DateTime.UtcNow.ToString("o");
            return new LevelDocument
            {
                SchemaVersion = profile.SchemaId + ".v1",
                LevelId       = "image_" + DateTime.UtcNow.ToString("yyMMdd_HHmmss"),
                DisplayName   = "Image · " + source.name,
                Metadata      = new LevelMetadata { Author = Environment.UserName, CreatedAt = nowIso, ModifiedAt = nowIso },
                Grid          = grid,
                GameData      = new JObject { ["conveyorCount"] = Mathf.Max(1, DefaultActiveSlots) },
            };
        }

        // ── Outline mask (copy-mirrored from YAKImageToGrid.ComputeOutlineMask) ──

        private static bool[] ComputeOutlineMask(bool[] isBg, int W, int H)
        {
            var mask = new bool[W * H];
            for (int i = 0; i < mask.Length; i++)
            {
                if (!isBg[i]) continue; // only bg cells can be outline
                int x = i % W, y = i / W;
                if ((x > 0     && !isBg[i - 1]) ||
                    (x < W - 1 && !isBg[i + 1]) ||
                    (y > 0     && !isBg[i - W]) ||
                    (y < H - 1 && !isBg[i + W]))
                    mask[i] = true;
            }
            return mask;
        }

        // Resolve the outline color id: OutlineColorId first, then darkest fallback.
        private string ResolveOutlineId(List<PaletteColor> palette)
        {
            foreach (var p in palette)
                if (string.Equals(p.Id, OutlineColorId, StringComparison.Ordinal)) return p.Id;

            if (palette.Count == 0)
            {
                Debug.LogWarning("[BusBuddiesImageToGrid] No palette colors for outline; skipping outline.");
                return null;
            }
            string darkest = null; float darkestLum = float.MaxValue;
            foreach (var p in palette)
            {
                float lum = Luminance(p.C);
                if (lum < darkestLum) { darkestLum = lum; darkest = p.Id; }
            }
            Debug.LogWarning($"[BusBuddiesImageToGrid] Outline color '{OutlineColorId}' not in palette; using darkest '{darkest}' instead.");
            return darkest;
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
                    return BorderRing(avg, W, H, palette);
                }
                case SegmentationMode.MostSaturated:
                    return BySaturation(avg, W, H);
                default:
                {
                    var bg = BorderRing(avg, W, H, palette);
                    float frac = Fraction(bg);
                    if (frac < 0.05f || frac > 0.95f)
                    {
                        Debug.Log($"[BusBuddiesImageToGrid] BorderRing segmentation ambiguous (bg {frac:P0}); falling back to MostSaturated.");
                        return BySaturation(avg, W, H);
                    }
                    return bg;
                }
            }
        }

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
            for (int i = 0; i < n; i++) bg[i] = sat[i] < median;
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

        private static double RedmeanDist(Color a, Color b)
        {
            double r1 = a.r * 255.0, g1 = a.g * 255.0, b1 = a.b * 255.0;
            double r2 = b.r * 255.0, g2 = b.g * 255.0, b2 = b.b * 255.0;
            double rmean = (r1 + r2) * 0.5;
            double dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
            return (2 + rmean / 256.0) * dr * dr + 4.0 * dg * dg + (2 + (255.0 - rmean) / 256.0) * db * db;
        }

        private static float Luminance(Color c) => 0.2126f * c.r + 0.7152f * c.g + 0.0722f * c.b;

        private static Color ColorOf(string id, List<PaletteColor> palette)
        {
            foreach (var p in palette) if (string.Equals(p.Id, id, StringComparison.Ordinal)) return p.C;
            return Color.gray;
        }

        // ── Color-cap merge (copy-mirrored from YAKImageToGrid.MergeToCap) ──────

        private static void MergeToCap(string[] ids, List<PaletteColor> palette, int cap,
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
    }
}
