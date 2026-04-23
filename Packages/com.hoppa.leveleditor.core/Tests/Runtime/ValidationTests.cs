using NUnit.Framework;
using UnityEngine;
using Hoppa.LevelEditor.Core;

namespace Hoppa.LevelEditor.Core.Tests
{
    [TestFixture]
    public sealed class ValidationTests
    {
        // ── PaletteColorsExistRule ────────────────────────────────────────────

        [Test]
        public void PaletteColorsExist_MissingColor_ReturnsError()
        {
            var rule = ScriptableObject.CreateInstance<PaletteColorsExistRule>();
            rule.Configure("core.palette-colors-exist");

            var grid = new GridData<ICellData>(1, 1);
            grid.Set(0, 0, new TestColoredCell("box", "purple"));

            var ctx = new ValidationContext(
                new LevelDocument { Grid = grid },
                new TestPalette("red", "blue")); // "purple" not in palette

            var entries = new System.Collections.Generic.List<ValidationEntry>(rule.Evaluate(ctx));

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(ValidationSeverity.Error, entries[0].Severity);
            StringAssert.Contains("purple", entries[0].Message);
        }

        [Test]
        public void PaletteColorsExist_AllColorsPresent_ReturnsNoEntries()
        {
            var rule = ScriptableObject.CreateInstance<PaletteColorsExistRule>();
            rule.Configure("core.palette-colors-exist");

            var grid = new GridData<ICellData>(2, 1);
            grid.Set(0, 0, new TestColoredCell("box", "red"));
            grid.Set(1, 0, new TestColoredCell("box", "blue"));

            var ctx = new ValidationContext(
                new LevelDocument { Grid = grid },
                new TestPalette("red", "blue"));

            var entries = new System.Collections.Generic.List<ValidationEntry>(rule.Evaluate(ctx));
            Assert.AreEqual(0, entries.Count);
        }

        // ── GridNonEmptyRule ──────────────────────────────────────────────────

        [Test]
        public void GridNonEmpty_AllEmpty_ReturnsError()
        {
            var rule = ScriptableObject.CreateInstance<GridNonEmptyRule>();
            rule.Configure("core.grid-non-empty", "core.empty");

            var grid = new GridData<ICellData>(2, 2);
            grid.Set(0, 0, new TestPlainCell("core.empty"));
            grid.Set(1, 0, new TestPlainCell("core.empty"));
            grid.Set(0, 1, new TestPlainCell("core.empty"));
            grid.Set(1, 1, new TestPlainCell("core.empty"));

            var ctx = new ValidationContext(new LevelDocument { Grid = grid });
            var entries = new System.Collections.Generic.List<ValidationEntry>(rule.Evaluate(ctx));

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(ValidationSeverity.Error, entries[0].Severity);
        }

        [Test]
        public void GridNonEmpty_HasContent_ReturnsNoEntries()
        {
            var rule = ScriptableObject.CreateInstance<GridNonEmptyRule>();
            rule.Configure("core.grid-non-empty", "core.empty");

            var grid = new GridData<ICellData>(2, 1);
            grid.Set(0, 0, new TestPlainCell("core.empty"));
            grid.Set(1, 0, new TestColoredCell("box", "red")); // content

            var ctx = new ValidationContext(new LevelDocument { Grid = grid });
            var entries = new System.Collections.Generic.List<ValidationEntry>(rule.Evaluate(ctx));

            Assert.AreEqual(0, entries.Count);
        }

        // ── ColorBalanceRule ──────────────────────────────────────────────────

        [Test]
        public void ColorBalance_Imbalanced_ReturnsError()
        {
            var rule = ScriptableObject.CreateInstance<ColorBalanceRule>();
            rule.Configure("core.color-balance", "source", "sink", sourceValue: 1, sinkValue: 1);

            var grid = new GridData<ICellData>(3, 1);
            grid.Set(0, 0, new TestColoredCell("source", "red")); // 1 source
            grid.Set(1, 0, new TestColoredCell("sink",   "red")); // 1 sink  → balanced
            grid.Set(2, 0, new TestColoredCell("source", "blue")); // 1 source blue, no sink → error

            var ctx = new ValidationContext(new LevelDocument { Grid = grid });
            var entries = new System.Collections.Generic.List<ValidationEntry>(rule.Evaluate(ctx));

            Assert.AreEqual(1, entries.Count);
            Assert.AreEqual(ValidationSeverity.Error, entries[0].Severity);
            StringAssert.Contains("blue", entries[0].Message);
        }

        [Test]
        public void ColorBalance_Balanced_ReturnsNoEntries()
        {
            var rule = ScriptableObject.CreateInstance<ColorBalanceRule>();
            rule.Configure("core.color-balance", "box", "spool", sourceValue: 9, sinkValue: 3);

            // 1 box (9) balanced by 3 spools (3 each) for red
            var grid = new GridData<ICellData>(4, 1);
            grid.Set(0, 0, new TestColoredCell("box",   "red"));
            grid.Set(1, 0, new TestColoredCell("spool", "red"));
            grid.Set(2, 0, new TestColoredCell("spool", "red"));
            grid.Set(3, 0, new TestColoredCell("spool", "red"));

            var ctx = new ValidationContext(new LevelDocument { Grid = grid });
            var entries = new System.Collections.Generic.List<ValidationEntry>(rule.Evaluate(ctx));

            Assert.AreEqual(0, entries.Count);
        }

        // ── test doubles ─────────────────────────────────────────────────────

        private sealed class TestPalette : IColorPalette
        {
            private readonly System.Collections.Generic.HashSet<string> _ids;

            public TestPalette(params string[] ids)
                => _ids = new System.Collections.Generic.HashSet<string>(ids);

            public bool Contains(string colorId) => _ids.Contains(colorId);
            public System.Collections.Generic.IEnumerable<string> ColorIds => _ids;
        }

        private sealed class TestColoredCell : IColoredCell
        {
            private readonly string _typeId;
            public string CellTypeId => _typeId;
            public string ColorId { get; }

            public TestColoredCell(string typeId, string colorId)
            {
                _typeId = typeId;
                ColorId = colorId;
            }
        }

        private sealed class TestPlainCell : ICellData
        {
            public string CellTypeId { get; }
            public TestPlainCell(string typeId) => CellTypeId = typeId;
        }
    }
}
