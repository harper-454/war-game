using System;
using EpochWar.Core.Math;

namespace EpochWar.Core.State
{
    /// <summary>
    /// A deterministic continuous world position for a <see cref="UnitInstance"/>, the core's
    /// engine-free equivalent of <c>UnityEngine.Vector3</c>.
    ///
    /// <see cref="EpochWar.Core"/> has no reference to <c>UnityEngine</c>, and the simulation
    /// advances on a fixed tick that must be bit-reproducible on the Host and in headless
    /// property tests. Positions are therefore stored as <see cref="Fixed"/> (Q32.32)
    /// components rather than <c>float</c>; presentation adapters in <c>EpochWar.Unity</c>
    /// convert to/from <c>Vector3</c> at the boundary via <see cref="ToFloatX"/> etc.
    /// <see cref="Y"/> is the vertical (up) axis, matching the terrain volume convention.
    ///
    /// Immutable value type with structural equality.
    /// </summary>
    public readonly struct WorldPosition : IEquatable<WorldPosition>
    {
        public Fixed X { get; }
        public Fixed Y { get; }
        public Fixed Z { get; }

        public WorldPosition(Fixed x, Fixed y, Fixed z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public static readonly WorldPosition Zero = new WorldPosition(Fixed.Zero, Fixed.Zero, Fixed.Zero);

        /// <summary>Builds a position from integer cell-aligned coordinates.</summary>
        public static WorldPosition FromInts(int x, int y, int z)
            => new WorldPosition(Fixed.FromInt(x), Fixed.FromInt(y), Fixed.FromInt(z));

        /// <summary>Builds a position from a terrain <see cref="CellCoord"/>.</summary>
        public static WorldPosition FromCell(CellCoord cell)
            => FromInts(cell.X, cell.Y, cell.Z);

        /// <summary>Builds a position from authored <see cref="float"/> components (boundary use only).</summary>
        public static WorldPosition FromFloats(float x, float y, float z)
            => new WorldPosition(Fixed.FromFloat(x), Fixed.FromFloat(y), Fixed.FromFloat(z));

        public static WorldPosition operator +(WorldPosition a, WorldPosition b)
            => new WorldPosition(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        public static WorldPosition operator -(WorldPosition a, WorldPosition b)
            => new WorldPosition(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        public static bool operator ==(WorldPosition a, WorldPosition b) => a.Equals(b);

        public static bool operator !=(WorldPosition a, WorldPosition b) => !a.Equals(b);

        /// <summary>The X component as a <see cref="float"/> (presentation only).</summary>
        public float ToFloatX() => X.ToFloat();

        /// <summary>The Y component as a <see cref="float"/> (presentation only).</summary>
        public float ToFloatY() => Y.ToFloat();

        /// <summary>The Z component as a <see cref="float"/> (presentation only).</summary>
        public float ToFloatZ() => Z.ToFloat();

        public bool Equals(WorldPosition other) => X == other.X && Y == other.Y && Z == other.Z;

        public override bool Equals(object obj) => obj is WorldPosition other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;
                hash = (hash * 31) + X.GetHashCode();
                hash = (hash * 31) + Y.GetHashCode();
                hash = (hash * 31) + Z.GetHashCode();
                return hash;
            }
        }

        public override string ToString() => $"World({X}, {Y}, {Z})";
    }
}
