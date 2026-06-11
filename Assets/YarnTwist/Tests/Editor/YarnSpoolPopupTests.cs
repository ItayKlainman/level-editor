using System.Collections.Generic;
using Hoppa.YarnTwist;
using NUnit.Framework;

namespace Hoppa.YarnTwist.Editor.Tests
{
    // Pure connect-button state machine behind the unified spool popup.
    public class YarnSpoolPopupTests
    {
        [Test]
        public void Unconnected_NoPending_OffersAddConnect()
        {
            var top = Top(new (string, int?)[] { ("pink", null) },
                          new (string, int?)[] { ("blue", null) });
            var b = Describe(top, 0, 0);
            Assert.AreEqual(YarnSpoolPopup.SpoolConnectAction.StartPair, b.Action);
            Assert.IsTrue(b.Enabled);
            Assert.AreEqual("Add Connect", b.Label);
        }

        [Test]
        public void PendingAdjacent_OffersComplete_Enabled()
        {
            // col0 holds a half-formed pair (id 1); the unconnected spool in adjacent col1
            // can complete it.
            var top = Top(new (string, int?)[] { ("pink", 1) },
                          new (string, int?)[] { ("blue", null) });
            var b = Describe(top, 1, 0);
            Assert.AreEqual(YarnSpoolPopup.SpoolConnectAction.CompletePair, b.Action);
            Assert.IsTrue(b.Enabled);
            StringAssert.Contains("complete Pair", b.Label);
        }

        [Test]
        public void PendingNonAdjacent_Disabled_NeedsAdjacent()
        {
            // Anchor in col0, candidate in col2 → not neighbours → disabled.
            var top = Top(new (string, int?)[] { ("pink", 1) },
                          new (string, int?)[] { },
                          new (string, int?)[] { ("blue", null) });
            var b = Describe(top, 2, 0);
            Assert.IsFalse(b.Enabled);
            Assert.AreEqual(YarnSpoolPopup.SpoolConnectAction.None, b.Action);
            StringAssert.Contains("needs an adjacent column", b.Label);
        }

        [Test]
        public void PendingWouldCross_Disabled_SoftLock()
        {
            // Pair 1 links the middle spools; completing pending Pair 2 at col1 pos0 would
            // cross it → soft-lock → disabled (mirrors CompletingDeadlocks coverage).
            var top = Top(new (string, int?)[] { ("pink", null), ("pink", 1), ("pink", 2) },
                          new (string, int?)[] { ("pink", null), ("pink", 1), ("pink", null) });
            var b = Describe(top, 1, 0);
            Assert.IsFalse(b.Enabled);
            Assert.AreEqual(YarnSpoolPopup.SpoolConnectAction.None, b.Action);
            StringAssert.Contains("soft-lock", b.Label);
        }

        [Test]
        public void ConnectedPair_OffersDisable()
        {
            var top = Top(new (string, int?)[] { ("pink", 1) },
                          new (string, int?)[] { ("blue", 1) });
            var b = Describe(top, 0, 0);
            Assert.AreEqual(YarnSpoolPopup.SpoolConnectAction.Disconnect, b.Action);
            Assert.IsTrue(b.Enabled);
            StringAssert.Contains("Disable Connect", b.Label);
        }

        // ── helpers ──────────────────────────────────────────────────────
        private static YarnSpoolPopup.ConnectButton Describe(YarnTopSectionData top, int col, int idx)
        {
            YarnSpoolConnection.BuildConnInfo(top, out var members, out var pendingId);
            return YarnSpoolPopup.DescribeConnectButton(top, members, pendingId, col, idx);
        }

        private static YarnTopSectionData Top(params (string color, int? conn)[][] columns)
        {
            var data = new YarnTopSectionData();
            for (int i = 0; i < 4; i++) data.Columns.Add(new YarnSpoolColumn());
            for (int i = 0; i < columns.Length && i < 4; i++)
                foreach (var (color, conn) in columns[i])
                    data.Columns[i].Spools.Add(new YarnSpoolData { ColorId = color, ConnectionId = conn });
            return data;
        }
    }
}
