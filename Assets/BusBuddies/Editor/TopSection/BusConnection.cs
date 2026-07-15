using System.Collections.Generic;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;

namespace Hoppa.BusBuddies.Editor
{
    // UI-agnostic operations for Connected Buses, shared by BusBuddiesQueuePanel and
    // exercised by tests. A connection is a stable ConnectedId (>= 0) on BusEntry; two
    // buses sharing an id form a pair. Positions shift as the designer edits, so the id —
    // not a position pointer — is the authoring handle; the exporter resolves each bus's
    // live (column, index) at export. Mirrors YarnSpoolConnection but uses BB's int
    // sentinel (-1 = unconnected) instead of a nullable id, and imposes NO adjacency
    // (the game pairs any two buses by coordinate).
    public static class BusConnection
    {
        public static void BuildConnInfo(BusQueueData queue,
            out Dictionary<int, List<(int col, int pos)>> members, out int? pendingId)
        {
            members = new Dictionary<int, List<(int col, int pos)>>();
            for (int c = 0; c < queue.Columns.Count; c++)
            {
                var buses = queue.Columns[c]?.Buses;
                if (buses == null) continue;
                for (int p = 0; p < buses.Count; p++)
                {
                    int id = buses[p].ConnectedId;
                    if (id < 0) continue;
                    if (!members.TryGetValue(id, out var list))
                        members[id] = list = new List<(int, int)>();
                    list.Add((c, p));
                }
            }

            pendingId = null;
            foreach (var kv in members)
                if (kv.Value.Count == 1 && (pendingId == null || kv.Key < pendingId.Value))
                    pendingId = kv.Key;
        }

        // Contiguous 1..N label for a connection id (ordinal rank among ids present).
        public static int DisplayNumber(Dictionary<int, List<(int col, int pos)>> members, int id)
        {
            int n = 1;
            foreach (var key in members.Keys)
                if (key < id) n++;
            return n;
        }

        // Lowest free id >= 0.
        public static int AllocId(BusQueueData queue)
        {
            int max = -1;
            foreach (var col in queue.Columns)
                if (col?.Buses != null)
                    foreach (var b in col.Buses)
                        if (b.ConnectedId > max) max = b.ConnectedId;
            return max + 1;
        }

        public static void Connect(LevelEditorSession session, BusQueueData queue, BusEntry bus, int id)
        {
            session?.PushUndoSnapshot();
            bus.ConnectedId = id;
            if (session != null)
            {
                session.Document.TopSection = JObject.FromObject(queue);
                session.MarkDirty();
            }
        }

        // Connects two buses into one pair as a SINGLE undo step. Unlike calling Connect
        // twice (which pushes two separate snapshots — one Ctrl+Z lands on a half-connected
        // count==1 group, which BBConnectedBusRule then flags as incomplete), this pushes
        // one snapshot, sets both ids, and rebuilds/marks-dirty once.
        public static void ConnectPair(LevelEditorSession session, BusQueueData queue, BusEntry busA, BusEntry busB, int id)
        {
            session?.PushUndoSnapshot();
            busA.ConnectedId = id;
            busB.ConnectedId = id;
            if (session != null)
            {
                session.Document.TopSection = JObject.FromObject(queue);
                session.MarkDirty();
            }
        }

        public static void DisconnectGroup(LevelEditorSession session, BusQueueData queue, int id)
        {
            session?.PushUndoSnapshot();
            foreach (var col in queue.Columns)
                if (col?.Buses != null)
                    foreach (var b in col.Buses)
                        if (b.ConnectedId == id) b.ConnectedId = -1;
            if (session != null)
            {
                session.Document.TopSection = JObject.FromObject(queue);
                session.MarkDirty();
            }
        }

        // Color-blind structural soft-lock check. Heads advance monotonically; an
        // unconnected head clears freely; a connected head clears only when its partner is
        // simultaneously at ITS column head. Same-column complete pairs can never both be
        // head → permanently locked. If any head can never reach the top → deadlock.
        public static bool ConnectionsDeadlock(BusQueueData queue)
        {
            int n = queue.Columns.Count;
            if (n == 0) return false;

            var len  = new int[n];
            var pCol = new int[n][];
            var pPos = new int[n][];
            var sameColLocked = new bool[n][];   // a member whose partner is in its own column
            for (int c = 0; c < n; c++)
            {
                int count = queue.Columns[c]?.Buses?.Count ?? 0;
                len[c]  = count;
                pCol[c] = new int[count];
                pPos[c] = new int[count];
                sameColLocked[c] = new bool[count];
                for (int p = 0; p < count; p++) { pCol[c][p] = -1; pPos[c][p] = -1; }
            }

            BuildConnInfo(queue, out var members, out _);
            foreach (var kv in members)
            {
                if (kv.Value.Count != 2) continue;   // incomplete/over-linked handled by the rule
                var a = kv.Value[0];
                var b = kv.Value[1];
                if (a.col == b.col)
                {
                    // Two buses in one column can never both be head → permanent lock.
                    sameColLocked[a.col][a.pos] = true;
                    sameColLocked[b.col][b.pos] = true;
                    continue;
                }
                pCol[a.col][a.pos] = b.col; pPos[a.col][a.pos] = b.pos;
                pCol[b.col][b.pos] = a.col; pPos[b.col][b.pos] = a.pos;
            }

            var head = new int[n];
            bool progress = true;
            while (progress)
            {
                progress = false;
                for (int c = 0; c < n; c++)
                {
                    while (head[c] < len[c])
                    {
                        int p = head[c];
                        if (sameColLocked[c][p]) break;                 // never clears
                        int pc = pCol[c][p];
                        if (pc < 0) { head[c]++; progress = true; continue; }               // unconnected
                        if (head[pc] == pPos[c][p]) { head[c]++; head[pc]++; progress = true; continue; } // pair ready
                        break;                                          // partner not at its head yet
                    }
                }
            }

            for (int c = 0; c < n; c++)
                if (head[c] < len[c]) return true;
            return false;
        }
    }
}
