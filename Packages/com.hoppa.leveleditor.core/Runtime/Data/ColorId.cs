using System;

namespace Hoppa.LevelEditor.Core
{
    public readonly struct ColorId : IEquatable<ColorId>
    {
        public readonly string Value;
        private readonly int _hash;

        public ColorId(string value)
        {
            Value = value ?? string.Empty;
            _hash = StringComparer.Ordinal.GetHashCode(Value);
        }

        public bool IsEmpty => string.IsNullOrEmpty(Value);

        public static readonly ColorId None = new ColorId(string.Empty);

        public bool Equals(ColorId other) => string.Equals(Value, other.Value, StringComparison.Ordinal);
        public override bool Equals(object obj) => obj is ColorId other && Equals(other);
        public override int GetHashCode() => _hash;
        public override string ToString() => Value;

        public static bool operator ==(ColorId a, ColorId b) => a.Equals(b);
        public static bool operator !=(ColorId a, ColorId b) => !a.Equals(b);

        public static implicit operator string(ColorId id) => id.Value;
        public static implicit operator ColorId(string value) => new ColorId(value);
    }
}
