using System;

namespace EpochWar.Core.State
{
    /// <summary>
    /// An integer 3-vector, the core's <c>UnityEngine.Vector3Int</c> equivalent.
    ///
    /// <see cref="EpochWar.Core"/> has no reference to <c>UnityEngine</c>, so the
    /// simulation needs its own integer vector for things like terrain volume
    /// dimensions and cell offsets. Presentation adapters in <c>EpochWar.Unity</c>
    /// convert to/from <c>Vector3Int</c> at the boundary.
    ///
    /// Immutable value type with structural equality so it is safe to use as a
    /// dictionary/set key.
    /// </summary>
    public readonly struct Int3 : IEquatable<Int3>
    {
        public int X { get; }
        public int Y { get; }
        public int Z { get; }

        public Int3(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly Int3 Zero = new Int3(0, 0, 0);
        public static readonly Int3 One = new Int3(1, 1, 1);

        public static Int3 operator +(Int3 a, Int3 b) => new Int3(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static Int3 operator -(Int3 a, Int3 b) => new Int3(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static Int3 operator *(Int3 a, int scalar) => new Int3(a.X * scalar, a.Y * scalar, a.Z * scalar);

        public static bool operator ==(Int3 a, Int3 b) => a.Equals(b);

        public static bool operator !=(Int3 a, Int3 b) => !a.Equals(b);

        /// <summary>The product of the components; useful as a flat cell count for a volume.</summary>
        public long Volume => (long)X * Y * Z;

        public bool Equals(Int3 other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object obj) => obj is Int3 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + X;
                hash = (hash * 31) + Y;
                hash = (hash * 31) + Z;
                return hash;
            }
        }

        public override string ToString() => $"({X}, {Y}, {Z})";
    }
}
