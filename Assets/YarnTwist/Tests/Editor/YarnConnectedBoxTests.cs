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
    // Validation rule + right-click Connect/Un-connect authoring for Connected Boxes.
    public class YarnConnectedBoxTests
    {
        private const string ProfilePath = "Assets/YarnTwist/Data/Config/YarnTwistProfile.asset";
        private GameProfile _profile;

        [SetUp]
        public void SetUp()
        {
            _profile = AssetDatabase.LoadAssetAtPath<GameProfile>(ProfilePath);
            Assert.IsNotNull(_profile);
        }

        // ── Validation (YarnConnectedBoxRule only emits Error entries) ───

        [Test]
        public void Rule_ReciprocalPair_NoErrors()
        {
            var doc = Doc(2, 1,
                (0, 0, new YarnBoxCell { ColorId = "pink", ConnectedDir = YarnDirection.Right }),
                (1, 0, new YarnBoxCell { ColorId = "blue", ConnectedDir = YarnDirection.Left }));
            Assert.AreEqual(0, RunRule(doc).Count);
        }

        [Test]
        public void Rule_DanglingConnection_Errors()
        {
            // (0,0) points Right but (1,0) does not point back.
            var doc = Doc(2, 1,
                (0, 0, new YarnBoxCell { ColorId = "pink", ConnectedDir = YarnDirection.Right }),
                (1, 0, new YarnBoxCell { ColorId = "blue" }));
            Assert.AreEqual(1, RunRule(doc).Count);
        }

        [Test]
        public void Rule_PointsToNonBox_Errors()
        {
            var doc = Doc(2, 1,
                (0, 0, new YarnBoxCell { ColorId = "pink", ConnectedDir = YarnDirection.Right }),
                (1, 0, new YarnWallCell()));
            Assert.AreEqual(1, RunRule(doc).Count);
        }

        [Test]
        public void Rule_PointsOutsideGrid_Errors()
        {
            var doc = Doc(1, 1,
                (0, 0, new YarnBoxCell { ColorId = "pink", ConnectedDir = YarnDirection.Right }));
            Assert.AreEqual(1, RunRule(doc).Count);
        }

        // ── Context actions: Connect / Un-connect ───────────────────────

        [Test]
        public void Connect_SetsReciprocalDirsOnBothBoxes()
        {
            var s = Session(Doc(2, 1,
                (0, 0, new YarnBoxCell { ColorId = "pink" }),
                (1, 0, new YarnBoxCell { ColorId = "blue" })));

            Actions(s, 0, 0).First(a => a.Label == "Connect Pair: Right").Apply(s);

            Assert.AreEqual(YarnDirection.Right, ((YarnBoxCell)s.Document.Grid.Get(0, 0)).ConnectedDir);
            Assert.AreEqual(YarnDirection.Left,  ((YarnBoxCell)s.Document.Grid.Get(1, 0)).ConnectedDir);
        }

        [Test]
        public void ConnectActions_OnlyOfferedForValidBoxNeighbours()
        {
            // (0,0) box with a box only to its Right; other neighbours are out of bounds.
            var s = Session(Doc(2, 1,
                (0, 0, new YarnBoxCell { ColorId = "pink" }),
                (1, 0, new YarnBoxCell { ColorId = "blue" })));

            var labels = Actions(s, 0, 0).Select(a => a.Label).ToList();
            Assert.IsTrue(labels.Contains("Connect Pair: Right"));
            Assert.IsFalse(labels.Contains("Connect Pair: Left"));
            Assert.IsFalse(labels.Contains("Connect Pair: Up"));
            Assert.IsFalse(labels.Contains("Connect Pair: Down"));
        }

        [Test]
        public void ConnectActions_NotOfferedWhenNeighbourAlreadyConnected()
        {
            // Right neighbour is already half of another pair → no connect offered.
            var s = Session(Doc(3, 1,
                (0, 0, new YarnBoxCell { ColorId = "pink" }),
                (1, 0, new YarnBoxCell { ColorId = "blue", ConnectedDir = YarnDirection.Right }),
                (2, 0, new YarnBoxCell { ColorId = "blue", ConnectedDir = YarnDirection.Left })));

            var labels = Actions(s, 0, 0).Select(a => a.Label).ToList();
            Assert.IsFalse(labels.Any(l => l.StartsWith("Connect Pair")));
        }

        [Test]
        public void ConnectedBox_OffersUnconnectOnly_NotConnect()
        {
            var s = Session(Doc(2, 1,
                (0, 0, new YarnBoxCell { ColorId = "pink", ConnectedDir = YarnDirection.Right }),
                (1, 0, new YarnBoxCell { ColorId = "blue", ConnectedDir = YarnDirection.Left })));

            var labels = Actions(s, 0, 0).Select(a => a.Label).ToList();
            Assert.IsTrue(labels.Contains("Un-connect"));
            Assert.IsFalse(labels.Any(l => l.StartsWith("Connect Pair")));
        }

        [Test]
        public void Disconnect_ClearsBothHalves()
        {
            var s = Session(Doc(2, 1,
                (0, 0, new YarnBoxCell { ColorId = "pink", ConnectedDir = YarnDirection.Right }),
                (1, 0, new YarnBoxCell { ColorId = "blue", ConnectedDir = YarnDirection.Left })));

            Actions(s, 0, 0).First(a => a.Label == "Un-connect").Apply(s);

            Assert.IsNull(((YarnBoxCell)s.Document.Grid.Get(0, 0)).ConnectedDir);
            Assert.IsNull(((YarnBoxCell)s.Document.Grid.Get(1, 0)).ConnectedDir);
        }

        [Test]
        public void Connect_ThenUndo_RestoresUnconnected()
        {
            var s = Session(Doc(2, 1,
                (0, 0, new YarnBoxCell { ColorId = "pink" }),
                (1, 0, new YarnBoxCell { ColorId = "blue" })));

            // Mirror GridCellPopup: snapshot, then apply.
            s.PushUndoSnapshot();
            Actions(s, 0, 0).First(a => a.Label == "Connect Pair: Right").Apply(s);
            Assert.AreEqual(YarnDirection.Right, ((YarnBoxCell)s.Document.Grid.Get(0, 0)).ConnectedDir);

            Assert.IsTrue(s.Undo()); // also exercises ConnectedDir serialization round-trip
            Assert.IsNull(((YarnBoxCell)s.Document.Grid.Get(0, 0)).ConnectedDir);
            Assert.IsNull(((YarnBoxCell)s.Document.Grid.Get(1, 0)).ConnectedDir);
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static List<ValidationEntry> RunRule(LevelDocument doc)
        {
            var rule = ScriptableObject.CreateInstance<YarnConnectedBoxRule>();
            rule.Configure("yt.connected_box");
            try { return rule.Evaluate(new ValidationContext(doc)).ToList(); }
            finally { Object.DestroyImmediate(rule); }
        }

        private LevelEditorSession Session(LevelDocument doc) => new LevelEditorSession(_profile, doc);

        private static List<CellContextAction> Actions(LevelEditorSession s, int x, int y)
        {
            s.CellTypes.TryGetDefinition("yt.box", out var def);
            var ca  = (ICellContextActions)def;
            var ctx = new CellActionContext(s.Document.Grid.Get(x, y), s.CellTypes, s, new CellRef(x, y));
            return ca.GetContextActions(ctx).ToList();
        }

        private static LevelDocument Doc(int w, int h, params (int x, int y, ICellData c)[] cells)
        {
            var grid = new GridData<ICellData>(w, h);
            for (int i = 0; i < grid.Cells.Length; i++) grid.Cells[i] = new YarnEmptyCell();
            foreach (var (x, y, c) in cells) grid.Set(x, y, c);
            return new LevelDocument
            {
                SchemaVersion = "yarn-twist.v1", LevelId = "test", Grid = grid, TopSection = new JObject(),
            };
        }
    }
}
