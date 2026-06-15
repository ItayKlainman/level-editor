using System.Text;

namespace Hoppa.YAK.Sim
{
    // Mutable simulation state for one YAK playthrough. Engine-agnostic plain C#.
    //
    // Turn model (v1, documented optimism risk — see plan, calibrate vs real
    // logs): the only player decision is "send the top spool of some column onto
    // the belt" when a belt slot is free. After each send the belt resolves to a
    // steady state (ResolveBelt) — every active spool sweeps the bottom row,
    // consuming matching exposed tiles (gravity re-exposes the next), repeating
    // until nothing can be consumed. This collapses the game's real-time
    // circulation into a deterministic per-move resolution.
    public sealed class YakSimState
    {
        public readonly YakLevelModel M;
        public readonly int[] Ptr;        // per grid column: tiles already consumed (next exposed = GridCols[c][Ptr[c]])
        public readonly int[] QHead;      // per spool column: spools already sent (next sendable = index QHead[c])
        public readonly int[] BeltColor;  // per belt slot: active spool color, or -1 if slot empty
        public readonly int[] BeltRem;    // per belt slot: remaining capacity to fill before completing
        public int WoolLeft;

        public YakSimState(YakLevelModel m)
        {
            M = m;
            Ptr   = new int[m.Width];
            QHead = new int[m.Columns];
            BeltColor = new int[m.ConveyorSlots];
            BeltRem   = new int[m.ConveyorSlots];
            for (int s = 0; s < BeltColor.Length; s++) BeltColor[s] = -1;
            WoolLeft = m.TotalWool;
        }

        private YakSimState(YakSimState src)
        {
            M = src.M;
            Ptr   = (int[])src.Ptr.Clone();
            QHead = (int[])src.QHead.Clone();
            BeltColor = (int[])src.BeltColor.Clone();
            BeltRem   = (int[])src.BeltRem.Clone();
            WoolLeft = src.WoolLeft;
        }

        public YakSimState Clone() => new YakSimState(this);

        // Color currently exposed at the bottom of grid column c, or -1 if that
        // column is fully consumed.
        public int Exposed(int c) => Ptr[c] < M.GridCols[c].Length ? M.GridCols[c][Ptr[c]] : -1;

        public int FreeSlot()
        {
            for (int s = 0; s < BeltColor.Length; s++) if (BeltColor[s] < 0) return s;
            return -1;
        }

        public bool BeltEmpty()
        {
            for (int s = 0; s < BeltColor.Length; s++) if (BeltColor[s] >= 0) return false;
            return true;
        }

        public bool CanSend(int col) => col >= 0 && col < M.Columns && QHead[col] < M.SpoolColor[col].Length;

        // A legal move exists iff a belt slot is free AND some column still has a
        // spool to send.
        public bool HasLegalMove()
        {
            if (FreeSlot() < 0) return false;
            for (int c = 0; c < M.Columns; c++) if (CanSend(c)) return true;
            return false;
        }

        // §2 lose condition: all belt slots occupied AND none of the active
        // spools matches any currently-exposed bottom-row tile.
        public bool IsDeadlock()
        {
            if (FreeSlot() >= 0) return false;
            for (int s = 0; s < BeltColor.Length; s++)
            {
                int color = BeltColor[s];
                if (color < 0) continue;
                for (int c = 0; c < M.Width; c++)
                    if (Exposed(c) == color) return false; // this spool can still progress
            }
            return true;
        }

        // Win: all wool cleared, all spools sent, belt empty (every sent spool
        // completed). A partially-filled spool stranded on the belt with no wool
        // left is NOT a win.
        public bool IsWin()
        {
            if (WoolLeft != 0) return false;
            if (!BeltEmpty()) return false;
            for (int c = 0; c < M.Columns; c++)
                if (QHead[c] < M.SpoolColor[c].Length) return false;
            return true;
        }

        // Send the top spool of `col` onto the first free belt slot, then resolve.
        // Returns the number of wool tiles consumed by the resulting resolution.
        // Caller must ensure CanSend(col) and FreeSlot() >= 0.
        public int ApplyMove(int col)
        {
            int slot = FreeSlot();
            int head = QHead[col];
            BeltColor[slot] = M.SpoolColor[col][head];
            BeltRem[slot]   = M.SpoolCap[col][head];
            QHead[col]++;
            return ResolveBelt();
        }

        // Resolve the belt to a steady state. One tile per grid column per pass
        // (left→right, lowest belt slot wins a contested tile) — a lap model
        // that loops until a full pass consumes nothing, so a consumption that
        // re-exposes a tile for a waiting spool is picked up on the next pass.
        public int ResolveBelt()
        {
            int consumed = 0;
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int c = 0; c < M.Width; c++)
                {
                    int t = Exposed(c);
                    if (t < 0) continue;
                    for (int s = 0; s < BeltColor.Length; s++)
                    {
                        if (BeltColor[s] != t || BeltRem[s] <= 0) continue;
                        Ptr[c]++;
                        WoolLeft--;
                        BeltRem[s]--;
                        consumed++;
                        changed = true;
                        if (BeltRem[s] == 0) BeltColor[s] = -1; // spool full → completed, frees slot
                        break;
                    }
                }
            }
            return consumed;
        }

        // Canonical key for memoisation: grid pointers + spool heads + a
        // slot-order-independent multiset of the active belt spools. Exact (not a
        // lossy hash) so the solver's "unsolvable" verdict can't be corrupted by
        // a collision.
        public string Key()
        {
            var sb = new StringBuilder(Ptr.Length * 3 + 32);
            for (int c = 0; c < Ptr.Length; c++)   { sb.Append(Ptr[c]);   sb.Append(','); }
            sb.Append('|');
            for (int c = 0; c < QHead.Length; c++) { sb.Append(QHead[c]); sb.Append(','); }
            sb.Append('|');
            // Canonicalise belt: collect active (color,rem) pairs and sort.
            int n = 0;
            for (int s = 0; s < BeltColor.Length; s++) if (BeltColor[s] >= 0) n++;
            var pairs = new long[n];
            int j = 0;
            for (int s = 0; s < BeltColor.Length; s++)
                if (BeltColor[s] >= 0) pairs[j++] = ((long)BeltColor[s] << 20) | (uint)BeltRem[s];
            System.Array.Sort(pairs);
            for (int i = 0; i < n; i++) { sb.Append(pairs[i]); sb.Append(';'); }
            return sb.ToString();
        }
    }
}
