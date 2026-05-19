using System;
using System.Collections.Generic;
using Hoppa.LevelEditor.Core.Editor;
using UnityEngine;
using YAK.Gamelogic;

namespace Hoppa.YAK.Editor
{
    // Editor-side color source that derives all of its data from a referenced
    // YAKStaticManagerScriptableObject (the game's source of truth for colors).
    // Inherits ColorPaletteAsset so it slots into every existing consumer:
    // YAKWoolCellDefinition._palette, ColorSwatchDrawer, ValidationContext.Palette.
    //
    // Replaces both YAKColorMapping (string→int) and YAKPalette (string→Color)
    // — those legacy assets can be deleted once this is wired in.
    [CreateAssetMenu(menuName = "Hoppa/YAK/Static Manager Color Source")]
    public sealed class YAKStaticManagerColorSource : ColorPaletteAsset
    {
        [Tooltip("Reference to the game's StaticManager ScriptableObject — single source of truth for all colors. Mirror or share-via-GUID with the game project's StaticManager.asset.")]
        [SerializeField] private YAKStaticManagerScriptableObject _staticManager;

        // Hot-path cache: ColorSwatchDrawer calls Entries multiple times per
        // IMGUI tick, and OnGUI fires repeatedly during a mouse drag. Without
        // this, every paint stroke allocates 36 fresh ColorEntry objects per
        // call and stalls the editor. Invalidated on OnEnable (load / domain
        // reload) and OnValidate (inspector edit).
        private IReadOnlyList<ColorEntry> _cached;

        private void OnEnable()   { _cached = null; }
        private void OnValidate() { _cached = null; }

        public YAKStaticManagerScriptableObject StaticManager => _staticManager;

        // colorId is the PascalCase enum name (e.g. "Blue", "BlueDark"). Returns
        // fallback when the id is empty or not a YAKColorType member.
        public int GetInt(string colorId, int fallback = 0)
        {
            if (string.IsNullOrEmpty(colorId)) return fallback;
            return Enum.TryParse<YAKColorType>(colorId, out var ct) ? (int)ct : fallback;
        }

        // Inverse of GetInt — maps an int back to the canonical enum-name colorId.
        // Returns null when the int doesn't match any defined enum value.
        public string GetColorId(int value)
        {
            return Enum.IsDefined(typeof(YAKColorType), value) && value != 0
                ? ((YAKColorType)value).ToString()
                : null;
        }

        protected override IReadOnlyList<ColorEntry> ResolveEntries()
        {
            if (_cached != null) return _cached;

            var helpers = _staticManager?.ColorTypeHelpers;
            if (helpers == null) return _cached = Array.Empty<ColorEntry>();

            var entries = new List<ColorEntry>(helpers.Count);
            foreach (var h in helpers)
            {
                if (h == null || h.ColorType == YAKColorType.None) continue;
                var name = h.ColorType.ToString();
                entries.Add(new ColorEntry
                {
                    Id          = name,
                    DisplayName = name,
                    Color       = h.Color,
                });
            }
            return _cached = entries;
        }
    }
}
