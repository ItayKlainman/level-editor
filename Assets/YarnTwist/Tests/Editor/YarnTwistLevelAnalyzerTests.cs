using System.Collections.Generic;
using Hoppa.LevelEditor.Core;
using Hoppa.LevelEditor.Core.Editor;
using Hoppa.YarnTwist;
using Hoppa.YarnTwist.Editor;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace Hoppa.YarnTwist.Editor.Tests
{
    public class YarnTwistLevelAnalyzerTests
    {
        private const string ProfilePath = "Assets/YarnTwist/Data/Config/YarnTwistProfile.asset";
        private GameProfile _profile;
        private YarnTwistLevelAnalyzer _analyzer;

        [SetUp]
        public void SetUp()
        {
            _profile = AssetDatabase.LoadAssetAtPath<GameProfile>(ProfilePath);
            Assert.IsNotNull(_profile);
            _analyzer = ScriptableObject.CreateInstance<YarnTwistLevelAnalyzer>();
        }

        [TearDown]
        public void TearDown()
        {
            if (_analyzer != null) Object.DestroyImmediate(_analyzer);
        }

        // ── Fixture 1: 1 pink box, 3 pink spools → 1 path ───────────────

        [Test]
        public void Analyze_SingleBoxWithMatchedSpools_ReportsExactlyOnePath()
        {
            var doc = MakeDoc(width: 1, height: 1,
                cells: new (int, int, ICellData)[] { (0, 0, new YarnBoxCell { ColorId = "pink" }) },
                topSection: SpoolColumns(("pink","pink","pink"), (null,null,null), (null,null,null), (null,null,null)));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Solvable, r.FailureReason);
            Assert.AreEqual(1L, r.WinPathCount);
            Assert.IsFalse(r.CountWasCapped);
        }

        // ── Fixture 2: color mismatch → 0 paths ─────────────────────────

        [Test]
        public void Analyze_ColorMismatch_ReportsUnsolvable()
        {
            var doc = MakeDoc(width: 1, height: 1,
                cells: new (int, int, ICellData)[] { (0, 0, new YarnBoxCell { ColorId = "pink" }) },
                topSection: SpoolColumns(("blue","blue","blue"), (null,null,null), (null,null,null), (null,null,null)));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.IsFalse(r.Solvable);
            Assert.AreEqual(0L, r.WinPathCount);
            Assert.IsNotNull(r.FailureReason);
        }

        // ── Fixture 3: conveyor overflow inevitable → 0 paths ───────────

        [Test]
        public void Analyze_OverflowInevitable_ReportsUnsolvable()
        {
            // 5 pink boxes = 45 balls. 3 pink spools = 9 ball capacity.
            // Even with conveyor cap=24, only 9 balls can ever be consumed.
            var doc = MakeDoc(width: 5, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnBoxCell{ColorId="pink"}),
                    (1,0,new YarnBoxCell{ColorId="pink"}),
                    (2,0,new YarnBoxCell{ColorId="pink"}),
                    (3,0,new YarnBoxCell{ColorId="pink"}),
                    (4,0,new YarnBoxCell{ColorId="pink"}),
                },
                topSection: SpoolColumns(("pink","pink","pink"), (null,null,null), (null,null,null), (null,null,null)));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.IsFalse(r.Solvable);
            Assert.AreEqual(0L, r.WinPathCount);
        }

        // ── Fixture 4: arrow box A points to box B → exactly 1 path ─────

        [Test]
        public void Analyze_ArrowBoxGating_OnlyOnePathWhenChained()
        {
            // Layout (1 row, 2 cells): [ArrowBox(pink, Direction=Right)] [Box(pink)]
            // Arrow at (0,0) points to box at (1,0). Arrow only becomes
            // tappable once the box at (1,0) is tapped.
            // Total balls: 9 + 9 = 18 pink. Need 6 pink spools = 2 columns × 3.
            var doc = MakeDoc(width: 2, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnArrowBoxCell{ColorId="pink", Direction=YarnDirection.Right}),
                    (1,0,new YarnBoxCell{ColorId="pink"}),
                },
                topSection: SpoolColumns(
                    ("pink","pink","pink"),
                    ("pink","pink","pink"),
                    (null,null,null),
                    (null,null,null)));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Solvable, r.FailureReason);
            Assert.AreEqual(1L, r.WinPathCount);  // must be B then A — A first is not tappable
        }

        // ── Fixture 5: tunnel queue=[pink, blue], matched spools → 1 path ──

        [Test]
        public void Analyze_TunnelQueue_DequeuesInOrder()
        {
            // 1 tunnel with queue [pink, blue] = 9 pink + 9 blue balls.
            // 3 pink spools + 3 blue spools, in two columns.
            var tunnel = new YarnTunnelCell {
                OutputDirection = YarnDirection.Up,
                Queue = new List<string> { "pink", "blue" },
            };
            var doc = MakeDoc(width: 1, height: 2,
                cells: new (int, int, ICellData)[] {
                    (0,0,tunnel),
                    (0,1,new YarnEmptyCell()),
                },
                topSection: SpoolColumns(
                    ("pink","pink","pink"),
                    ("blue","blue","blue"),
                    (null,null,null),
                    (null,null,null)));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Solvable, r.FailureReason);
            Assert.AreEqual(1L, r.WinPathCount); // tunnel tapped twice in queue order
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static LevelDocument MakeDoc(int width, int height, IEnumerable<(int x, int y, ICellData cell)> cells, JObject topSection)
        {
            var grid = new GridData<ICellData>(width, height);
            for (int i = 0; i < grid.Cells.Length; i++)
                grid.Cells[i] = new YarnEmptyCell();
            foreach (var (x, y, cell) in cells)
                grid.Set(x, y, cell);

            return new LevelDocument
            {
                SchemaVersion = "yarn-twist.v1",
                LevelId       = "test",
                Grid          = grid,
                TopSection    = topSection,
            };
        }

        private static JObject SpoolColumns(params (string a, string b, string c)[] columns)
        {
            var data = new YarnTopSectionData();
            for (int i = 0; i < 4; i++)
                data.Columns.Add(new YarnSpoolColumn());
            for (int i = 0; i < columns.Length && i < 4; i++)
            {
                foreach (var c in new[] { columns[i].a, columns[i].b, columns[i].c })
                    if (!string.IsNullOrEmpty(c))
                        data.Columns[i].Spools.Add(new YarnSpoolData { ColorId = c });
            }
            return JObject.FromObject(data);
        }
    }
}
