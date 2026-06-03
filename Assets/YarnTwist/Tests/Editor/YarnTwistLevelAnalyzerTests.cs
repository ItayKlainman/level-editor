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

        // ── Fixture 6: 2 independent matched boxes → exactly 2 paths ────

        [Test]
        public void Analyze_TwoIndependentBoxes_ReportsTwoPaths()
        {
            var doc = MakeDoc(width: 2, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnBoxCell{ColorId="pink"}),
                    (1,0,new YarnBoxCell{ColorId="blue"}),
                },
                topSection: SpoolColumns(
                    ("pink","pink","pink"),
                    ("blue","blue","blue"),
                    (null,null,null),
                    (null,null,null)));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Solvable);
            Assert.AreEqual(2L, r.WinPathCount);
        }

        // ── Fixture 7: many independent matched boxes → hits cap ────────

        [Test]
        public void Analyze_HighBranching_HitsCap()
        {
            // 6 boxes of 6 distinct colors: 6! = 720 orderings. Cap at 100.
            string[] colors = { "pink", "blue", "teal", "green", "yellow", "purple" };
            var cells = new List<(int, int, ICellData)>();
            for (int i = 0; i < colors.Length; i++)
                cells.Add((i, 0, new YarnBoxCell { ColorId = colors[i] }));

            var data = new YarnTopSectionData();
            for (int i = 0; i < 4; i++) data.Columns.Add(new YarnSpoolColumn());
            // 18 spools distributed across the 4 columns.
            int spoolIdx = 0;
            foreach (var c in colors)
                for (int s = 0; s < 3; s++)
                {
                    data.Columns[spoolIdx % 4].Spools.Add(new YarnSpoolData { ColorId = c });
                    spoolIdx++;
                }

            var doc = MakeDoc(width: colors.Length, height: 1, cells: cells, topSection: JObject.FromObject(data));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, WinPathCap = 100, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Solvable);
            Assert.AreEqual(100L, r.WinPathCount);
            Assert.IsTrue(r.CountWasCapped);
        }

        // ── Fixture 8: determinism ──────────────────────────────────────

        [Test]
        public void Analyze_SameFixture_ReturnsIdenticalCount()
        {
            var doc1 = MakeDoc(width: 2, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnBoxCell{ColorId="pink"}),
                    (1,0,new YarnBoxCell{ColorId="blue"}),
                },
                topSection: SpoolColumns(("pink","pink","pink"),("blue","blue","blue"),(null,null,null),(null,null,null)));
            var doc2 = MakeDoc(width: 2, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnBoxCell{ColorId="pink"}),
                    (1,0,new YarnBoxCell{ColorId="blue"}),
                },
                topSection: SpoolColumns(("pink","pink","pink"),("blue","blue","blue"),(null,null,null),(null,null,null)));

            var r1 = _analyzer.Analyze(doc1, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            var r2 = _analyzer.Analyze(doc2, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.AreEqual(r1.WinPathCount, r2.WinPathCount);
            Assert.AreEqual(r1.Solvable, r2.Solvable);
        }

        // ── Fixture 9: two columns same active color → greedy multi-clear ─

        [Test]
        public void Analyze_TwoColumnsSameActiveColor_OneTapMultiClears()
        {
            // 2 pink boxes (18 balls) and 2 columns × 3 pink spools (18 ball capacity).
            // The two boxes are independent — the only ordering choice is which goes first.
            // Expected: 2 win paths (tap order between the two boxes).
            var doc = MakeDoc(width: 2, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnBoxCell{ColorId="pink"}),
                    (1,0,new YarnBoxCell{ColorId="pink"}),
                },
                topSection: SpoolColumns(
                    ("pink","pink","pink"),
                    ("pink","pink","pink"),
                    (null,null,null), (null,null,null)));
            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Solvable);
            Assert.AreEqual(2L, r.WinPathCount); // tap order between the two boxes
        }

        // ── Fixture 10: WinRate metric is well-formed ───────────────────

        [Test]
        public void Analyze_WinRate_InRangeAndReportsRollouts()
        {
            var doc = MakeDoc(width: 2, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnBoxCell{ColorId="pink"}),
                    (1,0,new YarnBoxCell{ColorId="blue"}),
                },
                topSection: SpoolColumns(("pink","pink","pink"),("blue","blue","blue"),(null,null,null),(null,null,null)));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest
            {
                Mode = AnalysisMode.WinRate, RolloutCount = 200, ConveyorCapacityOverride = 24,
            });
            Assert.AreEqual(200L, r.RolloutsRun);
            Assert.GreaterOrEqual(r.WinRate, 0.0);
            Assert.LessOrEqual(r.WinRate, 1.0);
        }

        // ── Fixture 11: Count mode with rollouts populates both signals ──

        [Test]
        public void Analyze_CountWithRollouts_PopulatesBothMetrics()
        {
            var doc = MakeDoc(width: 2, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnBoxCell{ColorId="pink"}),
                    (1,0,new YarnBoxCell{ColorId="blue"}),
                },
                topSection: SpoolColumns(("pink","pink","pink"),("blue","blue","blue"),(null,null,null),(null,null,null)));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest
            {
                Mode = AnalysisMode.Count, RolloutCount = 100, ConveyorCapacityOverride = 24,
            });
            Assert.AreEqual(2L, r.WinPathCount);   // exact count still correct
            Assert.AreEqual(100L, r.RolloutsRun);  // rollouts layered on top
        }

        // ── Fixture 12: easy single-color level wins every playout ──────

        [Test]
        public void Analyze_WinRate_EasyLevelAlwaysWins()
        {
            // 2 pink boxes (18 balls), 6 pink spools, slack capacity. Every tap
            // order wins, hidden or not, so win-rate is exactly 1.
            var visible = TopWithHidden(allHidden: false,
                new[] { "pink","pink","pink" }, new[] { "pink","pink","pink" });
            var hidden  = TopWithHidden(allHidden: true,
                new[] { "pink","pink","pink" }, new[] { "pink","pink","pink" });

            var docV = MakeDoc(2, 1, TwoPink(), visible);
            var docH = MakeDoc(2, 1, TwoPink(), hidden);

            var rV = _analyzer.Analyze(docV, _profile, new AnalysisRequest { Mode = AnalysisMode.WinRate, RolloutCount = 200, ConveyorCapacityOverride = 24 });
            var rH = _analyzer.Analyze(docH, _profile, new AnalysisRequest { Mode = AnalysisMode.WinRate, RolloutCount = 200, ConveyorCapacityOverride = 24 });
            Assert.AreEqual(1.0, rV.WinRate, 1e-9);
            Assert.AreEqual(1.0, rH.WinRate, 1e-9);
        }

        // ── Fixture 13: hiding spools never raises the win-rate ─────────

        [Test]
        public void Analyze_WinRate_HiddenDoesNotIncreaseWinRate()
        {
            // Tight capacity, two colors: foresight helps avoid overflow, so
            // covering spools can only keep or lower the measured win-rate.
            var cells = new (int, int, ICellData)[] {
                (0,0,new YarnBoxCell{ColorId="pink"}),
                (1,0,new YarnBoxCell{ColorId="blue"}),
                (2,0,new YarnBoxCell{ColorId="pink"}),
                (3,0,new YarnBoxCell{ColorId="blue"}),
            };
            var visible = TopWithHidden(allHidden: false,
                new[] { "pink","pink","pink" }, new[] { "blue","blue","blue" },
                new[] { "pink","pink","pink" }, new[] { "blue","blue","blue" });
            var hidden  = TopWithHidden(allHidden: true,
                new[] { "pink","pink","pink" }, new[] { "blue","blue","blue" },
                new[] { "pink","pink","pink" }, new[] { "blue","blue","blue" });

            var rV = _analyzer.Analyze(MakeDoc(4,1,cells,visible), _profile,
                new AnalysisRequest { Mode = AnalysisMode.WinRate, RolloutCount = 800, PlayerLookahead = 6, ConveyorCapacityOverride = 12 });
            var rH = _analyzer.Analyze(MakeDoc(4,1,cells,hidden), _profile,
                new AnalysisRequest { Mode = AnalysisMode.WinRate, RolloutCount = 800, PlayerLookahead = 6, ConveyorCapacityOverride = 12 });

            // Hiding removes information the player could use; with 800 rollouts
            // the estimate is steady enough that hidden never exceeds visible
            // (small epsilon absorbs residual sampling noise).
            Assert.LessOrEqual(rH.WinRate, rV.WinRate + 0.05);
        }

        // ── Fixture 14: record solution — arrow-box gating order ────────

        [Test]
        public void Analyze_RecordSolution_ArrowBox_RecordsBoxThenArrowInOrder()
        {
            var doc = MakeDoc(width: 2, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnArrowBoxCell{ColorId="pink", Direction=YarnDirection.Right}),
                    (1,0,new YarnBoxCell{ColorId="pink"}),
                },
                topSection: SpoolColumns(("pink","pink","pink"),("pink","pink","pink"),(null,null,null),(null,null,null)));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { RecordSolution = true, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Solvable, r.FailureReason);
            Assert.IsNotNull(r.SolutionSteps);
            Assert.AreEqual(2, r.SolutionSteps.Count);                  // box + arrow = 2 taps
            StringAssert.Contains("Box", r.SolutionSteps[0]);           // box must come first
            StringAssert.Contains("(1,0)", r.SolutionSteps[0]);
            StringAssert.Contains("Arrow-box", r.SolutionSteps[1]);     // arrow only after box
            StringAssert.Contains("(0,0)", r.SolutionSteps[1]);
        }

        // ── Fixture 15: record solution — tunnel queue order ────────────

        [Test]
        public void Analyze_RecordSolution_Tunnel_RecordsQueueColorsInOrder()
        {
            var tunnel = new YarnTunnelCell {
                OutputDirection = YarnDirection.Up,
                Queue = new List<string> { "pink", "blue" },
            };
            var doc = MakeDoc(width: 1, height: 2,
                cells: new (int, int, ICellData)[] { (0,0,tunnel), (0,1,new YarnEmptyCell()) },
                topSection: SpoolColumns(("pink","pink","pink"),("blue","blue","blue"),(null,null,null),(null,null,null)));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { RecordSolution = true, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Solvable, r.FailureReason);
            Assert.AreEqual(2, r.SolutionSteps.Count);                  // tunnel tapped twice
            StringAssert.Contains("pink (1/2)", r.SolutionSteps[0]);
            StringAssert.Contains("blue (2/2)", r.SolutionSteps[1]);
        }

        // ── Fixture 16: record solution — unsolvable yields no steps ────

        [Test]
        public void Analyze_RecordSolution_Unsolvable_ReportsNoSteps()
        {
            var doc = MakeDoc(width: 1, height: 1,
                cells: new (int, int, ICellData)[] { (0,0,new YarnBoxCell { ColorId = "pink" }) },
                topSection: SpoolColumns(("blue","blue","blue"),(null,null,null),(null,null,null),(null,null,null)));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { RecordSolution = true, ConveyorCapacityOverride = 24 });
            Assert.IsFalse(r.Solvable);
            Assert.IsTrue(r.SolutionSteps == null || r.SolutionSteps.Count == 0);
            Assert.IsNotNull(r.FailureReason);
        }

        // ── Fixture 17: recorded step count == total taps, count unaffected ─

        [Test]
        public void Analyze_RecordSolution_StepCountMatchesTaps_AndCountStillExact()
        {
            var doc = MakeDoc(width: 2, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnBoxCell{ColorId="pink"}),
                    (1,0,new YarnBoxCell{ColorId="blue"}),
                },
                topSection: SpoolColumns(("pink","pink","pink"),("blue","blue","blue"),(null,null,null),(null,null,null)));

            var rSol = _analyzer.Analyze(doc, _profile, new AnalysisRequest { RecordSolution = true, ConveyorCapacityOverride = 24 });
            Assert.AreEqual(2, rSol.SolutionSteps.Count);  // two boxes = two taps

            // Count mode (no recording) still returns the exact path count.
            var rCount = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.AreEqual(2L, rCount.WinPathCount);
            Assert.IsNull(rCount.SolutionSteps);
        }

        // ── Fixture 18: connected same-color pair → ONE tap, ONE path ───

        [Test]
        public void Analyze_ConnectedSameColorPair_ClearsAsOneTap()
        {
            // Two pink boxes connected (0,0)→Right ↔ (1,0)→Left. Independent boxes
            // would give 2 orderings (see Fixture 9); connected, the pair is a single
            // atomic tap, so exactly 1 win path (no double-count).
            var doc = MakeDoc(width: 2, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnBoxCell{ColorId="pink", ConnectedDir=YarnDirection.Right}),
                    (1,0,new YarnBoxCell{ColorId="pink", ConnectedDir=YarnDirection.Left}),
                },
                topSection: SpoolColumns(
                    ("pink","pink","pink"),
                    ("pink","pink","pink"),
                    (null,null,null), (null,null,null)));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Solvable, r.FailureReason);
            Assert.AreEqual(1L, r.WinPathCount);
        }

        // ── Fixture 19: connected distinct-color pair → both colors at once ─

        [Test]
        public void Analyze_ConnectedDistinctColorPair_ReleasesBothColorsInOneTap()
        {
            // pink+blue connected. Independent would give 2 paths (Fixture 6); connected,
            // one tap releases both colors together → 1 path, and both spool columns clear.
            var doc = MakeDoc(width: 2, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnBoxCell{ColorId="pink", ConnectedDir=YarnDirection.Right}),
                    (1,0,new YarnBoxCell{ColorId="blue", ConnectedDir=YarnDirection.Left}),
                },
                topSection: SpoolColumns(
                    ("pink","pink","pink"),
                    ("blue","blue","blue"),
                    (null,null,null), (null,null,null)));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Solvable, r.FailureReason);
            Assert.AreEqual(1L, r.WinPathCount);
        }

        // ── Fixture 20: arrow-box prereq satisfied via partner co-tap ───

        [Test]
        public void Analyze_ArrowPrereqOnConnectedBox_SatisfiedByCoTap()
        {
            // [Arrow(pink,Right)] [Box pink ↔Right] [Box pink ↔Left].
            // The arrow's prereq is the box at (1,0), which is the canonical half of the
            // pair. Tapping the pair co-taps (1,0), satisfying the arrow. If co-tap did
            // NOT mark the partner tapped, the arrow could never fire → unsolvable.
            // 27 pink balls = 9 spools (3 columns × 3). Only order: pair, then arrow.
            var doc = MakeDoc(width: 3, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnArrowBoxCell{ColorId="pink", Direction=YarnDirection.Right}),
                    (1,0,new YarnBoxCell{ColorId="pink", ConnectedDir=YarnDirection.Right}),
                    (2,0,new YarnBoxCell{ColorId="pink", ConnectedDir=YarnDirection.Left}),
                },
                topSection: SpoolColumns(
                    ("pink","pink","pink"),
                    ("pink","pink","pink"),
                    ("pink","pink","pink"),
                    (null,null,null)));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Solvable, r.FailureReason);
            Assert.AreEqual(1L, r.WinPathCount);
        }

        // ── Fixture 21: non-reciprocal link degrades to independent boxes ─

        [Test]
        public void Analyze_NonReciprocalConnection_TreatedAsIndependent()
        {
            // (0,0) points Right but (1,0) does not point back → not a valid pair.
            // The analyzer must treat them as two independent boxes → 2 orderings.
            var doc = MakeDoc(width: 2, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnBoxCell{ColorId="pink", ConnectedDir=YarnDirection.Right}),
                    (1,0,new YarnBoxCell{ColorId="pink"}), // no ConnectedDir → dangling
                },
                topSection: SpoolColumns(
                    ("pink","pink","pink"),
                    ("pink","pink","pink"),
                    (null,null,null), (null,null,null)));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Solvable, r.FailureReason);
            Assert.AreEqual(2L, r.WinPathCount);
        }

        // ── Fixture 22: connected lock removes orderings vs unconnected ─

        [Test]
        public void Analyze_ConnectedSpoolLock_RemovesSomeOrderingsVsUnconnected()
        {
            // col0 (6 pink spools, head linked to col1's top spool) + col1 (3 blue).
            // 2 pink boxes + 1 blue box on a tight belt. Connected, col0's pink head
            // can't drain until col1 is consumed, so any ordering that taps BOTH pink
            // boxes before the blue box leaves the belt over capacity at the next node
            // → strictly fewer winning orderings than the same spools unconnected.
            var cells = new (int, int, ICellData)[] {
                (0,0,new YarnBoxCell{ColorId="pink"}),
                (1,0,new YarnBoxCell{ColorId="pink"}),
                (2,0,new YarnBoxCell{ColorId="blue"}),
            };
            var connected = ConnTop(
                new (string, int?)[] { ("pink",1),("pink",null),("pink",null),("pink",null),("pink",null),("pink",null) },
                new (string, int?)[] { ("blue",null),("blue",null),("blue",1) });
            var unconnected = ConnTop(
                new (string, int?)[] { ("pink",null),("pink",null),("pink",null),("pink",null),("pink",null),("pink",null) },
                new (string, int?)[] { ("blue",null),("blue",null),("blue",null) });

            var rc = _analyzer.Analyze(MakeDoc(3, 1, cells, connected),   _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 12 });
            var ru = _analyzer.Analyze(MakeDoc(3, 1, cells, unconnected), _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 12 });

            Assert.IsTrue(rc.Solvable, rc.FailureReason);
            Assert.IsTrue(ru.Solvable, ru.FailureReason);
            Assert.Less(rc.WinPathCount, ru.WinPathCount);
        }

        // ── Fixture 23: mutual lock → deadlock → unsolvable ─────────────

        [Test]
        public void Analyze_ConnectedSpoolsMutualLock_Unsolvable()
        {
            // conn1: col0 pos0 ↔ col1 pos2 ; conn2: col0 pos2 ↔ col1 pos0.
            // Each column's bottom spool waits for the other column to reach its top,
            // but neither column can start → deadlock → unsolvable.
            var top = ConnTop(
                new (string, int?)[] { ("pink", 1), ("pink", null), ("pink", 2) },
                new (string, int?)[] { ("blue", 2), ("blue", null), ("blue", 1) });
            var doc = MakeDoc(2, 1, new (int, int, ICellData)[] {
                (0,0,new YarnBoxCell{ColorId="pink"}),
                (1,0,new YarnBoxCell{ColorId="blue"}),
            }, top);

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 24 });
            Assert.IsFalse(r.Solvable);
            Assert.AreEqual(0L, r.WinPathCount);
        }

        // ── Fixture 24: belt permanently stuck → unsolvable ──────────────

        [Test]
        public void Analyze_BeltPermanentlyStuck_Unsolvable()
        {
            // 3 orange boxes + 3 orange spools (capacity=9). Tapping box1 fills and
            // clears all 3 orange spools (bag drains to 0). Tapping box2 adds 9 orange —
            // no spools remain to drain, bagSum stays 9 (at capacity). Tapping box3
            // would push bagSum to 18 > 9 at the NEXT DFS node start → dead end.
            // The 3rd orange box can never be cleared — unsolvable.
            var doc = MakeDoc(width: 3, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnBoxCell{ColorId="orange"}),
                    (1,0,new YarnBoxCell{ColorId="orange"}),
                    (2,0,new YarnBoxCell{ColorId="orange"}),
                },
                topSection: SpoolColumns(("orange","orange","orange"),(null,null,null),(null,null,null),(null,null,null)));

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { Mode = AnalysisMode.Count, ConveyorCapacityOverride = 9 });
            Assert.IsFalse(r.Solvable, "3 orange boxes with only 3 orange spools overflows after 1st clear");
            Assert.AreEqual(0L, r.WinPathCount);
        }

        // ── Fixture 25: demand ordering — recorded solution leads with a color ─
        //                that matches a current column head ───────────────────────

        [Test]
        public void Analyze_RecordSolution_PrefersTapThatMatchesColumnHead()
        {
            // Item 0 = orange at (0,0); item 1 = blue at (1,0).
            // Col0 has 6 spools: [blue, blue, blue, orange, orange, orange].
            // With lookahead=2, orange is only at positions 3-5 (all beyond the 0..2
            // window), so demand(orange)=0. demand(blue)=3 (head match). The DFS in
            // recording mode must explore blue first, giving "blue" as the first step.
            // (Both orderings are valid; the point is the demand-ordered DFS picks the
            // intuitive one.)
            var top = ConnTop(
                new (string, int?)[] {
                    ("blue",null),("blue",null),("blue",null),
                    ("orange",null),("orange",null),("orange",null)
                });

            var doc = MakeDoc(width: 2, height: 1,
                cells: new (int, int, ICellData)[] {
                    (0,0,new YarnBoxCell{ColorId="orange"}),
                    (1,0,new YarnBoxCell{ColorId="blue"}),
                },
                topSection: top);

            var r = _analyzer.Analyze(doc, _profile, new AnalysisRequest { RecordSolution = true, ConveyorCapacityOverride = 24 });
            Assert.IsTrue(r.Solvable, r.FailureReason);
            Assert.AreEqual(2, r.SolutionSteps.Count);
            // Step 1 must tap blue (demand=3) before orange (demand=0 — buried at depth 3).
            StringAssert.Contains("blue", r.SolutionSteps[0],
                $"Expected blue (head-match, demand=3) first, got: {r.SolutionSteps[0]}");
        }

        // ── Helpers ──────────────────────────────────────────────────────

        // Builds a top section from explicit per-column (color, connectionId) lists;
        // spools sharing a connectionId across two columns form a connected pair.
        private static JObject ConnTop(params (string color, int? conn)[][] columns)
        {
            var data = new YarnTopSectionData();
            for (int i = 0; i < 4; i++) data.Columns.Add(new YarnSpoolColumn());
            for (int i = 0; i < columns.Length && i < 4; i++)
                foreach (var (color, conn) in columns[i])
                    data.Columns[i].Spools.Add(new YarnSpoolData { ColorId = color, ConnectionId = conn });
            return JObject.FromObject(data);
        }

        private static (int, int, ICellData)[] TwoPink() => new (int, int, ICellData)[] {
            (0,0,new YarnBoxCell{ColorId="pink"}),
            (1,0,new YarnBoxCell{ColorId="pink"}),
        };

        // Builds a top section from explicit per-column color lists, with every
        // spool's Hidden flag set to `allHidden`.
        private static JObject TopWithHidden(bool allHidden, params string[][] columns)
        {
            var data = new YarnTopSectionData();
            for (int i = 0; i < 4; i++) data.Columns.Add(new YarnSpoolColumn());
            for (int i = 0; i < columns.Length && i < 4; i++)
                foreach (var c in columns[i])
                    if (!string.IsNullOrEmpty(c))
                        data.Columns[i].Spools.Add(new YarnSpoolData { ColorId = c, Hidden = allHidden });
            return JObject.FromObject(data);
        }

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
