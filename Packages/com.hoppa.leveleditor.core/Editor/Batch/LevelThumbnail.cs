using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Renders a LevelDocument's grid to a flat Texture2D thumbnail — one (or N)
    // pixels per cell, colored via the profile palette. Game-agnostic: any colored
    // cell (IColoredCell) is drawn with its palette color; everything else uses the
    // empty color. Grid y=0 is the bottom row and Texture2D pixel (0,0) is bottom-
    // left, so the thumbnail comes out upright with no flip.
    public static class LevelThumbnail
    {
        public static Texture2D Render(LevelDocument doc, IColorPalette palette,
            int cellPixels = 1, Color empty = default)
        {
            if (doc?.Grid == null) return null;
            int gw = doc.Grid.Width, gh = doc.Grid.Height;
            cellPixels = Mathf.Max(1, cellPixels);
            int tw = gw * cellPixels, th = gh * cellPixels;

            var px = new Color[tw * th];
            for (int gy = 0; gy < gh; gy++)
            for (int gx = 0; gx < gw; gx++)
            {
                var cell = doc.Grid.Get(gx, gy);
                Color c = empty;
                if (cell is IColoredCell col && palette != null
                    && palette.TryGetColor(col.ColorId, out var pc))
                    c = pc;

                for (int dy = 0; dy < cellPixels; dy++)
                for (int dx = 0; dx < cellPixels; dx++)
                {
                    int tx = gx * cellPixels + dx;
                    int ty = gy * cellPixels + dy;
                    px[ty * tw + tx] = c;
                }
            }

            var tex = new Texture2D(tw, th, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
            tex.SetPixels(px);
            tex.Apply();
            return tex;
        }

        // Encodes a rendered thumbnail to PNG bytes (for writing alongside batch
        // candidates). Returns null when doc/grid is null.
        public static byte[] RenderPng(LevelDocument doc, IColorPalette palette, int cellPixels = 1, Color empty = default)
        {
            var tex = Render(doc, palette, cellPixels, empty);
            if (tex == null) return null;
            var bytes = tex.EncodeToPNG();
            Object.DestroyImmediate(tex);
            return bytes;
        }
    }
}
