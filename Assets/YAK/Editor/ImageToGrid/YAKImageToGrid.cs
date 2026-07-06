using System;
using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;
using UnityEngine;

namespace Hoppa.YAK.Editor
{
    // Converts a source image into a YAK level grid: a clean, blocky,
    // palette-quantized 30×30 of wool tiles. Pipeline:
    //   downscale (area average) → segment subject/background
    //   → compute outline mask (if enabled) → quantize subject to the profile palette
    //   → fill background (neutral or empty) → apply outline → merge down to color cap
    //   → emit LevelDocument.
    //
    // Colors are produced as palette string ColorIds (PascalCase YAK enum names);
    // int/enum mapping stays in YAKLevelExporter / YAKStaticManagerColorSource.
    [CreateAssetMenu(menuName = "Hoppa/YAK/Image To Grid")]
    public sealed class YAKImageToGrid : ImageToGridAsset
    {
        public enum SegmentationMode { BorderRing, Alpha, MostSaturated }

        // How background cells are emitted.
        // FillNeutral (default) preserves the original all-wool, no-empty behavior.
        // Empty emits YAKEmptyCell for non-outline background cells.
        public enum BackgroundFill { FillNeutral, Empty }

        [Tooltip("Dominant (default): each square takes its majority color — truer, less muddy. AreaAverage: legacy blur.")]
        public SampleMode Sampling = SampleMode.Dominant;

        [Tooltip("Optional: force chosen source colors to specific palette colors (e.g. a lime frog → Green). Empty = automatic nearest-color matching.")]
        public List<ColorRemap> ColorRemaps = new List<ColorRemap>();

        [Tooltip("Maximum number of distinct colors in the output (background neutral counts toward this).")]
        [Min(2)] public int ColorCap = 6;

        [Tooltip("Candidate background neutrals, most-preferred first. The one with the greatest luminance distance from the subject's dominant color is chosen.")]
        public string[] BackgroundNeutrals = { "Grey", "GreyLight", "GreyDark", "DarkGrey", "White", "Black" };

        [Tooltip("FillNeutral: background cells become the most-contrasting neutral (original behavior). Empty: background cells become YAKEmptyCell (required for Bus Buddies flood-accessibility).")]
        public BackgroundFill Background = BackgroundFill.FillNeutral;

        [Tooltip("Paint background cells that are orthogonally adjacent to the subject with OutlineColorId, producing a silhouette outline.")]
        public bool OutlineSubject = false;

        [Tooltip("Palette ColorId used for the outline. Falls back to the darkest palette color if absent.")]
        public string OutlineColorId = "Black";

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
            var palette = ImageToGridMath.BuildPalette(profile);
            if (palette.Count == 0) throw new InvalidOperationException("Profile palette is empty.");

            // 1) Read + downscale (area average or dominant). avg[y*W+x], y=0 = bottom row.
            var (src, sw, sh) = ReadablePixels(source);
            var avg = ImageToGridMath.Downscale(src, sw, sh, W, H, Sampling, palette);

            // 2) Segment subject vs background.
            bool[] isBg = Segment(avg, W, H, palette);

            // 3) Quantize: apply any color remap, else nearest palette color id.
            var ids = new string[W * H];
            for (int i = 0; i < ids.Length; i++)
                ids[i] = ImageToGridMath.ResolveRemap(avg[i], ColorRemaps, palette)
                         ?? ImageToGridMath.NearestId(avg[i], palette);

            // 3b) Compute outline mask BEFORE any bg mutation (preserves original isBg[]).
            //     isOutline[i] = bg cell orthogonally adjacent to at least one subject cell.
            bool[] isOutline = OutlineSubject ? ImageToGridMath.ComputeOutlineMask(isBg, W, H) : null;

            // 4) Background body: fill non-outline bg cells.
            if (Background == BackgroundFill.FillNeutral)
            {
                string subjectDominant = MostFrequent(ids, isBg, wantBackground: false) ?? ids[0];
                string bgId = ChooseBackgroundNeutral(subjectDominant, palette);
                if (bgId != null)
                    for (int i = 0; i < ids.Length; i++)
                        if (isBg[i] && (isOutline == null || !isOutline[i])) ids[i] = bgId;
            }
            // Empty mode: non-outline bg cells will emit YAKEmptyCell at step 7; ids[i] is irrelevant for them.

            // 5) Apply outline color (counts toward ColorCap, protected from merge).
            string resolvedOutlineId = null;
            if (OutlineSubject && isOutline != null)
            {
                resolvedOutlineId = ResolveOutlineId(palette);
                if (resolvedOutlineId != null)
                    for (int i = 0; i < ids.Length; i++)
                        if (isOutline[i]) ids[i] = resolvedOutlineId;
            }

            // Build isEmpty mask: cells that will emit YAKEmptyCell (non-outline bg, Empty mode only).
            bool[] isEmpty = null;
            if (Background == BackgroundFill.Empty)
            {
                isEmpty = new bool[W * H];
                for (int i = 0; i < isEmpty.Length; i++)
                    isEmpty[i] = isBg[i] && (isOutline == null || !isOutline[i]);
            }

            // 6) Merge down to color cap.
            //    • Empties are excluded from count and merge loop (they are not a palette color).
            //    • resolvedOutlineId is protected from being chosen as the least (never silently vanishes).
            ImageToGridMath.MergeToCap(ids, palette, ColorCap, isEmpty, resolvedOutlineId);

            // 7) Emit LevelDocument. Empty bg cell → YAKEmptyCell; all others → YAKWoolCell.
            var grid = new GridData<ICellData>(W, H);
            for (int i = 0; i < ids.Length; i++)
                grid.Cells[i] = (isEmpty != null && isEmpty[i])
                    ? (ICellData)new YAKEmptyCell()
                    : new YAKWoolCell { ColorId = ids[i] };

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

        // ── Outline color resolution ─────────────────────────────────────────────

        // Resolve the outline color id: try OutlineColorId first, then darkest-by-luminance fallback.
        private string ResolveOutlineId(List<PaletteColor> palette)
        {
            // Exact match in palette.
            foreach (var p in palette)
                if (string.Equals(p.Id, OutlineColorId, StringComparison.Ordinal)) return p.Id;

            // Fall back to darkest palette color by luminance.
            if (palette.Count == 0)
            {
                Debug.LogWarning("[YAKImageToGrid] No palette colors for outline; skipping outline.");
                return null;
            }
            string darkest = null; float darkestLum = float.MaxValue;
            foreach (var p in palette)
            {
                float lum = ImageToGridMath.Luminance(p.C);
                if (lum < darkestLum) { darkestLum = lum; darkest = p.Id; }
            }
            Debug.LogWarning($"[YAKImageToGrid] Outline color '{OutlineColorId}' not in palette; using darkest '{darkest}' instead.");
            return darkest;
        }

        // ── Pixel access ─────────────────────────────────────────────────────────

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
                    return ImageToGridMath.BorderRing(avg, W, H, palette); // no alpha → fall back
                }
                case SegmentationMode.MostSaturated:
                    return ImageToGridMath.BySaturation(avg, W, H);
                default:
                {
                    var bg = ImageToGridMath.BorderRing(avg, W, H, palette);
                    float frac = ImageToGridMath.Fraction(bg);
                    if (frac < 0.05f || frac > 0.95f)
                    {
                        Debug.Log($"[YAKImageToGrid] BorderRing segmentation ambiguous (bg {frac:P0}); falling back to MostSaturated.");
                        return ImageToGridMath.BySaturation(avg, W, H);
                    }
                    return bg;
                }
            }
        }

        // ── Background neutral selection ────────────────────────────────────────

        private string ChooseBackgroundNeutral(string subjectDominantId, List<PaletteColor> palette)
        {
            if (BackgroundNeutrals == null) return null;
            Color subjectColor = ColorOf(subjectDominantId, palette);
            float subjectLum = ImageToGridMath.Luminance(subjectColor);

            string best = null; float bestDist = -1f;
            foreach (var id in BackgroundNeutrals)
            {
                if (string.IsNullOrEmpty(id)) continue;
                if (string.Equals(id, subjectDominantId, StringComparison.Ordinal)) continue; // exclude subject's own id
                if (!TryColorOf(id, palette, out var c)) continue;                            // must exist in palette
                float dist = Mathf.Abs(ImageToGridMath.Luminance(c) - subjectLum);
                if (dist > bestDist) { bestDist = dist; best = id; }
            }
            return best;
        }

        private static Color ColorOf(string id, List<PaletteColor> palette)
            => TryColorOf(id, palette, out var c) ? c : Color.gray;

        private static bool TryColorOf(string id, List<PaletteColor> palette, out Color c)
        {
            foreach (var p in palette) if (string.Equals(p.Id, id, StringComparison.Ordinal)) { c = p.C; return true; }
            c = Color.gray; return false;
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
