using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    // Layer 1 contract: convert a source image into a LevelDocument grid.
    //
    // The converter reads the grid size and color palette from the profile and
    // quantizes the image to palette colors (string ColorIds). Game-specific
    // enum/int color mapping stays in the game's exporter / color source — Layer 1
    // never sees a color enum. The produced LevelDocument is handed to the editor
    // via the same load path the generator uses (OnUseLevel).
    public interface IImageToGrid
    {
        LevelDocument Convert(Texture2D source, GameProfile profile);
    }
}
