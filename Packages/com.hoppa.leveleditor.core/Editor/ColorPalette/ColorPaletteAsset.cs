using System;
using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Level Editor/Color Palette", order = 1)]
    public sealed class ColorPaletteAsset : ScriptableObject, IColorPalette
    {
        [SerializeField]
        private List<ColorEntry> _entries = new List<ColorEntry>();

        public IReadOnlyList<ColorEntry> Entries => _entries;

        // IColorPalette
        public bool Contains(string colorId)
        {
            foreach (var entry in _entries)
                if (string.Equals(entry.Id, colorId, StringComparison.Ordinal)) return true;
            return false;
        }

        public IEnumerable<string> ColorIds
        {
            get
            {
                foreach (var entry in _entries) yield return entry.Id;
            }
        }

        public bool TryGetColor(string colorId, out Color color)
        {
            foreach (var entry in _entries)
            {
                if (string.Equals(entry.Id, colorId, StringComparison.Ordinal))
                {
                    color = entry.Color;
                    return true;
                }
            }
            color = Color.white;
            return false;
        }
    }

    [Serializable]
    public sealed class ColorEntry
    {
        public string Id;
        public string DisplayName;
        public Color Color = Color.white;
        public Texture2D SwatchIcon;
    }
}
