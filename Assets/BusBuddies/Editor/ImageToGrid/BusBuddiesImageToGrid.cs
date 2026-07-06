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
    //   downscale (area average or dominant) → segment subject/background
    //   → compute outline mask → quantize subject to the profile palette
    //     (applying any color remap, else nearest) → outline ring becomes
    //     BBPixelCell(OutlineColorId) → non-outline background becomes BBEmptyCell
    //   → merge subject/outline colors down to the cap
    //   → emit LevelDocument, stamping GameData["conveyorCount"] = DefaultActiveSlots.
    //
    // All pure color math routes through the shared ImageToGridMath in
    // Hoppa.LevelEditor.Core.Editor; only texture IO (ReadablePixels), the
    // segmentation policy wrapper, and outline-id resolution stay local.
    [CreateAssetMenu(menuName = "Hoppa/BusBuddies/Image To Grid")]
    public sealed class BusBuddiesImageToGrid : ImageToGridAsset
    {
        public enum SegmentationMode { BorderRing, Alpha, MostSaturated }

        [Tooltip("Dominant (default): each square takes its majority color — truer, less muddy. AreaAverage: legacy blur.")]
        public SampleMode Sampling = SampleMode.Dominant;

        [Tooltip("Optional: force chosen source colors to specific palette colors (e.g. a lime frog → green). Empty = automatic nearest-color matching.")]
        public List<ColorRemap> ColorRemaps = new List<ColorRemap>();

        [Tooltip("Maximum number of distinct pixel colors in the output (outline color counts toward this). Empty background cells do not count.")]
        [Min(2)] public int ColorCap = 6;

        [Tooltip("Candidate background neutrals — retained for parity with YAK, but unused in Bus Buddies (background is always emitted as empty cells).")]
        public string[] BackgroundNeutrals = { "Grey", "GreyLight", "GreyDark", "DarkGrey", "White", "Black" };

        [Tooltip("Paint background cells orthogonally adjacent to the subject with OutlineColorId, producing a silhouette outline of pixel cells. On by default for Bus Buddies.")]
        public bool OutlineSubject = true;

        [Tooltip("Palette ColorId used for the outline. Falls back to the darkest palette color if absent.")]
        public string OutlineColorId = "Black";

        [Tooltip("How subject is separated from background.")]
        public SegmentationMode Segmentation = SegmentationMode.BorderRing;

        [Tooltip("Active-row slot count stamped into GameData[\"conveyorCount\"] on the new level.")]
        [Min(1)] public int DefaultActiveSlots = 5;

        public override LevelDocument Convert(Texture2D source, GameProfile profile)
        {
            if (source == null) throw new ArgumentNullException(nameof(source));
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            int W = Mathf.Max(1, profile.GridWidth);
            int H = Mathf.Max(1, profile.GridHeight);

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

            // 3b) Outline mask BEFORE any bg mutation (bg cell adjacent to a subject cell).
            bool[] isOutline = OutlineSubject ? ImageToGridMath.ComputeOutlineMask(isBg, W, H) : null;

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
            ImageToGridMath.MergeToCap(ids, palette, ColorCap, isEmpty, resolvedOutlineId);

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

        // ── Outline color resolution ─────────────────────────────────────────────

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
                float lum = ImageToGridMath.Luminance(p.C);
                if (lum < darkestLum) { darkestLum = lum; darkest = p.Id; }
            }
            Debug.LogWarning($"[BusBuddiesImageToGrid] Outline color '{OutlineColorId}' not in palette; using darkest '{darkest}' instead.");
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
                    return ImageToGridMath.BorderRing(avg, W, H, palette);
                }
                case SegmentationMode.MostSaturated:
                    return ImageToGridMath.BySaturation(avg, W, H);
                default:
                {
                    var bg = ImageToGridMath.BorderRing(avg, W, H, palette);
                    float frac = ImageToGridMath.Fraction(bg);
                    if (frac < 0.05f || frac > 0.95f)
                    {
                        Debug.Log($"[BusBuddiesImageToGrid] BorderRing segmentation ambiguous (bg {frac:P0}); falling back to MostSaturated.");
                        return ImageToGridMath.BySaturation(avg, W, H);
                    }
                    return bg;
                }
            }
        }
    }
}
