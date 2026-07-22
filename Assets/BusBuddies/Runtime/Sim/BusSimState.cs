using System.Collections.Generic;
using System.Text;

namespace Hoppa.BusBuddies.Sim
{
    // Mutable simulation state for one Bus Buddies playthrough. Engine-agnostic plain C#.
    //
    // No gravity: removing a block just sets its cell to -1; nothing else moves.
    // Accessibility = a 4-way border flood of empty cells; a block is accessible iff
    // it sits on the grid edge (the outside counts as flooded) or an orthogonal
    // neighbour is a border-connected empty cell.
    //
    // The only player decision is "pull the top bus of some queue column into a free
    // Active Row slot" (ApplyMove); the row then auto-releases passengers to
    // quiescence (ResolveReleases).
    public sealed class BusSimState
    {
        public readonly BusLevelModel M;
        public readonly int[] Cell;        // [y*W+x] live colour, -1 = empty/removed
        public readonly int[] QHead;       // per column: next pullable bus index
        public readonly int[] ActiveColor; // per active slot: colour, -1 = empty slot
        public readonly int[] ActiveRem;   // per active slot: remaining passengers
        public int BlocksLeft;
        public int Picked;                 // total blocks picked so far (plate countdown)

        private readonly bool[] _accessible; // [y*W+x] true iff a block here is accessible
        private bool[] _floodScratch;        // reused empty-flood buffer

        public BusSimState(BusLevelModel m)
        {
            M = m;
            Cell = (int[])m.Grid.Clone();
            QHead = new int[m.Columns];
            ActiveColor = new int[m.ActiveSlots];
            ActiveRem = new int[m.ActiveSlots];
            for (int s = 0; s < ActiveColor.Length; s++) { ActiveColor[s] = -1; ActiveRem[s] = 0; }
            BlocksLeft = m.TotalBlocks;
            Picked = 0;
            _accessible = new bool[m.W * m.H];
            RecomputeAccess();
        }

        private BusSimState(BusSimState src)
        {
            M = src.M;
            Cell = (int[])src.Cell.Clone();
            QHead = (int[])src.QHead.Clone();
            ActiveColor = (int[])src.ActiveColor.Clone();
            ActiveRem = (int[])src.ActiveRem.Clone();
            BlocksLeft = src.BlocksLeft;
            Picked = src.Picked;
            _accessible = (bool[])src._accessible.Clone();
            _floodScratch = null;
        }

        public BusSimState Clone() => new BusSimState(this);

        public bool IsAccessible(int x, int y) => _accessible[y * M.W + x];

        // Full 4-way border flood of empty cells, then mark each block accessible if
        // it is on the grid edge (outside == flooded) or touches a flooded empty.
        public void RecomputeAccess()
        {
            int w = M.W, h = M.H, n = w * h;
            var flood = _floodScratch ?? (_floodScratch = new bool[n]);
            System.Array.Clear(flood, 0, n);

            var stack = new Stack<int>();
            for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                if (x != 0 && x != w - 1 && y != 0 && y != h - 1) continue; // border cells only
                int i = y * w + x;
                if (Cell[i] < 0 && !flood[i]) { flood[i] = true; stack.Push(i); }
            }
            while (stack.Count > 0)
            {
                int i = stack.Pop();
                int x = i % w, y = i / w;
                TryFlood(x - 1, y, flood, stack);
                TryFlood(x + 1, y, flood, stack);
                TryFlood(x, y - 1, flood, stack);
                TryFlood(x, y + 1, flood, stack);
            }

            for (int i = 0; i < n; i++)
            {
                if (Cell[i] < 0) { _accessible[i] = false; continue; }
                int x = i % w, y = i / w;
                _accessible[i] =
                    x == 0 || x == w - 1 || y == 0 || y == h - 1 ||
                    IsFloodedEmpty(x - 1, y, flood) || IsFloodedEmpty(x + 1, y, flood) ||
                    IsFloodedEmpty(x, y - 1, flood) || IsFloodedEmpty(x, y + 1, flood);
            }
        }

        private void TryFlood(int x, int y, bool[] flood, Stack<int> stack)
        {
            if (x < 0 || x >= M.W || y < 0 || y >= M.H) return;
            int i = y * M.W + x;
            if (Cell[i] < 0 && !flood[i]) { flood[i] = true; stack.Push(i); }
        }

        private bool IsFloodedEmpty(int x, int y, bool[] flood)
        {
            if (x < 0 || x >= M.W || y < 0 || y >= M.H) return false;
            int i = y * M.W + x;
            return Cell[i] < 0 && flood[i];
        }

        // Canonical passenger-targeting rule: among all accessible blocks of `color`,
        // pick the one NEAREST to the central Hole (bottom-center, one row below the
        // grid). Tie-break: lowest flat index (deterministic).
        //
        // WHY THIS MATTERS: with no gravity, the choice of which block to remove
        // directly changes which neighbours become accessible next. The game must
        // implement the identical rule so simulator and gameplay agree on unlock
        // sequences. Nearest-to-hole is the agreed canonical rule.
        //
        // Hole position: hx = (W-1)/2.0 (horizontally centered), hy = -1.0 (one row
        // below the bottom edge, row-0-bottom convention).
        private int FindAccessibleBlock(int color)
        {
            double hx = (M.W - 1) / 2.0;
            const double hy = -1.0;
            int bestIdx = -1;
            double bestDist = double.MaxValue;
            for (int i = 0; i < Cell.Length; i++)
            {
                if (!_accessible[i] || Cell[i] != color) continue;
                // Plate cover: a covered block is unpickable until the global picked-count
                // reaches the plate's requirement, at which point the plate opens. Picked
                // is monotonic + a pure function of occupancy (== TotalBlocks - BlocksLeft),
                // so this gate keeps the solver's memoisation correct.
                if (M.PlateReq[i] > 0 && Picked < M.PlateReq[i]) continue;
                int x = i % M.W, y = i / M.W;
                double dx = x - hx, dy = y - hy;
                double dist = dx * dx + dy * dy;
                if (dist < bestDist || (dist == bestDist && i < bestIdx))
                {
                    bestDist = dist;
                    bestIdx = i;
                }
            }
            return bestIdx;
        }

        public int FreeSlot()
        {
            for (int s = 0; s < ActiveColor.Length; s++) if (ActiveColor[s] < 0) return s;
            return -1;
        }

        public bool CanPull(int col) =>
            col >= 0 && col < M.Columns && QHead[col] < M.BusColor[col].Length;

        // A legal move exists iff an Active slot is free AND some column has a pullable bus.
        public bool HasLegalMove()
        {
            if (FreeSlot() < 0) return false;
            for (int c = 0; c < M.Columns; c++) if (CanPull(c)) return true;
            return false;
        }

        // Pull the head bus of `col` onto the first free Active slot, then resolve.
        // Returns blocks removed. Defensive: returns 0 without mutating state when
        // the precondition (!CanPull(col) || FreeSlot()<0) is violated.
        public int ApplyMove(int col)
        {
            int slot = FreeSlot();
            if (slot < 0 || !CanPull(col)) return 0;
            int head = QHead[col];
            ActiveColor[slot] = M.BusColor[col][head];
            ActiveRem[slot] = M.BusCap[col][head];
            QHead[col]++;
            return ResolveReleases();
        }

        // Loop to quiescence: while some active bus has an accessible block of its
        // colour, remove one, decrement that bus, free the slot if it hits 0, update
        // accessibility. (Order among same-colour buses does not change which blocks
        // ultimately clear, so a deterministic index-order greedy is sound.)
        public int ResolveReleases()
        {
            int removed = 0;
            bool changed = true;
            while (changed)
            {
                changed = false;
                for (int s = 0; s < ActiveColor.Length; s++)
                {
                    int color = ActiveColor[s];
                    if (color < 0) continue;
                    int cell = FindAccessibleBlock(color);
                    if (cell < 0) continue;
                    Cell[cell] = -1;
                    BlocksLeft--;
                    Picked++;
                    removed++;
                    ActiveRem[s]--;
                    if (ActiveRem[s] == 0) ActiveColor[s] = -1; // bus empty -> leaves, frees slot
                    RecomputeAccess();
                    changed = true;
                }
            }
            return removed;
        }

        // Win: every block removed, the Active Row empty, all queues exhausted.
        public bool IsWin()
        {
            if (BlocksLeft != 0) return false;
            for (int s = 0; s < ActiveColor.Length; s++) if (ActiveColor[s] >= 0) return false;
            for (int c = 0; c < M.Columns; c++) if (QHead[c] < M.BusColor[c].Length) return false;
            return true;
        }

        // Deadlock: Active Row full (no free slot) and no active colour has an
        // accessible matching block.
        public bool IsDeadlock()
        {
            if (FreeSlot() >= 0) return false;
            for (int s = 0; s < ActiveColor.Length; s++)
            {
                int color = ActiveColor[s];
                if (color < 0) continue;
                if (FindAccessibleBlock(color) >= 0) return false;
            }
            return true;
        }

        // Canonical memo key: occupancy bits + queue heads + a slot-order-independent
        // multiset of the active (colour,remaining) pairs. Exact (not a lossy hash)
        // so the solver's "unsolvable" verdict cannot be corrupted by a collision.
        public string Key()
        {
            var sb = new StringBuilder(Cell.Length + QHead.Length * 3 + 32);
            for (int i = 0; i < Cell.Length; i++) sb.Append(Cell[i] >= 0 ? '1' : '0');
            sb.Append('|');
            for (int c = 0; c < QHead.Length; c++) { sb.Append(QHead[c]); sb.Append(','); }
            sb.Append('|');
            int n = 0;
            for (int s = 0; s < ActiveColor.Length; s++) if (ActiveColor[s] >= 0) n++;
            var pairs = new long[n];
            int j = 0;
            for (int s = 0; s < ActiveColor.Length; s++)
                if (ActiveColor[s] >= 0) pairs[j++] = ((long)ActiveColor[s] << 20) | (uint)ActiveRem[s];
            System.Array.Sort(pairs);
            for (int i = 0; i < n; i++) { sb.Append(pairs[i]); sb.Append(';'); }
            return sb.ToString();
        }
    }
}
