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
    // Validation rule + UI-agnostic Connect/Disconnect authoring for Connected Spools.
    public class YarnConnectedSpoolTests
    {
        private const string ProfilePath = "Assets/YarnTwist/Data/Config/YarnTwistProfile.asset";
        private GameProfile _profile;

        [SetUp]
        public void SetUp()
        {
            _profile = AssetDatabase.LoadAssetAtPath<GameProfile>(ProfilePath);
            Assert.IsNotNull(_profile);
        }

        // ── Validation ──────────────────────────────────────────────────

        [Test]
        public void Rule_CompleteAdjacentPair_NoErrors()
        {
            var top = MakeTop(
                new (string, int?)[] { ("pink", 1) },
                new (string, int?)[] { ("blue", 1) });
            Assert.AreEqual(0, RunRule(top).Count);
        }

        [Test]
        public void Rule_IncompletePair_ErrorsWithGdditMessage()
        {
            var top = MakeTop(
                new (string, int?)[] { ("pink", 1) },
                new (string, int?)[] { });
            var errors = RunRule(top);
            Assert.AreEqual(1, errors.Count);
            StringAssert.Contains("Pair 1 is incomplete", errors[0].Message);
        }

        [Test]
        public void Rule_SameColumnPair_Errors()
        {
            // Two spools in the same column share an id → not a valid pair.
            var top = MakeTop(new (string, int?)[] { ("pink", 1), ("blue", 1) });
            Assert.AreEqual(1, RunRule(top).Count);
        }

        [Test]
        public void Rule_NonAdjacentColumns_Errors()
        {
            // Columns 1 and 3 (indices 0 and 2) are not neighbours.
            var top = MakeTop(
                new (string, int?)[] { ("pink", 1) },
                new (string, int?)[] { },
                new (string, int?)[] { ("blue", 1) });
            Assert.AreEqual(1, RunRule(top).Count);
        }

        // ── Authoring (YarnSpoolConnection) ─────────────────────────────

        [Test]
        public void AddConnect_FirstSpool_AssignsIdImmediately()
        {
            // Commit-on-first-click: the anchor spool gets its id right away.
            var s   = Session(MakeTop(
                new (string, int?)[] { ("pink", null) },
                new (string, int?)[] { ("blue", null) }));
            var top = Top(s);

            YarnSpoolConnection.Connect(s, top, top.Columns[0].Spools[0], YarnSpoolConnection.AllocId(top));

            var after = Top(s);
            Assert.AreEqual(1, after.Columns[0].Spools[0].ConnectionId);
            Assert.IsNull(after.Columns[1].Spools[0].ConnectionId);
        }

        [Test]
        public void AddConnect_TwoStep_BothSpoolsShareId()
        {
            var s = Session(MakeTop(
                new (string, int?)[] { ("pink", null) },
                new (string, int?)[] { ("blue", null) }));

            var top1 = Top(s);
            YarnSpoolConnection.Connect(s, top1, top1.Columns[0].Spools[0], YarnSpoolConnection.AllocId(top1));

            var top2 = Top(s);
            YarnSpoolConnection.BuildConnInfo(top2, out _, out var pendingId);
            Assert.AreEqual(1, pendingId);
            Assert.IsTrue(YarnSpoolConnection.CanComplete(0, 1));
            YarnSpoolConnection.Connect(s, top2, top2.Columns[1].Spools[0], pendingId.Value);

            var after = Top(s);
            Assert.AreEqual(1, after.Columns[0].Spools[0].ConnectionId);
            Assert.AreEqual(1, after.Columns[1].Spools[0].ConnectionId);
        }

        [Test]
        public void CanComplete_AcceptsAdjacent_RejectsSameAndNonAdjacent()
        {
            Assert.IsTrue(YarnSpoolConnection.CanComplete(0, 1));
            Assert.IsTrue(YarnSpoolConnection.CanComplete(2, 1));
            Assert.IsFalse(YarnSpoolConnection.CanComplete(1, 1)); // same column
            Assert.IsFalse(YarnSpoolConnection.CanComplete(0, 2)); // gap
            Assert.IsFalse(YarnSpoolConnection.CanComplete(0, 3)); // gap
        }

        [Test]
        public void DisableConnect_ClearsBothMembers()
        {
            var s = Session(MakeTop(
                new (string, int?)[] { ("pink", 1) },
                new (string, int?)[] { ("blue", 1) }));

            YarnSpoolConnection.DisconnectGroup(s, Top(s), 1);

            var after = Top(s);
            Assert.IsNull(after.Columns[0].Spools[0].ConnectionId);
            Assert.IsNull(after.Columns[1].Spools[0].ConnectionId);
        }

        [Test]
        public void Connect_ThenUndo_RestoresUnconnected()
        {
            var s = Session(MakeTop(
                new (string, int?)[] { ("pink", null) },
                new (string, int?)[] { ("blue", null) }));

            // Connect pushes its own undo snapshot before mutating.
            var top = Top(s);
            YarnSpoolConnection.Connect(s, top, top.Columns[0].Spools[0], 1);
            Assert.AreEqual(1, Top(s).Columns[0].Spools[0].ConnectionId);

            Assert.IsTrue(s.Undo()); // also exercises connId serialization round-trip
            Assert.IsNull(Top(s).Columns[0].Spools[0].ConnectionId);
        }

        [Test]
        public void BuildConnInfo_DetectsPendingSingle()
        {
            var top = MakeTop(
                new (string, int?)[] { ("pink", 1) },
                new (string, int?)[] { ("blue", null) }).ToObject<YarnTopSectionData>();
            YarnSpoolConnection.BuildConnInfo(top, out var members, out var pendingId);
            Assert.AreEqual(1, pendingId);
            Assert.AreEqual(1, members[1].Count);
        }

        // ── Soft-lock (deadlock) prevention ─────────────────────────────

        [Test]
        public void ConnectionsDeadlock_CrossingPairs_True()
        {
            // Pair1 links the middle spools; Pair2 links col0-top ↔ col1-bottom → they
            // cross, so neither column can ever advance (the reported soft-lock).
            var top = MakeTop(
                new (string, int?)[] { ("pink", null), ("pink", 1), ("pink", 2) },
                new (string, int?)[] { ("pink", 2), ("pink", 1), ("pink", null) }).ToObject<YarnTopSectionData>();
            Assert.IsTrue(YarnSpoolConnection.ConnectionsDeadlock(top));
        }

        [Test]
        public void ConnectionsDeadlock_NonCrossingPairs_False()
        {
            // Both pairs ascend in lock-step (no cross) → resolvable.
            var top = MakeTop(
                new (string, int?)[] { ("pink", null), ("pink", 1), ("pink", 2) },
                new (string, int?)[] { ("pink", null), ("pink", 1), ("pink", 2) }).ToObject<YarnTopSectionData>();
            Assert.IsFalse(YarnSpoolConnection.ConnectionsDeadlock(top));
        }

        [Test]
        public void Rule_CrossingPairs_ErrorsWithSoftlockMessage()
        {
            var top = MakeTop(
                new (string, int?)[] { ("pink", null), ("pink", 1), ("pink", 2) },
                new (string, int?)[] { ("pink", 2), ("pink", 1), ("pink", null) });
            Assert.IsTrue(RunRule(top).Any(e => e.Message.Contains("soft-lock")));
        }

        [Test]
        public void CompletingDeadlocks_CrossingCompletion_True()
        {
            // Pair1 = (col0 pos1)↔(col1 pos1). Pair2 anchored at col0 pos2 (pending);
            // completing it at col1 pos0 would cross Pair1 → soft-lock → blocked.
            var top = MakeTop(
                new (string, int?)[] { ("pink", null), ("pink", 1), ("pink", 2) },
                new (string, int?)[] { ("pink", null), ("pink", 1), ("pink", null) }).ToObject<YarnTopSectionData>();
            Assert.IsTrue(YarnSpoolConnection.CompletingDeadlocks(top, 2, 1, 0));
        }

        // ── Helpers ──────────────────────────────────────────────────────

        private static JObject MakeTop(params (string color, int? conn)[][] columns)
        {
            var data = new YarnTopSectionData();
            for (int i = 0; i < 4; i++) data.Columns.Add(new YarnSpoolColumn());
            for (int i = 0; i < columns.Length && i < 4; i++)
                foreach (var (color, conn) in columns[i])
                    data.Columns[i].Spools.Add(new YarnSpoolData { ColorId = color, ConnectionId = conn });
            return JObject.FromObject(data);
        }

        private static List<ValidationEntry> RunRule(JObject top)
        {
            var rule = ScriptableObject.CreateInstance<YarnConnectedSpoolRule>();
            rule.Configure("yt.connected_spool");
            try { return rule.Evaluate(new ValidationContext(Doc(top))).ToList(); }
            finally { Object.DestroyImmediate(rule); }
        }

        private LevelEditorSession Session(JObject top) => new LevelEditorSession(_profile, Doc(top));

        private static YarnTopSectionData Top(LevelEditorSession s) =>
            s.Document.TopSection.ToObject<YarnTopSectionData>();

        private static LevelDocument Doc(JObject top)
        {
            var grid = new GridData<ICellData>(1, 1);
            grid.Cells[0] = new YarnEmptyCell();
            return new LevelDocument
            {
                SchemaVersion = "yarn-twist.v1", LevelId = "test", Grid = grid, TopSection = top,
            };
        }
    }
}
