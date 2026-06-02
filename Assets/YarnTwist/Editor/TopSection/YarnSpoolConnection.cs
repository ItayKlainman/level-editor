using System.Collections.Generic;
using Hoppa.LevelEditor.Core.Editor;
using Newtonsoft.Json.Linq;

namespace Hoppa.YarnTwist.Editor
{
    // UI-agnostic operations for Connected Spools, shared by YarnTopSectionPanel
    // and exercised directly by tests. A connection is a stable ConnectionId on
    // YarnSpoolData; two spools sharing an id form a pair. Indices shift as the
    // designer edits, so the id — not a position pointer — is the authoring handle;
    // the exporter resolves the partner's live (column, index) at export time.
    public static class YarnSpoolConnection
    {
        // Groups every connected spool by ConnectionId → its member (column, dataIndex)
        // positions, and identifies the single in-progress pair (an id with one member;
        // lowest id wins if several were orphaned). pendingId == null means no pair is
        // mid-authoring, so the next Add Connect starts a fresh pair.
        public static void BuildConnInfo(YarnTopSectionData topData,
            out Dictionary<int, List<(int col, int pos)>> members, out int? pendingId)
        {
            members = new Dictionary<int, List<(int col, int pos)>>();
            for (int c = 0; c < topData.Columns.Count; c++)
            {
                var spools = topData.Columns[c]?.Spools;
                if (spools == null) continue;
                for (int p = 0; p < spools.Count; p++)
                {
                    int? id = spools[p].ConnectionId;
                    if (id == null) continue;
                    if (!members.TryGetValue(id.Value, out var list))
                        members[id.Value] = list = new List<(int, int)>();
                    list.Add((c, p));
                }
            }

            pendingId = null;
            foreach (var kv in members)
                if (kv.Value.Count == 1 && (pendingId == null || kv.Key < pendingId.Value))
                    pendingId = kv.Key;
        }

        // Contiguous 1..N label for a connection id (ordinal rank among the ids present).
        public static int DisplayNumber(Dictionary<int, List<(int col, int pos)>> members, int id)
        {
            int n = 1;
            foreach (var key in members.Keys)
                if (key < id) n++;
            return n;
        }

        public static int AllocId(YarnTopSectionData topData)
        {
            int max = 0;
            foreach (var col in topData.Columns)
                if (col?.Spools != null)
                    foreach (var sp in col.Spools)
                        if (sp.ConnectionId.HasValue && sp.ConnectionId.Value > max)
                            max = sp.ConnectionId.Value;
            return max + 1;
        }

        // A second spool may complete a pair only when it is in a different,
        // immediately-adjacent column to the anchor (columns 1↔2, 2↔3, 3↔4).
        public static bool CanComplete(int anchorCol, int targetCol) =>
            anchorCol != targetCol && System.Math.Abs(anchorCol - targetCol) == 1;

        // Color-blind structural check: assuming infinite yarn, can every column's
        // head advance from bottom to top while honouring connection locks (a
        // connected spool clears only when its partner is simultaneously at its own
        // head)? If any head can never reach the top, the connections form a
        // soft-lock (e.g. two pairs that "cross" between the same column-pair).
        // spoolHead is monotonic, so this terminates in O(total spools).
        public static bool ConnectionsDeadlock(YarnTopSectionData topData)
        {
            int n = topData.Columns.Count;
            if (n == 0) return false;

            var len  = new int[n];
            var pCol = new int[n][];
            var pPos = new int[n][];
            for (int c = 0; c < n; c++)
            {
                int count = topData.Columns[c]?.Spools?.Count ?? 0;
                len[c]  = count;
                pCol[c] = new int[count];
                pPos[c] = new int[count];
                for (int p = 0; p < count; p++) { pCol[c][p] = -1; pPos[c][p] = -1; }
            }

            // Link only complete pairs in different columns (mirrors the analyzer).
            BuildConnInfo(topData, out var members, out _);
            foreach (var kv in members)
            {
                if (kv.Value.Count != 2) continue;
                var a = kv.Value[0];
                var b = kv.Value[1];
                if (a.col == b.col) continue;
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
                        int p  = head[c];
                        int pc = pCol[c][p];
                        if (pc < 0) { head[c]++; progress = true; continue; }          // unconnected → clears
                        if (head[pc] == pPos[c][p]) { head[c]++; head[pc]++; progress = true; continue; } // pair ready → both clear
                        break;                                                          // locked: partner not at its head yet
                    }
                }
            }

            for (int c = 0; c < n; c++)
                if (head[c] < len[c]) return true;
            return false;
        }

        // Would completing the pending pair by linking (targetCol, targetPos) to the
        // anchor create a soft-lock? Used to disable the offending Add Connect.
        public static bool CompletingDeadlocks(YarnTopSectionData topData, int pendingId, int targetCol, int targetPos)
        {
            var target = topData.Columns[targetCol].Spools[targetPos];
            int? saved = target.ConnectionId;
            target.ConnectionId = pendingId;
            bool dead = ConnectionsDeadlock(topData);
            target.ConnectionId = saved;
            return dead;
        }

        public static void Connect(LevelEditorSession session, YarnTopSectionData topData,
            YarnSpoolData spool, int id)
        {
            session.PushUndoSnapshot();
            spool.ConnectionId = id;
            session.Document.TopSection = JObject.FromObject(topData);
            session.MarkDirty();
        }

        public static void DisconnectGroup(LevelEditorSession session, YarnTopSectionData topData, int id)
        {
            session.PushUndoSnapshot();
            foreach (var col in topData.Columns)
                if (col?.Spools != null)
                    foreach (var sp in col.Spools)
                        if (sp.ConnectionId == id) sp.ConnectionId = null;
            session.Document.TopSection = JObject.FromObject(topData);
            session.MarkDirty();
        }
    }
}
