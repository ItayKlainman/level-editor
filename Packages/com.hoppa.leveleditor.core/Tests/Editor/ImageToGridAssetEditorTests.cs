using System.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using Hoppa.LevelEditor.Core.Editor;

namespace Hoppa.LevelEditor.Core.EditorTests
{
    public class ImageToGridAssetEditorTests
    {
        // A minimal ImageToGridAsset subclass so CreateEditor has a concrete target.
        private sealed class DummyConverter : ImageToGridAsset
        {
            public override Hoppa.LevelEditor.Core.LevelDocument Convert(Texture2D s, GameProfile p) => null;
        }

        [Test]
        public void CreateEditor_ForConverter_ResolvesSharedCustomEditor()
        {
            var conv = ScriptableObject.CreateInstance<DummyConverter>();
            var ed = UnityEditor.Editor.CreateEditor(conv);
            Assert.IsInstanceOf<ImageToGridAssetEditor>(ed);
            Object.DestroyImmediate(ed);
            Object.DestroyImmediate(conv);
        }

        [Test]
        public void PaletteIds_FromActivePalette_AreUsableForDropdown()
        {
            // ActivePalette is the dropdown's source of truth; null → editor falls back to text.
            ImageToGridAssetEditor.ActivePalette = null;
            Assert.IsNull(ImageToGridAssetEditor.ActivePalette);
        }
    }
}
