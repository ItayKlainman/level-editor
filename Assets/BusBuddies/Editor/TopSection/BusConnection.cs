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
    }
}
