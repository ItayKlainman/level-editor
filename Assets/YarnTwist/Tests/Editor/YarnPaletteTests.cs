using System.Collections.Generic;
using System.Linq;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YarnTwist;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor.Tests
{
    // YarnPalettes helper + right-click Add/Remove/Requirement authoring + validation.
    public class YarnPaletteTests
    {
        private const string ProfilePath = "Assets/YarnTwist/Data/Config/YarnTwistProfile.asset";
        private GameProfile _profile;

        [SetUp]
        public void SetUp()
        {
            _profile = AssetDatabase.LoadAssetAtPath<GameProfile>(ProfilePath);
            Assert.IsNotNull(_profile);
        }

        // ── Helper: CanPlace / CoveredCells / TryPaletteAt ──────────────

        [Test]
        public void CanPlace_ValidCenter_True()
        {
            var grid = BoxGrid(3, 3);
            Assert.IsTrue(YarnPalettes.CanPlace(grid, new CellRef(1, 1), new List<YarnPalette>()));
        }

        [Test]
        public void CanPlace_EdgeCenter_FalseOutOfBounds()
        {
            var grid = BoxGrid(3, 3);
            Assert.IsFalse(YarnPalettes.CanPlace(grid, new CellRef(0, 0), new List<YarnPalette>()));
        }

        [Test]
        public void CanPlace_NonBoxInArea_False()
        {
            var grid = BoxGrid(3, 3);
            grid.Set(0, 0, new YarnWallCell()); // a covered cell is not a box
            Assert.IsFalse(YarnPalettes.CanPlace(grid, new CellRef(1, 1), new List<YarnPalette>()));
        }

        [Test]
        public void CanPlace_OverlapsExisting_False()
        {
            var grid = BoxGrid(7, 3);
            var existing = new List<YarnPalette> { new YarnPalette { CenterX = 1, CenterY = 1, Amount = 5 } };
            Assert.IsFalse(YarnPalettes.CanPlace(grid, new CellRef(3, 1), existing)); // |dx|=2 → overlap
            Assert.IsTrue(YarnPalettes.CanPlace(grid, new CellRef(4, 1), existing));  // |dx|=3 → clear
        }

        [Test]
        public void CoveredCells_ReturnsNineAroundCenter()
        {
            var covered = YarnPalettes.CoveredCells(new CellRef(2, 2)).ToList();
            Assert.AreEqual(9, covered.Count);
            Assert.Contains(new CellRef(2, 2), covered);
            Assert.Contains(new CellRef(1, 1), covered);
            Assert.Contains(new CellRef(3, 3), covered);
        }

        [Test]
        public void TryPaletteAt_FindsCoveringPalette_AndNullOutside()
        {
            var doc = BoxDoc(5, 3);
            YarnPalettes.Add(doc, new CellRef(1, 1));
            Assert.IsTrue(YarnPalettes.TryPaletteAt(doc, new CellRef(2, 2), out _)); // corner of the 3x3
            Assert.IsFalse(YarnPalettes.TryPaletteAt(doc, new CellRef(4, 1), out _)); // outside
        }

        // ── Authoring (context actions on yt.box) ───────────────────────

        [Test]
        public void AddPalette_OfferedForValidCenter_NotForEdge()
        {
            var s = Session(BoxDoc(3, 3));
            Assert.IsTrue(Labels(s, 1, 1).Any(l => l.StartsWith("Add Palette")));
            Assert.IsFalse(Labels(s, 0, 0).Any(l => l.StartsWith("Add Palette"))); // edge → 3x3 off-grid
        }

        [Test]
        public void Add_StoresPaletteWithDefaultAmount()
        {
            var s = Session(BoxDoc(3, 3));
            Actions(s, 1, 1).First(a => a.Label.StartsWith("Add Palette")).Apply(s);

            var all = YarnPalettes.All(s.Document);
            Assert.AreEqual(1, all.Count);
            Assert.AreEqual(1, all[0].CenterX);
            Assert.AreEqual(1, all[0].CenterY);
            Assert.AreEqual(YarnPalettes.DefaultAmount, all[0].Amount);
        }

        [Test]
        public void CoveredBox_OffersRemoveAndRequirement_NotAdd()
        {
            var s = Session(BoxDoc(3, 3));
            YarnPalettes.Add(s.Document, new CellRef(1, 1));

            var labels = Labels(s, 0, 0); // a covered (non-center) box
            Assert.IsTrue(labels.Any(l => l.StartsWith("Remove Palette")));
            Assert.IsTrue(labels.Any(l => l.StartsWith("Set Palette Requirement")));
            Assert.IsFalse(labels.Any(l => l.StartsWith("Add Palette")));
        }

        [Test]
        public void RemovePalette_ClearsIt()
        {
            var s = Session(BoxDoc(3, 3));
            YarnPalettes.Add(s.Document, new CellRef(1, 1));
            Actions(s, 2, 2).First(a => a.Label.StartsWith("Remove Palette")).Apply(s);
            Assert.AreEqual(0, YarnPalettes.All(s.Document).Count);
        }

        [Test]
        public void AddPalette_ThenUndo_RestoresNoPalette()
        {
            var s = Session(BoxDoc(3, 3));
            s.PushUndoSnapshot(); // GridCellPopup snapshots before applying
            Actions(s, 1, 1).First(a => a.Label.StartsWith("Add Palette")).Apply(s);
            Assert.AreEqual(1, YarnPalettes.All(s.Document).Count);

            Assert.IsTrue(s.Undo()); // exercises GameData["palettes"] round-trip
            Assert.AreEqual(0, YarnPalettes.All(s.Document).Count);
        }

        // ── Validation ──────────────────────────────────────────────────

        [Test]
        public void Rule_ValidPalette_NoErrors()
        {
            var doc = BoxDoc(3, 3);
            YarnPalettes.Add(doc, new CellRef(1, 1));
            Assert.AreEqual(0, RunRule(doc).Count);
        }

        [Test]
        public void Rule_CoveredCellNotBox_Errors()
        {
            var doc = BoxDoc(3, 3);
            YarnPalettes.Add(doc, new CellRef(1, 1));
            doc.Grid.Set(0, 0, new YarnWallCell()); // break a covered cell after placement
            Assert.IsTrue(RunRule(doc).Count >= 1);
        }

        [Test]
        public void Rule_OverlappingPalettes_Errors()
        {
            var doc = BoxDoc(7, 3);
            YarnPalettes.Write(doc, new List<YarnPalette>
            {
                new YarnPalette { CenterX = 1, CenterY = 1, Amount = 5 },
                new YarnPalette { CenterX = 3, CenterY = 1, Amount = 5 }, // |dx|=2 → overlap
            });
            Assert.IsTrue(RunRule(doc).Any(e => e.Message.Contains("overlap")));
        }

        [Test]
        public void Rule_OffGridPalette_Errors()
        {
            var doc = BoxDoc(3, 3);
            YarnPalettes.Write(doc, new List<YarnPalette>
            {
                new YarnPalette { CenterX = 0, CenterY = 0, Amount = 5 }, // 3x3 extends off-grid
            });
            Assert.IsTrue(RunRule(doc).Any(e => e.Message.Contains("outside the grid")));
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static GridData<ICellData> BoxGrid(int w, int h)
        {
            var grid = new GridData<ICellData>(w, h);
            for (int i = 0; i < grid.Cells.Length; i++) grid.Cells[i] = new YarnBoxCell { ColorId = "pink" };
            return grid;
        }

        private static LevelDocument BoxDoc(int w, int h) => new LevelDocument
        {
            SchemaVersion = "yarn-twist.v1", LevelId = "test", Grid = BoxGrid(w, h), TopSection = new JObject(),
        };

        private LevelEditorSession Session(LevelDocument doc) => new LevelEditorSession(_profile, doc);

        private static List<CellContextAction> Actions(LevelEditorSession s, int x, int y)
        {
            s.CellTypes.TryGetDefinition("yt.box", out var def);
            var ca  = (ICellContextActions)def;
            var ctx = new CellActionContext(s.Document.Grid.Get(x, y), s.CellTypes, s, new CellRef(x, y));
            return ca.GetContextActions(ctx).ToList();
        }

        private static List<string> Labels(LevelEditorSession s, int x, int y) =>
            Actions(s, x, y).Select(a => a.Label).ToList();

        private static List<ValidationEntry> RunRule(LevelDocument doc)
        {
            var rule = ScriptableObject.CreateInstance<YarnPaletteRule>();
            rule.Configure("yt.palette");
            try { return rule.Evaluate(new ValidationContext(doc)).ToList(); }
            finally { Object.DestroyImmediate(rule); }
        }
    }
}
