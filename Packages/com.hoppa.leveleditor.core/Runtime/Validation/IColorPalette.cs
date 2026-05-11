using System.Collections.Generic;
using UnityEngine;

namespace Hoppa.LevelEditor.Core
{
    // Read-only palette view used by validation rules.
    // ColorPaletteAsset (Editor SO) implements this so ValidationContext
    // stays in the Runtime assembly.
    public interface IColorPalette
    {
        bool Contains(string colorId);
        IEnumerable<string> ColorIds { get; }
        bool TryGetColor(string colorId, out Color color);
    }
}
