using System;

namespace EpochWar.Core.State
{
    /// <summary>
    /// The address of a single Terrain_Cell within the 3D terrain volume (Req 6.1).
    ///
    /// <see cref="Y"/> is the vertical (up) axis, matching the design's terrain volume
    /// dimensions <c>(X, Y(up), Z)</c>. Although structurally similar to
    /// <see cref="Int3"/>, this is a distinct type so APIs that address terrain cells
    /// are not accidentally mixed with general-purpose integer vectors.
    ///
    /// Immutable value type with structural equality so it can index dense or sparse
    /// cell collections deterministically.
    /// </summary>
    public readonly struct CellCoord : IEquatable<CellCoord>
    {
        public int X { get; }
        public int Y { get; }
        public int Z { get; }

        public CellCoord(int x, int y, int z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly CellCoord Zero = new CellCoord(0, 0, 0);

        public static CellCoord operator +(CellCoord a, CellCoord b)
            => new CellCoord(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static CellCoord operator -(CellCoord a, CellCoord b)
            => new CellCoord(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static bool operator ==(CellCoord a, CellCoord b) => a.Equals(b);

        public static bool operator !=(CellCoord a, CellCoord b) => !a.Equals(b);

        /// <summary>
        /// Returns true when this coordinate lies inside a volume of the given
        /// <paramref name="dimensions"/> (origin-anchored, half-open on the upper bound).
        /// </summary>
        public bool IsInside(Int3 dimensions)
        {
            return X >= 0 && X < dimensions.X
                && Y >= 0 && Y < dimensions.Y
                && Z >= 0 && Z < dimensions.Z;
        }

        /// <summary>
        /// Converts this coordinate to a flat array index for a volume of the given
        /// <paramref name="dimensions"/>, using X-fastest ordering. The caller is
        /// responsible for bounds checking (see <see cref="IsInside"/>).
        /// </summary>
        public int ToFlatIndex(Int3 dimensions)
        {
            return X + (dimensions.X * (Y + (dimensions.Y * Z)));
        }

        public bool Equals(CellCoord other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object obj) => obj is CellCoord other && Equals(other);

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

        public override string ToString() => $"Cell({X}, {Y}, {Z})";
    }
}
