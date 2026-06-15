using System.Collections.Generic;
using System.Diagnostics;

namespace Hoppa.YAK.Sim
{
    // Depth-first search over move sequences to find ONE winning column-tap
    // order, or prove none exists within a node/time budget.
    //
    // The state space is a DAG: grid pointers and spool heads only ever
    // increase, so states never repeat by going "backwards" — a visited-set of
    // proven-dead states is enough (no cycle handling). Branches are ordered by
    // tiles-consumed-desc so a winning line is usually found early.
    //
    // Honesty rule: a budget cut returns BudgetExceeded, NEVER Unsolvable. Only a
    // fully-explored search with no win is Unsolvable.
    public sealed class YakSolver
    {
        public enum Outcome { Solvable, Unsolvable, BudgetExceeded }

        public sealed class Result
        {
            public Outcome Outcome;
            public int[]   WinPath;     // column indices to tap, in order (null unless Solvable)
            public long    Nodes;
            public long    ElapsedMs;
        }

        private readonly long _maxNodes;
        private readonly long _timeoutMs;
        private readonly HashSet<string> _dead = new HashSet<string>();
        private readonly Stopwatch _sw = new Stopwatch();
        private long _nodes;
        private bool _budgetHit;
        private List<int> _path;

        public YakSolver(long maxNodes, long timeoutMs)
        {
            _maxNodes  = maxNodes  > 0 ? maxNodes  : 200_000;
            _timeoutMs = timeoutMs > 0 ? timeoutMs : 5_000;
        }

        public Result Solve(YakLevelModel model)
        {
            _sw.Restart();
            _nodes = 0; _budgetHit = false;
            _dead.Clear();
            _path = new List<int>(model.TotalSpools);

            var start = new YakSimState(model);
            bool found = Dfs(start);

            _sw.Stop();
            var r = new Result { Nodes = _nodes, ElapsedMs = _sw.ElapsedMilliseconds };
            if (found)            { r.Outcome = Outcome.Solvable; r.WinPath = _path.ToArray(); }
            else if (_budgetHit)    r.Outcome = Outcome.BudgetExceeded;
            else                    r.Outcome = Outcome.Unsolvable;
            return r;
        }

        // Returns true if a win is reachable from `s`; on success _path holds the
        // remaining moves appended in order.
        private bool Dfs(YakSimState s)
        {
            if (s.IsWin()) return true;
            if (_budgetHit) return false;
            if (_nodes >= _maxNodes || _sw.ElapsedMilliseconds > _timeoutMs) { _budgetHit = true; return false; }
            _nodes++;

            if (!s.HasLegalMove()) return false; // deadlock / stuck — dead branch

            string key = s.Key();
            if (_dead.Contains(key)) return false;

            // Expand children: one per sendable column, ordered by tiles consumed
            // (desc) so the most productive send is tried first.
            var children = new List<(int col, int consumed, YakSimState st)>(s.M.Columns);
            for (int c = 0; c < s.M.Columns; c++)
            {
                if (!s.CanSend(c)) continue;
                if (s.FreeSlot() < 0) break; // no slot — no move regardless of column
                var child = s.Clone();
                int consumed = child.ApplyMove(c);
                children.Add((c, consumed, child));
            }
            children.Sort((a, b) => b.consumed.CompareTo(a.consumed));

            foreach (var (col, _, child) in children)
            {
                _path.Add(col);
                if (Dfs(child)) return true;
                _path.RemoveAt(_path.Count - 1);
                if (_budgetHit) return false;
            }

            // Fully explored with no win and no budget cut → this state is dead.
            _dead.Add(key);
            return false;
        }
    }
}
