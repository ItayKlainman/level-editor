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
        public abstract LevelDocument Convert(Texture2D source, GameProfile profile);
    }
}
