using System;
using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using UnityEngine;

namespace Hoppa.LevelEditor.Core.Editor
{
    [CreateAssetMenu(menuName = "Hoppa/Level Editor/Color Palette", order = 1)]
    public sealed class ColorPaletteAsset : ScriptableObject, IColorPalette
    {
        [Tooltip("List of colors available in this game. Each entry must have a unique Id.")]
        [SerializeField]
        private List<ColorEntry> _entries = new List<ColorEntry>();

        public IReadOnlyList<ColorEntry> Entries => _entries;

        public bool Contains(string colorId)
        {
            foreach (var entry in _entries)
                if (string.Equals(entry.Id, colorId, StringComparison.Ordinal)) return true;
            return false;
        }

        public IEnumerable<string> ColorIds
        {
            get { foreach (var entry in _entries) yield return entry.Id; }
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
        [Tooltip("Unique string ID referenced in level JSON.\nExample: 'red'")]
        public string Id;

        [Tooltip("Human-readable name shown in inspector dropdowns.\nExample: 'Red'")]
        public string DisplayName;

        [Tooltip("RGBA color used for rendering in the canvas and palette.")]
        public Color Color = Color.white;

        [Tooltip("Optional swatch icon. Leave empty to auto-render the Color as a filled square.")]
        public Texture2D SwatchIcon;
    }
}
