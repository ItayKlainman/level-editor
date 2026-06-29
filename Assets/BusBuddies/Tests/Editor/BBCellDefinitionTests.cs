using NUnit.Framework;
using Hoppa.LevelEditor.Core;
using Hoppa.BusBuddies;
using Hoppa.BusBuddies.Editor;
using UnityEngine;

namespace Hoppa.BusBuddies.Editor.Tests
{
    public sealed class BBCellDefinitionTests
    {
        [Test]
        public void PixelDefinition_CreatesPixelCell()
        {
            var def = ScriptableObject.CreateInstance<BBPixelCellDefinition>();
            var cell = def.CreateDefault();
            Assert.IsInstanceOf<BBPixelCell>(cell);
            Assert.AreEqual("bb.pixel", cell.CellTypeId);
            Assert.IsInstanceOf<IColoredCell>(cell);
        }

        [Test]
        public void EmptyDefinition_CreatesEmptyCell()
        {
            var def = ScriptableObject.CreateInstance<BBEmptyCellDefinition>();
            var cell = def.CreateDefault();
            Assert.IsInstanceOf<BBEmptyCell>(cell);
            Assert.AreEqual("bb.empty", cell.CellTypeId);
        }
    }
}
