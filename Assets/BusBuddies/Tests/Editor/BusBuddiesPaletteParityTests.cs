using System.Collections.Generic;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Hoppa.LevelEditor.Core.Editor;

namespace Hoppa.BusBuddies.Editor.Tests
{
    // Colour parity guard: the editor palette a designer paints with MUST match the
    // Bus Buddies GAME colours exactly, so a level looks in-editor as it will in-game.
    //
    // GAME IS THE SOURCE OF TRUTH. The expected values below are a byte-for-byte
    // snapshot of BUBStaticManagerScriptableObject.GetHexByName in the BB game project
    // (E:/Projects/Hoppa/BusBuddies), taken 2026-07-12. The two projects are separate
    // Unity solutions, so this can't reference the game type directly — if the GAME
    // ever changes a colour, re-sync this table (and the palette asset) to match.
    public sealed class BusBuddiesPaletteParityTests
    {
        private const string PalettePath =
            "Assets/BusBuddies/Data/Config/Palette/BusBuddiesPalette.asset";

        // id (lowercase enum name) -> game hex (RRGGBB), from GetHexByName.
        private static readonly Dictionary<string, string> GameHex = new()
        {
            ["red"] = "E81E2C", ["blue"] = "1A5FE0", ["yellow"] = "FFD60A",
            ["orange"] = "FF8A1C", ["purple"] = "A035DB", ["pink"] = "FFB3C7",
            ["cyan"] = "5BC8F0", ["bluedark"] = "0B2E78", ["green"] = "1FA22A",
            ["magenta"] = "E83AAB", ["greenlime"] = "5EE83F", ["turquoise"] = "1FCBD4",
            ["purplebright"] = "C45EDB", ["white"] = "F5F5F5", ["grey"] = "A8AFBC",
            ["darkgrey"] = "3D4250", ["black"] = "1A1B23", ["brown"] = "A05A2A",
            ["browndark"] = "5C3624", ["brownlight"] = "D4A164", ["skin"] = "F5C896",
            ["greylight"] = "D0D5DE", ["greydark"] = "5D6577", ["blueocean"] = "1448B5",
            ["blueroyal"] = "3458CC", ["bluesky"] = "62B0F5", ["greendark"] = "0A6B1F",
            ["greengrass"] = "6BC834", ["gold"] = "FFB81C", ["orangered"] = "FF4A1C",
            ["orangelight"] = "FFCB85", ["purpledark"] = "4B1485",
            ["turquoiselight"] = "9FE8E6", ["pinkdark"] = "D43872",
            ["yellowpale"] = "FFEC8A", ["brownverydark"] = "553128",
        };

        private static ColorPaletteAsset LoadPalette()
        {
            var palette = AssetDatabase.LoadAssetAtPath<ColorPaletteAsset>(PalettePath);
            Assert.IsNotNull(palette, $"BusBuddiesPalette not found at {PalettePath}");
            return palette;
        }

        private static string ToHex(Color c) =>
            $"{Mathf.RoundToInt(c.r * 255f):X2}" +
            $"{Mathf.RoundToInt(c.g * 255f):X2}" +
            $"{Mathf.RoundToInt(c.b * 255f):X2}";

        [Test]
        public void EveryGameColor_ExistsInEditorPalette_WithExactHex()
        {
            var palette = LoadPalette();
            foreach (var kv in GameHex)
            {
                Assert.IsTrue(palette.TryGetColor(kv.Key, out var color),
                    $"Editor palette is missing colour id '{kv.Key}'.");
                Assert.AreEqual(kv.Value, ToHex(color),
                    $"Colour '{kv.Key}' differs from the game (game is source of truth).");
            }
        }

        [Test]
        public void EditorPalette_HasNoColorsAbsentFromGame()
        {
            var palette = LoadPalette();
            foreach (var id in palette.ColorIds)
                Assert.IsTrue(GameHex.ContainsKey(id),
                    $"Editor palette has id '{id}' with no matching game colour.");
        }
    }
}
