using System;

namespace Hoppa.LevelEditor.Core
{
    public readonly struct CellRef : IEquatable<CellRef>
    {
        public readonly int X;
        public readonly int Y;

        public CellRef(int x, int y) { X = x; Y = y; }

        public bool Equals(CellRef other) => X == other.X && Y == other.Y;
        public override bool Equals(object obj) => obj is CellRef other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(X, Y);
        public override string ToString() => $"({X},{Y})";

        public static bool operator ==(CellRef a, CellRef b) => a.Equals(b);
        public static bool operator !=(CellRef a, CellRef b) => !a.Equals(b);
    }
}
