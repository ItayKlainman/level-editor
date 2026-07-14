using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Abstract ScriptableObject base for an image→grid converter. GameProfile's
    // _imageToGrid field is typed as this so the inspector type-filters, and the
    // toolbar's 🖼 Image mode renders this asset's own inspector for tuning
    // (color cap, background neutrals, segmentation, …) before calling Convert.
    //
    // Mirrors the LevelGeneratorAsset pattern. Game-specific config lives as
    // serialized fields on the concrete subclass (or an SO it references).
    public abstract class ImageToGridAsset : ScriptableObject, IImageToGrid
    {
        // The pipeline. Subclasses implement conversion at an explicit grid size.
        public abstract LevelDocument Convert(Texture2D source, GameProfile profile, int width, int height);

        // Convenience overload: convert at the profile's configured grid size (what the
        // generator and other callers use). The Image→Grid tab calls the 4-arg form to
        // let the designer choose a size before converting.
        public LevelDocument Convert(Texture2D source, GameProfile profile)
            => Convert(source, profile,
                       Mathf.Max(1, profile != null ? profile.GridWidth : 1),
                       Mathf.Max(1, profile != null ? profile.GridHeight : 1));
    }
}
